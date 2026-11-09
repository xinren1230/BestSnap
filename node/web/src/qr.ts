import { decodeCodesFromLuma, lumaFromRgba, type CodeHit } from "@core/qr-decode.ts";

export type { CodeHit };

export function decodeQrFromRgba(rgba: Uint8ClampedArray, w: number, h: number): CodeHit[] {
  return decodeCodesFromLuma(lumaFromRgba(rgba, w, h), w, h);
}
