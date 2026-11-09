/** Port of SnapForge.Plugins.WatermarkMath. */

export const MAX_STAMPS = 80;
export const LIGHT_THRESHOLD = 0.55;

function finite(v: number, fallback: number): number {
  return Number.isFinite(v) ? v : fallback;
}

export function rotatedAabb(w: number, h: number, deg: number): { w: number; h: number } {
  w = Math.max(0, finite(w, 0));
  h = Math.max(0, finite(h, 0));
  deg = finite(deg, 0);
  const rad = (deg * Math.PI) / 180;
  const c = Math.abs(Math.cos(rad)),
    s = Math.abs(Math.sin(rad));
  let bw = w * c + h * s,
    bh = w * s + h * c;
  if (bw < 1e-6) bw = Math.max(1, w);
  if (bh < 1e-6) bh = Math.max(1, h);
  return { w: bw, h: bh };
}

export function place(
  canvasW: number,
  canvasH: number,
  itemW: number,
  itemH: number,
  anchor: number,
  margin: number,
  rotationDeg = 0,
): { x: number; y: number } {
  if (canvasW < 1 || canvasH < 1) return { x: 0, y: 0 };
  const a = anchor < 0 ? 0 : anchor > 8 ? 8 : anchor;
  const col = a % 3,
    row = Math.floor(a / 3);
  const m = Math.max(0, finite(margin, 0));
  const { w: bw, h: bh } = rotatedAabb(itemW, itemH, rotationDeg);
  const x = col === 0 ? m : col === 1 ? (canvasW - bw) / 2 : canvasW - bw - m;
  const y = row === 0 ? m : row === 1 ? (canvasH - bh) / 2 : canvasH - bh - m;
  return { x: x + (bw - itemW) / 2, y: y + (bh - itemH) / 2 };
}

export function clampPos(
  canvasW: number,
  canvasH: number,
  itemW: number,
  itemH: number,
  x: number,
  y: number,
  rotationDeg: number,
): { x: number; y: number } {
  if (canvasW < 1 || canvasH < 1) return { x: 0, y: 0 };
  x = finite(x, 0);
  y = finite(y, 0);
  const { w: bw, h: bh } = rotatedAabb(itemW, itemH, rotationDeg);
  const ox = (bw - itemW) / 2,
    oy = (bh - itemH) / 2;
  const aabbX = Math.min(Math.max(x - ox, 8 - bw), canvasW - 8);
  const aabbY = Math.min(Math.max(y - oy, 8 - bh), canvasH - 8);
  return { x: aabbX + ox, y: aabbY + oy };
}

function step(aabb: number, gap: number): number {
  let s = aabb + gap;
  if (!Number.isFinite(s) || s < 1) s = 1;
  if (s > 1_000_000) s = 1_000_000;
  return Math.max(1, Math.ceil(s));
}

export function tile(
  canvasW: number,
  canvasH: number,
  itemW: number,
  itemH: number,
  spacing: number,
  rotationDeg: number,
): { x: number; y: number }[] {
  const list: { x: number; y: number }[] = [];
  if (canvasW < 1 || canvasH < 1 || itemW < 1 || itemH < 1) return list;
  spacing = finite(spacing, 0);
  rotationDeg = finite(rotationDeg, 0);
  const { w: bw, h: bh } = rotatedAabb(itemW, itemH, rotationDeg);
  const gap = Math.max(0, spacing);
  const stepX = step(bw, gap),
    stepY = step(bh, gap);
  const ox = (bw - itemW) / 2,
    oy = (bh - itemH) / 2;
  const x0 = Math.min(Math.max(0, gap), Math.max(0, canvasW - 1));
  const y0 = Math.min(Math.max(0, gap), Math.max(0, canvasH - 1));
  let guard = 0;
  for (let y = y0; y < canvasH && list.length < MAX_STAMPS && guard < MAX_STAMPS * 4; y += stepY)
    for (let x = x0; x < canvasW && list.length < MAX_STAMPS && guard < MAX_STAMPS * 4; x += stepX, guard++)
      list.push({ x: x + ox, y: y + oy });
  if (list.length === 0) list.push(place(canvasW, canvasH, itemW, itemH, 4, gap, rotationDeg));
  return list;
}

export function luma(r: number, g: number, b: number): number {
  return (0.299 * finite(r, 0) + 0.587 * finite(g, 0) + 0.114 * finite(b, 0)) / 255;
}

export function isLightSurface(l: number): boolean {
  return finite(l, 0.5) >= LIGHT_THRESHOLD;
}

export function autoInk(l: number): { r: number; g: number; b: number } {
  if (!Number.isFinite(l)) return { r: 0x80, g: 0x80, b: 0x80 };
  return isLightSurface(l) ? { r: 0x22, g: 0x22, b: 0x22 } : { r: 0xf2, g: 0xf2, b: 0xf2 };
}

export function isAutoColor(color: string | null | undefined): boolean {
  return !color || color.trim().toLowerCase() === "auto";
}
