#!/usr/bin/env node
/** Copy OCR ONNX, pix2text-mfr, and onnxruntime-web WASM into public/. Models stay out of git. */
import { createHash } from "node:crypto";
import { createWriteStream, existsSync, mkdirSync, copyFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { pipeline } from "node:stream/promises";
import { Readable } from "node:stream";

const here = dirname(fileURLToPath(import.meta.url));
const webRoot = join(here, "..");
const nodeRoot = join(webRoot, "..");
const publicDir = join(webRoot, "public");
const modelsDir = join(publicDir, "models");
const ocrDest = join(modelsDir, "ocr");
const formulaDest = join(modelsDir, "pix2text-mfr");
const ortDest = join(publicDir, "ort");

const FORMULA = [
  { name: "encoder_model.onnx", sha256: "bd8d5c322792e9ec45793af5569e9748f82a3d728a9e00213dbfc56c1486f37d", size: 87496990 },
  { name: "decoder_model.onnx", sha256: "fd0f92d7a012f3dae41e1ac79421aea0ea888b5a66cb3f9a004e424f82f3daed", size: 30114937 },
  { name: "tokenizer.json", sha256: "3e2ab757277d22639bec28c9d7972e352d3d1dba223051fa674002dc5ab64df3", size: 39161 },
];
const HF = "https://huggingface.co/breezedeus/pix2text-mfr/resolve/main";

function ensure(d) {
  mkdirSync(d, { recursive: true });
}

function copyIfPresent(src, dest) {
  if (!existsSync(src)) return false;
  ensure(dirname(dest));
  copyFileSync(src, dest);
  return true;
}

async function download(url, dest, expectedSha, expectedSize) {
  if (existsSync(dest) && statSync(dest).size === expectedSize) return;
  console.log("download", url);
  const res = await fetch(url, { headers: { "User-Agent": "BestSnap-web/1.0" }, redirect: "follow" });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${url}`);
  ensure(dirname(dest));
  const tmp = dest + ".part";
  const sha = createHash("sha256");
  const file = createWriteStream(tmp);
  const nodeStream = Readable.fromWeb(res.body);
  nodeStream.on("data", (c) => sha.update(c));
  await pipeline(nodeStream, file);
  const hex = sha.digest("hex");
  if (hex !== expectedSha) {
    throw new Error(`SHA-256 mismatch for ${dest}: ${hex}`);
  }
  copyFileSync(tmp, dest);
}

ensure(ocrDest);
ensure(formulaDest);
ensure(ortDest);

for (const name of ["ch_PP-OCRv4_det_infer.onnx", "ch_PP-OCRv4_rec_infer.onnx"]) {
  const src = join(nodeRoot, "models", "ocr", name);
  if (!copyIfPresent(src, join(ocrDest, name))) {
    throw new Error("PP-OCRv4 ONNX missing at " + src + " (checked in with the Node port)");
  }
}

const formulaSrcDirs = [
  join(nodeRoot, "models", "pix2text-mfr"),
  join(process.env.HOME || "", ".local/share/BestSnap/models/pix2text-mfr"),
  process.env.BESTSNAP_FORMULA_DIR || "",
].filter(Boolean);

let formulaReady = FORMULA.every((f) => existsSync(join(formulaDest, f.name)) && statSync(join(formulaDest, f.name)).size === f.size);
if (!formulaReady) {
  for (const dir of formulaSrcDirs) {
    if (FORMULA.every((f) => existsSync(join(dir, f.name)))) {
      for (const f of FORMULA) copyIfPresent(join(dir, f.name), join(formulaDest, f.name));
      formulaReady = true;
      break;
    }
  }
}
if (!formulaReady) {
  for (const f of FORMULA) {
    await download(`${HF}/${f.name}`, join(formulaDest, f.name), f.sha256, f.size);
  }
}

const ortPkg = join(webRoot, "node_modules", "onnxruntime-web", "dist");
if (!existsSync(ortPkg)) {
  console.warn("onnxruntime-web not installed yet; skip wasm copy (npm install first)");
} else {
  for (const f of readdirSync(ortPkg)) {
    if (/\.(wasm|mjs|js)$/.test(f) && /wasm|ort-wasm|jsep/.test(f)) {
      copyFileSync(join(ortPkg, f), join(ortDest, f));
    }
  }
}

const logoSrc = join(nodeRoot, "assets", "logo_24.png");
if (existsSync(logoSrc)) copyIfPresent(logoSrc, join(publicDir, "logo_24.png"));
console.log("models ready in", modelsDir);
