import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import { cleanup, deletePair, isReady, isUnder, listReady, markComplete, newMediaPath } from "../src/core/temp-recordings.js";

describe("temp recordings", () => {
  it("newMediaPath writes sidecar and avoids collision", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-tmp-"));
    try {
      const t0 = new Date("2026-11-12T13:00:00Z");
      const a = newMediaPath(dir, "Rec", ".mp4", t0);
      fs.writeFileSync(a, Buffer.from([1, 2, 3]));
      assert.equal(isReady(a), false);
      markComplete(a, t0);
      assert.equal(isReady(a), true);
      assert.equal(fs.existsSync(a + ".meta.json"), true);
      const b = newMediaPath(dir, "Rec", ".mp4", t0);
      assert.notEqual(a, b);
      assert.equal(isUnder(a, dir), true);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("cleanup deletes only older than max age", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-tmp-"));
    try {
      const oldF = path.join(dir, "old.wav");
      const newF = path.join(dir, "new.wav");
      fs.writeFileSync(oldF, "x");
      fs.writeFileSync(newF, "y");
      const now = Date.now();
      fs.utimesSync(oldF, now / 1000 - 8 * 86400, now / 1000 - 8 * 86400);
      const gone = cleanup(dir, 7 * 86400 * 1000, now);
      assert.ok(gone.includes(oldF));
      assert.equal(fs.existsSync(oldF), false);
      assert.equal(fs.existsSync(newF), true);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("listReady skips incomplete", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-tmp-"));
    try {
      const t = new Date();
      const ready = newMediaPath(dir, "Audio", ".wav", t);
      fs.writeFileSync(ready, Buffer.from([9, 9]));
      markComplete(ready, t);
      const open = newMediaPath(dir, "Rec", ".mp4", t);
      fs.writeFileSync(open, Buffer.from([1]));
      const list = listReady(dir);
      assert.ok(list.includes(ready));
      assert.equal(list.includes(open), false);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("deletePair removes media sidecar and 16k", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-tmp-"));
    try {
      const m = path.join(dir, "a.mp4");
      fs.writeFileSync(m, "x");
      fs.writeFileSync(m + ".meta.json", "{}");
      fs.writeFileSync(m + ".16k.wav", "y");
      fs.writeFileSync(m.replace(/\.mp4$/i, ".txt"), "z");
      deletePair(m);
      assert.equal(fs.existsSync(m), false);
      assert.equal(fs.existsSync(m + ".meta.json"), false);
      assert.equal(fs.existsSync(m + ".16k.wav"), false);
      assert.equal(fs.existsSync(m.replace(/\.mp4$/i, ".txt")), false);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });
});
