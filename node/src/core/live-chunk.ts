import type { TranscriptCue } from "./transcript.js";

export const FIRST_WINDOW_SECONDS = 3.5;
export const WINDOW_SECONDS = 10;
export const OVERLAP_SECONDS = 2;
export const HOP_SECONDS = WINDOW_SECONDS - OVERLAP_SECONDS;

export function windowForIndex(index: number, firstWindow = FIRST_WINDOW_SECONDS, window = WINDOW_SECONDS): number {
  return index <= 0 ? Math.max(0.4, firstWindow) : Math.max(0.4, window);
}

export function fireAt(index: number, firstWindow = FIRST_WINDOW_SECONDS, window = WINDOW_SECONDS, overlap = OVERLAP_SECONDS): number {
  const first = Math.max(0.4, firstWindow);
  if (index <= 0) return first;
  const hop = Math.max(0.1, window - overlap);
  return first + index * hop;
}

export function chunkAbsStart(index: number, window = WINDOW_SECONDS, overlap = OVERLAP_SECONDS, firstWindow = FIRST_WINDOW_SECONDS): number {
  if (index <= 0) return 0;
  return Math.max(0, fireAt(index, firstWindow, window, overlap) - windowForIndex(index, firstWindow, window));
}

function normalizeCueText(text: string | null | undefined): string {
  const t = (text ?? "").trim();
  if (!t) return "";
  if (t.startsWith("[") && t.endsWith("]")) {
    const inner = t.slice(1, -1).trim().toLowerCase();
    if (inner === "blank_audio" || inner === "silence" || inner === "inaudible" || inner === "music") return "";
  }
  return t;
}

export function stitch(incomingRelative: TranscriptCue[] | null | undefined, absStart: number, committedThrough: number, chunkDuration = 0): TranscriptCue[] {
  const list: TranscriptCue[] = [];
  if (!incomingRelative) return list;
  for (const c of incomingRelative) {
    const text = normalizeCueText(c.text);
    if (!text) continue;
    let a = absStart + c.start;
    let b = absStart + Math.max(c.end, c.start);
    if (b - a < 0.04) {
      a = Math.max(absStart, committedThrough);
      const span = chunkDuration > 0 ? chunkDuration : 0.5;
      b = absStart + span;
      if (b <= a) b = a + 0.04;
    }
    if (b <= committedThrough + 1e-3) continue;
    if (a < committedThrough) a = committedThrough;
    if (b - a < 0.04) continue;
    list.push({ start: a, end: b, text });
  }
  return list;
}

export function extendCommitted(added: TranscriptCue[] | null | undefined, previous: number): number {
  let t = previous;
  if (!added) return t;
  for (const c of added) if (c.end > t) t = c.end;
  return t;
}
