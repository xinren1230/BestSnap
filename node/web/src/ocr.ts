import { InferenceSession, Tensor, env } from "onnxruntime-web";
import { readOnnxCharacterDict, rgbaToBgra, runOcr, type OrtSession } from "@core/ocr-engine.ts";
import { loadOcrBuffers, wasmPaths, type ProgressFn } from "./models.ts";

let det: InferenceSession | null = null;
let rec: InferenceSession | null = null;
let chars: string[] = [];
let loading: Promise<void> | null = null;

async function configureOrt(): Promise<void> {
  env.wasm.wasmPaths = wasmPaths();
  env.wasm.numThreads = 1;
  env.wasm.simd = true;
}

async function createSession(buf: ArrayBuffer): Promise<InferenceSession> {
  try {
    return await InferenceSession.create(buf, { executionProviders: ["webgpu", "wasm"] });
  } catch {
    return await InferenceSession.create(buf, { executionProviders: ["wasm"] });
  }
}

export async function ensureOcr(onProgress?: ProgressFn): Promise<void> {
  if (rec && det) return;
  if (loading) return loading;
  loading = (async () => {
    await configureOrt();
    const { det: detBuf, rec: recBuf } = await loadOcrBuffers(onProgress);
    det = await createSession(detBuf);
    rec = await createSession(recBuf);
    chars = readOnnxCharacterDict(new Uint8Array(recBuf));
  })();
  try {
    await loading;
  } catch (e) {
    loading = null;
    throw e;
  }
}

function makeTensor(data: Float32Array, dims: number[]): Tensor {
  return new Tensor("float32", data, dims);
}

export async function recognizeFromRgba(rgba: Uint8ClampedArray, w: number, h: number, onProgress?: ProgressFn): Promise<string> {
  await ensureOcr(onProgress);
  const im = rgbaToBgra(rgba, w, h);
  const { text } = await runOcr(im, det as unknown as OrtSession, rec as unknown as OrtSession, chars, makeTensor);
  return text;
}
