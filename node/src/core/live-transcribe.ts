/** Rolling ffmpeg pulse capture → optional whisper-cli. Chunk wav files are deleted immediately. */
import { execFileSync, spawn, type ChildProcess } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { chunkAbsStart, FIRST_WINDOW_SECONDS, stitch, WINDOW_SECONDS, extendCommitted } from "./live-chunk.js";
import { localAppDataDir } from "./paths.js";
import { composeWhisperPrompt, type TranscriptCue } from "./transcript.js";
import { parseNvidiaSmi, pickWhisper } from "./whisper-auto.js";

function which(bin: string): string | null {
  const pathEnv = process.env.PATH || "";
  for (const dir of pathEnv.split(path.delimiter)) {
    const p = path.join(dir, bin);
    if (fs.existsSync(p)) return p;
  }
  return null;
}

export function findWhisper(): string | null {
  return which("whisper-cli") || which("whisper-cpp") || which("main") || which("whisper");
}

export class LiveTranscribe {
  private proc: ChildProcess | null = null;
  private dir = "";
  private timer: NodeJS.Timeout | null = null;
  private index = 0;
  private committed = 0;
  private paused = false;
  cues: TranscriptCue[] = [];
  onCaption?: (text: string) => void;

  async start(opts: { mic?: boolean; system?: boolean }): Promise<void> {
    this.cues = [];
    this.index = 0;
    this.committed = 0;
    this.paused = false;
    this.dir = fs.mkdtempSync(path.join(os.tmpdir(), "bestsnap-live-"));
    const out = path.join(this.dir, "chunk_%03d.wav");
    const src = opts.system && !opts.mic ? "auto_null.monitor" : "default";
    this.proc = spawn("ffmpeg", ["-y", "-f", "pulse", "-i", src, "-ac", "1", "-ar", "16000", "-f", "segment", "-segment_time", String(FIRST_WINDOW_SECONDS), "-reset_timestamps", "1", out], {
      stdio: ["ignore", "ignore", "pipe"],
    });
    this.timer = setInterval(() => void this.drain(), 800);
  }

  pause(): void {
    this.paused = true;
    try { this.proc?.kill("SIGSTOP"); } catch { /* ignore */ }
  }
  resume(): void {
    this.paused = false;
    try { this.proc?.kill("SIGCONT"); } catch { /* ignore */ }
  }

  async stop(): Promise<TranscriptCue[]> {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    try { this.proc?.kill("SIGTERM"); } catch { /* ignore */ }
    this.proc = null;
    await this.drain();
    try { fs.rmSync(this.dir, { recursive: true, force: true }); } catch { /* ignore */ }
    return this.cues;
  }

  private async drain(): Promise<void> {
    if (this.paused || !this.dir || !fs.existsSync(this.dir)) return;
    const files = fs.readdirSync(this.dir).filter((n) => n.endsWith(".wav")).sort();
    // keep the newest (in-progress) file; transcribe completed ones
    for (const n of files.slice(0, Math.max(0, files.length - 1))) {
      const f = path.join(this.dir, n);
      try {
        await this.transcribeFile(f, chunkAbsStart(this.index));
      } finally {
        try { fs.unlinkSync(f); } catch { /* ignore */ }
      }
      this.index++;
    }
  }

  private async transcribeFile(file: string, absStart: number): Promise<void> {
    const bin = findWhisper();
    if (!bin) return;
    const modelDir = path.join(localAppDataDir(), "models", "whisper");
    let smi = "";
    try {
      smi = execFileSync("nvidia-smi", ["--query-gpu=name,memory.total", "--format=csv,noheader,nounits"], { encoding: "utf8", timeout: 2000 });
    } catch { /* no NVIDIA driver */ }
    const pick = pickWhisper(
      {
        adapters: parseNvidiaSmi(smi),
        ramBytes: os.totalmem(),
        cpuCores: os.cpus().length,
        cudaRuntime: false,
        vulkanRuntime: false,
      },
      true,
    );
    const preferred = [pick.modelId, "large-v3-turbo", "large-v3-turbo-q5_0", "medium", "medium-q5_0", "small", "base", "tiny"];
    const model = preferred.map((id) => path.join(modelDir, `ggml-${id}.bin`)).find((p) => fs.existsSync(p));
    const lang = "auto";
    const prev = this.cues.slice(-4).map((c) => c.text).filter(Boolean).join(" ");
    const prompt = composeWhisperPrompt(lang, "", prev);
    const args = ["-f", file, "-nt", "-bs", "5", "-l", lang];
    if (model) args.unshift("-m", model);
    if (prompt) args.push("--prompt", prompt);
    const text = await new Promise<string>((resolve) => {
      const p = spawn(bin, args, { stdio: ["ignore", "pipe", "pipe"] });
      let out = "";
      p.stdout?.on("data", (d: Buffer) => { out += d.toString(); });
      p.on("close", () => resolve(out));
      setTimeout(() => { try { p.kill("SIGKILL"); } catch { /* ignore */ } resolve(out); }, 20000);
    });
    const incoming: TranscriptCue[] = [];
    const t = text.trim();
    if (t) incoming.push({ start: 0, end: WINDOW_SECONDS, text: t.replace(/\s+/g, " ") });
    const added = stitch(incoming, absStart, this.committed);
    this.cues.push(...added);
    this.committed = extendCommitted(added, this.committed);
    if (added.length) this.onCaption?.(added[added.length - 1].text);
  }
}
