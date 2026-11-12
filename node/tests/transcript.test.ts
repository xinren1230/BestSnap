import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { srtTimestamp, toSrt, toTxt } from "../src/core/transcript.js";
import { chunkAbsStart, extendCommitted, FIRST_WINDOW_SECONDS, fireAt, HOP_SECONDS, stitch, WINDOW_SECONDS, windowForIndex } from "../src/core/live-chunk.js";

describe("transcript + live stitch", () => {
  it("formats SRT timestamps", () => {
    assert.equal(srtTimestamp(1.5), "00:00:01,500");
    assert.equal(srtTimestamp(61), "00:01:01,000");
    assert.equal(srtTimestamp(3600), "01:00:00,000");
    assert.equal(srtTimestamp(-3), "00:00:00,000");
  });

  it("numbers cues and drops empty", () => {
    const srt = toSrt([
      { start: 0, end: 1.2, text: "Hello" },
      { start: 1.2, end: 2, text: "  " },
      { start: 2, end: 3.5, text: "世界" },
    ]);
    assert.match(srt, /1\r\n00:00:00,000 --> 00:00:01,200\r\nHello/);
    assert.match(srt, /2\r\n00:00:02,000 --> 00:00:03,500\r\n世界/);
    assert.equal((srt.match(/\r\n\r\n/g) || []).length, 2);
  });

  it("txt includes bracket timestamps", () => {
    const t = toTxt([{ start: 5, end: 6, text: "hi" }]);
    assert.ok(t.startsWith("[00:00:05.000] hi"));
  });

  it("chunk abs start uses first window then hop", () => {
    assert.equal(chunkAbsStart(0), 0);
    assert.equal(fireAt(0), FIRST_WINDOW_SECONDS);
    assert.equal(fireAt(1), FIRST_WINDOW_SECONDS + HOP_SECONDS);
    const a1 = chunkAbsStart(1);
    assert.equal(a1, fireAt(1) - WINDOW_SECONDS);
    assert.ok(a1 > 0 && a1 < FIRST_WINDOW_SECONDS);
    assert.equal(windowForIndex(0), FIRST_WINDOW_SECONDS);
    assert.equal(windowForIndex(1), WINDOW_SECONDS);
  });

  it("stitch drops overlap and clips straddle", () => {
    const incoming = [
      { start: 0, end: 1, text: "old" },
      { start: 1.2, end: 2.5, text: "keep" },
      { start: 0.8, end: 1.6, text: "straddle" },
    ];
    const stitched = stitch(incoming, 6.5, 8.0);
    assert.equal(stitched.find((c) => c.text === "old"), undefined);
    const keep = stitched.find((c) => c.text === "keep");
    assert.ok(keep);
    assert.equal(keep!.start, 8.0); // 7.7 was inside the overlap, clipped to committedThrough
    const straddle = stitched.find((c) => c.text === "straddle");
    assert.ok(straddle);
    assert.equal(straddle!.start, 8.0);
  });

  it("stitch then srt uses absolute times", () => {
    const added = stitch([{ start: 2, end: 3, text: "Hello" }], 10, 0);
    const srt = toSrt(added);
    assert.match(srt, /00:00:12,000 --> 00:00:13,000/);
  });

  it("extendCommitted takes max end", () => {
    assert.equal(
      extendCommitted(
        [
          { start: 1, end: 2, text: "a" },
          { start: 2, end: 4.5, text: "b" },
        ],
        1.5,
      ),
      4.5,
    );
  });
});
