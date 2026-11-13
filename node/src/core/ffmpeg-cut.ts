import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { buildArgs, type TimeRange } from "./trim-range.js";

export function findFfmpeg(): string | null {
  const env = process.env.BESTSNAP_FFMPEG || process.env.FFMPEG;
  if (env && fs.existsSync(env)) return env;
  for (const n of ["ffmpeg"]) {
    const dirs = (process.env.PATH || "").split(path.delimiter);
    for (const d of dirs) {
      const p = path.join(d, n);
      if (fs.existsSync(p)) return p;
    }
  }
  return null;
}

export function cut(input: string, output: string, keep: TimeRange[], video: boolean, audio: boolean): Promise<void> {
  const ff = findFfmpeg();
  if (!ff) return Promise.reject(new Error("ffmpeg not found"));
  const args = buildArgs(input, output, keep, video, audio);
  fs.mkdirSync(path.dirname(path.resolve(output)), { recursive: true });
  return new Promise((resolve, reject) => {
    const p = spawn(ff, args, { stdio: ["ignore", "pipe", "pipe"] });
    let err = "";
    p.stderr?.on("data", (d) => {
      err += d.toString();
    });
    p.on("error", reject);
    p.on("close", (code) => {
      if (code) reject(new Error("ffmpeg: " + err.trim()));
      else resolve();
    });
  });
}
