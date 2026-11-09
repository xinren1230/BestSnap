/** Port of BestSnap.Plugins.EraserMath. */

export function distToSegment(px: number, py: number, ax: number, ay: number, bx: number, by: number): number {
  const abx = bx - ax,
    aby = by - ay;
  const len2 = abx * abx + aby * aby;
  if (len2 < 1e-12) {
    const dx = px - ax,
      dy = py - ay;
    return Math.sqrt(dx * dx + dy * dy);
  }
  let t = ((px - ax) * abx + (py - ay) * aby) / len2;
  if (t < 0) t = 0;
  else if (t > 1) t = 1;
  const qx = ax + t * abx,
    qy = ay + t * aby;
  const dx2 = px - qx,
    dy2 = py - qy;
  return Math.sqrt(dx2 * dx2 + dy2 * dy2);
}

export function hitsRect(px: number, py: number, x0: number, y0: number, x1: number, y1: number, radius: number): boolean {
  if (x0 > x1) [x0, x1] = [x1, x0];
  if (y0 > y1) [y0, y1] = [y1, y0];
  const r = Math.max(0, radius);
  return px >= x0 - r && px <= x1 + r && py >= y0 - r && py <= y1 + r;
}

export function hitsSegment(px: number, py: number, ax: number, ay: number, bx: number, by: number, radius: number): boolean {
  return distToSegment(px, py, ax, ay, bx, by) <= Math.max(0, radius);
}

export function medianRgb(samples: { r: number; g: number; b: number }[]): { r: number; g: number; b: number } {
  if (!samples || samples.length === 0) return { r: 255, g: 255, b: 255 };
  const list = [...samples].sort((a, b) => a.r + a.g + a.b - (b.r + b.g + b.b));
  return list[list.length >> 1];
}

export function ringSamples(cx: number, cy: number, inner: number, outer: number, w: number, h: number, step = 2): { x: number; y: number }[] {
  const pts: { x: number; y: number }[] = [];
  if (w <= 0 || h <= 0) return pts;
  if (inner < 0) inner = 0;
  if (outer < inner) outer = inner;
  if (step < 1) step = 1;
  const x0 = Math.max(0, cx - outer),
    x1 = Math.min(w - 1, cx + outer);
  const y0 = Math.max(0, cy - outer),
    y1 = Math.min(h - 1, cy + outer);
  const inner2 = inner * inner,
    outer2 = outer * outer;
  for (let y = y0; y <= y1; y += step)
    for (let x = x0; x <= x1; x += step) {
      const dx = x - cx,
        dy = y - cy,
        d2 = dx * dx + dy * dy;
      if (d2 >= inner2 && d2 <= outer2) pts.push({ x, y });
    }
  return pts;
}

export function isTransparent(a: number): boolean {
  return a === 0;
}

export function sweepHits(
  samples: { x: number; y: number }[],
  count: number,
  hits: (i: number, x: number, y: number) => boolean,
): number[] {
  const order: number[] = [];
  if (!samples || count <= 0 || !hits) return order;
  const seen = new Array<boolean>(count).fill(false);
  for (const s of samples) {
    for (let i = count - 1; i >= 0; i--) {
      if (seen[i]) continue;
      if (hits(i, s.x, s.y)) {
        seen[i] = true;
        order.push(i);
      }
    }
  }
  return order;
}
