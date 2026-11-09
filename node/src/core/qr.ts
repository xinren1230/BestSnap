/** QR / barcode recognition via @zxing/library (Apache-2.0), matching ZXing.Net usage. */
import sharp from "sharp";
import { decodeCodesFromLuma, lumaFromRgba, type CodeHit } from "./qr-decode.js";

export type { CodeHit };
export { decodeCodesFromLuma, lumaFromRgba };

async function luminance(png: Buffer): Promise<{ w: number; h: number; lum: Uint8ClampedArray }> {
  const { data, info } = await sharp(png).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  return { w: info.width, h: info.height, lum: lumaFromRgba(data, info.width, info.height) };
}

export async function decodeCodes(png: Buffer): Promise<CodeHit[]> {
  const { w, h, lum } = await luminance(png);
  return decodeCodesFromLuma(lum, w, h);
}
