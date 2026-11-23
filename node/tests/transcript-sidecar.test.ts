import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import { defaults } from "../src/core/settings.js";
import { deletePair, listReady, markComplete, newMediaPath } from "../src/core/temp-recordings.js";
import {
  copyWithMedia,
  hasUtf8Bom,
  shouldAutoTranscribe,
  transcriptPathFor,
  writeForMedia,
} from "../src/core/transcript-sidecar.js";

describe("transcript sidecar", () => {
  it("names wav and mp3 as same-base txt", () => {
    assert.equal(path.basename(transcriptPathFor("Rec_20261113_120000.wav")), "Rec_20261113_120000.txt");
    assert.equal(path.basename(transcriptPathFor("Rec_20261113_120000.mp3")), "Rec_20261113_120000.txt");
  });

  it("auto-transcribes only when the setting is on and the file is audio", () => {
    assert.equal(defaults().autoTranscribeAudioAfterStop, true);
    assert.equal(shouldAutoTranscribe(true, "Rec.wav"), true);
    assert.equal(shouldAutoTranscribe(true, "Rec.mp3"), true);
    assert.equal(shouldAutoTranscribe(false, "Rec.wav"), false);
    assert.equal(shouldAutoTranscribe(false, "Rec.mp3"), false);
    assert.equal(shouldAutoTranscribe(true, "Rec.mp4"), false);
  });

  it("writes UTF-8 BOM so Notepad shows Chinese", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-txt-"));
    try {
      const media = path.join(dir, "Rec_zh.wav");
      fs.writeFileSync(media, Buffer.from([1]));
      writeForMedia(media, "你好，世界");
      const bytes = fs.readFileSync(transcriptPathFor(media));
      assert.equal(hasUtf8Bom(bytes), true);
      assert.equal(bytes.subarray(3).toString("utf8"), "你好，世界");
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("copies txt on save and deletes it on discard", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-txt-"));
    try {
      const temp = path.join(dir, "Rec_tmp.wav");
      const dest = path.join(dir, "saved", "Rec_final.wav");
      fs.writeFileSync(temp, Buffer.from([9]));
      writeForMedia(temp, "cue");
      assert.equal(copyWithMedia(temp, dest), true);
      fs.mkdirSync(path.dirname(dest), { recursive: true });
      fs.copyFileSync(temp, dest);
      deletePair(temp);
      assert.equal(fs.existsSync(temp), false);
      assert.equal(fs.existsSync(transcriptPathFor(temp)), false);
      assert.equal(fs.existsSync(dest), true);
      assert.equal(fs.existsSync(transcriptPathFor(dest)), true);
      assert.equal(fs.readFileSync(transcriptPathFor(dest), "utf8").replace(/^\uFEFF/, ""), "cue");
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("listReady skips transcript txt", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-txt-"));
    try {
      const t = new Date();
      const ready = newMediaPath(dir, "Audio", ".wav", t);
      fs.writeFileSync(ready, Buffer.from([9, 9]));
      markComplete(ready, t);
      writeForMedia(ready, "text");
      const list = listReady(dir);
      assert.ok(list.includes(ready));
      assert.equal(list.includes(transcriptPathFor(ready)), false);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });
});
