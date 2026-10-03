#!/usr/bin/env bash
# Builds and pushes both release images with one tag. Equivalent of build-all.bat.
# Usage: ./build-all.sh <tag>   (example: ./build-all.sh v2.0.0)
set -euo pipefail

if [ "$#" -lt 1 ] || [ -z "$1" ]; then
    echo "Usage: build-all.sh DOCKER_IMAGE_TAG"
    echo "Example: build-all.sh v2.0.0"
    exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"${ROOT}/build-server.sh" "$1"
"${ROOT}/build-dashboard.sh" "$1"

echo "Done"
