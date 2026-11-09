/**
 * Built-in offline Chinese + English OCR: PaddleOCR PP-OCRv4 mobile det + rec
 * (Apache-2.0, ONNX exports from RapidOCR), run with onnxruntime-node.
 * Port of SnapForge.Ocr.PaddleOcr.
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { InferenceSession, Tensor } from "onnxruntime-node";
import sharp from "sharp";
import { ocrModelDir } from "./bundled-models.js";
import {
  readOnnxCharacterDict,
  rgbaToBgra,
  runOcr,
  type Img,
  type OcrWord,
  type OrtSession,
} from "./ocr-engine.js";

let det: InferenceSession | null = null;
let rec: InferenceSession | null = null;
let chars: string[] = [];
let loadPromise: Promise<void> | null = null;

export type { OcrWord };

async function load(): Promise<void> {
  if (rec) return;
  if (loadPromise) return loadPromise;
  loadPromise = (async () => {
    const dir = ocrModelDir();
    const detPath = path.join(dir, "ch_PP-OCRv4_det_infer.onnx");
    const recPath = path.join(dir, "ch_PP-OCRv4_rec_infer.onnx");
    if (!fs.existsSync(detPath) || !fs.existsSync(recPath)) {
      throw new Error("PP-OCRv4 models not found in " + dir + ". Run scripts/setup.sh");
    }
    const threads = Math.max(1, Math.min(4, Math.floor(os.cpus().length / 2) || 1));
    const opts: InferenceSession.SessionOptions = {
      graphOptimizationLevel: "all",
      intraOpNumThreads: threads,
    };
    det = await InferenceSession.create(detPath, opts);
    rec = await InferenceSession.create(recPath, opts);
    chars = readOnnxCharacterDict(fs.readFileSync(recPath));
  })();
  return loadPromise;
}

export async function decodeImage(png: Buffer): Promise<Img> {
  const { data, info } = await sharp(png).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  return rgbaToBgra(data, info.width, info.height);
}

function makeTensor(data: Float32Array, dims: number[]): Tensor {
  return new Tensor("float32", data, dims);
}

export async function recognizeWords(png: Buffer): Promise<OcrWord[]> {
  await load();
  const im = await decodeImage(png);
  const { words } = await runOcr(im, det as unknown as OrtSession, rec as unknown as OrtSession, chars, makeTensor);
  return words;
}

export async function recognize(png: Buffer): Promise<string> {
  await load();
  const im = await decodeImage(png);
  const { text } = await runOcr(im, det as unknown as OrtSession, rec as unknown as OrtSession, chars, makeTensor);
  return text;
}

export function ocrReady(): boolean {
  try {
    const dir = ocrModelDir();
    return fs.existsSync(path.join(dir, "ch_PP-OCRv4_det_infer.onnx"));
  } catch {
    return false;
  }
}
