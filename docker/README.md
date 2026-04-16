# HnswLite Docker deployment

This directory contains a `compose.yaml` that runs both the HnswLite server and
dashboard containers together.

## Layout

```
docker/
├── compose.yaml              # runs server + dashboard
└── hnswlite/
    ├── hnswindex.json        # mounted into the server as /app/hnswindex.json
    ├── data/                 # mounted as /app/data (index storage)
    └── logs/                 # mounted as /app/logs
```

## Quick start

```bash
cd docker
docker compose up -d
```

Then:

- Server API:  http://localhost:8080/
- Dashboard:   http://localhost:8081/dashboard/

The admin API key is defined in `hnswlite/hnswindex.json` (`Server.AdminApiKey`).
Paste it into the dashboard login screen.

## CORS

CORS headers come from `hnswindex.json` under the `Cors` block. The server sends
them on every response, and responds to OPTIONS pre-flight requests via Watson's
built-in pre-flight hook (no authentication required for OPTIONS).

## Proxying from the dashboard

The dashboard's nginx (see `../dashboard/nginx.conf`) proxies `/v1.0/` to
`http://hnswlite-server:8080` inside the compose network, so browsers only need
to connect to the dashboard container. If you want the dashboard to talk to a
remote server instead, set `HNSWLITE_SERVER_URL` at dashboard build time
and rebuild the dashboard image.
