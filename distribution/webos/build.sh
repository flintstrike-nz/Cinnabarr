#!/usr/bin/env bash
# Builds the Cinnabarr Games app for an LG webOS TV.
#
#   ./build.sh http://192.168.1.20:8989/tv
#
# The address is the games page of YOUR Cinnabarr, as the TV reaches it on your network (use the PC's address, not
# "localhost"). Needs Node.js. The output is out/org.cinnabarr.games_1.0.0_all.ipk, to install with ares-install or
# webOS Dev Manager (see docs/cinnabarr/TV.md).
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "usage: $0 http://<address-of-cinnabarr>:8989/tv" >&2
  exit 1
fi

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cp "$here"/appinfo.json "$here"/index.html "$here"/icon.png "$here"/largeIcon.png "$here"/splash.png "$work"/
printf 'window.CINNABARR_URL = "%s";\n' "$1" > "$work/config.js"

mkdir -p "$here/out"
if command -v ares-package >/dev/null 2>&1; then
  ares-package "$work" -o "$here/out"
else
  npx --yes -p @webos-tools/cli ares-package "$work" -o "$here/out"
fi
