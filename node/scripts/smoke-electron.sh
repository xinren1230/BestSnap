#!/usr/bin/env bash
# Launch Electron under Xvfb, grab a screenshot, then quit.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
npx tsc -p tsconfig.json
node scripts/copy-static.mjs
OUT="${1:-$ROOT/test-results/electron-smoke.png}"
mkdir -p "$(dirname "$OUT")"
export DISPLAY="${DISPLAY:-:99}"
if ! xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then
  Xvfb "$DISPLAY" -screen 0 1920x1080x24 -ac +extension RANDR >/tmp/snapforge-xvfb.log 2>&1 &
  echo $! > /tmp/snapforge-xvfb.pid
  sleep 0.4
fi
npx electron . --no-sandbox >/tmp/snapforge-electron.log 2>&1 &
EPID=$!
sleep 4
ffmpeg -y -f x11grab -video_size 1920x1080 -i "${DISPLAY}.0" -frames:v 1 "$OUT" >/dev/null 2>&1 || import -window root "$OUT" || true
kill "$EPID" >/dev/null 2>&1 || true
wait "$EPID" 2>/dev/null || true
echo "smoke screenshot: $OUT"
ls -l "$OUT" || true
