import type { TranscriptCue } from "./transcript.js";

export const FIRST_WINDOW_SECONDS = 3.5;
export const WINDOW_SECONDS = 10;
export const OVERLAP_SECONDS = 2;
export const HOP_SECONDS = WINDOW_SECONDS - OVERLAP_SECONDS;
export const FILE_WINDOW_SECONDS = 30;
export const FILE_OVERLAP_SECONDS = 2;
export const FILE_HOP_SECONDS = FILE_WINDOW_SECONDS - FILE_OVERLAP_SECONDS;
export const LIVE_MAX_PENDING = 2;
export const LIVE_MAX_LAG_HOPS = 2;
export const LIVE_TIMEOUT_FACTOR = 2.5;
export const LIVE_TIMEOUT_DEFAULT = 25;
export const LIVE_TIMEOUT_MIN = 8;
export const LIVE_TIMEOUT_MAX = 45;
export const FILE_TIMEOUT_FACTOR = 2.5;
export const FILE_TIMEOUT_MIN = 20;
export const FILE_TIMEOUT_MAX = 90;
export const FAIL_FACTOR = 3;

export function timeoutSeconds(chunkDuration: number, live: boolean): number {
  let dur = chunkDuration;
  if (!Number.isFinite(dur) || dur < 0.4) dur = live ? WINDOW_SECONDS : FILE_WINDOW_SECONDS;
  if (live) {
    let t = Math.max(LIVE_TIMEOUT_MIN, dur * LIVE_TIMEOUT_FACTOR);
    if (dur >= 8 && t < LIVE_TIMEOUT_DEFAULT) t = LIVE_TIMEOUT_DEFAULT;
    if (t > LIVE_TIMEOUT_MAX) t = LIVE_TIMEOUT_MAX;
    return t;
  }
  let f = Math.max(FILE_TIMEOUT_MIN, dur * FILE_TIMEOUT_FACTOR);
  if (f > FILE_TIMEOUT_MAX) f = FILE_TIMEOUT_MAX;
  return f;
}

export function dropOldestCount(pending: number, maxPending = LIVE_MAX_PENDING): number {
  if (pending < 0) pending = 0;
  if (maxPending < 1) maxPending = 1;
  return pending > maxPending ? pending - maxPending : 0;
}

export function lagExceedsHops(oldestAbsStart: number, newestAbsStart: number, hopSeconds = HOP_SECONDS, maxHops = LIVE_MAX_LAG_HOPS): boolean {
  return newestAbsStart - oldestAbsStart > maxHops * hopSeconds - 1e-6;
}

export function exceedsDurationFactor(elapsedSeconds: number, chunkDuration: number, factor = FAIL_FACTOR): boolean {
  if (factor < 1) factor = FAIL_FACTOR;
  if (chunkDuration <= 0) return elapsedSeconds > LIVE_TIMEOUT_DEFAULT;
  return elapsedSeconds > chunkDuration * factor;
}

export function cuesMissingInSecondHalf(cues: TranscriptCue[] | null | undefined, durationSeconds: number): boolean {
  if (durationSeconds <= 0) return !cues || cues.length === 0;
  const mid = durationSeconds / 2;
  if (!cues) return true;
  for (const c of cues) {
    if (!c) continue;
    if (c.start >= mid || c.end >= mid) return false;
  }
  return true;
}

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
