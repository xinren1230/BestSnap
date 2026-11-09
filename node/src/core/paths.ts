import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

/** Directory that contains package.json / models / lang (node/ in the repo, or extraResources next to an AppImage). */
export function appRoot(): string {
  if (process.env.SNAPFORGE_ROOT) return process.env.SNAPFORGE_ROOT;
  const candidates = [
    path.resolve(here, "..", ".."),
    path.resolve(here, "..", "..", ".."),
    process.cwd(),
    path.join(process.resourcesPath ?? "", ".."),
    process.resourcesPath ?? "",
  ];
  for (const c of candidates) {
    if (!c) continue;
    if (fs.existsSync(path.join(c, "package.json")) && fs.existsSync(path.join(c, "models"))) return c;
    if (fs.existsSync(path.join(c, "models", "ocr"))) return c;
  }
  return path.resolve(here, "..", "..");
}

export function userDataDir(): string {
  if (process.env.SNAPFORGE_DATA) return process.env.SNAPFORGE_DATA;
  const xdg = process.env.XDG_CONFIG_HOME || path.join(os.homedir(), ".config");
  return path.join(xdg, "SnapForge");
}

export function localAppDataDir(): string {
  if (process.env.SNAPFORGE_LOCAL) return process.env.SNAPFORGE_LOCAL;
  const xdg = process.env.XDG_DATA_HOME || path.join(os.homedir(), ".local", "share");
  return path.join(xdg, "SnapForge");
}

export function ensureDir(p: string): string {
  fs.mkdirSync(p, { recursive: true });
  return p;
}
