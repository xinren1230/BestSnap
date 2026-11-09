#!/usr/bin/env node
/**
 * Headless SnapForge CLI for Linux.
 *
 *   snapforge ocr <img> [-o out.txt]
 *   snapforge latex <img> [-o out.tex]
 *   snapforge qr <img>
 *   snapforge capture --full|--window|--region x,y,w,h [-o out.png]
 *   snapforge record --duration s --out file.mp4 [--full|--region x,y,w,h]
 *
 * Also accepts the Windows-style flags (--ocr, --latex, --fullscreen, --save, --image, …).
 */
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { captureToFile, type Rect } from "../core/capture.js";
import { saveImage } from "../core/exporter.js";
import { recognizeLatex } from "../core/formula.js";
import { recognize } from "../core/ocr.js";
import { ocrAvailable } from "../core/bundled-models.js";
import { decodeCodes } from "../core/qr.js";
import { recordOnce } from "../core/recorder.js";
import { loadSettings } from "../core/settings.js";

export const Exit = { Ok: 0, Error: 1, BadArgs: 2, Cancelled: 3, OcrUnavailable: 4, Busy: 5 } as const;

function usage(): string {
  return `SnapForge (Node.js / Linux)

Usage:
  snapforge ocr <image> [-o out.txt]
  snapforge latex <image> [-o out.tex]
  snapforge qr <image>
  snapforge capture --full|--window|--region x,y,w,h [-o out.png] [--cursor]
  snapforge record --duration <s> --out <file.mp4|file.gif> [--full|--region x,y,w,h] [--cursor]
  snapforge --help

Windows-compatible flags (run against a file or the screen):
  snapforge --image file.png --ocr out.txt [--latex out.tex]
  snapforge --fullscreen|--window|--region x,y,w,h [--save path] [--ocr out.txt] [--latex out.tex]
  snapforge --record --duration s --out file.mp4 [--fullscreen|--region x,y,w,h]
`;
}

function die(code: number, msg: string): never {
  process.stderr.write(msg + "\n");
  process.exit(code);
}

function next(args: string[], i: number, name: string): [string, number] {
  if (i + 1 >= args.length) die(Exit.BadArgs, name + " needs a value");
  return [args[i + 1], i + 1];
}

function parseRegion(s: string): Rect {
  const p = s.split(",");
  if (p.length !== 4) die(Exit.BadArgs, "--region expects x,y,w,h");
  const [x, y, w, h] = p.map((n) => parseInt(n, 10));
  if (![x, y, w, h].every((n) => Number.isFinite(n)) || w < 1 || h < 1) die(Exit.BadArgs, "--region: width/height must be > 0");
  return { x, y, w, h };
}

async function readImage(p: string): Promise<Buffer> {
  const abs = path.resolve(p);
  if (!fs.existsSync(abs)) die(Exit.Error, "file not found: " + abs);
  return fs.readFileSync(abs);
}

function writeText(dest: string | undefined, text: string, utf8bom = false): void {
  if (dest) {
    fs.mkdirSync(path.dirname(path.resolve(dest)), { recursive: true });
    const body = utf8bom ? Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from(text, "utf8")]) : Buffer.from(text, "utf8");
    fs.writeFileSync(dest, body);
  } else process.stdout.write(text.endsWith("\n") ? text : text + "\n");
}

export async function main(argv = process.argv.slice(2)): Promise<number> {
  if (argv.length === 0 || argv.includes("-h") || argv.includes("--help")) {
    process.stdout.write(usage());
    return Exit.Ok;
  }

  const cmd = argv[0];
  try {
    if (cmd === "ocr") {
      if (!ocrAvailable()) return Exit.OcrUnavailable;
      const img = argv[1];
      if (!img || img.startsWith("-")) die(Exit.BadArgs, "ocr needs an image path");
      let out: string | undefined;
      for (let i = 2; i < argv.length; i++) {
        if (argv[i] === "-o" || argv[i] === "--out") [out, i] = next(argv, i, argv[i]);
        else die(Exit.BadArgs, "unknown argument: " + argv[i]);
      }
      const text = await recognize(await readImage(img));
      writeText(out, text, true);
      return Exit.Ok;
    }
    if (cmd === "latex") {
      const img = argv[1];
      if (!img || img.startsWith("-")) die(Exit.BadArgs, "latex needs an image path");
      let out: string | undefined;
      for (let i = 2; i < argv.length; i++) {
        if (argv[i] === "-o" || argv[i] === "--out") [out, i] = next(argv, i, argv[i]);
        else die(Exit.BadArgs, "unknown argument: " + argv[i]);
      }
      const tex = await recognizeLatex(await readImage(img));
      writeText(out, tex, false);
      return Exit.Ok;
    }
    if (cmd === "qr") {
      const img = argv[1];
      if (!img || img.startsWith("-")) die(Exit.BadArgs, "qr needs an image path");
      const hits = await decodeCodes(await readImage(img));
      if (hits.length === 0) {
        process.stdout.write("未识别到二维码或条码。\n");
        return Exit.Ok;
      }
      process.stdout.write(hits.map((h) => `${h.format}\t${h.text}`).join("\n") + "\n");
      return Exit.Ok;
    }
    if (cmd === "capture") {
      let kind: "full" | "window" | "region" = "full";
      let region: Rect | undefined;
      let out = "capture.png";
      let cursor = false;
      for (let i = 1; i < argv.length; i++) {
        const a = argv[i];
        if (a === "--full" || a === "--fullscreen") kind = "full";
        else if (a === "--window") kind = "window";
        else if (a === "--region") {
          kind = "region";
          const [v, j] = next(argv, i, a);
          region = parseRegion(v);
          i = j;
        } else if (a === "-o" || a === "--out" || a === "--save") [out, i] = next(argv, i, a);
        else if (a === "--cursor") cursor = true;
        else if (!a.startsWith("-") && (a.endsWith(".png") || a.endsWith(".jpg"))) out = a;
        else die(Exit.BadArgs, "unknown argument: " + a);
      }
      await captureToFile(kind, out, { region, cursor });
      process.stdout.write(path.resolve(out) + "\n");
      return Exit.Ok;
    }
    if (cmd === "record") {
      let duration = 0;
      let out = "";
      let region: Rect | undefined;
      let cursor = true;
      let full = true;
      for (let i = 1; i < argv.length; i++) {
        const a = argv[i];
        if (a === "--duration" || a === "-t") {
          const [v, j] = next(argv, i, a);
          duration = parseInt(v, 10);
          i = j;
        } else if (a === "--out" || a === "-o") [out, i] = next(argv, i, a);
        else if (a === "--full" || a === "--fullscreen") full = true;
        else if (a === "--region") {
          full = false;
          const [v, j] = next(argv, i, a);
          region = parseRegion(v);
          i = j;
        } else if (a === "--cursor") cursor = true;
        else if (a === "--no-cursor") cursor = false;
        else die(Exit.BadArgs, "unknown argument: " + a);
      }
      if (duration < 1) die(Exit.BadArgs, "--record needs --duration s (>= 1)");
      if (!out) die(Exit.BadArgs, "--record needs --out file.mp4|.gif");
      void full;
      const file = await recordOnce({ out, durationSec: duration, region, showCursor: cursor, gif: out.toLowerCase().endsWith(".gif") });
      process.stdout.write(file + "\n");
      return Exit.Ok;
    }

    // Windows-style flags
    if (argv.some((a) => a.startsWith("--"))) {
      return await windowsStyle(argv);
    }
    die(Exit.BadArgs, "unknown command: " + cmd + "\n" + usage());
  } catch (e) {
    process.stderr.write((e instanceof Error ? e.message : String(e)) + "\n");
    return Exit.Error;
  }
}

async function windowsStyle(args: string[]): Promise<number> {
  let source: "fullscreen" | "window" | "region" | "image" = "fullscreen";
  let region: Rect | undefined;
  let image: string | undefined;
  let save: string | undefined;
  let format: string | undefined;
  let ocrOut: string | undefined;
  let latexOut: string | undefined;
  let record = false;
  let duration = 0;
  let out: string | undefined;
  let delay = 0;
  let clipboard = false;
  for (let i = 0; i < args.length; i++) {
    const a = args[i].toLowerCase();
    if (a === "--tray") continue;
    else if (a === "--fullscreen") source = "fullscreen";
    else if (a === "--window") source = "window";
    else if (a === "--interactive-region") die(Exit.BadArgs, "--interactive-region needs the Electron UI (npm start)");
    else if (a === "--region") {
      source = "region";
      const [v, j] = next(args, i, a);
      region = parseRegion(v);
      i = j;
    } else if (a === "--image") {
      source = "image";
      [image, i] = next(args, i, a);
    } else if (a === "--save") [save, i] = next(args, i, a);
    else if (a === "--format") [format, i] = next(args, i, a);
    else if (a === "--ocr") [ocrOut, i] = next(args, i, a);
    else if (a === "--latex") [latexOut, i] = next(args, i, a);
    else if (a === "--record") record = true;
    else if (a === "--duration") {
      const [v, j] = next(args, i, a);
      duration = parseInt(v, 10);
      i = j;
    } else if (a === "--out") [out, i] = next(args, i, a);
    else if (a === "--delay") {
      const [v, j] = next(args, i, a);
      delay = parseInt(v, 10);
      i = j;
    } else if (a === "--clipboard") clipboard = true;
    else die(Exit.BadArgs, "unknown argument: " + args[i]);
  }
  if (delay > 0) await new Promise((r) => setTimeout(r, delay * 1000));
  const S = loadSettings();
  if (record) {
    if (duration < 1) die(Exit.BadArgs, "--record needs --duration s (>= 1)");
    if (!out) die(Exit.BadArgs, "--record needs --out file.mp4|.gif");
    await recordOnce({
      out,
      durationSec: duration,
      region: source === "region" ? region : undefined,
      showCursor: S.includeCursor,
      gif: out.toLowerCase().endsWith(".gif"),
    });
    return Exit.Ok;
  }
  let png: Buffer;
  if (source === "image") {
    if (!image) die(Exit.BadArgs, "--image needs a path");
    png = await readImage(image);
  } else {
    const tmp = path.join(process.cwd(), `.snapforge-cli-${process.pid}.png`);
    const kind = source === "window" ? "window" : source === "region" ? "region" : "full";
    await captureToFile(kind, tmp, { region, cursor: S.includeCursor });
    png = fs.readFileSync(tmp);
    fs.rmSync(tmp, { force: true });
  }
  let any = false;
  if (save) {
    let dest = save;
    if (fs.existsSync(save) && fs.statSync(save).isDirectory() || save.endsWith("/") || save.endsWith("\\")) {
      fs.mkdirSync(save, { recursive: true });
      dest = path.join(save, `Snap_${Date.now()}.${format || "png"}`);
    } else if (format && !dest.toLowerCase().endsWith("." + format)) dest += "." + format;
    await saveImage(png, dest, S.jpegQuality);
    any = true;
  }
  if (ocrOut) {
    if (!ocrAvailable()) return Exit.OcrUnavailable;
    writeText(ocrOut, await recognize(png), true);
    any = true;
  }
  if (latexOut) {
    writeText(latexOut, await recognizeLatex(png), false);
    any = true;
  }
  if (clipboard) {
    process.stderr.write("clipboard copy is handled by the Electron UI on Linux; image saved if --save was given\n");
  }
  if (!any && !clipboard) {
    process.stdout.write("captured " + png.length + " bytes (pass --save/--ocr/--latex)\n");
  }
  return Exit.Ok;
}

const entry = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : "";
if (import.meta.url === entry || process.argv[1]?.endsWith("cli/index.ts") || process.argv[1]?.endsWith("cli/index.js")) {
  main().then((c) => process.exit(c), (e) => {
    console.error(e);
    process.exit(Exit.Error);
  });
}
