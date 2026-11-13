import assert from "node:assert/strict";
import { test } from "node:test";
import { borderOutside, evenSize, intersect, normalize, normalizeFps, pickMonitorIndex, virtualScreen } from "../src/core/record-rect.js";

test("normalize flips negative size", () => {
  assert.deepEqual(normalize(100, 80, -40, -20), { x: 60, y: 60, w: 40, h: 20 });
});

test("virtualScreen unions negative origin", () => {
  assert.deepEqual(
    virtualScreen([
      { x: -1920, y: 0, w: 1920, h: 1080 },
      { x: 0, y: 0, w: 1920, h: 1080 },
    ]),
    { x: -1920, y: 0, w: 3840, h: 1080 },
  );
});

test("pickMonitor largest overlap and spans", () => {
  const m = [
    { x: 0, y: 0, w: 1920, h: 1080, deviceName: "A" },
    { x: 1920, y: 0, w: 1920, h: 1080, deviceName: "B" },
  ];
  const r = pickMonitorIndex({ x: 1800, y: 10, w: 200, h: 100 }, m);
  assert.equal(r.spans, true);
  assert.equal(r.index, 0);
});

test("evenSize and borderOutside", () => {
  assert.deepEqual(evenSize({ x: 3, y: 4, w: 11, h: 21 }), { x: 3, y: 4, w: 10, h: 20 });
  assert.deepEqual(borderOutside({ x: 10, y: 20, w: 100, h: 50 }, 4), { x: 6, y: 16, w: 108, h: 58 });
});

test("normalizeFps snaps", () => {
  assert.equal(normalizeFps(9), 10);
  assert.equal(normalizeFps(30), 30);
});

test("intersect clamps", () => {
  assert.deepEqual(intersect({ x: -10, y: -5, w: 30, h: 20 }, { x: 0, y: 0, w: 100, h: 50 }), { x: 0, y: 0, w: 20, h: 15 });
});
