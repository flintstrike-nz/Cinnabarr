#!/usr/bin/env bash
# Builds the Cinnabarr plugin for Jellyfin and packages it the way Jellyfin installs plugins.
#
#   ./build.sh                 Jellyfin 12.x (.NET 10)
#   ./build.sh 10.11           Jellyfin 10.11 (.NET 9)
#
# Writes out/cinnabarr_<version>_jf<target>.zip and out/manifest.json. To install by hand, unzip into
# <Jellyfin data>/plugins/Cinnabarr_<version>/ and restart Jellyfin. To install from Jellyfin's catalogue, host
# manifest.json and the zip somewhere Jellyfin can reach and add the manifest URL under
# Dashboard → Plugins → Repositories (set CINNABARR_PLUGIN_BASE_URL to where the zip will live).
set -euo pipefail

cd "$(dirname "$0")"

TARGET="${1:-12}"
VERSION="0.1.0.0"
OUT="out"
STAGE="$OUT/stage-$TARGET"
BASE_URL="${CINNABARR_PLUGIN_BASE_URL:-}"

case "$TARGET" in
  12) ABI="12.4.0.0" ;;
  10.11) ABI="10.11.0.0" ;;
  *) echo "Unknown Jellyfin target '$TARGET' (use 12 or 10.11)" >&2; exit 1 ;;
esac

rm -rf "$STAGE"
mkdir -p "$STAGE"

dotnet publish src/Jellyfin.Plugin.Cinnabarr/Jellyfin.Plugin.Cinnabarr.csproj \
  -c Release -p:JellyfinTarget="$TARGET" -o "$STAGE/publish" --nologo -v q

# Only the plugin itself: Jellyfin provides everything it references.
cp "$STAGE/publish/Jellyfin.Plugin.Cinnabarr.dll" "$STAGE/"
rm -rf "$STAGE/publish"

TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

cat > "$STAGE/meta.json" <<JSON
{
  "category": "General",
  "changelog": "First version: settings, roles and the TV check page.",
  "description": "Browse and request shows and movies, manage downloads and play games from Cinnabarr.",
  "guid": "04afd9e8-511a-4cc3-a5d7-efd4e175eddf",
  "name": "Cinnabarr",
  "overview": "Cinnabarr inside the Jellyfin apps",
  "owner": "Cinnabarr",
  "targetAbi": "$ABI",
  "timestamp": "$TIMESTAMP",
  "version": "$VERSION"
}
JSON

ZIP="cinnabarr_${VERSION}_jf${TARGET}.zip"
rm -f "$OUT/$ZIP"
(cd "$STAGE" && zip -q -X "../$ZIP" Jellyfin.Plugin.Cinnabarr.dll meta.json)
rm -rf "$STAGE"

CHECKSUM="$(md5sum "$OUT/$ZIP" | cut -d' ' -f1)"

cat > "$OUT/manifest.json" <<JSON
[
  {
    "guid": "04afd9e8-511a-4cc3-a5d7-efd4e175eddf",
    "name": "Cinnabarr",
    "description": "Browse and request shows and movies, manage downloads and play games from Cinnabarr.",
    "overview": "Cinnabarr inside the Jellyfin apps",
    "owner": "Cinnabarr",
    "category": "General",
    "versions": [
      {
        "version": "$VERSION",
        "changelog": "First version: settings, roles and the TV check page.",
        "targetAbi": "$ABI",
        "sourceUrl": "${BASE_URL%/}/$ZIP",
        "checksum": "$CHECKSUM",
        "timestamp": "$TIMESTAMP"
      }
    ]
  }
]
JSON

echo "Built $OUT/$ZIP (md5 $CHECKSUM) and $OUT/manifest.json"
