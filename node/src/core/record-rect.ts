/** Region / monitor geometry for screen recording. Physical pixels on the virtual desktop. */

export interface IntRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface MonitorRect extends IntRect {
  deviceName?: string;
}

export function normalize(x: number, y: number, w: number, h: number): IntRect {
  if (w < 0) {
    x += w;
    w = -w;
  }
  if (h < 0) {
    y += h;
    h = -h;
  }
  return { x, y, w, h };
}

export function intersect(a: IntRect, b: IntRect): IntRect {
  const x1 = Math.max(a.x, b.x),
    y1 = Math.max(a.y, b.y);
  const x2 = Math.min(a.x + a.w, b.x + b.w),
    y2 = Math.min(a.y + a.h, b.y + b.h);
  return { x: x1, y: y1, w: Math.max(0, x2 - x1), h: Math.max(0, y2 - y1) };
}

export function virtualScreen(monitors: MonitorRect[]): IntRect {
  if (!monitors.length) return { x: 0, y: 0, w: 0, h: 0 };
  let x1 = Infinity,
    y1 = Infinity,
    x2 = -Infinity,
    y2 = -Infinity;
  for (const m of monitors) {
    x1 = Math.min(x1, m.x);
    y1 = Math.min(y1, m.y);
    x2 = Math.max(x2, m.x + m.w);
    y2 = Math.max(y2, m.y + m.h);
  }
  return { x: x1, y: y1, w: x2 - x1, h: y2 - y1 };
}

export function evenSize(r: IntRect): IntRect {
  return { x: r.x, y: r.y, w: r.w & ~1, h: r.h & ~1 };
}

export function borderOutside(capture: IntRect, pad: number): IntRect {
  const p = Math.max(0, pad);
  return { x: capture.x - p, y: capture.y - p, w: capture.w + 2 * p, h: capture.h + 2 * p };
}

export const VIDEO_FPS = [10, 15, 24, 30, 60];

export function normalizeFps(fps: number): number {
  let best = VIDEO_FPS[0],
    dist = Math.abs(fps - best);
  for (const v of VIDEO_FPS) {
    const d = Math.abs(fps - v);
    if (d < dist) {
      dist = d;
      best = v;
    }
  }
  return best;
}

export function pickMonitorIndex(region: IntRect, monitors: MonitorRect[]): { index: number; spans: boolean } {
  if (!monitors.length) return { index: -1, spans: false };
  let best = 0,
    bestArea = -1,
    hits = 0;
  monitors.forEach((m, i) => {
    const inter = intersect(region, m);
    const a = inter.w * inter.h;
    if (a > 0) hits++;
    if (a > bestArea) {
      bestArea = a;
      best = i;
    }
  });
  return { index: best, spans: hits > 1 };
}

export function isValidCapture(r: IntRect): boolean {
  return r.w >= 2 && r.h >= 2;
}
