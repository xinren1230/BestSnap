#!/usr/bin/env bash
# Install apt deps, npm packages, OCR models (copied from this repo), pix2text-mfr, and test fixtures.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$ROOT/.." && pwd)"

echo "==> apt packages (sudo if needed)"
PKGS=(ffmpeg xdotool x11-utils xvfb curl ca-certificates libgtk-3-0 libnotify4 libnss3 libxss1 libxtst6 xdg-utils libatspi2.0-0 libsecret-1-0)
if command -v sudo >/dev/null 2>&1; then
  sudo apt-get update -y
  sudo apt-get install -y "${PKGS[@]}"
else
  apt-get update -y
  apt-get install -y "${PKGS[@]}"
fi

echo "==> Node $(node -v), npm $(npm -v)"
cd "$ROOT"

echo "==> copy PP-OCRv4 ONNX if missing"
mkdir -p "$ROOT/models/ocr"
if [[ ! -f "$ROOT/models/ocr/ch_PP-OCRv4_det_infer.onnx" && -f "$REPO/src/SnapForge.Ocr/models/ch_PP-OCRv4_det_infer.onnx" ]]; then
  cp "$REPO/src/SnapForge.Ocr/models/"*.onnx "$ROOT/models/ocr/"
fi

echo "==> npm ci (or npm install)"
if [[ -f package-lock.json ]]; then npm ci; else npm install; fi

echo "==> pix2text-mfr (Hugging Face, SHA-256 checked)"
bash "$ROOT/scripts/download-models.sh"

echo "==> demo / test fixtures"
node "$ROOT/scripts/generate-fixtures.mjs"

echo "==> TypeScript build"
npx tsc -p tsconfig.json
node "$ROOT/scripts/copy-static.mjs"

echo "Setup done. CLI: npx tsx src/cli/index.ts --help"
echo "UI:    npm start"
echo "Tests: npm test"
