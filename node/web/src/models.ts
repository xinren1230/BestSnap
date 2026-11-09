import { FORMULA_FILES, OCR_FILES } from "@core/model-catalog.ts";

export type ProgressFn = (p: { file: string; loaded: number; total: number; fraction: number }) => void;

/** LEGACY MIGRATION: Cache API bucket name from the SnapForge web app. Renaming would re-download ~120MB. */
const CACHE = "snapforge-models-v1";

function base(): string {
  return import.meta.env.BASE_URL || "/";
}

export function modelUrl(kind: "ocr" | "formula", name: string): string {
  const folder = kind === "ocr" ? "ocr" : "pix2text-mfr";
  return `${base()}models/${folder}/${name}`;
}

async function cacheGet(url: string): Promise<ArrayBuffer | null> {
  try {
    const c = await caches.open(CACHE);
    const hit = await c.match(url);
    if (!hit) return null;
    return await hit.arrayBuffer();
  } catch {
    return null;
  }
}

async function cachePut(url: string, buf: ArrayBuffer, type: string): Promise<void> {
  try {
    const c = await caches.open(CACHE);
    await c.put(url, new Response(buf, { headers: { "content-type": type } }));
  } catch {
    /* private mode */
  }
}

async function fetchProgress(url: string, file: string, expected: number, onProgress?: ProgressFn): Promise<ArrayBuffer> {
  const cached = await cacheGet(url);
  if (cached && (!expected || cached.byteLength === expected || cached.byteLength > 1000)) {
    onProgress?.({ file, loaded: cached.byteLength, total: cached.byteLength, fraction: 1 });
    return cached;
  }
  const res = await fetch(url);
  if (!res.ok) throw new Error(`模型下载失败 HTTP ${res.status}: ${file}`);
  const total = Number(res.headers.get("content-length")) || expected || 0;
  if (!res.body) {
    const buf = await res.arrayBuffer();
    await cachePut(url, buf, res.headers.get("content-type") || "application/octet-stream");
    return buf;
  }
  const reader = res.body.getReader();
  const chunks: Uint8Array[] = [];
  let loaded = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    chunks.push(value);
    loaded += value.byteLength;
    onProgress?.({ file, loaded, total: total || loaded, fraction: total ? loaded / total : 0 });
  }
  const buf = new Uint8Array(loaded);
  let o = 0;
  for (const c of chunks) {
    buf.set(c, o);
    o += c.byteLength;
  }
  await cachePut(url, buf.buffer, res.headers.get("content-type") || "application/octet-stream");
  onProgress?.({ file, loaded, total: loaded, fraction: 1 });
  return buf.buffer;
}

export async function loadOcrBuffers(onProgress?: ProgressFn): Promise<{ det: ArrayBuffer; rec: ArrayBuffer }> {
  const [detMeta, recMeta] = OCR_FILES;
  const det = await fetchProgress(modelUrl("ocr", detMeta.name), detMeta.name, detMeta.size, onProgress);
  const rec = await fetchProgress(modelUrl("ocr", recMeta.name), recMeta.name, recMeta.size, onProgress);
  return { det, rec };
}

export async function loadFormulaBuffers(onProgress?: ProgressFn): Promise<{
  encoder: ArrayBuffer;
  decoder: ArrayBuffer;
  tokenizer: string;
}> {
  const out: Record<string, ArrayBuffer> = {};
  for (const f of FORMULA_FILES) {
    out[f.name] = await fetchProgress(modelUrl("formula", f.name), f.name, f.size, onProgress);
  }
  return {
    encoder: out["encoder_model.onnx"],
    decoder: out["decoder_model.onnx"],
    tokenizer: new TextDecoder().decode(out["tokenizer.json"]),
  };
}

export function wasmPaths(): string {
  return `${base()}ort/`;
}
