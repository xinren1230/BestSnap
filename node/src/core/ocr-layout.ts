/** Port of SnapForge.Plugins.OcrLayout — reading order, line merge, paragraphs, wrap-join. */

import { isCjk, isCjkPunct } from "./ocr-text.js";

export interface OcrSpan {
  text: string;
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

interface Line {
  text: string;
  x0: number;
  y0: number;
  y1: number;
  cy: number;
  tab: boolean;
}

function height(b: OcrSpan): number {
  return b.y1 - b.y0;
}
function cy(b: OcrSpan): number {
  return (b.y0 + b.y1) / 2;
}

function median(xs: number[]): number {
  const n = xs.length;
  if (n === 0) return 0;
  const a = [...xs].sort((x, y) => x - y);
  if (n & 1) return a[n >> 1];
  return (a[n / 2 - 1] + a[n / 2]) / 2;
}

function firstChar(s: string): string {
  if (!s) return "";
  for (const c of s) if (!/\s/.test(c)) return c;
  return "";
}
function lastChar(s: string): string {
  if (!s) return "";
  for (let i = s.length - 1; i >= 0; i--) if (!/\s/.test(s[i])) return s[i];
  return "";
}

function avgY(line: OcrSpan[]): number {
  return line.reduce((s, x) => s + cy(x), 0) / line.length;
}

function clusterLines(items: OcrSpan[]): OcrSpan[][] {
  const ordered = [...items].sort((a, b) => {
    const c = cy(a) - cy(b);
    return c !== 0 ? c : a.x0 - b.x0;
  });
  const lines: OcrSpan[][] = [];
  for (const b of ordered) {
    let hit: OcrSpan[] | null = null;
    let best = Number.POSITIVE_INFINITY;
    for (const line of lines) {
      let ly0 = 0,
        ly1 = 0;
      for (const x of line) {
        ly0 += x.y0;
        ly1 += x.y1;
      }
      ly0 /= line.length;
      ly1 /= line.length;
      const overlap = Math.min(b.y1, ly1) - Math.max(b.y0, ly0);
      const mh = Math.min(Math.max(1, height(b)), Math.max(1, ly1 - ly0));
      if (overlap <= 0.45 * mh) continue;
      const d = Math.abs(cy(b) - (ly0 + ly1) / 2);
      if (d < best) {
        best = d;
        hit = line;
      }
    }
    if (!hit) lines.push([b]);
    else hit.push(b);
  }
  lines.sort((a, b) => avgY(a) - avgY(b));
  return lines;
}

function joinLine(cells: OcrSpan[], medianH: number): Line {
  cells.sort((a, b) => a.x0 - b.x0);
  let sb = "";
  for (let i = 0; i < cells.length; i++) {
    if (i > 0) {
      const gap = cells[i].x0 - cells[i - 1].x1;
      const prev = lastChar(cells[i - 1].text);
      const next = firstChar(cells[i].text);
      if (gap > medianH * 1.5) sb += "\t";
      else if (prev && next && (isCjkPunct(prev) || isCjkPunct(next))) {
        /* no space */
      } else if (prev && next && isCjk(prev) && isCjk(next)) {
        /* flush */
      } else if (gap > medianH * 0.12) sb += " ";
      else if (prev && next && !isCjk(prev) && !isCjk(next)) sb += " ";
    }
    sb += cells[i].text;
  }
  let x0 = cells[0].x0,
    y0 = cells[0].y0,
    y1 = cells[0].y1,
    cys = 0;
  for (const c of cells) {
    if (c.y0 < y0) y0 = c.y0;
    if (c.y1 > y1) y1 = c.y1;
    cys += cy(c);
  }
  return { text: sb, x0, y0, y1, cy: cys / cells.length, tab: sb.includes("\t") };
}

function indentSpaces(x0: number, pageLeft: number, medianH: number): number {
  const dx = x0 - pageLeft;
  if (dx < medianH * 0.8) return 0;
  let n = Math.round(dx / (medianH * 0.55));
  if (n < 1) n = 1;
  if (n > 16) n = 16;
  return n;
}

function looksLikeList(s: string): boolean {
  if (!s) return false;
  let k = 0;
  while (k < s.length && /\s/.test(s[k])) k++;
  if (k >= s.length) return false;
  if (s[k] === "•" || s[k] === "·") return true;
  if (k + 1 < s.length && (s[k] === "-" || s[k] === "*") && s[k + 1] === " ") return true;
  let i = k;
  while (i < s.length && /\d/.test(s[i])) i++;
  return i > k && i < s.length && (s[i] === "." || s[i] === ")") && (i + 1 === s.length || s[i + 1] === " ");
}

function shouldJoin(prev: Line, next: Line, medianH: number): boolean {
  if (prev.tab || next.tab) return false;
  if (looksLikeList(next.text)) return false;
  if (next.x0 > prev.x0 + medianH * 0.8) return false;
  if (next.x0 < prev.x0 - medianH * 0.8) return false;
  const a = prev.text.trimEnd(),
    b = next.text.trimStart();
  if (!a || !b) return false;
  const last = a[a.length - 1],
    first = b[0];
  if (last === "-") return true;
  if (",;:，、".includes(last)) return true;
  if (".!?。！？…".includes(last)) return false;
  if (isCjk(last) && isCjk(first)) return true;
  if (first === first.toLowerCase() && /[a-z]/.test(first) && (first.codePointAt(0) ?? 0) < 0x2e80) return true;
  return false;
}

function joinWrapped(prev: string, next: string): string {
  const a = prev.trimEnd(),
    b = next.trimStart();
  if (!a) return b;
  if (!b) return a;
  const last = a[a.length - 1],
    first = b[0];
  if (last === "-" && /[A-Za-z]/.test(first)) return a.slice(0, -1) + b;
  if (isCjk(last) && isCjk(first)) return a + b;
  if (/\s/.test(last) || /\s/.test(first)) return a + b;
  return a + " " + b;
}

function typicalLineSpacing(lines: Line[], medianH: number): number {
  if (lines.length < 2) return Math.max(1, medianH);
  const d: number[] = [];
  for (let i = 1; i < lines.length; i++) {
    const g = lines[i].cy - lines[i - 1].cy;
    if (g > 1) d.push(g);
  }
  if (d.length === 0) return Math.max(1, medianH);
  const min = Math.min(...d);
  const tight = d.filter((x) => x <= min * 1.35);
  const t = median(tight.length > 0 ? tight : d);
  return t > 0 ? t : Math.max(1, medianH);
}

function isParagraphBreak(prev: Line, next: Line, typical: number, medianH: number, lineCount: number): boolean {
  const delta = next.cy - prev.cy;
  const ws = next.y0 - prev.y1;
  if (lineCount <= 2) return ws > medianH * 2.0 && delta > medianH * 2.5;
  return delta > typical * 1.8 && ws > medianH * 1.0;
}

export function formatOcrLayout(boxes: ReadonlyArray<OcrSpan>): string {
  if (!boxes || boxes.length === 0) return "";
  const items: OcrSpan[] = [];
  for (const b of boxes) {
    const t = (b.text ?? "").trim();
    if (!t) continue;
    items.push({ text: t, x0: b.x0, y0: b.y0, x1: b.x1, y1: b.y1 });
  }
  if (items.length === 0) return "";
  if (items.length === 1) return items[0].text;

  const heights: number[] = [];
  let pageLeft = Number.POSITIVE_INFINITY;
  for (const b of items) {
    heights.push(Math.max(1, height(b)));
    if (b.x0 < pageLeft) pageLeft = b.x0;
  }
  let medianH = median(heights);
  if (medianH <= 0) medianH = 16;

  const lines = clusterLines(items);
  const built: Line[] = [];
  for (const cells of lines) {
    const line = joinLine(cells, medianH);
    const ind = indentSpaces(line.x0, pageLeft, medianH);
    if (ind > 0 && !line.text.includes("\t")) line.text = " ".repeat(ind) + line.text;
    built.push(line);
  }

  const typical = typicalLineSpacing(built, medianH);
  const parts: string[] = [];
  for (let i = 0; i < built.length; i++) {
    if (i === 0) {
      parts.push(built[i].text);
      continue;
    }
    if (isParagraphBreak(built[i - 1], built[i], typical, medianH, built.length)) {
      if (parts.length === 0 || parts[parts.length - 1].length !== 0) parts.push("");
      parts.push(built[i].text);
      continue;
    }
    if (shouldJoin(built[i - 1], built[i], medianH)) parts[parts.length - 1] = joinWrapped(parts[parts.length - 1], built[i].text);
    else parts.push(built[i].text);
  }
  return parts.join("\n").replace(/\s+$/, "");
}
