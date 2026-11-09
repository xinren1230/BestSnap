import assert from "node:assert/strict";
import { test } from "node:test";
import {
  isCjk,
  joinCjk,
  postProcess,
  restoreLatinSpaces,
  splitLetterDigit,
} from "../src/core/ocr-text.js";

function ctc(text: string, ...positions: number[]) {
  const glyphs: { text: string; position: number }[] = [];
  let i = 0;
  for (const c of text) {
    if (c === " ") continue;
    glyphs.push({ text: c, position: positions[i++] });
  }
  return glyphs;
}

test("JoinCjk_RemovesSpacesOnlyBetweenCjk", () => {
  assert.equal(joinCjk("中 文"), "中文");
  assert.equal(joinCjk("中 文 测 试"), "中文测试");
  assert.equal(joinCjk("こ ん に ち は"), "こんにちは");
  assert.equal(joinCjk("한 글"), "한글");
});

test("JoinCjk_KeepsLatinSpaces", () => {
  assert.equal(joinCjk("Windows 11"), "Windows 11");
  assert.equal(joinCjk("Hello World"), "Hello World");
  assert.equal(joinCjk("使 用 Windows 11 系 统"), "使用 Windows 11 系统");
});

test("JoinCjk_DoesNotTreatFullwidthLatinAsCjk", () => {
  assert.equal(joinCjk("Ｗｉｎｄｏｗｓ １１"), "Ｗｉｎｄｏｗｓ １１");
});

test("JoinCjk_PreservesNewlines", () => {
  assert.equal(joinCjk("中 文\n测 试"), "中文\n测试");
  assert.equal(joinCjk("Windows 11\nHello"), "Windows 11\nHello");
});

test("RestoreLatinSpaces_InsertsSpaceAtWideGap", () => {
  const glyphs = [
    ["W", 0], ["i", 1], ["n", 2], ["d", 3], ["o", 4], ["w", 5], ["s", 6],
    ["1", 10], ["1", 11],
  ] as [string, number][];
  assert.equal(restoreLatinSpaces(glyphs), "Windows 11");
});

test("RestoreLatinSpaces_DoesNotSplitTightLatin", () => {
  const glyphs: [string, number][] = [];
  const s = "Windows11";
  for (let i = 0; i < s.length; i++) glyphs.push([s[i], i]);
  assert.equal(restoreLatinSpaces(glyphs), "Windows11");
});

test("RestoreLatinSpaces_RealisticCtc_DoesNotSplitLetters", () => {
  assert.equal(restoreLatinSpaces(ctc("Windows", 0, 1, 10, 19, 28, 36, 45)), "Windows");
  assert.equal(restoreLatinSpaces(ctc("Hello", 0, 1, 10, 19, 28)), "Hello");
});

test("RestoreLatinSpaces_RealisticCtc_InsertsWordSpacesOnly", () => {
  const line1 = ctc("升级到Windows 11系统", 0, 8, 17, 42, 43, 52, 61, 70, 79, 88, 116, 125, 150, 159);
  assert.equal(restoreLatinSpaces(line1), "升级到 Windows 11 系统");
  const line2 = ctc(
    "Hello World, open source tool截图",
    0, 1, 10, 19, 28, 56, 65, 74, 83, 92, 93, 121, 130, 139, 148, 176, 185, 194, 203, 212, 221, 249, 258, 267, 276, 301, 310,
  );
  assert.equal(restoreLatinSpaces(line2), "Hello World, open source tool 截图");
});

test("RestoreLatinSpaces_KeepsModelEmittedSpacesBetweenLatinTokens", () => {
  const glyphs = [
    { text: "H", position: 0 }, { text: "e", position: 9 }, { text: "l", position: 18 }, { text: "l", position: 27 }, { text: "o", position: 36 },
    { text: " ", position: 44 },
    { text: "W", position: 52 }, { text: "o", position: 61 }, { text: "r", position: 70 }, { text: "l", position: 79 }, { text: "d", position: 88 },
  ];
  assert.equal(restoreLatinSpaces(glyphs), "Hello World");
});

test("RestoreLatinSpaces_InsertsAfterCommaEvenWhenPacked", () => {
  assert.equal(restoreLatinSpaces(ctc("a,bc", 0, 9, 28, 37)), "a, bc");
  assert.equal(restoreLatinSpaces(ctc("a,bc", 0, 9, 10, 19)), "a, bc");
  assert.equal(restoreLatinSpaces(ctc("1,000", 0, 9, 10, 19, 28)), "1,000");
});

test("RestoreLatinSpaces_InsertsCjkLatinEvenWhenPacked", () => {
  assert.equal(restoreLatinSpaces(ctc("到Win", 0, 9, 18, 27)), "到 Win");
});

test("RestoreLatinSpaces_DoesNotInsertBetweenCjk", () => {
  const glyphs: [string, number][] = [["中", 0], ["文", 4], ["测", 8], ["试", 12]];
  assert.equal(restoreLatinSpaces(glyphs), "中文测试");
});

test("SplitLetterDigit_Windows11", () => {
  assert.equal(splitLetterDigit("Windows11"), "Windows 11");
  assert.equal(splitLetterDigit("Win10"), "Win10");
  assert.equal(splitLetterDigit("MP4"), "MP4");
});

test("PostProcess_FixesMixedLine", () => {
  assert.equal(postProcess("使 用 Windows11 系 统"), "使用 Windows 11 系统");
});

test("PostProcess_InsertsCjkLatinAndCommaSpacesWithoutPositions", () => {
  assert.equal(postProcess("升级到Windows 11系统"), "升级到 Windows 11 系统");
  assert.equal(postProcess("Hello World,open source tool截图"), "Hello World, open source tool 截图");
  assert.equal(postProcess("使用 Windows 11 系统"), "使用 Windows 11 系统");
  assert.equal(postProcess("1,000"), "1,000");
});

test("PostProcess_NoSpaceAroundFullwidthPunct", () => {
  assert.equal(postProcess("第一段： BestSnap 是"), "第一段：BestSnap 是");
  assert.equal(postProcess("60 FPS 。"), "60 FPS。");
  assert.equal(postProcess("12345 。"), "12345。");
  assert.equal(postProcess("“ 白字黑底 ”"), "“白字黑底”");
  assert.equal(postProcess("第一段：BestSnap"), "第一段：BestSnap");
});

test("PostProcess_KeepsMp4", () => {
  assert.equal(postProcess("录屏支持 MP4 和 GIF 格式"), "录屏支持 MP4 和 GIF 格式");
});

test("RestoreLatinSpaces_PrefersPackedThisisOverGuessingAWordBreak", () => {
  const glyphs = ctc("Thisis", 0, 10, 20, 30, 44, 54);
  assert.equal(restoreLatinSpaces(glyphs), "Thisis");
  assert.equal(restoreLatinSpaces(glyphs, 20), "Thisis");
});

test("RestoreLatinSpaces_DoesNotSplitWorldWithUnevenLetterPitch", () => {
  const world = ctc("World", 0, 10, 23, 33, 43);
  assert.equal(restoreLatinSpaces(world), "World");
  assert.equal(restoreLatinSpaces(world, 20), "World");
});

test("RestoreLatinSpaces_DoesNotSplitMp4", () => {
  const glyphs = ctc("MP4", 0, 10, 20);
  assert.equal(restoreLatinSpaces(glyphs), "MP4");
  assert.equal(restoreLatinSpaces(glyphs, 20), "MP4");
});

test("IsCjk_ExcludesAsciiAndFullwidthLatin", () => {
  assert.equal(isCjk("中"), true);
  assert.equal(isCjk("あ"), true);
  assert.equal(isCjk("한"), true);
  assert.equal(isCjk("W"), false);
  assert.equal(isCjk("1"), false);
  assert.equal(isCjk(" "), false);
  assert.equal(isCjk("Ｗ"), false);
  assert.equal(isCjk("１"), false);
});
