import fs from "node:fs";
import path from "node:path";
import { appRoot, localAppDataDir } from "./paths.js";
export {
  FORMULA_FILES,
  FORMULA_FOLDER,
  MODEL_REPO,
  MODEL_REV,
  OCR_FILES,
  OCR_FOLDER,
} from "./model-catalog.js";
import { FORMULA_FILES, FORMULA_FOLDER } from "./model-catalog.js";

export function bundledFormulaDir(root = appRoot()): string {
  return path.join(root, "models", FORMULA_FOLDER);
}

export function cacheFormulaDir(local = localAppDataDir()): string {
  return path.join(local, "models", FORMULA_FOLDER);
}

export function isComplete(dir: string): boolean {
  if (!dir || !fs.existsSync(dir)) return false;
  for (const f of FORMULA_FILES) {
    const p = path.join(dir, f.name);
    if (!fs.existsSync(p) || fs.statSync(p).size !== f.size) return false;
  }
  return true;
}

export function resolveFormulaDir(root = appRoot(), local = localAppDataDir()): string {
  const bundled = bundledFormulaDir(root);
  if (isComplete(bundled)) return bundled;
  return cacheFormulaDir(local);
}

export function ocrModelDir(root = appRoot()): string {
  const candidates = [
    path.join(root, "models", "ocr"),
    path.join(root, "..", "src", "BestSnap.Ocr", "models"),
    process.env.BESTSNAP_OCR_MODELS || process.env.SNAPFORGE_OCR_MODELS || "",
  ].filter(Boolean);
  for (const d of candidates) {
    if (fs.existsSync(path.join(d, "ch_PP-OCRv4_det_infer.onnx")) && fs.existsSync(path.join(d, "ch_PP-OCRv4_rec_infer.onnx"))) return d;
  }
  return candidates[0];
}

export function ocrAvailable(root = appRoot()): boolean {
  const d = ocrModelDir(root);
  return fs.existsSync(path.join(d, "ch_PP-OCRv4_det_infer.onnx")) && fs.existsSync(path.join(d, "ch_PP-OCRv4_rec_infer.onnx"));
}
