#!/usr/bin/env bash
# Record a ~1–2 min 1920x1080 demo of the real Electron toolbar on X11/Xvfb.
# Repro: cd node && bash demo/run-demo.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
OUT="${SNAPFORGE_DEMO_OUT:-$ROOT/demo/snapforge-linux-demo.mp4}"
ART="${SNAPFORGE_DEMO_ARTIFACT:-/opt/cursor/artifacts/snapforge-linux-demo.mp4}"
mkdir -p "$(dirname "$OUT")"
mkdir -p "$(dirname "$ART")" 2>/dev/null || true

npx tsc -p tsconfig.json
node scripts/copy-static.mjs
node scripts/generate-fixtures.mjs
bash scripts/download-models.sh

pkill -f "/SnapForge/node/node_modules/electron/dist/electron" >/dev/null 2>&1 || true
pkill -f "/SnapForge/node/node_modules/.bin/electron" >/dev/null 2>&1 || true
pkill -f "/SnapForge/node/dist/electron/worker.js" >/dev/null 2>&1 || true
sleep 1

DISPLAY_NUM="${SNAPFORGE_DEMO_DISPLAY:-:97}"
NEED_XVFB=0
if ! xdpyinfo -display "$DISPLAY_NUM" >/dev/null 2>&1; then
  NEED_XVFB=1
  Xvfb "$DISPLAY_NUM" -screen 0 1920x1080x24 -ac +extension RANDR -nolisten tcp >/tmp/snapforge-demo-xvfb.log 2>&1 &
  echo $! > /tmp/snapforge-demo-xvfb.pid
  for _ in 1 2 3 4 5 6 7 8; do
    xdpyinfo -display "$DISPLAY_NUM" >/dev/null 2>&1 && break
    sleep 0.25
  done
fi
export DISPLAY="$DISPLAY_NUM"
export LIBGL_ALWAYS_SOFTWARE=1
export ELECTRON_OZONE_PLATFORM_HINT=x11
export ELECTRON_ENABLE_LOGGING=1
# Isolated settings/history so the demo does not touch a personal profile.
export SNAPFORGE_DATA="${TMPDIR:-/tmp}/snapforge-demo-data"
rm -rf "$SNAPFORGE_DATA"
mkdir -p "$SNAPFORGE_DATA"
xsetroot -solid "#3d4a63" >/dev/null 2>&1 || true
xsetroot -cursor_name left_ptr >/dev/null 2>&1 || true

PROFILE="${TMPDIR:-/tmp}/snapforge-demo-profile"
rm -rf "$PROFILE"
mkdir -p "$PROFILE"
ELECTRON_BIN="$ROOT/node_modules/electron/dist/electron"
"$ELECTRON_BIN" "$ROOT" --demo --no-sandbox --disable-gpu --user-data-dir="$PROFILE" \
  >/tmp/snapforge-demo-electron.log 2>&1 &
EPID=$!

# Wait for the live-demo control port (toolbar + demo page are up) before recording
# so the tape does not start on a black Xvfb or a white Electron first-paint.
for i in $(seq 1 40); do
  if curl -fsS http://127.0.0.1:38471 >/dev/null 2>&1; then
    echo "demo control up at t=${i}s"
    break
  fi
  if ! kill -0 "$EPID" 2>/dev/null; then
    echo "electron exited before demo server" >&2
    cat /tmp/snapforge-demo-electron.log >&2 || true
    exit 1
  fi
  sleep 0.25
done
sleep 0.6

RAW="$(mktemp --suffix=.mkv)"
ffmpeg -y -thread_queue_size 512 -f x11grab -video_size 1920x1080 -framerate 25 -draw_mouse 1 \
  -i "${DISPLAY}.0" -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -an "$RAW" \
  >/tmp/snapforge-demo-ffmpeg.log 2>&1 &
FPID=$!
sleep 0.4

DRIVE_STATUS=0
node "$ROOT/demo/drive.mjs" >/tmp/snapforge-demo-drive.log 2>&1 || DRIVE_STATUS=$?
echo "drive exit=$DRIVE_STATUS"
cat /tmp/snapforge-demo-drive.log || true

# Stop the camera first so Electron quitting does not leave a black tail.
kill -INT "$FPID" >/dev/null 2>&1 || true
sleep 1
kill "$FPID" >/dev/null 2>&1 || true
wait "$FPID" 2>/dev/null || true
kill "$EPID" >/dev/null 2>&1 || true
wait "$EPID" 2>/dev/null || true

ffmpeg -y -i "$RAW" -vf "scale=1920:1080" -c:v libx264 -crf 22 -preset medium -pix_fmt yuv420p \
  -movflags +faststart -an "$OUT" >/tmp/snapforge-demo-encode.log 2>&1
rm -f "$RAW"

if [[ "$NEED_XVFB" = 1 && -f /tmp/snapforge-demo-xvfb.pid ]]; then
  kill "$(cat /tmp/snapforge-demo-xvfb.pid)" >/dev/null 2>&1 || true
  rm -f /tmp/snapforge-demo-xvfb.pid
fi

BYTES="$(stat -c%s "$OUT" 2>/dev/null || echo 0)"
DUR="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$OUT" 2>/dev/null || echo 0)"
echo "demo video: $OUT ($BYTES bytes, ${DUR}s)"
ffprobe -v error -show_entries format=duration,size,bit_rate -of default=nw=1 "$OUT" || true
ls -lh "$OUT"

if ! python3 - <<PY
import sys
dur = float("${DUR}" or 0)
size = int("${BYTES}" or 0)
sys.exit(0 if dur >= 50 and dur <= 130 and size >= 80000 else 1)
PY
then
  echo "ERROR: demo MP4 duration/size out of range (duration=${DUR}s size=${BYTES})." >&2
  echo "----- electron log -----" >&2
  cat /tmp/snapforge-demo-electron.log >&2 || true
  echo "----- drive log -----" >&2
  cat /tmp/snapforge-demo-drive.log >&2 || true
  exit 1
fi

# Frame check: no >4s freeze, and the left demo cards keep ink (not blank white).
STILL_DIR="$(mktemp -d --suffix=-sf-demo-frames)"
ffmpeg -y -i "$OUT" -vf "fps=2,scale=320:180" -q:v 5 "$STILL_DIR/f%04d.jpg" >/tmp/snapforge-demo-still.log 2>&1 || true
python3 - "$STILL_DIR" "$OUT" <<'PY' || { echo "ERROR: demo frame check failed" >&2; exit 1; }
import hashlib, os, struct, subprocess, sys
d, src = sys.argv[1], sys.argv[2]
files = sorted(p for p in os.listdir(d) if p.endswith(".jpg"))
if len(files) < 20:
    raise SystemExit(f"too few sampled frames: {len(files)}")

def raw(path):
    p = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"],
        stdout=subprocess.PIPE, check=True,
    )
    return p.stdout

prev = None
run = 0
max_run = 0
max_at = 0
for i, name in enumerate(files):
    b = raw(os.path.join(d, name))
    h = hashlib.md5(b).hexdigest()
    if h == prev:
        run += 1
        if run > max_run:
            max_run, max_at = run, i
    else:
        run = 0
        prev = h
# 2 fps → 8 identical frames = 4s. Allow 7 (3.5s) for brief holds.
if max_run >= 8:
    raise SystemExit(f"still segment {max_run/2:.1f}s around frame {max_at} ({max_at/2:.1f}s)")

# Sample mid/late frames at native 1920x1080: left column must not be empty white.
dur = float(subprocess.run(
    ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", src],
    stdout=subprocess.PIPE, text=True, check=False,
).stdout or 0)
times = [max(2, int(dur * p)) for p in (0.18, 0.38, 0.55, 0.72, 0.88)]
ok = 0
for t in times:
    png = os.path.join(d, f"full-{t}.png")
    subprocess.run(
        ["ffmpeg", "-y", "-ss", str(t), "-i", src, "-frames:v", "1", png],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False,
    )
    if not os.path.isfile(png) or os.path.getsize(png) < 1000:
        continue
    rawb = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", png, "-vf", "crop=620:700:40:64", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"],
        stdout=subprocess.PIPE, check=False,
    ).stdout
    if len(rawb) < 1000:
        continue
    dark = 0
    for i in range(0, len(rawb), 12):  # subsample
        r, g, b = rawb[i], rawb[i+1], rawb[i+2]
        if r + g + b < 540:
            dark += 1
    # Text/formula/QR ink on the left column.
    if dark > 80:
        ok += 1
    print(f"t={t}s left-column dark-samples={dark}")
if ok < 3:
    raise SystemExit(f"left demo cards look blank in sampled frames (ok={ok})")
print(f"frame-check ok still_max={max_run/2:.1f}s left_ok={ok}/{len(times)}")
PY

# Full-frame black/white flashes: mean luma of a 160x90 gray downscale.
# Overlay dim is ~120; demo page ~220. A 1-frame paint of HTML white (~245) or
# native black (~3) is the bug. Ignore blackdetect on the light UI cards.
python3 - "$OUT" <<'PY' || { echo "ERROR: demo flash-frame check failed" >&2; exit 1; }
import subprocess, sys
src = sys.argv[1]
raw = subprocess.run(
    ["ffmpeg", "-v", "error", "-i", src, "-vf", "fps=25,scale=160:90,format=gray", "-f", "rawvideo", "pipe:1"],
    stdout=subprocess.PIPE, check=False,
).stdout
w, h = 160, 90
frame = w * h
if len(raw) < frame * 25:
    raise SystemExit("too few luma frames for flash scan")
flashes = []
prev = None
n = len(raw) // frame
mins, maxs = [], []
for i in range(n):
    sl = raw[i * frame:(i + 1) * frame]
    mean = sum(sl) / frame
    t = i / 25
    mins.append(mean)
    maxs.append(mean)
    # Skip t<0.08 in case the first captured frame is still settling.
    if t < 0.08:
        prev = mean
        continue
    if mean < 18:
        flashes.append(f"black t={t:.2f}s y={mean:.1f}")
    elif mean > 242:
        flashes.append(f"white t={t:.2f}s y={mean:.1f}")
    elif prev is not None and abs(mean - prev) > 90 and (mean < 28 or mean > 240):
        flashes.append(f"spike t={t:.2f}s y={prev:.1f}->{mean:.1f}")
    prev = mean
print(f"luma min={min(mins):.1f} max={max(maxs):.1f} frames={n}")
if flashes:
    raise SystemExit("luma flashes: " + "; ".join(flashes[:8]))
print(f"flash-check ok frames={n}")
PY
rm -rf "$STILL_DIR"

if [[ -n "$ART" ]]; then
  cp -f "$OUT" "$ART"
  echo "artifact: $ART"
  ls -lh "$ART" || true
fi

exit "$DRIVE_STATUS"
