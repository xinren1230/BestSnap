import assert from "node:assert/strict";
import { test } from "node:test";
import { bottomCenter, CAPTION_MARGIN, monitorIndexForPoint, placeCaption } from "../src/core/caption-place.js";

const left = { x: -1920, y: 0, w: 1920, h: 1080 };
const right = { x: 0, y: 0, w: 1920, h: 1080 };

test("point on left monitor, not primary", () => {
  assert.equal(monitorIndexForPoint(-1127, 12, [left, right]), 0);
  assert.equal(monitorIndexForPoint(100, 10, [left, right]), 1);
});

test("bottom center on left work area", () => {
  const r = bottomCenter(left, 640, 360);
  assert.equal(r.w, 640);
  assert.equal(r.h, 360);
  assert.equal(r.x, -1920 + Math.floor((1920 - 640) / 2));
  assert.equal(r.y, 1080 - CAPTION_MARGIN - 360);
});

test("place uses owner monitor not primary", () => {
  const r = placeCaption([left, right], 640, 360, null, -1127, 12);
  assert.ok(r.x < 0);
  assert.ok(r.x + r.w <= 0);
  assert.ok(r.y > 500);
});

test("remembered position on same monitor wins", () => {
  const last = { x: -1800, y: 40, w: 640, h: 360 };
  const r = placeCaption([left, right], 640, 360, last, 100, 10);
  assert.equal(r.x, -1800);
  assert.equal(r.y, 40);
});
