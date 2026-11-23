import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { defaultOutputPath } from "./trim-range.js";
import { copyWithMedia, deleteTranscript } from "./transcript-sidecar.js";

export const EDITED_SUFFIX = "-edited";

export function uniqueInFolder(folder: string, stem: string, ext: string): string {
  if (!folder) throw new Error("folder required");
  fs.mkdirSync(folder, { recursive: true });
  if (!stem) stem = "Rec";
  if (!ext) ext = ".mp4";
  if (!ext.startsWith(".")) ext = "." + ext;
  let dest = path.join(folder, stem + ext);
  for (let i = 2; fs.existsSync(dest); i++) dest = path.join(folder, `${stem} (${i})${ext}`);
  return path.resolve(dest);
}

export function defaultEditedPath(original: string, exportExt?: string): string {
  if (!original) throw new Error("original path required");
  let ext = exportExt || path.extname(original) || ".mp4";
  if (!ext.startsWith(".")) ext = "." + ext;
  const dir = path.dirname(path.resolve(original));
  const stem = path.basename(original, path.extname(original));
  return defaultOutputPath(path.join(dir, stem + ext), EDITED_SUFFIX);
}

export function resolveDestFolder(audio: boolean, recordingFolder?: string, audioFolder?: string, defaultFolder?: string): string {
  if (audio && audioFolder) return audioFolder;
  if (!audio && recordingFolder) return recordingFolder;
  if (defaultFolder) return defaultFolder;
  return path.join(os.homedir(), audio ? "Music" : "Videos", "BestSnap");
}

export function promoteToFolder(source: string, destFolder: string): string {
  if (!source || !fs.existsSync(source)) return source;
  if (!destFolder) destFolder = path.dirname(path.resolve(source));
  fs.mkdirSync(destFolder, { recursive: true });
  const fullSrc = path.resolve(source);
  const fullDestDir = path.resolve(destFolder);
  if (path.resolve(path.dirname(fullSrc)).toLowerCase() === fullDestDir.toLowerCase()) return fullSrc;
  const dest = uniqueInFolder(fullDestDir, path.basename(fullSrc, path.extname(fullSrc)), path.extname(fullSrc));
  fs.copyFileSync(fullSrc, dest);
  copyWithMedia(fullSrc, dest);
  for (const extra of [fullSrc + ".meta.json", fullSrc + ".16k.wav"]) {
    try {
      if (fs.existsSync(extra)) fs.unlinkSync(extra);
    } catch {
      /* ignore */
    }
  }
  deleteTranscript(fullSrc);
  try {
    fs.unlinkSync(fullSrc);
  } catch {
    /* ignore */
  }
  return dest;
}
