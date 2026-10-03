#!/bin/bash
# Pulls the latest published images and recreates the stack. Non-destructive:
# named volumes and bind-mounted data are preserved. See factory/reset.sh for a full reset.
set -e
cd "$(dirname "${BASH_SOURCE[0]}")"
docker compose pull
docker compose down
docker compose up -d
docker ps -a
