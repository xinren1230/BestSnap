#!/usr/bin/env bash
# Download pix2text-mfr from the official Hugging Face repo and verify SHA-256.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${SNAPFORGE_LOCAL:-${XDG_DATA_HOME:-$HOME/.local/share}/SnapForge}/models/pix2text-mfr"
BUNDLED="$ROOT/models/pix2text-mfr"
REPO="https://huggingface.co/breezedeus/pix2text-mfr/resolve/main"

declare -A SHA=(
  [encoder_model.onnx]=bd8d5c322792e9ec45793af5569e9748f82a3d728a9e00213dbfc56c1486f37d
  [decoder_model.onnx]=fd0f92d7a012f3dae41e1ac79421aea0ea888b5a66cb3f9a004e424f82f3daed
  [tokenizer.json]=3e2ab757277d22639bec28c9d7972e352d3d1dba223051fa674002dc5ab64df3
)
declare -A SIZE=(
  [encoder_model.onnx]=87496990
  [decoder_model.onnx]=30114937
  [tokenizer.json]=39161
)

complete_dir() {
  local d="$1"
  [[ -d "$d" ]] || return 1
  local f
  for f in encoder_model.onnx decoder_model.onnx tokenizer.json; do
    [[ -f "$d/$f" ]] || return 1
    [[ "$(stat -c%s "$d/$f")" == "${SIZE[$f]}" ]] || return 1
  done
  return 0
}

if complete_dir "$BUNDLED"; then
  echo "Using bundled models in $BUNDLED"
  exit 0
fi
if complete_dir "$DEST"; then
  echo "Models already present in $DEST"
  exit 0
fi

mkdir -p "$DEST"
for f in encoder_model.onnx decoder_model.onnx tokenizer.json; do
  path="$DEST/$f"
  if [[ -f "$path" && "$(stat -c%s "$path")" == "${SIZE[$f]}" ]]; then
    echo "skip $f"
    continue
  fi
  echo "Downloading $f …"
  tmp="$path.part"
  curl -L --fail --retry 4 --retry-delay 4 -A "SnapForge-node/1.0" -o "$tmp" "$REPO/$f"
  got="$(sha256sum "$tmp" | awk '{print $1}')"
  if [[ "$got" != "${SHA[$f]}" ]]; then
    rm -f "$tmp"
    echo "SHA-256 mismatch for $f" >&2
    exit 1
  fi
  mv "$tmp" "$path"
done
echo "Models ready in $DEST"
