/** Audio-only recorder via ffmpeg pulse (mic and/or sink monitor). WAV or MP3. */
import { spawn, type ChildProcess } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

export interface AudioRecOpts {
  out: string;
  mic?: boolean;
  system?: boolean;
  micDevice?: string;
  systemDevice?: string;
}

export function listPulseSources(): { name: string; desc: string; monitor: boolean }[] {
  try {
    const out = execFileSync("pactl", ["list", "short", "sources"], { encoding: "utf8", timeout: 4000 });
    return out
      .split("\n")
      .map((l) => l.trim())
      .filter(Boolean)
      .map((l) => {
        const cols = l.split("\t");
        const name = cols[1] || cols[0];
        return { name, desc: name, monitor: name.includes(".monitor") };
      });
  } catch {
    return [];
  }
}

export class AudioRecorder {
  private child: ChildProcess | null = null;
  private stopped = false;
  private paused = false;

  start(opts: AudioRecOpts): Promise<void> {
    return new Promise((resolve, reject) => {
      const out = path.resolve(opts.out);
      fs.mkdirSync(path.dirname(out), { recursive: true });
      const sys = !!opts.system;
      const mic = opts.mic === true || (opts.mic !== false && !sys);
      const args: string[] = ["-y", "-hide_banner", "-loglevel", "error"];
      let inputs = 0;
      if (mic || (!mic && !sys)) {
        args.push("-f", "pulse", "-i", opts.micDevice || "default");
        inputs++;
      }
      if (sys) {
        args.push("-f", "pulse", "-i", opts.systemDevice || defaultMonitor());
        inputs++;
      }
      if (inputs === 2) args.push("-filter_complex", "amix=inputs=2:duration=longest");
      const mp3 = out.toLowerCase().endsWith(".mp3");
      if (mp3) args.push("-c:a", "libmp3lame", "-b:a", "192k");
      else args.push("-c:a", "pcm_s16le");
      args.push(out);
      const child = spawn("ffmpeg", args, { stdio: ["ignore", "pipe", "pipe"] });
      this.child = child;
      child.on("error", reject);
      child.on("spawn", () => resolve());
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
    this.child = null;
  }
}

function defaultMonitor(): string {
  const src = listPulseSources().find((s) => s.monitor);
  return src?.name || "default";
}

void AudioRecorder;
