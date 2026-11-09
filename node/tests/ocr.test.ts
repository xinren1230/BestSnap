import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { recognize } from "../src/core/ocr.js";
import { ocrAvailable } from "../src/core/bundled-models.js";

const dir = path.dirname(fileURLToPath(import.meta.url));
const img = path.join(dir, "fixtures", "ocr.png");

test("OCR e2e on mixed Chinese/English fixture", { timeout: 120_000 }, async () => {
  assert.ok(ocrAvailable(), "PP-OCRv4 models missing — run scripts/setup.sh");
  assert.ok(fs.existsSync(img), "fixture missing — run node scripts/generate-fixtures.mjs");
  const text = await recognize(fs.readFileSync(img));
  console.log("OCR result:\n" + text);
  assert.ok(text.length > 0, "empty OCR");
  assert.match(text, /Windows/);
  assert.match(text, /11/);
  assert.ok(/升级|系统|Hello|World|SnapForge|截图|OCR/.test(text), "expected keywords missing: " + text);
});
