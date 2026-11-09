import assert from "node:assert/strict";
import { test } from "node:test";
import { place } from "../src/core/watermark.js";

test("Place_NineGridCornersAndCenter", () => {
  const tl = place(200, 100, 40, 20, 0, 10);
  assert.equal(tl.x, 10); assert.equal(tl.y, 10);
  const tr = place(200, 100, 40, 20, 2, 10);
  assert.equal(tr.x, 150); assert.equal(tr.y, 10);
  const c = place(200, 100, 40, 20, 4, 10);
  assert.equal(c.x, 80); assert.equal(c.y, 40);
  const br = place(200, 100, 40, 20, 8, 10);
  assert.equal(br.x, 150); assert.equal(br.y, 70);
});
