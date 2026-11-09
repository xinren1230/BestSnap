#!/usr/bin/env bash
# Produce a Linux AppImage with electron-builder (unsigned).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
npx tsc -p tsconfig.json
node scripts/copy-static.mjs
npx electron-builder --linux AppImage --publish never
echo "AppImage (if built) is under $ROOT/dist-app/"
ls -lh "$ROOT/dist-app"/*.AppImage 2>/dev/null || true
