import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

test("CLI --help exits 0", () => {
  const r = spawnSync("npx", ["tsx", "src/cli/index.ts", "--help"], { cwd: root, encoding: "utf8" });
  assert.equal(r.status, 0, r.stderr);
  assert.match(r.stdout, /bestsnap ocr/);
  assert.match(r.stdout, /latex/);
});
