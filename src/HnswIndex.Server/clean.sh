#!/usr/bin/env bash
set -u

echo "========================================"
echo " HnswLite Server - Clean"
echo "========================================"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}"

if [ -f "hnswindex.json" ]; then
    rm -f "hnswindex.json"
    echo "  hnswindex.json deleted."
else
    echo "  hnswindex.json not found."
fi

if [ -d "data" ]; then
    rm -rf "data"
    echo "  data/ directory deleted."
else
    echo "  data/ directory not found."
fi

if [ -d "logs" ]; then
    rm -rf "logs"
    echo "  logs/ directory deleted."
else
    echo "  logs/ directory not found."
fi

echo "========================================"
echo " Clean complete."
echo "========================================"
exit 0
