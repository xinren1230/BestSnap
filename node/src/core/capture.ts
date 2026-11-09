/**
 * X11 screen capture via ffmpeg x11grab (fullscreen / region / window).
 */
import { execFile, execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { promisify } from "node:util";

const exec = promisify(execFile);

export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface WinInfo {
  id: string;
  title: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

function display(): string {
  return process.env.DISPLAY || ":0";
}

export function screenSize(): { w: number; h: number } {
  try {
    const out = execFileSync("xdpyinfo", ["-display", display()], { encoding: "utf8", timeout: 5000 });
    const m = out.match(/dimensions:\s+(\d+)x(\d+)/);
    if (m) return { w: parseInt(m[1], 10), h: parseInt(m[2], 10) };
  } catch {
    /* fall through */
  }
  try {
    const out = execFileSync("xrandr", [], { encoding: "utf8", timeout: 5000 });
    const m = out.match(/current\s+(\d+)\s+x\s+(\d+)/);
    if (m) return { w: parseInt(m[1], 10), h: parseInt(m[2], 10) };
  } catch {
    /* default */
  }
  return { w: 1920, h: 1080 };
}

async function ffmpegGrab(args: string[], outFile: string): Promise<Buffer> {
  await exec("ffmpeg", ["-y", "-hide_banner", "-loglevel", "error", ...args, outFile], { timeout: 20000 });
  const buf = fs.readFileSync(outFile);
  fs.rmSync(outFile, { force: true });
  return buf;
}

export async function captureFull(includeCursor = false): Promise<Buffer> {
  const { w, h } = screenSize();
  const tmp = path.join(os.tmpdir(), `snapforge-full-${process.pid}.png`);
  return ffmpegGrab(
    ["-f", "x11grab", "-show_region", "0", "-draw_mouse", includeCursor ? "1" : "0", "-video_size", `${w}x${h}`, "-i", `${display()}.0+0,0`, "-frames:v", "1"],
    tmp,
  );
}

export async function captureRect(r: Rect, includeCursor = false): Promise<Buffer> {
  if (r.w < 1 || r.h < 1) throw new Error("region width/height must be > 0");
  const tmp = path.join(os.tmpdir(), `snapforge-rect-${process.pid}.png`);
  return ffmpegGrab(
    [
      "-f",
      "x11grab",
      "-show_region",
      "0",
      "-draw_mouse",
      includeCursor ? "1" : "0",
      "-video_size",
      `${r.w}x${r.h}`,
      "-i",
      `${display()}.0+${r.x},${r.y}`,
      "-frames:v",
      "1",
    ],
    tmp,
  );
}

export function listWindows(): WinInfo[] {
  try {
    const tree = execFileSync("xwininfo", ["-root", "-tree"], { encoding: "utf8", timeout: 8000 });
    const wins: WinInfo[] = [];
    const re = /^\s+(0x[0-9a-f]+)\s+"([^"]*)".*?\s+(\d+)x(\d+)\+(\-?\d+)\+(\-?\d+)/gim;
    let m: RegExpExecArray | null;
    while ((m = re.exec(tree))) {
      const w = parseInt(m[3], 10),
        h = parseInt(m[4], 10);
      if (w < 40 || h < 40) continue;
      const title = m[2];
      if (!title || title === "(has no name)") continue;
      wins.push({ id: m[1], title, w, h, x: parseInt(m[5], 10), y: parseInt(m[6], 10) });
    }
    return wins;
  } catch {
    return [];
  }
}

export function activeWindowId(): string | null {
  try {
    const id = execFileSync("xdotool", ["getactivewindow"], { encoding: "utf8", timeout: 3000 }).trim();
    return id ? "0x" + parseInt(id, 10).toString(16) : null;
  } catch {
    return null;
  }
}

export function windowGeometry(id: string): Rect | null {
  try {
    const out = execFileSync("xwininfo", ["-id", id], { encoding: "utf8", timeout: 3000 });
    const absX = out.match(/Absolute upper-left X:\s+(\-?\d+)/);
    const absY = out.match(/Absolute upper-left Y:\s+(\-?\d+)/);
    const w = out.match(/Width:\s+(\d+)/);
    const h = out.match(/Height:\s+(\d+)/);
    if (!absX || !absY || !w || !h) return null;
    return { x: parseInt(absX[1], 10), y: parseInt(absY[1], 10), w: parseInt(w[1], 10), h: parseInt(h[1], 10) };
  } catch {
    return null;
  }
}

export async function captureWindow(id?: string, includeCursor = false): Promise<Buffer> {
  const wid = id || activeWindowId();
  if (!wid) throw new Error("no window");
  const g = windowGeometry(wid);
  if (!g) throw new Error("cannot read window geometry");
  return captureRect(g, includeCursor);
}

export async function captureToFile(kind: "full" | "window" | "region", outPath: string, opts: { region?: Rect; windowId?: string; cursor?: boolean } = {}): Promise<string> {
  let buf: Buffer;
  if (kind === "full") buf = await captureFull(opts.cursor);
  else if (kind === "window") buf = await captureWindow(opts.windowId, opts.cursor);
  else {
    if (!opts.region) throw new Error("--region needs x,y,w,h");
    buf = await captureRect(opts.region, opts.cursor);
  }
  fs.mkdirSync(path.dirname(path.resolve(outPath)), { recursive: true });
  fs.writeFileSync(outPath, buf);
  return path.resolve(outPath);
}
