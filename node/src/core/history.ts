import fs from "node:fs";
import path from "node:path";
import { ensureDir, userDataDir } from "./paths.js";

export interface HistoryItem {
  file: string;
  time: number;
  w: number;
  h: number;
}

function dir(): string {
  return path.join(userDataDir(), "history");
}

function indexPath(): string {
  return path.join(dir(), "index.json");
}

export function loadHistory(): HistoryItem[] {
  try {
    return JSON.parse(fs.readFileSync(indexPath(), "utf8")) as HistoryItem[];
  } catch {
    return [];
  }
}

function saveIndex(items: HistoryItem[]): void {
  ensureDir(dir());
  fs.writeFileSync(indexPath(), JSON.stringify(items, null, 2));
}

export function addHistory(png: Buffer, w: number, h: number, max = 50): HistoryItem {
  ensureDir(dir());
  const item: HistoryItem = { file: `${Date.now()}.png`, time: Date.now(), w, h };
  fs.writeFileSync(path.join(dir(), item.file), png);
  let items = [item, ...loadHistory().filter((x) => x.file !== item.file)];
  while (items.length > max) {
    const drop = items.pop()!;
    fs.rmSync(path.join(dir(), drop.file), { force: true });
  }
  saveIndex(items);
  return item;
}

export function historyFile(item: HistoryItem): string {
  return path.join(dir(), item.file);
}

export function clearHistory(): void {
  const items = loadHistory();
  for (const i of items) fs.rmSync(path.join(dir(), i.file), { force: true });
  saveIndex([]);
}
