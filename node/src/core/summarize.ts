import { type TranscriptCue, toTxt } from "./transcript.js";

export const DEFAULT_CHUNK_CHARS = 6000;

export function looksChinese(text: string): boolean {
  let cjk = 0, n = 0;
  for (const ch of text ?? "") {
    if (/\s/.test(ch)) continue;
    n++;
    const code = ch.codePointAt(0) ?? 0;
    if (code >= 0x4e00 && code <= 0x9fff) cjk++;
  }
  return n > 0 && cjk * 2 >= n;
}

export function splitSentences(text: string): string[] {
  const list: string[] = [];
  if (!text) return list;
  let cur = "";
  for (const ch of text) {
    cur += ch;
    if ("。！？!?.\n".includes(ch)) {
      const s = cur.trim();
      if (s) list.push(s);
      cur = "";
    }
  }
  const tail = cur.trim();
  if (tail) list.push(tail);
  return list;
}

export function chunkCues(cues: TranscriptCue[] | null | undefined, maxChars: number): TranscriptCue[][] {
  const chunks: TranscriptCue[][] = [];
  if (!cues?.length) return chunks;
  if (maxChars < 8) maxChars = 8;
  let cur: TranscriptCue[] = [];
  let len = 0;
  for (const c of cues) {
    const add = (c.text ?? "").length + 8;
    if (cur.length > 0 && len + add > maxChars) {
      chunks.push(cur);
      cur = [];
      len = 0;
    }
    cur.push(c);
    len += add;
  }
  if (cur.length) chunks.push(cur);
  return chunks;
}

export function chunkText(text: string, maxChars: number): string[] {
  const chunks: string[] = [];
  const sents = splitSentences(text ?? "");
  if (!sents.length) {
    if (text?.trim()) chunks.push(text.trim());
    return chunks;
  }
  if (maxChars < 8) maxChars = 8;
  let cur = "";
  for (const s of sents) {
    if (cur && cur.length + s.length + 1 > maxChars) {
      chunks.push(cur);
      cur = "";
    }
    cur = cur ? cur + " " + s : s;
  }
  if (cur) chunks.push(cur);
  return chunks;
}

export function isAction(sentence: string): boolean {
  if (!sentence?.trim()) return false;
  return /需要|请|務必|记得|应当|应该|待办/u.test(sentence) || /\b(please|should|must|need to|todo|to-do|action item)\b/i.test(sentence);
}

export function stamp(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "";
  const s = Math.floor(seconds);
  const mm = String(Math.floor(s / 60)).padStart(2, "0");
  const ss = String(s % 60).padStart(2, "0");
  return `[${mm}:${ss}] `;
}

function tokens(s: string): Set<string> {
  const set = new Set<string>();
  let word = "";
  for (const ch of s ?? "") {
    const code = ch.codePointAt(0) ?? 0;
    if (code >= 0x4e00 && code <= 0x9fff) {
      set.add(ch);
      continue;
    }
    if (/[0-9A-Za-z]/.test(ch)) word += ch;
    else if (word.length) {
      if (word.length > 1) set.add(word.toLowerCase());
      word = "";
    }
  }
  if (word.length > 1) set.add(word.toLowerCase());
  return set;
}

function overlap(a: Set<string>, b: Set<string>): number {
  if (!a.size || !b.size) return 0;
  let n = 0;
  for (const x of a) if (b.has(x)) n++;
  return n / Math.sqrt(a.size * b.size);
}

function pageRank(sim: number[][], iter = 20): number[] {
  const n = sim.length;
  let r = Array(n).fill(1 / Math.max(1, n));
  const d = 0.85;
  for (let it = 0; it < iter; it++) {
    const nr = Array(n).fill((1 - d) / n);
    for (let i = 0; i < n; i++) {
      let sum = 0;
      for (let j = 0; j < n; j++) sum += (sim[j]?.[i] ?? 0) * r[j];
      nr[i] = (1 - d) / n + d * sum;
    }
    r = nr;
  }
  return r;
}

export function formatMarkdown(zh: boolean, basic: boolean, overview: string, keys: string[], actions: string[]): string {
  let sb = (zh ? "# 摘要" : "# Summary") + "\r\n";
  if (basic) sb += (zh ? "（本机抽取式摘要）" : "(basic on-device extractive summary)") + "\r\n";
  sb += "\r\n" + (zh ? "## 概述" : "## Overview") + "\r\n";
  sb += (overview?.trim() || (zh ? "（无）" : "(none)")) + "\r\n\r\n";
  sb += (zh ? "## 要点" : "## Key points") + "\r\n";
  if (!keys?.length) sb += (zh ? "- （无）" : "- (none)") + "\r\n";
  else for (const k of keys) sb += "- " + k + "\r\n";
  sb += "\r\n" + (zh ? "## 待办" : "## Action items") + "\r\n";
  if (!actions?.length) sb += (zh ? "- （无）" : "- (none)") + "\r\n";
  else for (const a of actions) sb += "- " + a + "\r\n";
  return sb;
}

export function extractiveMarkdown(cues: TranscriptCue[] | null | undefined, keyCount = 5): string {
  const items: { sent: string; start: number }[] = [];
  for (const c of cues ?? []) {
    for (const s of splitSentences(c.text ?? "")) items.push({ sent: s, start: c.start });
  }
  if (!items.length) return formatMarkdown(false, true, "", [], []);
  const all = items.map((i) => i.sent).join(" ");
  const zh = looksChinese(all);
  const n = items.length;
  const tok = items.map((i) => tokens(i.sent));
  const sim = Array.from({ length: n }, () => Array(n).fill(0));
  const outW = Array(n).fill(0);
  for (let i = 0; i < n; i++) {
    for (let j = 0; j < n; j++) {
      if (i === j) continue;
      const s = overlap(tok[i], tok[j]);
      sim[i][j] = s;
      outW[i] += s;
    }
  }
  for (let i = 0; i < n; i++) {
    if (outW[i] <= 0) continue;
    for (let j = 0; j < n; j++) sim[i][j] /= outW[i];
  }
  const rank = pageRank(sim);
  const order = [...Array(n).keys()].sort((a, b) => rank[b] - rank[a]);
  const overN = n === 1 ? 1 : 2;
  const overview = order.slice(0, overN).map((i) => items[i].sent).join(zh ? "" : " ");
  const take = Math.min(Math.max(1, keyCount), n);
  const keyIdx = order.slice(0, take).sort((a, b) => a - b);
  const keys = keyIdx.map((i) => stamp(items[i].start) + items[i].sent);
  const actions = items.filter((it) => isAction(it.sent)).map((it) => stamp(it.start) + it.sent);
  return formatMarkdown(zh, true, overview, keys, actions);
}

export function chunkTxt(cues: TranscriptCue[], maxChars = DEFAULT_CHUNK_CHARS): string[] {
  return chunkCues(cues, maxChars).map((c) => toTxt(c));
}
