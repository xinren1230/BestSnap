using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BestSnap.Plugins
{
    /// <summary>
    /// PCM helpers for the audio-only recorder: mix, RMS meters, linear resample to 44.1 kHz stereo 16-bit, WAV mux.
    /// Sample buffers are interleaved little-endian signed 16-bit.
    /// </summary>
    public static class AudioPcm
    {
        public const int TargetRate = 44100;
        public const int TargetChannels = 2;
        public const int TargetBits = 16;
        public const int BytesPerSample = TargetChannels * (TargetBits / 8);

        /// <summary>Mix two equal-length (or pad-to-longest) 16-bit streams with per-source gain. Result length = max(a,b).</summary>
        public static short[] Mix(short[] a, short[] b, double volA, double volB)
        {
            a ??= Array.Empty<short>();
            b ??= Array.Empty<short>();
            volA = ClampVolume(volA); volB = ClampVolume(volB);
            int n = Math.Max(a.Length, b.Length);
            var d = new short[n];
            for (int i = 0; i < n; i++)
            {
                int sa = i < a.Length ? a[i] : 0;
                int sb = i < b.Length ? b[i] : 0;
                int s = (int)Math.Round(sa * volA + sb * volB);
                if (s > short.MaxValue) s = short.MaxValue;
                else if (s < short.MinValue) s = short.MinValue;
                d[i] = (short)s;
            }
            return d;
        }

        public static double ClampVolume(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            if (v < 0) return 0;
            if (v > 1) return 1;
            return v;
        }

        /// <summary>RMS of 16-bit samples, normalized to 0..1.</summary>
        public static double Rms(short[] samples, int count = -1)
        {
            if (samples == null || samples.Length == 0) return 0;
            int n = count < 0 ? samples.Length : Math.Min(count, samples.Length);
            if (n <= 0) return 0;
            double acc = 0;
            for (int i = 0; i < n; i++)
            {
                double x = samples[i] / 32768.0;
                acc += x * x;
            }
            return Math.Sqrt(acc / n);
        }

        /// <summary>Log-ish meter in 0..1 so quiet speech is still visible.</summary>
        public static double Meter(double rms)
        {
            if (double.IsNaN(rms) || rms <= 0) return 0;
            double db = 20.0 * Math.Log10(Math.Max(1e-9, Math.Min(1, rms)));
            // map -60 dB .. 0 dB → 0..1
            double t = (db + 60.0) / 60.0;
            if (t < 0) return 0;
            if (t > 1) return 1;
            return t;
        }

        /// <summary>
        /// Convert a packed PCM buffer to interleaved stereo 16-bit at <see cref="TargetRate"/>.
        /// Supports 1/2 channels and 16/32-bit integer PCM. 32-bit is treated as 16.16? No: 32-bit PCM is 32-bit int.
        /// Float32 (IEEE) is accepted when <paramref name="ieeeFloat"/> is true.
        /// </summary>
        public static short[] ToStereo16(byte[] src, int bytesRecorded, int srcRate, int srcChannels, int srcBits, bool ieeeFloat = false)
        {
            if (src == null || bytesRecorded <= 0 || srcChannels < 1 || srcRate < 1)
                return Array.Empty<short>();
            int bytesPerFrame = srcChannels * (srcBits / 8);
            if (bytesPerFrame <= 0) return Array.Empty<short>();
            int frames = bytesRecorded / bytesPerFrame;
            if (frames <= 0) return Array.Empty<short>();

            var stereo = new short[frames * 2];
            for (int f = 0; f < frames; f++)
            {
                int off = f * bytesPerFrame;
                short l = ReadSample(src, off, srcBits, ieeeFloat);
                short r = srcChannels > 1 ? ReadSample(src, off + srcBits / 8, srcBits, ieeeFloat) : l;
                stereo[f * 2] = l;
                stereo[f * 2 + 1] = r;
            }
            if (srcRate == TargetRate) return stereo;
            return ResampleLinear(stereo, 2, srcRate, TargetRate);
        }

        public const int WhisperRate = 16000;
        public const int WaveFormatIeeeFloat = 3;
        public const int WaveFormatExtensible = 0xFFFE;
        public static readonly Guid IeeeFloatSubType = new Guid("00000003-0000-0010-8000-00aa00389b71");
        public static readonly Guid PcmSubType = new Guid("00000001-0000-0010-8000-00aa00389b71");

        /// <summary>
        /// WASAPI often reports IEEE float as WAVEFORMATEXTENSIBLE (0xFFFE) with the IEEE float GUID,
        /// not as encoding 3. Treat both as float so loopback is not decoded as 32-bit PCM.
        /// </summary>
        public static bool IsIeeeFloatWave(int encoding, Guid subFormat = default)
        {
            if (encoding == WaveFormatIeeeFloat) return true;
            if (encoding == WaveFormatExtensible && subFormat != Guid.Empty)
                return subFormat == IeeeFloatSubType;
            return false;
        }

        /// <summary>Packed PCM → 16 kHz mono 16-bit (Whisper input).</summary>
        public static short[] ToMono16k(byte[] src, int bytesRecorded, int srcRate, int srcChannels, int srcBits, bool ieeeFloat = false)
        {
            if (src == null || bytesRecorded <= 0 || srcChannels < 1 || srcRate < 1)
                return Array.Empty<short>();
            int bytesPerFrame = srcChannels * (srcBits / 8);
            if (bytesPerFrame <= 0) return Array.Empty<short>();
            int frames = bytesRecorded / bytesPerFrame;
            if (frames <= 0) return Array.Empty<short>();
            var mono = new short[frames];
            for (int f = 0; f < frames; f++)
            {
                int off = f * bytesPerFrame;
                short l = ReadSample(src, off, srcBits, ieeeFloat);
                short r = srcChannels > 1 ? ReadSample(src, off + srcBits / 8, srcBits, ieeeFloat) : l;
                mono[f] = (short)((l + r) / 2);
            }
            if (srcRate == WhisperRate) return mono;
            return ResampleLinear(mono, 1, srcRate, WhisperRate);
        }

        public static bool HasVoice(short[] samples, double rmsThreshold = 0.002)
        {
            if (samples == null || samples.Length == 0) return false;
            if (double.IsNaN(rmsThreshold) || rmsThreshold < 0) rmsThreshold = 0;
            return Rms(samples) >= rmsThreshold;
        }

        /// <summary>
        /// Adaptive VAD for live captions. Quiet headset loopback sits well below a fixed 0.010 RMS;
        /// compare each chunk to a tracked noise floor, with a low absolute floor so speech still passes.
        /// </summary>
        public static class VoiceGate
        {
            public const double AbsoluteMin = 0.002;
            public const double FloorMin = 0.0004;
            public const double Relative = 2.5;
            public const double ThresholdCap = 0.012;
            public const int SilentHintChunks = 4;

            public static double Threshold(double noiseFloor)
            {
                if (double.IsNaN(noiseFloor) || noiseFloor < 0) noiseFloor = 0;
                if (noiseFloor <= 0) return AbsoluteMin;
                double t = noiseFloor * Relative;
                if (t < FloorMin) t = FloorMin;
                if (t > ThresholdCap) t = ThresholdCap;
                return t;
            }

            public static bool IsVoice(double rms, double noiseFloor)
            {
                if (double.IsNaN(rms) || rms < 0) rms = 0;
                return rms >= Threshold(noiseFloor);
            }

            public static bool IsVoice(short[] samples, double noiseFloor) =>
                IsVoice(Rms(samples), noiseFloor);

            public static double UpdateFloor(double floor, double rms, bool voiced)
            {
                if (double.IsNaN(rms) || rms < 0) rms = 0;
                if (double.IsNaN(floor) || floor <= 0) return Math.Max(FloorMin, rms);
                if (voiced) return floor * 0.95 + Math.Min(rms, floor) * 0.05;
                return floor * 0.7 + rms * 0.3;
            }
        }

        static short ReadSample(byte[] src, int offset, int bits, bool ieeeFloat)
        {
            if (offset < 0 || offset >= src.Length) return 0;
            if (ieeeFloat && bits == 32 && offset + 4 <= src.Length)
            {
                float f = BitConverter.ToSingle(src, offset);
                if (float.IsNaN(f)) f = 0;
                int s = (int)Math.Round(Math.Clamp(f, -1f, 1f) * 32767.0);
                return (short)s;
            }
            if (bits == 16 && offset + 2 <= src.Length)
                return (short)(src[offset] | (src[offset + 1] << 8));
            if (bits == 32 && offset + 4 <= src.Length)
            {
                int v = src[offset] | (src[offset + 1] << 8) | (src[offset + 2] << 16) | (src[offset + 3] << 24);
                return (short)(v >> 16);
            }
            if (bits == 8 && offset < src.Length)
                return (short)((src[offset] - 128) << 8);
            if (bits == 24 && offset + 3 <= src.Length)
            {
                int v = src[offset] | (src[offset + 1] << 8) | (src[offset + 2] << 16);
                if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                return (short)(v >> 8);
            }
            return 0;
        }

        /// <summary>Linear resample of interleaved PCM. <paramref name="channels"/> is 1 or 2.</summary>
        public static short[] ResampleLinear(short[] src, int channels, int srcRate, int dstRate)
        {
            if (src == null || src.Length == 0 || channels < 1) return Array.Empty<short>();
            if (srcRate == dstRate || srcRate <= 0 || dstRate <= 0) return (short[])src.Clone();
            int srcFrames = src.Length / channels;
            if (srcFrames <= 0) return Array.Empty<short>();
            int dstFrames = (int)Math.Max(1L, (long)srcFrames * dstRate / srcRate);
            var dst = new short[dstFrames * channels];
            for (int i = 0; i < dstFrames; i++)
            {
                double pos = (double)i * srcRate / dstRate;
                int i0 = (int)pos;
                int i1 = i0 + 1;
                if (i0 >= srcFrames) i0 = srcFrames - 1;
                if (i1 >= srcFrames) i1 = srcFrames - 1;
                double t = pos - Math.Floor(pos);
                for (int c = 0; c < channels; c++)
                {
                    int a = src[i0 * channels + c];
                    int b = src[i1 * channels + c];
                    dst[i * channels + c] = (short)Math.Round(a + (b - a) * t);
                }
            }
            return dst;
        }

        /// <summary>44-byte WAV header for PCM; <paramref name="dataBytes"/> is the payload size.</summary>
        public static byte[] WavHeader(int dataBytes, int sampleRate = TargetRate, int channels = TargetChannels, int bits = TargetBits)
        {
            if (dataBytes < 0) dataBytes = 0;
            if (sampleRate < 1) sampleRate = TargetRate;
            if (channels < 1) channels = TargetChannels;
            if (bits < 8) bits = TargetBits;
            int block = channels * (bits / 8);
            int byteRate = sampleRate * block;
            int riffSize = 36 + dataBytes;
            var h = new byte[44];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(h, 0);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), riffSize);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(h, 8);
            Encoding.ASCII.GetBytes("fmt ").CopyTo(h, 12);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(16), 16);
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(20), 1); // PCM
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(22), (short)channels);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(24), sampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(28), byteRate);
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(32), (short)block);
            BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(34), (short)bits);
            Encoding.ASCII.GetBytes("data").CopyTo(h, 36);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(40), dataBytes);
            return h;
        }

        public static void WriteWav(Stream stream, short[] interleaved, int sampleRate = TargetRate, int channels = TargetChannels)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            interleaved ??= Array.Empty<short>();
            var bytes = new byte[interleaved.Length * 2];
            Buffer.BlockCopy(interleaved, 0, bytes, 0, bytes.Length);
            var hdr = WavHeader(bytes.Length, sampleRate, channels, 16);
            stream.Write(hdr, 0, hdr.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        public static byte[] ToWavBytes(short[] interleaved, int sampleRate, int channels)
        {
            using var ms = new MemoryStream();
            WriteWav(ms, interleaved, sampleRate, channels);
            return ms.ToArray();
        }

        /// <summary>PCM WAV file → 16 kHz mono 16-bit samples (Whisper). Returns empty if not PCM WAV.</summary>
        public static short[] Mono16kFromWavFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Array.Empty<short>();
            using var fs = File.OpenRead(path);
            if (!TryReadPcmWav(fs, out int rate, out int ch, out int bits, out long dataPos, out int dataBytes))
                return Array.Empty<short>();
            if (dataBytes <= 0 || bits < 8) return Array.Empty<short>();
            int n = Math.Min(dataBytes, int.MaxValue - 16);
            var buf = new byte[n];
            fs.Position = dataPos;
            int got = fs.Read(buf, 0, n);
            return ToMono16k(buf, got, rate, ch, bits);
        }

        public static string NormalizeSource(string s)
        {
            if (string.Equals(s, "System", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Loopback", StringComparison.OrdinalIgnoreCase))
                return "System";
            if (string.Equals(s, "Mix", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Both", StringComparison.OrdinalIgnoreCase))
                return "Mix";
            return "Microphone";
        }

        public static string NormalizeFormat(string s) =>
            string.Equals(s, "Mp3", StringComparison.OrdinalIgnoreCase) ? "Mp3" : "Wav";

        public static string CoalesceFolder(string folder, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(folder)) return folder.Trim();
            return string.IsNullOrWhiteSpace(fallback) ? "" : fallback.Trim();
        }

        public static float[] Peaks(short[] samples, int buckets, int channels = TargetChannels)
        {
            if (samples == null || buckets <= 0) return Array.Empty<float>();
            int ch = Math.Max(1, channels);
            int frames = samples.Length / ch;
            var p = new float[buckets];
            if (frames <= 0) return p;
            for (int i = 0; i < buckets; i++)
            {
                int a = (int)((long)i * frames / buckets);
                int b = (int)((long)(i + 1) * frames / buckets);
                if (b <= a) b = a + 1;
                int m = 0;
                for (int f = a; f < b && f < frames; f++)
                    for (int c = 0; c < ch; c++)
                    {
                        int v = samples[f * ch + c];
                        if (v < 0) v = -v;
                        if (v > m) m = v;
                    }
                p[i] = m / 32768f;
            }
            return p;
        }

        /// <summary>PCM WAV only (format 1). Concatenates keep-ranges into a new file.</summary>
        public static void CutWavFile(string input, string output, IReadOnlyList<TimeRange> keep)
        {
            if (keep == null || keep.Count == 0) throw new ArgumentException("need keep segments", nameof(keep));
            using var src = File.OpenRead(input);
            if (!TryReadPcmWav(src, out int rate, out int ch, out int bits, out long dataPos, out int dataBytes))
                throw new InvalidDataException("not a PCM WAV file");
            int block = ch * (bits / 8);
            if (block <= 0) throw new InvalidDataException("bad WAV block align");
            int frames = dataBytes / block;
            double dur = rate <= 0 ? 0 : (double)frames / rate;
            var segs = TrimRangeMath.Merge(keep, dur <= 0 ? 1 : dur);
            if (segs.Count == 0) throw new InvalidOperationException("nothing to keep");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
            using var dst = File.Create(output);
            dst.Write(WavHeader(0, rate, ch, bits), 0, 44);
            var buf = new byte[64 * 1024];
            long written = 0;
            foreach (var s in segs)
            {
                int f0 = (int)Math.Clamp(Math.Round(s.Start * rate), 0, frames);
                int f1 = (int)Math.Clamp(Math.Round(s.End * rate), 0, frames);
                if (f1 <= f0) continue;
                long pos = dataPos + (long)f0 * block;
                long remain = (long)(f1 - f0) * block;
                src.Position = pos;
                while (remain > 0)
                {
                    int n = src.Read(buf, 0, (int)Math.Min(buf.Length, remain));
                    if (n <= 0) break;
                    dst.Write(buf, 0, n);
                    remain -= n;
                    written += n;
                }
            }
            var hdr = WavHeader(checked((int)Math.Min(int.MaxValue, written)), rate, ch, bits);
            dst.Position = 0;
            dst.Write(hdr, 0, 44);
        }

        public static bool TryReadPcmWav(Stream s, out int sampleRate, out int channels, out int bits, out long dataPos, out int dataBytes)
        {
            sampleRate = 0; channels = 0; bits = 0; dataPos = 0; dataBytes = 0;
            if (s == null || !s.CanSeek) return false;
            s.Position = 0;
            var hdr = new byte[12];
            if (s.Read(hdr, 0, 12) < 12) return false;
            if (hdr[0] != 'R' || hdr[1] != 'I' || hdr[2] != 'F' || hdr[3] != 'F') return false;
            if (hdr[8] != 'W' || hdr[9] != 'A' || hdr[10] != 'V' || hdr[11] != 'E') return false;
            int fmt = 0;
            while (s.Position + 8 <= s.Length)
            {
                var chBuf = new byte[8];
                if (s.Read(chBuf, 0, 8) < 8) return false;
                int size = BinaryPrimitives.ReadInt32LittleEndian(chBuf.AsSpan(4));
                if (size < 0) return false;
                long next = s.Position + size + (size & 1);
                if (chBuf[0] == 'f' && chBuf[1] == 'm' && chBuf[2] == 't' && chBuf[3] == ' ')
                {
                    var fmtBuf = new byte[Math.Max(16, size)];
                    if (s.Read(fmtBuf, 0, Math.Min(fmtBuf.Length, size)) < Math.Min(16, size)) return false;
                    fmt = BinaryPrimitives.ReadInt16LittleEndian(fmtBuf.AsSpan(0));
                    channels = BinaryPrimitives.ReadInt16LittleEndian(fmtBuf.AsSpan(2));
                    sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmtBuf.AsSpan(4));
                    bits = BinaryPrimitives.ReadInt16LittleEndian(fmtBuf.AsSpan(14));
                }
                else if (chBuf[0] == 'd' && chBuf[1] == 'a' && chBuf[2] == 't' && chBuf[3] == 'a')
                {
                    dataPos = s.Position;
                    dataBytes = size;
                    return fmt == 1 && channels > 0 && sampleRate > 0 && bits > 0;
                }
                if (next > s.Length) return false;
                s.Position = next;
            }
            return false;
        }
    }

    /// <summary>Streaming WAV writer that patches RIFF/data sizes on dispose.</summary>
    public sealed class WavPcmWriter : IDisposable
    {
        readonly Stream _s;
        readonly bool _leaveOpen;
        long _dataBytes;
        bool _done;

        public WavPcmWriter(Stream stream, int sampleRate = AudioPcm.TargetRate, int channels = AudioPcm.TargetChannels, bool leaveOpen = false)
        {
            _s = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
            SampleRate = sampleRate < 1 ? AudioPcm.TargetRate : sampleRate;
            Channels = channels < 1 ? AudioPcm.TargetChannels : channels;
            var hdr = AudioPcm.WavHeader(0, SampleRate, Channels, 16);
            _s.Write(hdr, 0, hdr.Length);
        }

        public int SampleRate { get; }
        public int Channels { get; }
        public long DataBytes => _dataBytes;
        public long SampleFrames => Channels == 0 ? 0 : _dataBytes / (2L * Channels);

        public void Write(short[] samples, int count = -1)
        {
            if (_done || samples == null) return;
            int n = count < 0 ? samples.Length : Math.Min(count, samples.Length);
            if (n <= 0) return;
            var bytes = new byte[n * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            _s.Write(bytes, 0, bytes.Length);
            _dataBytes += bytes.Length;
        }

        public void Finish()
        {
            if (_done) return;
            _done = true;
            if (!_s.CanSeek) return;
            var hdr = AudioPcm.WavHeader(checked((int)Math.Min(int.MaxValue, _dataBytes)), SampleRate, Channels, 16);
            long pos = _s.Position;
            _s.Position = 0;
            _s.Write(hdr, 0, hdr.Length);
            _s.Position = pos;
            _s.Flush();
        }

        public void Dispose()
        {
            Finish();
            if (!_leaveOpen) _s.Dispose();
        }
    }
}
