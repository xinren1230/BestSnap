import assert from "node:assert/strict";
import { test } from "node:test";
import { formatOcrLayout, type OcrSpan } from "../src/core/ocr-layout.js";
import { postProcess } from "../src/core/ocr-text.js";

function B(t: string, x0: number, y0: number, x1: number, y1: number): OcrSpan {
  return { text: t, x0, y0, x1, y1 };
}

test("Format_SortsTopToBottomThenLeftToRight", () => {
  assert.equal(formatOcrLayout([B("Bottom", 0, 22, 40, 38), B("right", 50, 0, 90, 16), B("left", 0, 1, 40, 15)]), "left right\nBottom");
});

test("Format_MergesSameVisualLineWithSpace", () => {
  assert.equal(formatOcrLayout([B("Hello", 0, 0, 40, 16), B("World", 48, 1, 96, 17)]), "Hello World");
});

test("Format_InsertsParagraphBreakOnLargeVerticalGap", () => {
  assert.equal(formatOcrLayout([B("First", 0, 0, 50, 16), B("Second", 0, 50, 60, 66)]), "First\n\nSecond");
});

test("Format_PreservesRoughIndent", () => {
  const s = formatOcrLayout([B("Title", 0, 0, 50, 16), B("body", 40, 20, 90, 36)]);
  assert.match(s, /^Title\n +body/);
});

test("Format_AlignsSimpleColumnsWithTabs", () => {
  assert.equal(formatOcrLayout([B("A", 0, 0, 20, 16), B("B", 100, 0, 120, 16), B("C", 0, 20, 20, 36), B("D", 100, 20, 120, 36)]), "A\tB\nC\tD");
});

test("Format_JoinsWrappedCjkWithoutSpace", () => {
  assert.equal(formatOcrLayout([B("你好世界", 0, 0, 80, 16), B("这是续行", 0, 18, 80, 34)]), "你好世界这是续行");
});

test("Format_JoinsWrappedLatinWithSpace", () => {
  assert.equal(formatOcrLayout([B("hello", 0, 0, 40, 16), B("world", 0, 18, 50, 34)]), "hello world");
});

test("Format_DehyphenatesWrappedLatin", () => {
  assert.equal(formatOcrLayout([B("exam-", 0, 0, 50, 16), B("ple", 0, 18, 30, 34)]), "example");
});

test("Format_KeepsSentenceBreak", () => {
  assert.equal(formatOcrLayout([B("Done.", 0, 0, 50, 16), B("Next", 0, 18, 40, 34)]), "Done.\nNext");
});

test("Format_ThenPostProcess_KeepsCjkLatinRules", () => {
  assert.equal(postProcess(formatOcrLayout([B("升级到Windows", 0, 0, 80, 16), B("11系统", 84, 0, 130, 16)])), "升级到 Windows 11 系统");
});

test("Format_NoSpaceAroundFullwidthPunctOnSameLine", () => {
  assert.equal(
    postProcess(formatOcrLayout([B("第一段：", 0, 0, 50, 16), B("BestSnap", 52, 0, 120, 16), B("FPS", 130, 0, 160, 16), B("。", 162, 0, 178, 16)])),
    "第一段：BestSnap FPS。",
  );
});

test("Format_TwoOrdinaryLines_NoParagraphBreak", () => {
  assert.equal(
    formatOcrLayout([B("升级到 Windows 11 系统", 10, 8, 380, 32), B("Hello World, open source tool 截图", 10, 36, 520, 60)]),
    "升级到 Windows 11 系统\nHello World, open source tool 截图",
  );
});
