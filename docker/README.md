# HnswLite Docker Deployment

This directory contains the default HnswLite Docker Compose deployment. In V2,
PostgreSQL is the default storage backend for the server.

## Layout

```text
docker/
|-- compose.yaml
|-- prometheus.yaml
|-- tempo.yaml
|-- update.bat
|-- update.sh
|-- factory/
|   |-- reset.bat
|   `-- reset.sh
|-- grafana/
|   `-- provisioning/
|       |-- datasources/hnswlite-datasources.yaml
|       `-- dashboards/hnswlite-dashboards.yaml
|-- hnswlite/
|   |-- hnswindex.json
|   |-- data/
|   `-- logs/
`-- postgres/
    |-- data/
    `-- provision/
        `-- 001_hnswlite.sql
```

Dashboard JSON lives in `../assets/grafana/` and is mounted read-only into Grafana.

## Services

- `hnswlite-postgres`: PostgreSQL 16 database used by default for indexes.
- `hnswlite-postgres-provisioner`: one-shot container that runs the schema and
  seed SQL after PostgreSQL is healthy.
- `hnswlite-server`: HnswLite REST API, built from the local V2 source tree.
- `hnswlite-dashboard`: dashboard UI, built from the local source tree.
- `hnswlite-prometheus`: Prometheus `v3.5.4`; scrapes the server's telemetry endpoint (`hnswlite-server:9464`, not published to the host).
- `hnswlite-tempo`: Tempo `2.6.1`; receives OTLP traces from the server on 4317/4318 and serves the query API on 3200.
- `hnswlite-grafana`: Grafana OSS `13.0.2` with the Prometheus and Tempo datasources and the six HnswLite dashboards provisioned into the `HnswLite` folder.

Startup is health-gated: PostgreSQL and Tempo become healthy before the server starts, the dashboard waits for the server, and Grafana waits for Prometheus and Tempo.

## Quick Start

```bash
cd docker
docker compose up -d --build
docker compose ps
```

Then use:

- Server API: `http://localhost:8080/`
- Dashboard: `http://localhost:8081/dashboard/`
- Grafana: `http://localhost:3000/` (`admin` / `admin`)
- Prometheus: `http://localhost:9090/`
- Tempo API: `http://localhost:3200/`

The admin API key is defined in `hnswlite/hnswindex.json` under
`Server.AdminApiKey`. Paste it into the dashboard login screen.

## Build Published Images

From the repository root, build and push both multi-architecture images with one
tag:

```cmd
build-all.bat v2.0.0
```

On macOS/Linux use the equivalent `./build-all.sh v2.0.0` (also `build-server.sh` and `build-dashboard.sh`).

The wrapper calls `build-server.bat` and `build-dashboard.bat` with the same
tag. Those scripts also publish the `latest` tag.

To avoid host port conflicts, override the published ports while keeping the
same internal service configuration:

```bash
HNSWLITE_POSTGRES_PORT=55432 HNSWLITE_SERVER_PORT=18080 HNSWLITE_DASHBOARD_PORT=18081 docker compose up -d --build
```

The observability ports have the same kind of overrides: `HNSWLITE_GRAFANA_PORT`, `HNSWLITE_PROMETHEUS_PORT`, `HNSWLITE_TEMPO_PORT`, `HNSWLITE_OTLP_GRPC_PORT`, and `HNSWLITE_OTLP_HTTP_PORT`. If you change the Grafana, Prometheus, or Tempo host ports, rebuild the dashboard with `HNSWLITE_GRAFANA_URL`, `HNSWLITE_PROMETHEUS_URL`, and `HNSWLITE_TEMPO_URL` so its External services card links to the right place.

## Observability

The server's `Telemetry` block in `hnswlite/hnswindex.json` points OTLP at `http://hnswlite-tempo:4317` and binds the Prometheus endpoint to the container name `hnswlite-server` on port 9464. See [../TELEMETRY.md](../TELEMETRY.md) for the metrics and spans catalogs, dashboards, and alerts.

Grafana's admin credentials default to `admin` / `admin` for local development. For any shared or hosted deployment, set `GRAFANA_ADMIN_PASSWORD` (and optionally `GRAFANA_ADMIN_USER`) in the environment or an untracked `.env` file before `docker compose up`. Do not publish Prometheus, Tempo, or the server's 9464 endpoint on a public interface.

## Update

`update.bat` / `update.sh` pull the latest published images and recreate the stack (`docker compose pull`, `down`, `up -d`, then `docker ps -a`). They are non-destructive: data directories and named volumes are preserved.

## PostgreSQL Defaults

Compose creates a PostgreSQL database with these default credentials:

```text
Host: hnswlite-postgres
Port: 5432
Database: hnswlite
Username: hnswlite
Password: hnswlite
```

The server config points at that service name:

```json
"Storage": {
  "DefaultStorageType": "PostgreSQL",
  "SqliteDirectory": "./data/indexes/",
  "PostgresqlConnectionString": "Host=hnswlite-postgres;Port=5432;Database=hnswlite;Username=hnswlite;Password=hnswlite",
  "PostgresqlAutoProvision": true
}
```

SQLite remains available as an explicit `StorageType` for compatibility with
embedded and fallback deployments, but Docker-created indexes default to
PostgreSQL.

## Provisioning

`hnswlite-postgres-provisioner` mounts `postgres/provision` and runs
`001_hnswlite.sql` with `psql`. The script is idempotent and creates:

- HNSW index metadata table.
- HNSW node table.
- Node layer and neighbor tables.
- Per-index metadata table.
- System metadata table with a schema/provisioning marker.

The server also has `PostgresqlAutoProvision` enabled so local non-Compose
deployments can create the same schema when using the PostgreSQL provider.

## Volumes

- `postgres/data`: PostgreSQL data directory. Back this up for durable index
  storage.
- `hnswlite/data`: optional SQLite fallback data and local index files.
- `hnswlite/logs`: server logs.
- `hnswlite/hnswindex.json`: mounted server configuration.

## Reset

Use the factory reset scripts to wipe local data while preserving configuration:

```bash
cd docker/factory
./reset.sh
```

On Windows:

```bat
cd docker\factory
reset.bat
```

The reset stops the Compose stack, removes the Prometheus, Tempo, and Grafana
volumes (`docker compose down -v`), removes PostgreSQL data, clears SQLite index
data and logs, and leaves `hnswindex.json` in place. The next
`docker compose up -d --build` run provisions a fresh PostgreSQL schema.

## Backup and Restore

For PostgreSQL-backed deployments, back up `postgres/data` or use `pg_dump`:

```bash
docker compose exec -T hnswlite-postgres pg_dump -U hnswlite hnswlite > hnswlite.sql
```

Restore into a fresh database after the PostgreSQL service is running:

```bash
docker compose exec -T hnswlite-postgres psql -U hnswlite -d hnswlite < hnswlite.sql
```

## CORS

CORS headers come from `hnswindex.json` under the `Cors` block. The server sends
them on every response and responds to OPTIONS pre-flight requests without API
key authentication.

## Dashboard Proxying

The dashboard nginx config proxies `/v1.0/` to
`http://hnswlite-server:8080` inside the Compose network, so browsers only need
to connect to the dashboard container. To point the dashboard at a remote server,
set `HNSWLITE_SERVER_URL` at dashboard build time and rebuild the dashboard
image.
