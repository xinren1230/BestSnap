/**
 * pix2text-mfr encoder/decoder loop. No filesystem / sharp / specific ORT package.
 */
import { tidyLatex } from "./formula-latex.js";
import type { OrtSession, OrtTensor } from "./ocr-engine.js";

export const FORMULA_SIZE = 384;
const START = 2;
const EOS = 2;
const MAX_TOKENS = 512;

export type MakeFloatTensor = (data: Float32Array, dims: number[]) => unknown;
export type MakeInt64Tensor = (data: BigInt64Array, dims: number[]) => unknown;

export function gpt2ByteDecoder(): Map<number, number> {
  const bs: number[] = [];
  for (let b = 33; b < 127; b++) bs.push(b);
  for (let b = 161; b < 173; b++) bs.push(b);
  for (let b = 174; b < 256; b++) bs.push(b);
  const cs = [...bs];
  let n = 0;
  for (let b = 0; b < 256; b++) {
    if (!bs.includes(b)) {
      bs.push(b);
      cs.push(256 + n);
      n++;
    }
  }
  const map = new Map<number, number>();
  for (let i = 0; i < bs.length; i++) map.set(cs[i], bs[i]);
  return map;
}

export function vocabFromTokenizerJson(json: string): Map<number, string> {
  const tok = JSON.parse(json) as { model: { vocab: Record<string, number> } };
  const vocab = new Map<number, string>();
  for (const [k, v] of Object.entries(tok.model.vocab)) vocab.set(v, k);
  return vocab;
}

/** Resize RGBA to 384×384 NCHW float in [-1, 1]. */
export function pixelsFromRgba(data: Uint8Array | Uint8ClampedArray, srcW: number, srcH: number): Float32Array {
  const SIZE = FORMULA_SIZE;
  const f = new Float32Array(3 * SIZE * SIZE);
  const plane = SIZE * SIZE;
  for (let y = 0; y < SIZE; y++) {
    const sy = ((y + 0.5) * srcH) / SIZE - 0.5;
    const y1 = Math.max(0, Math.min(srcH - 1, Math.floor(sy)));
    const y2 = Math.max(0, Math.min(srcH - 1, y1 + 1));
    const ty = sy - Math.floor(sy);
    for (let x = 0; x < SIZE; x++) {
      const sx = ((x + 0.5) * srcW) / SIZE - 0.5;
      const x1 = Math.max(0, Math.min(srcW - 1, Math.floor(sx)));
      const x2 = Math.max(0, Math.min(srcW - 1, x1 + 1));
      const tx = sx - Math.floor(sx);
      const sample = (xx: number, yy: number, c: number) => data[(yy * srcW + xx) * 4 + c];
      const a1 = sample(x1, y1, 3) / 255;
      const a2 = sample(x2, y1, 3) / 255;
      const a3 = sample(x1, y2, 3) / 255;
      const a4 = sample(x2, y2, 3) / 255;
      const i = y * SIZE + x;
      for (let c = 0; c < 3; c++) {
        const v1 = sample(x1, y1, c) * a1 + 255 * (1 - a1);
        const v2 = sample(x2, y1, c) * a2 + 255 * (1 - a2);
        const v3 = sample(x1, y2, c) * a3 + 255 * (1 - a3);
        const v4 = sample(x2, y2, c) * a4 + 255 * (1 - a4);
        const v = (v1 * (1 - tx) + v2 * tx) * (1 - ty) + (v3 * (1 - tx) + v4 * tx) * ty;
        f[c * plane + i] = (v / 255 - 0.5) / 0.5;
      }
    }
  }
  return f;
}

export async function decodeLatex(
  enc: OrtSession,
  dec: OrtSession,
  vocab: Map<number, string>,
  u2b: Map<number, number>,
  pix: Float32Array,
  makeFloat: MakeFloatTensor,
  makeInt64: MakeInt64Tensor,
): Promise<string> {
  const SIZE = FORMULA_SIZE;
  const encName = enc.inputNames.includes("pixel_values") ? "pixel_values" : enc.inputNames[0];
  const encOut = await enc.run({ [encName]: makeFloat(pix, [1, 3, SIZE, SIZE]) });
  const hiddenTensor = encOut[enc.outputNames[0]] as OrtTensor;
  const ids: number[] = [START];
  for (let step = 0; step < MAX_TOKENS; step++) {
    const idT = makeInt64(BigInt64Array.from(ids.map((x) => BigInt(x))), [1, ids.length]);
    const idName = dec.inputNames.find((n) => n.includes("input_id")) ?? dec.inputNames[0];
    const hidName = dec.inputNames.find((n) => n.includes("hidden") || n.includes("encoder")) ?? dec.inputNames[1];
    const o = await dec.run({ [idName]: idT, [hidName]: hiddenTensor });
    const lg = o[dec.outputNames[0]];
    const V = lg.dims[2];
    const last = ids.length - 1;
    const data = lg.data as Float32Array;
    let arg = 0,
      best = -Infinity;
    const offset = last * V;
    for (let v = 0; v < V; v++) {
      const x = data[offset + v];
      if (x > best) {
        best = x;
        arg = v;
      }
    }
    if (arg === EOS) break;
    ids.push(arg);
  }
  let sb = "";
  for (const id of ids.slice(1)) {
    const tok = vocab.get(id);
    if (tok && !(tok.startsWith("<") && tok.endsWith(">"))) sb += tok;
  }
  const bytes = Uint8Array.from([...sb].map((c) => u2b.get(c.codePointAt(0) ?? 0) ?? 0x3f));
  return tidyLatex(new TextDecoder("utf-8").decode(bytes));
}
