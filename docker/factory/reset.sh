#!/bin/bash
set -e

echo "========================================"
echo " HnswLite Factory Reset"
echo "========================================"
echo ""
echo "This will delete all PostgreSQL data, SQLite index databases, log files, and the"
echo "Prometheus, Tempo, and Grafana volumes (metrics history, traces, Grafana state)."
echo "Configuration (hnswindex.json) will be preserved."
echo ""
read -p "Type 'RESET' to confirm: " confirm
if [ "$confirm" != "RESET" ]; then
    echo "Cancelled."
    exit 1
fi
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOCKER_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
HNSWLITE_DIR="${DOCKER_DIR}/hnswlite"
POSTGRES_DIR="${DOCKER_DIR}/postgres"

echo "[1/4] Stopping containers and removing observability volumes..."
(cd "${DOCKER_DIR}" && docker compose down -v)
echo ""

echo "[2/4] Deleting PostgreSQL data..."
if [ -d "${POSTGRES_DIR}/data" ]; then
    rm -rf "${POSTGRES_DIR}/data"
    echo "  PostgreSQL data deleted."
else
    echo "  No PostgreSQL data directory found."
fi
echo ""

echo "[3/4] Deleting SQLite index databases..."
if [ -d "${HNSWLITE_DIR}/data" ]; then
    rm -rf "${HNSWLITE_DIR}/data"/*
    echo "  SQLite index databases deleted."
else
    echo "  No data directory found."
fi
echo ""

echo "[4/4] Deleting log files..."
if [ -d "${HNSWLITE_DIR}/logs" ]; then
    rm -rf "${HNSWLITE_DIR}/logs"/*
    echo "  Log files deleted."
else
    echo "  No logs directory found."
fi
echo ""

echo "========================================"
echo " Factory reset complete."
echo " PostgreSQL will be re-provisioned on next startup."
echo " Run 'docker compose up -d' to restart."
echo "========================================"
