import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { isComplete, resolveFormulaDir } from "../src/core/bundled-models.js";
import { tidyLatex } from "../src/core/formula-latex.js";
import { recognizeLatex } from "../src/core/formula.js";

const dir = path.dirname(fileURLToPath(import.meta.url));
const img = path.join(dir, "fixtures", "formula.png");

test("FormulaLatex.Tidy keeps command-word spaces only", () => {
  assert.equal(tidyLatex("\\frac { a } { b }"), "\\frac{a}{b}");
  assert.equal(tidyLatex("\\mathrm ab"), "\\mathrm ab");
});

test("LaTeX e2e on printed formula fixture", { timeout: 180_000 }, async () => {
  if (!isComplete(resolveFormulaDir())) {
    console.log("skipping latex e2e: pix2text-mfr models not downloaded");
    return;
  }
  assert.ok(fs.existsSync(img), "formula fixture missing");
  const tex = await recognizeLatex(fs.readFileSync(img));
  console.log("LaTeX result:\n" + tex);
  assert.ok(tex.length > 0, "empty latex");
  assert.ok(/[xX]|frac|sqrt|pm|b/.test(tex), "result does not look like the quadratic formula: " + tex);
});
