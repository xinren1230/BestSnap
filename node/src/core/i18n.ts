import fs from "node:fs";
import path from "node:path";
import { appRoot } from "./paths.js";

type Pack = { _name?: string; _code?: string; strings?: Record<string, string> };

const packs = new Map<string, Pack>();
let current = "zh-CN";

function langDir(): string {
  return path.join(appRoot(), "lang");
}

export function availableLanguages(): { code: string; name: string }[] {
  loadAll();
  return [...packs.entries()].map(([code, p]) => ({ code, name: p._name || code }));
}

function loadAll(): void {
  if (packs.size) return;
  const dir = langDir();
  if (!fs.existsSync(dir)) return;
  for (const f of fs.readdirSync(dir).filter((x) => x.endsWith(".json"))) {
    try {
      const p = JSON.parse(fs.readFileSync(path.join(dir, f), "utf8")) as Pack;
      packs.set(p._code || f.replace(".json", ""), p);
    } catch {
      /* skip */
    }
  }
}

export function setLanguage(code: string): void {
  loadAll();
  if (code === "auto") {
    const loc = (process.env.LANG || "zh_CN").replace(/_/g, "-");
    if (loc.toLowerCase().startsWith("zh-tw") || loc.toLowerCase().startsWith("zh-hk") || loc.toLowerCase().startsWith("zh-mo")) current = packs.has("zh-TW") ? "zh-TW" : "zh-CN";
    else if (loc.toLowerCase().startsWith("zh")) current = "zh-CN";
    else {
      const short = loc.split("-")[0];
      current = packs.has(loc) ? loc : packs.has(short) ? short : "en";
    }
    return;
  }
  current = packs.has(code) ? code : "en";
}

export function t(s: string): string {
  loadAll();
  const cur = packs.get(current)?.strings?.[s];
  if (cur) return cur;
  const en = packs.get("en")?.strings?.[s];
  return en || s;
}

export function currentLanguage(): string {
  return current;
}
