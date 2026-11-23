#!/usr/bin/env bash
# Development helper: build the plugin, copy it into a Jellyfin plugin folder and restart a Docker container.
#
# Usage: ./deploy.sh [Debug|Release]   (default: Debug)
# Environment:
#   JELLYFIN_PLUGIN_DIR   plugin folder of the Jellyfin instance on the host (required),
#                         e.g. /srv/jellyfin/config/plugins/DVB-I for a container with /config mapped to /srv/jellyfin/config
#   JELLYFIN_CONTAINER    Docker container to restart (default: jellyfin)
#   JELLYFIN_URL          URL used to wait for start  (default: http://localhost:8096)
set -euo pipefail

CONFIG="${1:-Debug}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
PLUGIN_DIR="${JELLYFIN_PLUGIN_DIR:?Set JELLYFIN_PLUGIN_DIR to the DVB-I folder in the Jellyfin plugin directory}"
CONTAINER="${JELLYFIN_CONTAINER:-jellyfin}"
JELLYFIN_URL="${JELLYFIN_URL:-http://localhost:8096}"

dotnet publish "$ROOT/Jellyfin.Plugin.DvbI/Jellyfin.Plugin.DvbI.csproj" -c "$CONFIG" -o "$ROOT/artifacts/$CONFIG" --nologo -v q

mkdir -p "$PLUGIN_DIR"
rm -f "$PLUGIN_DIR"/*.dll "$PLUGIN_DIR"/*.pdb
cp "$ROOT/artifacts/$CONFIG/Jellyfin.Plugin.DvbI.dll" "$PLUGIN_DIR/"
cp "$ROOT/artifacts/$CONFIG/Jellyfin.Plugin.DvbI.pdb" "$PLUGIN_DIR/" 2>/dev/null || true
cp "$ROOT/images/logo.png" "$PLUGIN_DIR/logo.png"

# Plugin manifest with thumbnail (Jellyfin reads imagePath relative to the plugin folder).
VERSION=$(grep -oP '^version:\s*"\K[^"]+' "$ROOT/build.yaml")
TARGET_ABI=$(grep -oP '^targetAbi:\s*"\K[^"]+' "$ROOT/build.yaml")
cat > "$PLUGIN_DIR/meta.json" <<JSON
{
  "category": "LiveTV",
  "changelog": "",
  "description": "Live TV channels and programme guide from DVB-I service lists (ETSI TS 103 770).",
  "guid": "0cd0bef6-8b6b-468b-9153-4360f27c466e",
  "name": "DVB-I",
  "overview": "DVB-I service discovery and programme guide for Live TV",
  "owner": "local",
  "targetAbi": "$TARGET_ABI",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "$VERSION",
  "status": "Active",
  "autoUpdate": false,
  "imagePath": "logo.png",
  "assemblies": []
}
JSON

docker restart "$CONTAINER" >/dev/null
echo "Deployed ($CONFIG) to $PLUGIN_DIR, waiting for Jellyfin..."
for _ in $(seq 1 60); do
    if curl -sf -m 2 "$JELLYFIN_URL/health" >/dev/null; then echo "Jellyfin is up."; exit 0; fi
    sleep 1
done
echo "Jellyfin did not become healthy within 60s" >&2
exit 1
