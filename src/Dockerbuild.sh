#!/bin/bash

echo "Building HNSW Index Server Docker image..."

cd ..
docker build -f HnswIndex.Server/Dockerfile -t hnswindex-server:latest .

if [ $? -eq 0 ]; then
    echo "Docker image built successfully!"
    echo ""
    echo "To run the container:"
    echo "docker run -p 8080:8080 -v \$(pwd)/data:/app/data -v \$(pwd)/logs:/app/logs hnswindex-server:latest"
else
    echo "Docker build failed!"
    exit 1
fi