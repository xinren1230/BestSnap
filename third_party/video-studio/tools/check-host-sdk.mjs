#!/usr/bin/env node
// Fails if the vendored editor source or built output still names the old
// cloud host SDK. LICENSE and NOTICE keep the required attribution.
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const skipNames = new Set(['LICENSE', 'NOTICE', 'NOTICE.md']);
const skipDirs = new Set(['node_modules', '.git']);
const skipExt = new Set(['.png', '.jpg', '.jpeg', '.webp', '.ico', '.wasm', '.tflite', '.task', '.woff', '.woff2', '.ttf', '.otf']);
const needle = ['pu', 'ter'].join('');
const re = new RegExp(`(?<![A-Za-z])${needle}(?![A-Za-z])`, 'gi');
const allow = /Based on Video Studio \(AGPL-3\.0\)/;

function walk(dir, hits) {
  for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
    if (ent.isDirectory()) {
      if (skipDirs.has(ent.name)) continue;
      walk(path.join(dir, ent.name), hits);
      continue;
    }
    if (skipNames.has(ent.name)) continue;
    const ext = path.extname(ent.name).toLowerCase();
    if (skipExt.has(ext)) continue;
    const full = path.join(dir, ent.name);
    let text;
    try { text = fs.readFileSync(full, 'utf8'); } catch { continue; }
    const lines = text.split(/\r?\n/);
    lines.forEach((line, i) => {
      if (allow.test(line) && !/\.(com|ai|fs|kv|auth|js)\b/i.test(line)) return;
      re.lastIndex = 0;
      if (re.test(line)) hits.push(`${path.relative(root, full)}:${i + 1}: ${line.trim().slice(0, 160)}`);
    });
  }
}

const hits = [];
walk(root, hits);
if (hits.length) {
  console.error(`Forbidden host-SDK name found:\n${hits.join('\n')}`);
  process.exit(1);
}
console.log('No forbidden host-SDK names in vendored editor.');
