import path from "node:path";
import fs from "node:fs";

export interface TimeRange {
  start: number;
  end: number;
}

export const EPSILON = 1e-4;

export function tryCreate(start: number, end: number, duration: number): TimeRange | null {
  if (!Number.isFinite(start) || !Number.isFinite(end) || !Number.isFinite(duration) || duration <= 0) return null;
  let a = Math.min(start, end),
    b = Math.max(start, end);
  a = Math.min(Math.max(a, 0), duration);
  b = Math.min(Math.max(b, 0), duration);
  if (b - a < EPSILON) return null;
  return { start: a, end: b };
}

export function merge(ranges: TimeRange[], duration: number, minGap = 0): TimeRange[] {
  const list: TimeRange[] = [];
  if (!ranges || duration <= 0) return list;
  for (const r of ranges) {
    const n = tryCreate(r.start, r.end, duration);
    if (n) list.push(n);
  }
  list.sort((x, y) => x.start - y.start);
  const merged: TimeRange[] = [];
  for (const r of list) {
    const last = merged[merged.length - 1];
    if (!last || r.start > last.end + minGap + EPSILON) merged.push({ ...r });
    else last.end = Math.max(last.end, r.end);
  }
  return merged;
}

export function keepFromRemove(duration: number, remove: TimeRange[]): TimeRange[] {
  const cuts = merge(remove, duration);
  const keep: TimeRange[] = [];
  if (duration <= 0) return keep;
  let t = 0;
  for (const c of cuts) {
    if (c.start > t + EPSILON) keep.push({ start: t, end: c.start });
    if (c.end > t) t = c.end;
  }
  if (duration > t + EPSILON) keep.push({ start: t, end: duration });
  return keep;
}

export function totalDuration(ranges: TimeRange[]): number {
  return (ranges || []).reduce((s, r) => s + Math.max(0, r.end - r.start), 0);
}

export function defaultOutputPath(original: string, suffix = "_trimmed", exists: (p: string) => boolean = (p) => fs.existsSync(p)): string {
  const dir = path.dirname(original);
  const name = path.basename(original, path.extname(original));
  const ext = path.extname(original);
  let p = path.join(dir, name + suffix + ext);
  if (!exists(p) && path.resolve(p) !== path.resolve(original)) return p;
  for (let n = 2; n < 10000; n++) {
    p = path.join(dir, `${name}${suffix}-${n}${ext}`);
    if (!exists(p) && path.resolve(p) !== path.resolve(original)) return p;
  }
  return path.join(dir, `${name}${suffix}-${Date.now()}${ext}`);
}

export function canTrimPath(p: string): boolean {
  const e = path.extname(p).toLowerCase();
  return [".mp4", ".wav", ".mp3", ".m4a", ".mov", ".mkv"].includes(e);
}

export function isAudioPath(p: string): boolean {
  const e = path.extname(p).toLowerCase();
  return [".wav", ".mp3", ".m4a", ".aac", ".flac"].includes(e);
}

function ts(s: number): string {
  return String(Number(s.toFixed(3)));
}

export function buildFilter(keep: TimeRange[], video: boolean, audio: boolean): string {
  if (!keep.length) throw new Error("need at least one keep segment");
  const parts: string[] = [];
  keep.forEach((r, i) => {
    if (video) parts.push(`[0:v]trim=start=${ts(r.start)}:end=${ts(r.end)},setpts=PTS-STARTPTS[v${i}]`);
    if (audio) parts.push(`[0:a]atrim=start=${ts(r.start)}:end=${ts(r.end)},asetpts=PTS-STARTPTS[a${i}]`);
  });
  let concat = "";
  keep.forEach((_, i) => {
    if (video) concat += `[v${i}]`;
    if (audio) concat += `[a${i}]`;
  });
  concat += `concat=n=${keep.length}:v=${video ? 1 : 0}:a=${audio ? 1 : 0}`;
  if (video) concat += "[outv]";
  if (audio) concat += "[outa]";
  return parts.join(";") + ";" + concat;
}

export function buildArgs(input: string, output: string, keep: TimeRange[], video: boolean, audio: boolean): string[] {
  if (!keep.length) throw new Error("need at least one keep segment");
  const args = ["-y", "-hide_banner", "-loglevel", "error", "-i", input];
  const ext = path.extname(output).toLowerCase();
  if (keep.length === 1) args.push("-ss", ts(keep[0].start), "-to", ts(keep[0].end));
  else {
    args.push("-filter_complex", buildFilter(keep, video, audio));
    if (video) args.push("-map", "[outv]");
    if (audio) args.push("-map", "[outa]");
  }
  if (video) args.push("-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p");
  if (audio) {
    if (ext === ".wav") args.push("-c:a", "pcm_s16le");
    else if (ext === ".mp3") args.push("-c:a", "libmp3lame", "-b:a", "192k");
    else args.push("-c:a", "aac", "-b:a", "192k");
  }
  if (video) args.push("-movflags", "+faststart");
  args.push(output);
  return args;
}
