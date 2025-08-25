#!/bin/bash

echo "Running HNSW Index Server Docker container..."

mkdir -p data
mkdir -p logs

docker run -it --rm \
  -p 8080:8080 \
  -v "$(pwd)/hnswindex.json:/app/hnswindex.json" \
  -v "$(pwd)/data:/app/data" \
  -v "$(pwd)/logs:/app/logs" \
  jchristn/hnswindex-server:%1