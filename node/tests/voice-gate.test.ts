import assert from "node:assert/strict";
import { test } from "node:test";
import { isVoice, VOICE_ABSOLUTE_MIN, VOICE_FLOOR_MIN, VOICE_THRESHOLD_CAP, voiceThreshold } from "../src/core/audio-pcm.js";

test("unknown floor uses absolute min", () => {
  assert.equal(voiceThreshold(0), VOICE_ABSOLUTE_MIN);
  assert.equal(voiceThreshold(Number.NaN), VOICE_ABSOLUTE_MIN);
});

test("quiet headset floor is well below the old 0.010 bar", () => {
  const t = voiceThreshold(0.0002);
  assert.ok(t < 0.01);
  assert.ok(t >= VOICE_FLOOR_MIN);
  assert.equal(isVoice(0.001, 0.0002), true);
  assert.equal(isVoice(0.0001, 0.0002), false);
});

test("noisy floor raises then caps", () => {
  assert.equal(voiceThreshold(0.02), VOICE_THRESHOLD_CAP);
  assert.equal(isVoice(0.005, 0.02), false);
  assert.equal(isVoice(0.02, 0.02), true);
});
