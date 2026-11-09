/**
 * PP-OCRv4 detect + recognize loop. No filesystem / sharp / specific ORT package.
 * Used by Node (onnxruntime-node) and the browser build (onnxruntime-web).
 */
import { formatOcrLayout, type OcrSpan } from "./ocr-layout.js";
import { isCjk, postProcess, restoreLatinSpaces } from "./ocr-text.js";

const MAX_SIDE = 1280;
const MIN_LONG = 640;
const PAD_PX = 16;
const BIN_THRESH = 0.25;
const BOX_THRESH = 0.42;
const UNCLIP = 1.8;

export interface Img {
  w: number;
  h: number;
  bgra: Uint8Array;
}

export interface OrtTensor {
  data: ArrayBufferView;
  dims: readonly number[];
}

export interface OrtSession {
  inputNames: readonly string[];
  outputNames: readonly string[];
  run(feeds: Record<string, unknown>): Promise<Record<string, OrtTensor>>;
}

export type MakeTensor = (data: Float32Array, dims: number[]) => unknown;

export interface OcrWord {
  text: string;
  line: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

interface Box {
  x0: number;
  y0: number;
  x1: number;
  y1: number;
  text?: string;
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, v));
}

/** Read PP-OCRv4 rec `character` metadata from an ONNX protobuf buffer. */
export function readOnnxCharacterDict(buf: Uint8Array): string[] {
  const key = new TextEncoder().encode("character");
  const map: Record<string, string> = {};
  let from = 0;
  const bufA = buf;
  while (from < bufA.length) {
    let i = -1;
    for (let p = from; p <= bufA.length - key.length; p++) {
      let ok = true;
      for (let k = 0; k < key.length; k++) {
        if (bufA[p + k] !== key[k]) {
          ok = false;
          break;
        }
      }
      if (ok) {
        i = p;
        break;
      }
    }
    if (i < 0) break;
    from = i + 1;
    if (i < 2 || bufA[i - 2] !== 0x0a || bufA[i - 1] !== key.length || bufA[i + key.length] !== 0x12) continue;
    let p = i + key.length + 1;
    let len = 0,
      shift = 0;
    while (p < bufA.length) {
      const b = bufA[p++];
      len |= (b & 0x7f) << shift;
      if ((b & 0x80) === 0) break;
      shift += 7;
    }
    if (p + len > bufA.length) continue;
    map.character = new TextDecoder("utf-8").decode(bufA.subarray(p, p + len));
    break;
  }
  const raw = map.character?.replace(/\r/g, "") ?? "";
  if (!raw) throw new Error("PP-OCRv4 rec model is missing character metadata");
  return ["", ...raw.split("\n"), " "];
}

export function rgbaToBgra(rgba: Uint8ClampedArray | Uint8Array, w: number, h: number): Img {
  const bgra = new Uint8Array(w * h * 4);
  for (let i = 0, j = 0; i < rgba.length; i += 4, j += 4) {
    let r = rgba[i],
      g = rgba[i + 1],
      b = rgba[i + 2],
      a = rgba[i + 3];
    if (a !== 255) {
      r = Math.round((r * a + 255 * (255 - a)) / 255);
      g = Math.round((g * a + 255 * (255 - a)) / 255);
      b = Math.round((b * a + 255 * (255 - a)) / 255);
      a = 255;
    }
    bgra[j] = b;
    bgra[j + 1] = g;
    bgra[j + 2] = r;
    bgra[j + 3] = a;
  }
  return { w, h, bgra };
}

function lumaStats(im: Img): { mean: number; min: number; max: number } {
  let sum = 0,
    lo = 255,
    hi = 0;
  const n = im.w * im.h;
  const p = im.bgra;
  for (let i = 0; i < p.length; i += 4) {
    const y = (29 * p[i] + 150 * p[i + 1] + 77 * p[i + 2]) >> 8;
    sum += y;
    if (y < lo) lo = y;
    if (y > hi) hi = y;
  }
  return { mean: n === 0 ? 0 : sum / n, min: lo, max: hi };
}

function pad(im: Img, p: number): Img {
  if (p <= 0) return im;
  const nw = im.w + 2 * p,
    nh = im.h + 2 * p;
  const d = new Uint8Array(nw * nh * 4);
  let b = 0,
    g = 0,
    r = 0;
  const n = Math.max(1, im.w * im.h);
  const s = im.bgra;
  for (let i = 0; i < s.length; i += 4) {
    b += s[i];
    g += s[i + 1];
    r += s[i + 2];
  }
  const mb = (b / n) | 0,
    mg = (g / n) | 0,
    mr = (r / n) | 0;
  for (let i = 0; i < d.length; i += 4) {
    d[i] = mb;
    d[i + 1] = mg;
    d[i + 2] = mr;
    d[i + 3] = 255;
  }
  for (let y = 0; y < im.h; y++) {
    d.set(s.subarray(y * im.w * 4, (y + 1) * im.w * 4), ((y + p) * nw + p) * 4);
  }
  return { w: nw, h: nh, bgra: d };
}

function bilinear(im: Img, sx: number, sy: number): [number, number, number] {
  const x1 = Math.floor(sx),
    y1 = Math.floor(sy);
  const tx = sx - x1,
    ty = sy - y1;
  const xa = clamp(x1, 0, im.w - 1),
    xb = clamp(x1 + 1, 0, im.w - 1);
  const ya = clamp(y1, 0, im.h - 1),
    yb = clamp(y1 + 1, 0, im.h - 1);
  const sample = (c: number) =>
    (im.bgra[(ya * im.w + xa) * 4 + c] * (1 - tx) + im.bgra[(ya * im.w + xb) * 4 + c] * tx) * (1 - ty) +
    (im.bgra[(yb * im.w + xa) * 4 + c] * (1 - tx) + im.bgra[(yb * im.w + xb) * 4 + c] * tx) * ty;
  return [sample(0), sample(1), sample(2)];
}

function resize(
  im: Img,
  x0: number,
  y0: number,
  sw: number,
  sh: number,
  w: number,
  h: number,
  put: (x: number, y: number, b: number, g: number, r: number) => void,
): void {
  const fx = sw / w,
    fy = sh / h;
  for (let y = 0; y < h; y++) {
    const sy = y0 + (y + 0.5) * fy - 0.5;
    for (let x = 0; x < w; x++) {
      const sx = x0 + (x + 0.5) * fx - 0.5;
      const [b, g, r] = bilinear(im, sx, sy);
      put(x, y, b, g, r);
    }
  }
}

function detScale(w: number, h: number): number {
  const lon = Math.max(w, h),
    shor = Math.min(w, h);
  let s = 1;
  if (lon > MAX_SIDE) s = MAX_SIDE / lon;
  else {
    if (h < 400) s = Math.min(2.0, MAX_SIDE / lon);
    else if (lon < MIN_LONG) s = Math.min(2.0, MIN_LONG / lon);
  }
  if (shor * s < 48) s = Math.max(s, 48.0 / Math.max(1, shor));
  if (lon * s > MAX_SIDE) s = MAX_SIDE / lon;
  return s;
}

async function detectOnce(
  im: Img,
  invert: boolean,
  lumaMin: number,
  lumaMax: number,
  det: OrtSession,
  makeTensor: MakeTensor,
): Promise<Box[]> {
  const s = detScale(im.w, im.h);
  const w = Math.max(32, Math.round((im.w * s) / 32) * 32);
  const h = Math.max(32, Math.round((im.h * s) / 32) * 32);
  const t = new Float32Array(3 * h * w);
  const mean = [0.485, 0.456, 0.406],
    std = [0.229, 0.224, 0.225];
  const stretch = lumaMax > lumaMin && lumaMax - lumaMin < 55;
  const plane = h * w;
  resize(im, 0, 0, im.w, im.h, w, h, (x, y, b, g, r) => {
    if (invert) {
      b = 255 - b;
      g = 255 - g;
      r = 255 - r;
    }
    if (stretch) {
      const srcL = 0.114 * b + 0.587 * g + 0.299 * r;
      const lo = invert ? 255 - lumaMax : lumaMin;
      const hi = invert ? 255 - lumaMin : lumaMax;
      const L = ((srcL - lo) / Math.max(1, hi - lo)) * 255;
      b = g = r = clamp(L, 0, 255);
    }
    const i = y * w + x;
    t[i] = (b / 255 - mean[0]) / std[0];
    t[plane + i] = (g / 255 - mean[1]) / std[1];
    t[2 * plane + i] = (r / 255 - mean[2]) / std[2];
  });
  const inputName = det.inputNames[0];
  const out = await det.run({ [inputName]: makeTensor(t, [1, 3, h, w]) });
  const p = out[det.outputNames[0]].data as Float32Array;
  const lab = new Int32Array(w * h);
  const boxes: Box[] = [];
  const sx = im.w / w,
    sy = im.h / h;
  const stack: number[] = [];
  let id = 0;
  for (let i = 0; i < p.length; i++) {
    if (p[i] <= BIN_THRESH || lab[i] !== 0) continue;
    id++;
    let x0 = w,
      y0 = h,
      x1 = 0,
      y1 = 0,
      n = 0,
      sum = 0;
    stack.push(i);
    lab[i] = id;
    while (stack.length) {
      const k = stack.pop()!;
      const kx = k % w,
        ky = (k / w) | 0;
      n++;
      sum += p[k];
      if (kx < x0) x0 = kx;
      if (kx > x1) x1 = kx;
      if (ky < y0) y0 = ky;
      if (ky > y1) y1 = ky;
      const visit = (q: number) => {
        if (lab[q] === 0 && p[q] > BIN_THRESH) {
          lab[q] = id;
          stack.push(q);
        }
      };
      if (kx > 0) visit(k - 1);
      if (kx < w - 1) visit(k + 1);
      if (ky > 0) visit(k - w);
      if (ky < h - 1) visit(k + w);
    }
    const bw = x1 - x0 + 1,
      bh = y1 - y0 + 1;
    if (Math.min(bw, bh) < 2 || Math.max(bw, bh) < 4 || sum / n < BOX_THRESH) continue;
    const d = (bw * bh * UNCLIP) / (2.0 * (bw + bh));
    boxes.push({
      x0: Math.max(0, (x0 - d) * sx),
      y0: Math.max(0, (y0 - d) * sy),
      x1: Math.min(im.w, (x1 + 1 + d) * sx),
      y1: Math.min(im.h, (y1 + 1 + d) * sy),
    });
  }
  return boxes;
}

function nms(boxes: Box[]): Box[] {
  const a = [...boxes].sort(
    (b, c) => Math.max(0, c.x1 - c.x0) * Math.max(0, c.y1 - c.y0) - Math.max(0, b.x1 - b.x0) * Math.max(0, b.y1 - b.y0),
  );
  const keep: Box[] = [];
  for (const b of a) {
    let hit = false;
    for (const k of keep) {
      const x0 = Math.max(b.x0, k.x0),
        y0 = Math.max(b.y0, k.y0),
        x1 = Math.min(b.x1, k.x1),
        y1 = Math.min(b.y1, k.y1);
      const inter = Math.max(0, x1 - x0) * Math.max(0, y1 - y0);
      const area = Math.min(Math.max(1, (b.x1 - b.x0) * (b.y1 - b.y0)), Math.max(1, (k.x1 - k.x0) * (k.y1 - k.y0)));
      if (inter / area > 0.5) {
        hit = true;
        break;
      }
    }
    if (!hit) keep.push(b);
  }
  return keep;
}

async function detect(im: Img, det: OrtSession, makeTensor: MakeTensor): Promise<Box[]> {
  const { mean, min: lo, max: hi } = lumaStats(im);
  const work = pad(im, PAD_PX);
  let boxes = await detectOnce(work, false, lo, hi, det, makeTensor);
  if (mean < 90 || boxes.length === 0) {
    const inv = await detectOnce(work, true, lo, hi, det, makeTensor);
    boxes = nms([...boxes, ...inv]);
  }
  const mapped: Box[] = [];
  for (const b of boxes) {
    const m: Box = {
      x0: clamp(b.x0 - PAD_PX, 0, im.w),
      y0: clamp(b.y0 - PAD_PX, 0, im.h),
      x1: clamp(b.x1 - PAD_PX, 0, im.w),
      y1: clamp(b.y1 - PAD_PX, 0, im.h),
    };
    if (m.x1 - m.x0 >= 4 && m.y1 - m.y0 >= 4) mapped.push(m);
  }
  return mapped;
}

function glyphScore(s: string): number {
  let n = 0;
  if (!s) return 0;
  for (const c of s) {
    if (/\s/.test(c)) continue;
    if (isCjk(c) || /[0-9A-Za-z]/.test(c)) n += 2;
    else n += 1;
  }
  return n;
}

type CropRot = "upright" | "cw90" | "ccw90";

async function recognizeRot(
  im: Img,
  box: Box,
  rot: CropRot,
  rec: OrtSession,
  chars: string[],
  makeTensor: MakeTensor,
): Promise<string> {
  const bw = box.x1 - box.x0,
    bh = box.y1 - box.y0;
  if (bw < 2 || bh < 2) return "";
  const H = 48;
  const upright = rot === "upright";
  const w = upright ? clamp(Math.ceil((H * bw) / bh), 8, 3200) : clamp(Math.ceil((H * bh) / bw), 8, 3200);
  const t = new Float32Array(3 * H * w);
  const plane = H * w;
  const put = (x: number, y: number, bl: number, g: number, r: number) => {
    const i = y * w + x;
    t[i] = (bl / 255 - 0.5) / 0.5;
    t[plane + i] = (g / 255 - 0.5) / 0.5;
    t[2 * plane + i] = (r / 255 - 0.5) / 0.5;
  };
  if (upright) resize(im, box.x0, box.y0, bw, bh, w, H, put);
  else {
    for (let y = 0; y < H; y++)
      for (let x = 0; x < w; x++) {
        let sx: number, sy: number;
        if (rot === "cw90") {
          sx = box.x0 + bw - ((y + 0.5) * bw) / H;
          sy = box.y0 + ((x + 0.5) * bh) / w;
        } else {
          sx = box.x0 + ((y + 0.5) * bw) / H;
          sy = box.y0 + bh - ((x + 0.5) * bh) / w;
        }
        const [bl, g, r] = bilinear(im, sx, sy);
        put(x, y, bl, g, r);
      }
  }
  const inputName = rec.inputNames[0];
  const out = await rec.run({ [inputName]: makeTensor(t, [1, 3, H, w]) });
  const tensor = out[rec.outputNames[0]];
  const dims = tensor.dims;
  const T = dims[1],
    C = dims[2];
  const arr = tensor.data as Float32Array;
  const glyphs: { text: string; position: number }[] = [];
  let prev = -1;
  for (let j = 0; j < T; j++) {
    let best = 0,
      bv = -Infinity;
    for (let c = 0; c < C; c++) {
      const v = arr[j * C + c];
      if (v > bv) {
        bv = v;
        best = c;
      }
    }
    if (best !== 0 && best !== prev && best < chars.length) {
      const ch = chars[best];
      if (ch) glyphs.push({ text: ch, position: j });
    }
    prev = best;
  }
  const em = w > 0 ? (48.0 * T) / w : 0;
  return restoreLatinSpaces(glyphs, em);
}

async function recognizeBest(im: Img, b: Box, rec: OrtSession, chars: string[], makeTensor: MakeTensor): Promise<string> {
  const bw = b.x1 - b.x0,
    bh = b.y1 - b.y0;
  const u = await recognizeRot(im, b, "upright", rec, chars, makeTensor);
  if (bh <= bw * 1.35) return u;
  const cw = await recognizeRot(im, b, "cw90", rec, chars, makeTensor);
  const ccw = await recognizeRot(im, b, "ccw90", rec, chars, makeTensor);
  let best = u,
    sc = glyphScore(u);
  let s2 = glyphScore(cw);
  if (s2 > sc) {
    sc = s2;
    best = cw;
  }
  if (glyphScore(ccw) > sc) best = ccw;
  return best;
}

export async function runOcr(
  im: Img,
  det: OrtSession,
  rec: OrtSession,
  chars: string[],
  makeTensor: MakeTensor,
): Promise<{ text: string; words: OcrWord[] }> {
  const boxes = await detect(im, det, makeTensor);
  const spans: OcrSpan[] = [];
  for (const b of boxes) {
    b.text = await recognizeBest(im, b, rec, chars, makeTensor);
    if (!b.text?.trim()) continue;
    spans.push({ text: b.text.trim(), x0: b.x0, y0: b.y0, x1: b.x1, y1: b.y1 });
  }
  const ordered = [...spans].sort((a, b) => (a.y0 + a.y1) / 2 - (b.y0 + b.y1) / 2 || a.x0 - b.x0);
  let line = 0;
  let prevCy = -1e9;
  const medH = ordered.length ? ordered.reduce((s, x) => s + (x.y1 - x.y0), 0) / ordered.length : 16;
  const words: OcrWord[] = [];
  for (const s of ordered) {
    const c = (s.y0 + s.y1) / 2;
    if (prevCy > -1e8 && Math.abs(c - prevCy) > medH * 0.55) line++;
    prevCy = c;
    words.push({ text: s.text, line, x: s.x0, y: s.y0, width: s.x1 - s.x0, height: s.y1 - s.y0 });
  }
  return { text: postProcess(formatOcrLayout(spans)), words };
}
