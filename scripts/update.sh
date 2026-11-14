#!/usr/bin/env bash
# Updates a Docker Compose install of Cinnabarr: pulls the newest code, rebuilds the image and restarts the container.
# Your library and settings live in ./config and your media folders, so they are not touched.
#
# Run it by hand, or from cron (see docs/cinnabarr/DOCKER.md), for example every night at 4am:
#   0 4 * * * /path/to/Cinnabarr/scripts/update.sh >> /path/to/Cinnabarr/update.log 2>&1
set -euo pipefail

cd "$(dirname "$0")/.."

before=$(git rev-parse HEAD)
git pull --ff-only
after=$(git rev-parse HEAD)

# Rebuild when there is new code, or when the running image was built from something else.
if [ "$before" = "$after" ] && [ -z "${FORCE:-}" ] && docker compose images 2>/dev/null | grep -q cinnabarr; then
  echo "$(date -Is) already up to date ($after)"
  exit 0
fi

echo "$(date -Is) updating $before -> $after"
CINNABARR_COMMIT="$after" docker compose up -d --build
docker image prune -f > /dev/null
echo "$(date -Is) done"
