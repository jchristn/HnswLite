#!/bin/bash
set -e

echo "========================================"
echo " HnswLite Factory Reset"
echo "========================================"
echo ""
echo "This will delete all index databases and log files."
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

echo "[1/3] Stopping containers..."
(cd "${DOCKER_DIR}" && docker compose down)
echo ""

echo "[2/3] Deleting index databases..."
if [ -d "${HNSWLITE_DIR}/data" ]; then
    rm -rf "${HNSWLITE_DIR}/data"/*
    echo "  Index databases deleted."
else
    echo "  No data directory found."
fi
echo ""

echo "[3/3] Deleting log files..."
if [ -d "${HNSWLITE_DIR}/logs" ]; then
    rm -rf "${HNSWLITE_DIR}/logs"/*
    echo "  Log files deleted."
else
    echo "  No logs directory found."
fi
echo ""

echo "========================================"
echo " Factory reset complete."
echo " Run 'docker compose up -d' to restart."
echo "========================================"
