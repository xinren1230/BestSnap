import fs from "node:fs";
import path from "node:path";
import sharp from "sharp";
import type { AppSettings } from "./settings.js";

export const PLACEHOLDERS = ["n", "yyyy", "yy", "MM", "dd", "HH", "mm", "ss", "fff", "yyyyMMdd", "HHmmss", "yyyy-MM-dd", "HH-mm-ss", "date", "time"];

function pad(n: number, w: number): string {
  return String(n).padStart(w, "0");
}

export function expandTemplate(s: AppSettings, now = new Date()): string {
  const n = s.counter;
  const tmpl = s.fileNameTemplate?.trim() ? s.fileNameTemplate : "Snap_{n}";
  let name = tmpl.replace(/\{([\w\-]+)\}/g, (m, t: string) => {
    if (!PLACEHOLDERS.includes(t)) return m;
    switch (t) {
      case "n":
        return pad(n, 4);
      case "date":
        return `${now.getFullYear()}-${pad(now.getMonth() + 1, 2)}-${pad(now.getDate(), 2)}`;
      case "time":
        return `${pad(now.getHours(), 2)}-${pad(now.getMinutes(), 2)}-${pad(now.getSeconds(), 2)}`;
      case "yyyy":
        return String(now.getFullYear());
      case "yy":
        return pad(now.getFullYear() % 100, 2);
      case "MM":
        return pad(now.getMonth() + 1, 2);
      case "dd":
        return pad(now.getDate(), 2);
      case "HH":
        return pad(now.getHours(), 2);
      case "mm":
        return pad(now.getMinutes(), 2);
      case "ss":
        return pad(now.getSeconds(), 2);
      case "fff":
        return pad(now.getMilliseconds(), 3);
      case "yyyyMMdd":
        return `${now.getFullYear()}${pad(now.getMonth() + 1, 2)}${pad(now.getDate(), 2)}`;
      case "HHmmss":
        return `${pad(now.getHours(), 2)}${pad(now.getMinutes(), 2)}${pad(now.getSeconds(), 2)}`;
      case "yyyy-MM-dd":
        return `${now.getFullYear()}-${pad(now.getMonth() + 1, 2)}-${pad(now.getDate(), 2)}`;
      case "HH-mm-ss":
        return `${pad(now.getHours(), 2)}-${pad(now.getMinutes(), 2)}-${pad(now.getSeconds(), 2)}`;
      default:
        return m;
    }
  });
  name = name.replace(/[\\/:*?"<>|]/g, "_");
  return name.length === 0 ? "Snap" : name;
}

export function commitCounter(s: AppSettings): void {
  if ((s.fileNameTemplate ?? "Snap_{n}").includes("{n}") || !s.fileNameTemplate?.trim()) s.counter++;
}

export async function saveImage(png: Buffer, dest: string, jpegQuality = 90): Promise<string> {
  const ext = path.extname(dest).toLowerCase();
  fs.mkdirSync(path.dirname(path.resolve(dest)), { recursive: true });
  if (ext === ".pdf") {
    await savePdf(png, dest, jpegQuality);
    return dest;
  }
  if (ext === ".bmp") {
    await writeBmp(png, dest);
    return dest;
  }
  let img = sharp(png);
  if (ext === ".jpg" || ext === ".jpeg") img = img.jpeg({ quality: jpegQuality });
  else if (ext === ".gif") img = img.gif();
  else if (ext === ".tif" || ext === ".tiff") img = img.tiff();
  else img = img.png();
  await img.toFile(dest);
  return dest;
}

async function writeBmp(png: Buffer, dest: string): Promise<void> {
  const { data, info } = await sharp(png).removeAlpha().raw().toBuffer({ resolveWithObject: true });
  const w = info.width,
    h = info.height;
  const row = ((w * 3 + 3) >> 2) << 2;
  const pixels = Buffer.alloc(row * h);
  for (let y = 0; y < h; y++) {
    const dst = (h - 1 - y) * row;
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 3;
      pixels[dst + x * 3] = data[i + 2];
      pixels[dst + x * 3 + 1] = data[i + 1];
      pixels[dst + x * 3 + 2] = data[i];
    }
  }
  const header = Buffer.alloc(54);
  header.write("BM", 0);
  header.writeUInt32LE(54 + pixels.length, 2);
  header.writeUInt32LE(54, 10);
  header.writeUInt32LE(40, 14);
  header.writeInt32LE(w, 18);
  header.writeInt32LE(h, 22);
  header.writeUInt16LE(1, 26);
  header.writeUInt16LE(24, 28);
  header.writeUInt32LE(pixels.length, 34);
  fs.writeFileSync(dest, Buffer.concat([header, pixels]));
}

/** Minimal single-page PDF with JPEG (DCTDecode), matching the C# writer. */
export async function savePdf(png: Buffer, dest: string, q: number): Promise<void> {
  const jpg = await sharp(png).jpeg({ quality: q }).toBuffer();
  const meta = await sharp(png).metadata();
  const w = meta.width ?? 1,
    h = meta.height ?? 1;
  const chunks: Buffer[] = [];
  const offsets = [0, 0, 0, 0, 0, 0];
  let pos = 0;
  const W = (s: string | Buffer) => {
    const b = typeof s === "string" ? Buffer.from(s, "ascii") : s;
    chunks.push(b);
    pos += b.length;
  };
  W("%PDF-1.4\n");
  offsets[1] = pos;
  W("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n");
  offsets[2] = pos;
  W("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
  offsets[3] = pos;
  W(`3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 ${w} ${h}]/Resources<</XObject<</Im0 4 0 R>>>>/Contents 5 0 R>>endobj\n`);
  offsets[4] = pos;
  W(`4 0 obj<</Type/XObject/Subtype/Image/Width ${w}/Height ${h}/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode/Length ${jpg.length}>>stream\n`);
  W(jpg);
  W("\nendstream endobj\n");
  const content = `q ${w} 0 0 ${h} 0 0 cm /Im0 Do Q`;
  offsets[5] = pos;
  W(`5 0 obj<</Length ${content.length}>>stream\n${content}\nendstream endobj\n`);
  const xref = pos;
  W("xref\n0 6\n0000000000 65535 f \n");
  for (let i = 1; i <= 5; i++) W(offsets[i].toString().padStart(10, "0") + " 00000 n \n");
  W(`trailer<</Size 6/Root 1 0 R>>\nstartxref\n${xref}\n%%EOF\n`);
  fs.writeFileSync(dest, Buffer.concat(chunks));
}

export async function autoSave(png: Buffer, s: AppSettings): Promise<string> {
  fs.mkdirSync(s.saveFolder, { recursive: true });
  const ext = "." + (s.defaultFormat || "png").replace(/^\./, "");
  const base = expandTemplate(s);
  let dest = path.join(s.saveFolder, base + ext);
  for (let i = 2; fs.existsSync(dest); i++) dest = path.join(s.saveFolder, `${base} (${i})${ext}`);
  await saveImage(png, dest, s.jpegQuality);
  commitCounter(s);
  return dest;
}
