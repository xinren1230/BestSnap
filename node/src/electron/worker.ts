/** JSON-line worker so Electron can run ONNX under system Node (not Electron's ABI). */
import fs from "node:fs";
import readline from "node:readline";
import { recognize, recognizeWords } from "../core/ocr.js";
import { recognizeLatex } from "../core/formula.js";
import { decodeCodes } from "../core/qr.js";
import { findInWords } from "../core/auto-redact.js";
import { translateText, openAiLatex, mathpixLatex } from "../core/translator.js";
import { loadSettings } from "../core/settings.js";
import { cleanChatLatex } from "../core/formula-latex.js";

type Req = { id: number; cmd: string; args?: Record<string, unknown> };

async function handle(req: Req): Promise<unknown> {
  const a = req.args ?? {};
  const buf = (p: unknown) => (typeof p === "string" ? (p.startsWith("/") || p.includes("\\") ? fs.readFileSync(p) : Buffer.from(p, "base64")) : Buffer.alloc(0));
  switch (req.cmd) {
    case "ocr":
      return { text: await recognize(buf(a.image)) };
    case "ocr-words":
      return { words: await recognizeWords(buf(a.image)) };
    case "redact": {
      const words = await recognizeWords(buf(a.image));
      return { hits: findInWords(words) };
    }
    case "latex": {
      const s = loadSettings();
      const png = buf(a.image);
      if (s.formula.engine === "OpenAI") return { text: cleanChatLatex(await openAiLatex(png, s)) };
      if (s.formula.engine === "Mathpix") return { text: cleanChatLatex(await mathpixLatex(png, s)) };
      return { text: await recognizeLatex(png) };
    }
    case "qr":
      return { hits: await decodeCodes(buf(a.image)) };
    case "translate":
      return { text: await translateText(String(a.text ?? ""), (a.target as "zh" | "en") || "zh", loadSettings()) };
    case "ping":
      return { ok: true };
    default:
      throw new Error("unknown cmd " + req.cmd);
  }
}

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
rl.on("line", async (line) => {
  let req: Req;
  try {
    req = JSON.parse(line) as Req;
  } catch {
    return;
  }
  try {
    const result = await handle(req);
    process.stdout.write(JSON.stringify({ id: req.id, ok: true, result }) + "\n");
  } catch (e) {
    process.stdout.write(JSON.stringify({ id: req.id, ok: false, error: e instanceof Error ? e.message : String(e) }) + "\n");
  }
});
