export type TranscriptCue = { start: number; end: number; text: string };

export function srtTimestamp(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) seconds = 0;
  const msTotal = Math.round(seconds * 1000);
  const ms = msTotal % 1000;
  const s = Math.floor(msTotal / 1000) % 60;
  const m = Math.floor(msTotal / 60000) % 60;
  const h = Math.min(99, Math.floor(msTotal / 3600000));
  const pad = (n: number, w = 2) => String(n).padStart(w, "0");
  return `${pad(h)}:${pad(m)}:${pad(s)},${pad(ms, 3)}`;
}

export function toSrt(cues: TranscriptCue[]): string {
  let n = 0;
  let out = "";
  for (const c of cues ?? []) {
    const text = (c.text ?? "").trim();
    if (!text) continue;
    n++;
    const end = Math.max(c.end, c.start);
    out += `${n}\r\n${srtTimestamp(c.start)} --> ${srtTimestamp(end)}\r\n${text}\r\n\r\n`;
  }
  return out;
}

export function toTxt(cues: TranscriptCue[]): string {
  let out = "";
  for (const c of cues ?? []) {
    const text = (c.text ?? "").trim();
    if (!text) continue;
    out += `[${srtTimestamp(c.start).replace(",", ".")}] ${text}\r\n`;
  }
  return out;
}

export function plainText(cues: TranscriptCue[]): string {
  return (cues ?? []).map((c) => (c.text ?? "").trim()).filter(Boolean).join(" ");
}

export const whisperModels = {
  defaultId: "auto",
  ids: ["tiny", "base", "small", "medium-q5_0", "medium", "large-v3-turbo-q5_0", "large-v3-turbo"] as const,
  choiceIds: ["auto", "tiny", "base", "small", "medium-q5_0", "medium", "large-v3-turbo-q5_0", "large-v3-turbo"] as const,
  fileName(id: string): string {
    return `ggml-${normalizeWhisper(id)}.bin`;
  },
};

export function normalizeWhisper(id: string): string {
  const s = (id || "").trim().toLowerCase().replace(/\s+/g, "-");
  if (!s || s === "auto" || s === "recommended") return "auto";
  if (s === "turbo" || s === "large" || s === "large-v3") return "large-v3-turbo";
  if (s === "turbo-q5_0" || s === "turbo-q5" || s === "large-v3-turbo-q5") return "large-v3-turbo-q5_0";
  if (s === "medium-q5" || s === "mediumq5_0") return "medium-q5_0";
  if ((whisperModels.ids as readonly string[]).includes(s)) return s;
  return "auto";
}

export const SIMPLIFIED_ZH_HINT = "以下使用简体中文转写，标点用，。？！、；";

export function normalizeWhisperLanguage(lang: string | undefined | null): string {
  const s = (lang || "").trim().toLowerCase();
  if (!s || s === "auto" || s === "detect") return "auto";
  if (s.startsWith("zh") || s === "chinese" || s === "cn") return "zh";
  if (s.startsWith("en") || s === "english") return "en";
  if (s.length === 2) return s;
  return "auto";
}

export function composeWhisperPrompt(language: string, userPrompt: string, previousText: string, maxChars = 400): string {
  const lang = normalizeWhisperLanguage(language);
  const hint = lang === "zh" || lang === "yue" ? SIMPLIFIED_ZH_HINT : "";
  const user = (userPrompt || "").trim();
  let prev = (previousText || "").trim();
  const head = [hint, user].filter(Boolean).join(" ");
  if (head.length >= maxChars) return head.slice(0, maxChars);
  const room = maxChars - head.length - (head && prev ? 1 : 0);
  if (prev.length > room) prev = room <= 0 ? "" : prev.slice(prev.length - room);
  return [head, prev].filter(Boolean).join(" ");
}
