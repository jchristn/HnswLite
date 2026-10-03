#!/usr/bin/env bash
# Builds and pushes the multi-architecture HnswLite server image, then pulls it into the local registry.
# Equivalent of build-server.bat. Usage: ./build-server.sh <tag>   (example: ./build-server.sh v2.0.0)
set -euo pipefail

if [ "$#" -lt 1 ] || [ -z "$1" ]; then
    echo "Provide a tag argument for the build."
    echo "Example: ./build-server.sh v2.0.0"
    exit 1
fi

TAG="$1"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

fail() {
    echo ""
    echo "Build failed."
    exit 1
}

echo ""
echo "Building for linux/amd64 and linux/arm64/v8..."
cd "${ROOT}/src"
docker buildx build -f HnswIndex.Server/Dockerfile --builder cloud-jchristn77-jchristn77 --platform linux/amd64,linux/arm64/v8 --tag "jchristn77/hnswlite-server:${TAG}" --tag jchristn77/hnswlite-server:latest --push . || fail

echo ""
echo "Loading images into the local registry..."
docker pull "jchristn77/hnswlite-server:${TAG}" || fail
docker pull jchristn77/hnswlite-server:latest || fail

echo "Done"
