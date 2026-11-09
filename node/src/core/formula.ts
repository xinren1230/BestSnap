/**
 * Offline image → LaTeX with pix2text-mfr (breezedeus/pix2text-mfr, MIT).
 * Port of BestSnap.Ocr.Pix2TextFormulaRecognizer + FormulaService download/hash.
 */
import crypto from "node:crypto";
import fs from "node:fs";
import http from "node:https";
import os from "node:os";
import path from "node:path";
import { InferenceSession, Tensor } from "onnxruntime-node";
import sharp from "sharp";
import { cacheFormulaDir, FORMULA_FILES, isComplete, MODEL_REPO, MODEL_REV, resolveFormulaDir } from "./bundled-models.js";
import { decodeLatex, FORMULA_SIZE, gpt2ByteDecoder, vocabFromTokenizerJson } from "./formula-engine.js";
import { tidyLatex } from "./formula-latex.js";
import type { OrtSession } from "./ocr-engine.js";
import { ensureDir, localAppDataDir } from "./paths.js";

const SIZE = FORMULA_SIZE;

let enc: InferenceSession | null = null;
let dec: InferenceSession | null = null;
let loadedDir = "";
let vocab: Map<number, string> = new Map();
let u2b: Map<number, number> = new Map();

async function load(dir: string): Promise<void> {
  if (loadedDir === dir && enc && dec) return;
  await enc?.release();
  await dec?.release();
  enc = dec = null;
  const threads = Math.max(1, Math.min(4, Math.floor(os.cpus().length / 2) || 1));
  const opts: InferenceSession.SessionOptions = { graphOptimizationLevel: "all", intraOpNumThreads: threads };
  enc = await InferenceSession.create(path.join(dir, "encoder_model.onnx"), opts);
  dec = await InferenceSession.create(path.join(dir, "decoder_model.onnx"), opts);
  vocab = vocabFromTokenizerJson(fs.readFileSync(path.join(dir, "tokenizer.json"), "utf8"));
  u2b = gpt2ByteDecoder();
  loadedDir = dir;
}

async function pixels(png: Buffer): Promise<Float32Array> {
  const { data } = await sharp(png)
    .resize(SIZE, SIZE, { fit: "fill", kernel: sharp.kernel.cubic })
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });
  const f = new Float32Array(3 * SIZE * SIZE);
  const plane = SIZE * SIZE;
  for (let i = 0; i < plane; i++) {
    const a = data[i * 4 + 3] / 255;
    for (let c = 0; c < 3; c++) f[c * plane + i] = ((data[i * 4 + c] * a + 255 * (1 - a)) / 255 - 0.5) / 0.5;
  }
  return f;
}

function downloadFile(url: string, destPart: string, onBytes: (n: number) => void): Promise<string> {
  return new Promise((resolve, reject) => {
    const sha = crypto.createHash("sha256");
    const follow = (u: string, hops: number) => {
      if (hops > 8) return reject(new Error("too many redirects"));
      http
        .get(u, { headers: { "User-Agent": "BestSnap-node/1.0" } }, (res) => {
          if (res.statusCode && res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
            res.resume();
            follow(new URL(res.headers.location, u).href, hops + 1);
            return;
          }
          if (res.statusCode !== 200) {
            res.resume();
            reject(new Error("HTTP " + res.statusCode + " for " + u));
            return;
          }
          const out = fs.createWriteStream(destPart);
          res.on("data", (chunk: Buffer) => {
            sha.update(chunk);
            onBytes(chunk.length);
          });
          res.pipe(out);
          out.on("finish", () => {
            out.close(() => resolve(sha.digest("hex")));
          });
          out.on("error", reject);
        })
        .on("error", reject);
    };
    follow(url, 0);
  });
}

export async function ensureModels(onProgress?: (p: number) => void): Promise<string> {
  const dir = resolveFormulaDir(undefined, localAppDataDir());
  if (isComplete(dir)) {
    onProgress?.(1);
    return dir;
  }
  const dest = cacheFormulaDir(localAppDataDir());
  ensureDir(dest);
  const total = FORMULA_FILES.reduce((s, f) => s + f.size, 0);
  let done = 0;
  for (const f of FORMULA_FILES) {
    const p = path.join(dest, f.name);
    if (fs.existsSync(p) && fs.statSync(p).size === f.size) {
      done += f.size;
      onProgress?.(done / total);
      continue;
    }
    const url = `${MODEL_REPO}/resolve/${MODEL_REV}/${f.name}`;
    const part = p + ".part";
    const hex = await downloadFile(url, part, (n) => {
      done += n;
      onProgress?.(Math.min(1, done / total));
    });
    if (hex !== f.sha256) {
      fs.rmSync(part, { force: true });
      throw new Error("模型文件校验失败: " + f.name);
    }
    fs.renameSync(part, p);
  }
  return dest;
}

export async function recognizeLatex(png: Buffer, onDownload?: (p: number) => void): Promise<string> {
  const dir = await ensureModels(onDownload);
  const pix = await pixels(png);
  await load(dir);
  return decodeLatex(
    enc as unknown as OrtSession,
    dec as unknown as OrtSession,
    vocab,
    u2b,
    pix,
    (data, dims) => new Tensor("float32", data, dims),
    (data, dims) => new Tensor("int64", data, dims),
  );
}

export { tidyLatex };
