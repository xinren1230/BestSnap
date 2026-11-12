import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { composeWhisperPrompt, normalizeWhisper, normalizeWhisperLanguage, srtTimestamp, toSrt, toTxt, whisperModels } from "../src/core/transcript.js";
import { parseNvidiaSmi, pickWhisper } from "../src/core/whisper-auto.js";
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

  it("warmed live window is 10s with 2s overlap", () => {
    assert.equal(FIRST_WINDOW_SECONDS, 3.5);
    assert.equal(WINDOW_SECONDS, 10);
    assert.equal(HOP_SECONDS, 8);
  });

  it("whisper models default to auto and keep aliases", () => {
    assert.equal(whisperModels.defaultId, "auto");
    assert.equal(whisperModels.choiceIds[0], "auto");
    assert.ok(whisperModels.ids.includes("large-v3-turbo-q5_0"));
    assert.ok(whisperModels.ids.includes("medium-q5_0"));
    assert.equal(normalizeWhisper("base"), "base");
    assert.equal(normalizeWhisper("turbo"), "large-v3-turbo");
    assert.equal(normalizeWhisper(""), "auto");
    assert.equal(normalizeWhisperLanguage("zh-CN"), "zh");
    const p = composeWhisperPrompt("zh", "BestSnap", "上一句");
    assert.match(p, /简体中文/);
    assert.match(p, /BestSnap/);
  });

  it("auto-picks turbo on 8 GB CUDA and shrinks for live at 6 GB", () => {
    const GiB = 1024 ** 3;
    const rtx = pickWhisper(
      { adapters: [{ name: "NVIDIA GeForce RTX 4060", vendor: "nvidia", dedicatedBytes: 8 * GiB }], ramBytes: 16 * GiB, cpuCores: 8, cudaRuntime: true, vulkanRuntime: true },
      false,
    );
    assert.equal(rtx.modelId, "large-v3-turbo");
    assert.equal(rtx.backend, "cuda");
    assert.match(rtx.reason, /RTX 4060/);
    const live6 = pickWhisper(
      { adapters: [{ name: "NVIDIA GeForce RTX 3060", vendor: "nvidia", dedicatedBytes: 6 * GiB }], ramBytes: 16 * GiB, cpuCores: 8, cudaRuntime: true, vulkanRuntime: true },
      true,
    );
    assert.equal(live6.modelId, "large-v3-turbo-q5_0");
    const cpu = pickWhisper({ adapters: [], ramBytes: 8 * GiB, cpuCores: 4, cudaRuntime: false, vulkanRuntime: false }, false);
    assert.equal(cpu.modelId, "base");
    const smi = parseNvidiaSmi("NVIDIA GeForce RTX 4060, 8188");
    assert.equal(smi[0].vendor, "nvidia");
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
