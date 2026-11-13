import assert from "node:assert/strict";
import path from "node:path";
import { test } from "node:test";
import { mix, wavHeader } from "../src/core/audio-pcm.js";
import { buildArgs, buildFilter, defaultOutputPath, keepFromRemove, merge } from "../src/core/trim-range.js";

test("merge overlapping and touching", () => {
  const m = merge(
    [
      { start: 1, end: 3 },
      { start: 2.5, end: 4 },
      { start: 4, end: 5 },
      { start: 8, end: 9 },
    ],
    20,
  );
  assert.equal(m.length, 2);
  assert.equal(m[0].start, 1);
  assert.equal(m[0].end, 5);
});

test("keepFromRemove cuts middle", () => {
  const keep = keepFromRemove(10, [
    { start: 0, end: 1 },
    { start: 4, end: 6 },
    { start: 9, end: 10 },
  ]);
  assert.deepEqual(keep, [
    { start: 1, end: 4 },
    { start: 6, end: 9 },
  ]);
});

test("defaultOutputPath collision", () => {
  const taken = new Set<string>();
  const orig = path.join("/tmp", "clip.mp4");
  const p1 = defaultOutputPath(orig, "_trimmed", (p) => taken.has(p));
  assert.ok(p1.endsWith("clip_trimmed.mp4"));
  taken.add(p1);
  const p2 = defaultOutputPath(orig, "_trimmed", (p) => taken.has(p));
  assert.ok(p2.endsWith("clip_trimmed-2.mp4"));
});

test("buildFilter concat", () => {
  const f = buildFilter(
    [
      { start: 0, end: 1.5 },
      { start: 3, end: 5 },
    ],
    true,
    true,
  );
  assert.match(f, /trim=start=0:end=1.5/);
  assert.match(f, /concat=n=2:v=1:a=1\[outv\]\[outa\]/);
});

test("buildArgs single uses ss/to", () => {
  const args = buildArgs("in.mp4", "out.mp4", [{ start: 1, end: 4 }], true, true);
  assert.ok(args.includes("-ss"));
  assert.ok(!args.includes("-filter_complex"));
});

test("mix clips", () => {
  const m = mix(new Int16Array([20000, -20000]), new Int16Array([20000, -20000]), 1, 1);
  assert.equal(m[0], 32767);
  assert.equal(m[1], -32768);
});

test("wavHeader riff", () => {
  const h = wavHeader(8);
  assert.equal(h.toString("ascii", 0, 4), "RIFF");
  assert.equal(h.readUInt32LE(40), 8);
});
