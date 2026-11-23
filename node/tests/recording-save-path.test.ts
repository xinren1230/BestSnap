import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import { defaultEditedPath, promoteToFolder, uniqueInFolder } from "../src/core/recording-save-path.js";
import { transcriptPathFor, writeForMedia } from "../src/core/transcript-sidecar.js";

describe("recording save path", () => {
  it("defaultEditedPath uses -edited and never the original", () => {
    const orig = path.join(os.tmpdir(), "clip.mp4");
    const p = defaultEditedPath(orig);
    assert.match(p, /clip-edited\.mp4$/);
    assert.notEqual(path.resolve(p), path.resolve(orig));
    assert.match(defaultEditedPath(orig, ".webm"), /clip-edited\.webm$/);
  });

  it("uniqueInFolder skips existing", () => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bs-save-"));
    try {
      fs.writeFileSync(path.join(dir, "Rec.mp4"), Buffer.from([1]));
      const p = uniqueInFolder(dir, "Rec", ".mp4");
      assert.match(p, /Rec \(2\)\.mp4$/);
    } finally {
      fs.rmSync(dir, { recursive: true, force: true });
    }
  });

  it("promoteToFolder moves media and txt", () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "bs-prom-"));
    const temp = path.join(root, "temp");
    const dest = path.join(root, "Videos");
    fs.mkdirSync(temp);
    fs.mkdirSync(dest);
    try {
      const src = path.join(temp, "Rec_20261123_120000.mp4");
      fs.writeFileSync(src, Buffer.from([9, 9, 9]));
      fs.writeFileSync(src + ".meta.json", "{\"complete\":true}");
      writeForMedia(src, "hello");
      const saved = promoteToFolder(src, dest);
      assert.equal(fs.existsSync(saved), true);
      assert.equal(path.dirname(saved), dest);
      assert.equal(fs.existsSync(src), false);
      assert.equal(fs.existsSync(transcriptPathFor(src)), false);
      assert.equal(fs.existsSync(transcriptPathFor(saved)), true);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });
});
