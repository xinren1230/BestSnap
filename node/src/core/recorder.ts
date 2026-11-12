/** Screen recording via ffmpeg x11grab. MP4 (H.264) or GIF. */
import { spawn, type ChildProcess } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { screenSize, type Rect, windowGeometry } from "./capture.js";
import { listPulseSources } from "./audio-recorder.js";

function x11Display(): string {
  return process.env.DISPLAY || ":0";
}

export interface RecordOpts {
  out: string;
  durationSec?: number;
  fps?: number;
  quality?: number; // 0-100, mapped to x264 crf
  region?: Rect;
  windowId?: string;
  showCursor?: boolean;
  mic?: boolean;
  systemAudio?: boolean;
  gif?: boolean;
  gifMaxWidth?: number;
}

export class Recorder {
  private child: ChildProcess | null = null;
  private stopped = false;
  private paused = false;

  start(opts: RecordOpts): Promise<void> {
    return new Promise((resolve, reject) => {
      const out = path.resolve(opts.out);
      fs.mkdirSync(path.dirname(out), { recursive: true });
      let r = opts.region;
      if (!r && opts.windowId) r = windowGeometry(opts.windowId) ?? undefined;
      if (!r) {
        const s = screenSize();
        r = { x: 0, y: 0, w: s.w, h: s.h };
      }
      r = { x: r.x, y: r.y, w: r.w - (r.w % 2), h: r.h - (r.h % 2) };
      if (r.w < 2 || r.h < 2) {
        reject(new Error("recording region too small"));
        return;
      }
      const fps = Math.max(1, opts.fps ?? 30);
      const gif = opts.gif || out.toLowerCase().endsWith(".gif");
      const args: string[] = [
        "-y",
        "-hide_banner",
        "-loglevel",
        "error",
        "-f",
        "x11grab",
        "-show_region",
        "0",
        "-draw_mouse",
        opts.showCursor ? "1" : "0",
        "-framerate",
        String(gif ? Math.min(fps, opts.fps ?? 10) : fps),
        "-video_size",
        `${r.w}x${r.h}`,
        "-i",
        `${x11Display()}.0+${r.x},${r.y}`,
      ];
      let ain = 0;
      if (opts.mic && !gif) {
        args.push("-f", "pulse", "-i", "default");
        ain++;
      }
      if (opts.systemAudio && !gif) {
        const mon = process.env.BESTSNAP_PULSE_MONITOR || listPulseSources().find((s) => s.monitor)?.name || "default";
        args.push("-f", "pulse", "-i", mon);
        ain++;
      }
      if (ain === 2) args.push("-filter_complex", "amix=inputs=2:duration=longest");
      if (opts.durationSec && opts.durationSec > 0) args.push("-t", String(opts.durationSec));
      if (gif) {
        const maxW = opts.gifMaxWidth ?? 960;
        args.push("-vf", `fps=${Math.min(15, fps)},scale='min(${maxW},iw)':-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse`, out);
      } else {
        const q = opts.quality ?? 70;
        const crf = Math.round(18 + ((100 - q) / 100) * 20);
        args.push("-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", String(crf), "-preset", "veryfast", out);
      }
      const child = spawn("ffmpeg", args, { stdio: ["ignore", "pipe", "pipe"] });
      this.child = child;
      let err = "";
      child.stderr?.on("data", (d) => {
        err += d.toString();
      });
      child.on("error", reject);
      child.on("spawn", () => resolve());
      child.on("close", (code) => {
        this.child = null;
        if (!this.stopped && code && code !== 0 && code !== 255) {
          /* completed with error after start — callers wait on wait() */
        }
        void err;
      });
    });
  }

  pause(): void {
    if (!this.child?.pid || this.paused) return;
    try {
      process.kill(this.child.pid, "SIGSTOP");
      this.paused = true;
    } catch {
      /* ignore */
    }
  }

  resume(): void {
    if (!this.child?.pid || !this.paused) return;
    try {
      process.kill(this.child.pid, "SIGCONT");
      this.paused = false;
    } catch {
      /* ignore */
    }
  }

  async stop(): Promise<void> {
    this.stopped = true;
    if (this.paused) this.resume();
    const c = this.child;
    if (!c || c.killed) return;
    c.kill("SIGINT");
    await new Promise<void>((resolve) => {
      const t = setTimeout(() => {
        c.kill("SIGKILL");
        resolve();
      }, 8000);
      c.on("close", () => {
        clearTimeout(t);
        resolve();
      });
    });
  }

  wait(): Promise<void> {
    const c = this.child;
    if (!c) return Promise.resolve();
    return new Promise((resolve, reject) => {
      c.on("close", (code) => {
        if (code && code !== 0 && code !== 255 && !this.stopped) reject(new Error("ffmpeg exit " + code));
        else resolve();
      });
    });
  }
}

export async function recordOnce(opts: RecordOpts): Promise<string> {
  const rec = new Recorder();
  await rec.start(opts);
  if (opts.durationSec && opts.durationSec > 0) {
    await rec.wait();
  }
  const out = path.resolve(opts.out);
  if (!fs.existsSync(out) || fs.statSync(out).size === 0) throw new Error("recording produced an empty file: " + out);
  return out;
}

void x11Display;
