import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

export const PRODUCT_NAME = "BestSnap";
/** LEGACY MIGRATION: previous on-disk config/data directory name. */
export const LEGACY_PRODUCT_NAME = "SnapForge";

function envOrLegacy(current: string, legacy: string): string | undefined {
  const a = process.env[current];
  if (a) return a;
  const b = process.env[legacy];
  if (b) return b;
  return undefined;
}

/** Move when dest is missing; otherwise copy files that dest does not already have. */
export function migrateTree(from: string, to: string): void {
  if (!from || !to) return;
  try {
    if (path.resolve(from) === path.resolve(to)) return;
    if (!fs.existsSync(from)) return;
    if (!fs.existsSync(to)) {
      fs.mkdirSync(path.dirname(to), { recursive: true });
      fs.renameSync(from, to);
      return;
    }
    const walk = (dir: string) => {
      for (const name of fs.readdirSync(dir)) {
        const src = path.join(dir, name);
        const dest = path.join(to, path.relative(from, src));
        const st = fs.statSync(src);
        if (st.isDirectory()) {
          fs.mkdirSync(dest, { recursive: true });
          walk(src);
        } else if (!fs.existsSync(dest)) {
          fs.mkdirSync(path.dirname(dest), { recursive: true });
          fs.copyFileSync(src, dest);
        }
      }
    };
    walk(from);
  } catch {
    /* ignore */
  }
}

let migrated = false;
export function ensureMigrated(): void {
  if (migrated) return;
  migrated = true;
  if (!envOrLegacy("BESTSNAP_DATA", "SNAPFORGE_DATA")) {
    const xdg = process.env.XDG_CONFIG_HOME || path.join(os.homedir(), ".config");
    migrateTree(path.join(xdg, LEGACY_PRODUCT_NAME), path.join(xdg, PRODUCT_NAME));
  }
  if (!envOrLegacy("BESTSNAP_LOCAL", "SNAPFORGE_LOCAL")) {
    const xdg = process.env.XDG_DATA_HOME || path.join(os.homedir(), ".local", "share");
    migrateTree(path.join(xdg, LEGACY_PRODUCT_NAME), path.join(xdg, PRODUCT_NAME));
  }
}

/** Directory that contains package.json / models / lang (node/ in the repo, or extraResources next to an AppImage). */
export function appRoot(): string {
  const fromEnv = envOrLegacy("BESTSNAP_ROOT", "SNAPFORGE_ROOT");
  if (fromEnv) return fromEnv;
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
  const fromEnv = envOrLegacy("BESTSNAP_DATA", "SNAPFORGE_DATA");
  if (fromEnv) return fromEnv;
  ensureMigrated();
  const xdg = process.env.XDG_CONFIG_HOME || path.join(os.homedir(), ".config");
  return path.join(xdg, PRODUCT_NAME);
}

export function localAppDataDir(): string {
  const fromEnv = envOrLegacy("BESTSNAP_LOCAL", "SNAPFORGE_LOCAL");
  if (fromEnv) return fromEnv;
  ensureMigrated();
  const xdg = process.env.XDG_DATA_HOME || path.join(os.homedir(), ".local", "share");
  return path.join(xdg, PRODUCT_NAME);
}

export function ensureDir(p: string): string {
  fs.mkdirSync(p, { recursive: true });
  return p;
}
