# HnswLite Docker Deployment

This directory contains the default HnswLite Docker Compose deployment. In V2,
PostgreSQL is the default storage backend for the server.

## Layout

```text
docker/
|-- compose.yaml
|-- factory/
|   |-- reset.bat
|   `-- reset.sh
|-- hnswlite/
|   |-- hnswindex.json
|   |-- data/
|   `-- logs/
`-- postgres/
    |-- data/
    `-- provision/
        `-- 001_hnswlite.sql
```

## Services

- `hnswlite-postgres`: PostgreSQL 16 database used by default for indexes.
- `hnswlite-postgres-provisioner`: one-shot container that runs the schema and
  seed SQL after PostgreSQL is healthy.
- `hnswlite-server`: HnswLite REST API, built from the local V2 source tree.
- `hnswlite-dashboard`: dashboard UI, built from the local source tree.

## Quick Start

```bash
cd docker
docker compose up -d --build
docker compose ps
```

Then use:

- Server API: `http://localhost:8080/`
- Dashboard: `http://localhost:8081/dashboard/`

The admin API key is defined in `hnswlite/hnswindex.json` under
`Server.AdminApiKey`. Paste it into the dashboard login screen.

## Build Published Images

From the repository root, build and push both multi-architecture images with one
tag:

```cmd
build-all.bat v2.0.0
```

The wrapper calls `build-server.bat` and `build-dashboard.bat` with the same
tag. Those scripts also publish the `latest` tag.

To avoid host port conflicts, override the published ports while keeping the
same internal service configuration:

```bash
HNSWLITE_POSTGRES_PORT=55432 HNSWLITE_SERVER_PORT=18080 HNSWLITE_DASHBOARD_PORT=18081 docker compose up -d --build
```

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

The reset stops the Compose stack, removes PostgreSQL data, clears SQLite index
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
