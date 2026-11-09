import assert from "node:assert/strict";
import { test } from "node:test";
import { FORMULA_FILES } from "../src/core/bundled-models.js";

test("FormulaFiles_MatchPublishedSha256AndSizes", () => {
  assert.equal(FORMULA_FILES.length, 3);
  const enc = FORMULA_FILES.find((f) => f.name === "encoder_model.onnx")!;
  assert.equal(enc.size, 87496990);
  assert.equal(enc.sha256, "bd8d5c322792e9ec45793af5569e9748f82a3d728a9e00213dbfc56c1486f37d");
  const dec = FORMULA_FILES.find((f) => f.name === "decoder_model.onnx")!;
  assert.equal(dec.size, 30114937);
  assert.equal(dec.sha256, "fd0f92d7a012f3dae41e1ac79421aea0ea888b5a66cb3f9a004e424f82f3daed");
  const tok = FORMULA_FILES.find((f) => f.name === "tokenizer.json")!;
  assert.equal(tok.size, 39161);
  assert.equal(tok.sha256, "3e2ab757277d22639bec28c9d7972e352d3d1dba223051fa674002dc5ab64df3");
});
