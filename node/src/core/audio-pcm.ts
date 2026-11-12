/** Mix / WAV helpers shared with the audio-only recorder tests. */

export function clampVolume(v: number): number {
  if (!Number.isFinite(v)) return 0;
  if (v < 0) return 0;
  if (v > 1) return 1;
  return v;
}

export function mix(a: Int16Array, b: Int16Array, volA: number, volB: number): Int16Array {
  volA = clampVolume(volA);
  volB = clampVolume(volB);
  const n = Math.max(a.length, b.length);
  const d = new Int16Array(n);
  for (let i = 0; i < n; i++) {
    const sa = i < a.length ? a[i] : 0;
    const sb = i < b.length ? b[i] : 0;
    let s = Math.round(sa * volA + sb * volB);
    if (s > 32767) s = 32767;
    else if (s < -32768) s = -32768;
    d[i] = s;
  }
  return d;
}

export function rms(samples: Int16Array): number {
  if (!samples.length) return 0;
  let acc = 0;
  for (let i = 0; i < samples.length; i++) {
    const x = samples[i] / 32768;
    acc += x * x;
  }
  return Math.sqrt(acc / samples.length);
}

export function wavHeader(dataBytes: number, sampleRate = 44100, channels = 2, bits = 16): Buffer {
  const block = channels * (bits / 8);
  const h = Buffer.alloc(44);
  h.write("RIFF", 0);
  h.writeUInt32LE(36 + dataBytes, 4);
  h.write("WAVE", 8);
  h.write("fmt ", 12);
  h.writeUInt32LE(16, 16);
  h.writeUInt16LE(1, 20);
  h.writeUInt16LE(channels, 22);
  h.writeUInt32LE(sampleRate, 24);
  h.writeUInt32LE(sampleRate * block, 28);
  h.writeUInt16LE(block, 32);
  h.writeUInt16LE(bits, 34);
  h.write("data", 36);
  h.writeUInt32LE(dataBytes, 40);
  return h;
}
