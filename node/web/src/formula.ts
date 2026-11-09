import { InferenceSession, Tensor, env } from "onnxruntime-web";
import { decodeLatex, gpt2ByteDecoder, pixelsFromRgba, vocabFromTokenizerJson } from "@core/formula-engine.ts";
import type { OrtSession } from "@core/ocr-engine.ts";
import { loadFormulaBuffers, wasmPaths, type ProgressFn } from "./models.ts";

let enc: InferenceSession | null = null;
let dec: InferenceSession | null = null;
let vocab: Map<number, string> = new Map();
let u2b: Map<number, number> = new Map();
let loading: Promise<void> | null = null;

async function createSession(buf: ArrayBuffer): Promise<InferenceSession> {
  env.wasm.wasmPaths = wasmPaths();
  env.wasm.numThreads = 1;
  env.wasm.simd = true;
  try {
    return await InferenceSession.create(buf, { executionProviders: ["webgpu", "wasm"] });
  } catch {
    return await InferenceSession.create(buf, { executionProviders: ["wasm"] });
  }
}

export async function ensureFormula(onProgress?: ProgressFn): Promise<void> {
  if (enc && dec) return;
  if (loading) return loading;
  loading = (async () => {
    const bufs = await loadFormulaBuffers(onProgress);
    enc = await createSession(bufs.encoder);
    dec = await createSession(bufs.decoder);
    vocab = vocabFromTokenizerJson(bufs.tokenizer);
    u2b = gpt2ByteDecoder();
  })();
  try {
    await loading;
  } catch (e) {
    loading = null;
    throw e;
  }
}

export async function recognizeLatexFromRgba(
  rgba: Uint8ClampedArray,
  w: number,
  h: number,
  onProgress?: ProgressFn,
): Promise<string> {
  await ensureFormula(onProgress);
  const pix = pixelsFromRgba(rgba, w, h);
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
