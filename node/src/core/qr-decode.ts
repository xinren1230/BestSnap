/** QR / barcode recognition via @zxing/library (Apache-2.0). No sharp / Node APIs. */
import {
  RGBLuminanceSource,
  HybridBinarizer,
  BinaryBitmap,
  MultiFormatReader,
  DecodeHintType,
  BarcodeFormat,
  type Result,
} from "@zxing/library";

export interface CodeHit {
  text: string;
  format: string;
}

export function lumaFromRgba(data: Uint8Array | Uint8ClampedArray, w: number, h: number): Uint8ClampedArray {
  const lum = new Uint8ClampedArray(w * h);
  for (let i = 0, p = 0; i < data.length; i += 4, p++) {
    lum[p] = (data[i] * 77 + data[i + 1] * 150 + data[i + 2] * 29) >> 8;
  }
  return lum;
}

function reader(): MultiFormatReader {
  const r = new MultiFormatReader();
  const hints = new Map();
  hints.set(DecodeHintType.TRY_HARDER, true);
  hints.set(DecodeHintType.POSSIBLE_FORMATS, [
    BarcodeFormat.QR_CODE,
    BarcodeFormat.DATA_MATRIX,
    BarcodeFormat.AZTEC,
    BarcodeFormat.PDF_417,
    BarcodeFormat.CODE_128,
    BarcodeFormat.CODE_39,
    BarcodeFormat.EAN_13,
    BarcodeFormat.EAN_8,
    BarcodeFormat.UPC_A,
    BarcodeFormat.UPC_E,
    BarcodeFormat.ITF,
    BarcodeFormat.CODABAR,
  ]);
  r.setHints(hints);
  return r;
}

function decodeBitmap(lum: Uint8ClampedArray, w: number, h: number): Result | null {
  try {
    const src = new RGBLuminanceSource(lum, w, h);
    const bmp = new BinaryBitmap(new HybridBinarizer(src));
    return reader().decode(bmp);
  } catch {
    return null;
  }
}

export function decodeCodesFromLuma(lum: Uint8ClampedArray, w: number, h: number): CodeHit[] {
  const hits: CodeHit[] = [];
  const seen = new Set<string>();
  const add = (r: Result | null) => {
    if (!r) return;
    const t = r.getText();
    if (!t || seen.has(t)) return;
    seen.add(t);
    hits.push({ text: t, format: BarcodeFormat[r.getBarcodeFormat()] ?? String(r.getBarcodeFormat()) });
  };
  add(decodeBitmap(lum, w, h));
  const inv = lum.map((v) => 255 - v);
  add(decodeBitmap(inv, w, h));
  if (hits.length === 0 && w * h < 8_000_000) {
    const rot = new Uint8ClampedArray(w * h);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) rot[x * h + (h - 1 - y)] = lum[y * w + x];
    add(decodeBitmap(rot, h, w));
  }
  return hits;
}
