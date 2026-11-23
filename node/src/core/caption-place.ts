import type { IntRect, MonitorRect } from "./record-rect.js";

export const CAPTION_DEFAULT_WIDTH = 640;
export const CAPTION_DEFAULT_HEIGHT = 360;
export const CAPTION_MARGIN = 16;

export function monitorIndexForPoint(x: number, y: number, monitors: MonitorRect[]): number {
  if (!monitors.length) return -1;
  for (let i = 0; i < monitors.length; i++) {
    const m = monitors[i];
    if (x >= m.x && y >= m.y && x < m.x + m.w && y < m.y + m.h) return i;
  }
  let best = 0;
  let bestD = Infinity;
  for (let i = 0; i < monitors.length; i++) {
    const cx = monitors[i].x + monitors[i].w / 2;
    const cy = monitors[i].y + monitors[i].h / 2;
    const d = (cx - x) ** 2 + (cy - y) ** 2;
    if (d < bestD) {
      bestD = d;
      best = i;
    }
  }
  return best;
}

export function containsPoint(r: IntRect, x: number, y: number): boolean {
  return x >= r.x && y >= r.y && x < r.x + r.w && y < r.y + r.h;
}

export function clampTo(win: IntRect, work: IntRect): IntRect {
  const w = Math.min(Math.max(1, win.w), Math.max(1, work.w));
  const h = Math.min(Math.max(1, win.h), Math.max(1, work.h));
  let x = win.x;
  let y = win.y;
  if (x < work.x) x = work.x;
  if (y < work.y) y = work.y;
  if (x + w > work.x + work.w) x = work.x + work.w - w;
  if (y + h > work.y + work.h) y = work.y + work.h - h;
  return { x, y, w, h };
}

export function bottomCenter(work: IntRect, width: number, height: number, margin = CAPTION_MARGIN): IntRect {
  if (margin < 0) margin = 0;
  const maxW = Math.max(1, work.w - 2 * margin);
  const maxH = Math.max(1, work.h - 2 * margin);
  const w = width < 1 ? CAPTION_DEFAULT_WIDTH : Math.min(width, maxW);
  const h = height < 1 ? CAPTION_DEFAULT_HEIGHT : Math.min(height, maxH);
  const x = work.x + Math.max(0, Math.floor((work.w - w) / 2));
  let y = work.y + work.h - margin - h;
  if (y < work.y + margin) y = work.y + margin;
  return clampTo({ x, y, w, h }, work);
}

export function placeCaption(
  workAreas: MonitorRect[],
  width: number,
  height: number,
  remembered: IntRect | null | undefined,
  anchorX: number,
  anchorY: number,
): IntRect {
  if (!workAreas.length) return { x: anchorX, y: anchorY, w: Math.max(1, width), h: Math.max(1, height) };
  if (remembered && remembered.w > 0 && remembered.h > 0) {
    const cx = remembered.x + Math.floor(remembered.w / 2);
    const cy = remembered.y + Math.floor(remembered.h / 2);
    const mi = monitorIndexForPoint(cx, cy, workAreas);
    if (mi >= 0 && containsPoint(workAreas[mi], cx, cy)) return clampTo(remembered, workAreas[mi]);
  }
  let i = monitorIndexForPoint(anchorX, anchorY, workAreas);
  if (i < 0) i = 0;
  return bottomCenter(workAreas[i], width, height);
}
