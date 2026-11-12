import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { chunkCues, chunkText, extractiveMarkdown, isAction, looksChinese, splitSentences } from "../src/core/summarize.js";

describe("summarize", () => {
  it("looksChinese by CJK ratio", () => {
    assert.equal(looksChinese("今天天气很好，我们开会。"), true);
    assert.equal(looksChinese("Hello world, this is English."), false);
  });

  it("splits zh and en sentences", () => {
    assert.equal(splitSentences("你好。世界！行吗？").length, 3);
    assert.equal(splitSentences("Hello. World? Yes!").length, 3);
  });

  it("chunkCues splits on char budget", () => {
    const cues = Array.from({ length: 6 }, (_, i) => ({ start: i, end: i + 1, text: "a".repeat(20) }));
    const chunks = chunkCues(cues, 50);
    assert.ok(chunks.length >= 2);
    assert.equal(chunks.reduce((n, c) => n + c.length, 0), 6);
  });

  it("chunkText keeps sentences", () => {
    const chunks = chunkText("One. Two. Three. Four.", 12);
    assert.ok(chunks.length >= 2);
  });

  it("extractive markdown has sections and actions", () => {
    const md = extractiveMarkdown([
      { start: 0, end: 2, text: "We discussed the launch plan in detail today." },
      { start: 2, end: 4, text: "The launch plan needs more testing before Friday." },
      { start: 4, end: 6, text: "Please send the notes to the team." },
      { start: 6, end: 8, text: "The launch plan is the main topic of this meeting." },
    ], 3);
    assert.match(md, /# Summary/);
    assert.match(md, /basic on-device/);
    assert.match(md, /## Overview/);
    assert.match(md, /## Key points/);
    assert.match(md, /## Action items/);
    assert.match(md, /Please send/);
    assert.match(md, /\[00:04\]/);
  });

  it("chinese headings and action", () => {
    const md = extractiveMarkdown([
      { start: 1, end: 3, text: "今天我们讨论了发布计划。" },
      { start: 3, end: 5, text: "请务必在周五前完成测试。" },
      { start: 5, end: 7, text: "发布计划是本次会议的重点。" },
    ]);
    assert.match(md, /# 摘要/);
    assert.match(md, /本机抽取式摘要/);
    assert.match(md, /## 待办/);
    assert.match(md, /周五/);
  });

  it("isAction matches zh and en", () => {
    assert.equal(isAction("Please update the doc."), true);
    assert.equal(isAction("我们需要明天发版。"), true);
    assert.equal(isAction("The weather is nice."), false);
  });
});
