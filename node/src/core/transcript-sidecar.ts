import fs from "node:fs";
import path from "node:path";
import { isAudioPath } from "./trim-range.js";
import { plainText, type TranscriptCue } from "./transcript.js";

const UTF8_BOM = Buffer.from([0xef, 0xbb, 0xbf]);

export function transcriptPathFor(media: string): string {
  if (!media) return "";
  const p = path.parse(media);
  return path.join(p.dir, p.name + ".txt");
}

export function isTranscriptPath(p: string): boolean {
  return !!p && path.extname(p).toLowerCase() === ".txt";
}

export function shouldAutoTranscribe(settingOn: boolean, mediaPath: string): boolean {
  return !!settingOn && isAudioPath(mediaPath);
}

export function writeUtf8Bom(file: string, text: string): void {
  if (!file) return;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, Buffer.concat([UTF8_BOM, Buffer.from(text ?? "", "utf8")]));
}

export function writeForMedia(media: string, text: string): void {
  writeUtf8Bom(transcriptPathFor(media), text);
}

export function writeCues(media: string, cues: TranscriptCue[]): void {
  writeForMedia(media, plainText(cues));
}

export function hasUtf8Bom(bytes: Uint8Array | Buffer): boolean {
  return !!bytes && bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf;
}

export function copyWithMedia(fromMedia: string, toMedia: string): boolean {
  if (!fromMedia || !toMedia) return false;
  const src = transcriptPathFor(fromMedia);
  const dst = transcriptPathFor(toMedia);
  if (!src || !fs.existsSync(src)) return false;
  try {
    if (path.resolve(src).toLowerCase() === path.resolve(dst).toLowerCase()) return true;
    fs.mkdirSync(path.dirname(dst), { recursive: true });
    fs.copyFileSync(src, dst);
    return true;
  } catch {
    return false;
  }
}

export function deleteTranscript(media: string): void {
  const p = transcriptPathFor(media);
  if (!p) return;
  try {
    if (fs.existsSync(p)) fs.unlinkSync(p);
  } catch {
    /* ignore */
  }
}
