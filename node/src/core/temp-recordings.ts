import fs from "node:fs";
import path from "node:path";
import { localAppDataDir } from "./paths.js";
import { deleteTranscript, isTranscriptPath } from "./transcript-sidecar.js";

export const DEFAULT_MAX_AGE_MS = 7 * 24 * 60 * 60 * 1000;

export function folder(localData = localAppDataDir()): string {
  return path.join(localData, "temp", "recordings");
}

export function ensureFolder(localData = localAppDataDir()): string {
  const d = folder(localData);
  fs.mkdirSync(d, { recursive: true });
  return d;
}

export function sidecarPath(media: string): string {
  return (media ?? "") + ".meta.json";
}

export function isUnder(p: string, dir: string): boolean {
  if (!p || !dir) return false;
  try {
    const full = path.resolve(p);
    const root = path.resolve(dir).replace(/[/\\]+$/, "") + path.sep;
    return full.toLowerCase().startsWith(root.toLowerCase()) || full === path.resolve(dir);
  } catch {
    return false;
  }
}

export function newMediaPath(dir: string, prefix: string, ext: string, utcNow = new Date()): string {
  fs.mkdirSync(dir, { recursive: true });
  if (!prefix) prefix = "Rec";
  if (!ext) ext = ".mp4";
  if (!ext.startsWith(".")) ext = "." + ext;
  const stamp = utcNow.toISOString().replace(/[-:]/g, "").replace("T", "_").slice(0, 15);
  let p = path.join(dir, `${prefix}_${stamp}${ext}`);
  if (fs.existsSync(p)) p = path.join(dir, `${prefix}_${stamp}-${Math.random().toString(16).slice(2, 8)}${ext}`);
  writeSidecar(p, false, utcNow);
  return p;
}

export function markComplete(media: string, utcNow = new Date()): void {
  if (!media) return;
  writeSidecar(media, true, utcNow);
}

export function isReady(media: string): boolean {
  if (!media || !fs.existsSync(media)) return false;
  try {
    if (fs.statSync(media).size <= 0) return false;
  } catch {
    return false;
  }
  const sc = sidecarPath(media);
  if (!fs.existsSync(sc)) return true;
  try {
    const t = fs.readFileSync(sc, "utf8");
    return /"complete"\s*:\s*true/i.test(t);
  } catch {
    return true;
  }
}

export function listReady(dir: string): string[] {
  if (!dir || !fs.existsSync(dir)) return [];
  return fs
    .readdirSync(dir)
    .map((n) => path.join(dir, n))
    .filter((f) => !f.endsWith(".meta.json") && !f.endsWith(".16k.wav") && !isTranscriptPath(f) && isReady(f))
    .sort((a, b) => a.localeCompare(b));
}

export function cleanup(dir: string, maxAgeMs = DEFAULT_MAX_AGE_MS, now = Date.now()): string[] {
  const deleted: string[] = [];
  if (!dir || !fs.existsSync(dir)) return deleted;
  for (const n of fs.readdirSync(dir)) {
    const f = path.join(dir, n);
    let t = 0;
    try {
      t = fs.statSync(f).mtimeMs;
    } catch {
      continue;
    }
    if (now - t < maxAgeMs) continue;
    try {
      fs.unlinkSync(f);
      deleted.push(f);
    } catch {
      /* ignore */
    }
  }
  return deleted;
}

export function deletePair(media: string): void {
  if (!media) return;
  for (const f of [media, sidecarPath(media), media + ".16k.wav"]) {
    try {
      if (fs.existsSync(f)) fs.unlinkSync(f);
    } catch {
      /* ignore */
    }
  }
  deleteTranscript(media);
}

function writeSidecar(media: string, complete: boolean, utc: Date): void {
  try {
    fs.writeFileSync(sidecarPath(media), JSON.stringify({ complete, utc: utc.toISOString() }));
  } catch {
    /* ignore */
  }
}
