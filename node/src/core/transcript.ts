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
  defaultId: "base",
  ids: ["tiny", "base", "small", "medium"] as const,
  fileName(id: string): string {
    return `ggml-${normalizeWhisper(id)}.bin`;
  },
};

export function normalizeWhisper(id: string): string {
  const s = (id || "").toLowerCase();
  if (s === "tiny" || s === "small" || s === "medium") return s;
  return "base";
}
