import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { decodeCodes } from "../src/core/qr.js";

const img = path.join(path.dirname(fileURLToPath(import.meta.url)), "fixtures", "qr.png");

test("QR e2e decodes example.com payload", async () => {
  assert.ok(fs.existsSync(img), "qr fixture missing — run node scripts/generate-fixtures.mjs");
  const hits = await decodeCodes(fs.readFileSync(img));
  console.log("QR hits", hits);
  assert.ok(hits.length >= 1, "no codes");
  assert.ok(hits.some((h) => h.text.includes("example.com/snapforge")), JSON.stringify(hits));
});
