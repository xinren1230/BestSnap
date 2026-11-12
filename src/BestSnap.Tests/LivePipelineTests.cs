using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class LivePipelineTests
    {
        internal static string JfkPath()
        {
            string named = Path.Combine(AppContext.BaseDirectory, "fixtures", "jfk.wav");
            if (File.Exists(named)) return named;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string p = Path.Combine(dir.FullName, "src", "BestSnap.Tests", "fixtures", "jfk.wav");
                if (File.Exists(p)) return p;
                p = Path.Combine(dir.FullName, "fixtures", "jfk.wav");
                if (File.Exists(p)) return p;
            }
            throw new FileNotFoundException("jfk.wav");
        }

        [Fact]
        public void JfkWav_Is16kMonoPcmWithSpeechEnergy()
        {
            var pcm = AudioPcm.Mono16kFromWavFile(JfkPath());
            Assert.True(pcm.Length > 8 * AudioPcm.WhisperRate, "jfk.wav should be ~11 s");
            Assert.InRange(pcm.Length / (double)AudioPcm.WhisperRate, 8, 14);
            double rms = AudioPcm.Rms(pcm);
            Assert.True(rms > AudioPcm.VoiceGate.AbsoluteMin, "speech RMS " + rms);
            var f = AudioPcm.ToFloat32(pcm);
            Assert.Equal(pcm.Length, f.Length);
            float min = 0, max = 0;
            for (int i = 0; i < f.Length; i++)
            {
                if (f[i] < min) min = f[i];
                if (f[i] > max) max = f[i];
            }
            Assert.InRange(min, -1f, 0f);
            Assert.InRange(max, 0f, 1f);
        }

        [Fact]
        public void JfkWav_LiveSliceProducesVoicedWindows()
        {
            var pcm = AudioPcm.Mono16kFromWavFile(JfkPath());
            var windows = LiveTranscribePipeline.SlicePcm(pcm);
            Assert.NotEmpty(windows);
            double floor = 0;
            int voiced = 0;
            foreach (var w in windows)
            {
                if (LiveTranscribePipeline.EvaluateVad(w.Pcm, ref floor, out _, out _))
                    voiced++;
            }
            Assert.True(voiced >= 1);
        }

        [Fact]
        public async Task JfkLivePipeline_ZeroDurationTimestampsSurviveStitch()
        {
            var pcm = AudioPcm.Mono16kFromWavFile(JfkPath());
            var result = await LiveTranscribePipeline.RunAsync(pcm, (chunk, prev, ct) =>
            {
                var cues = new List<TranscriptCue>
                {
                    new TranscriptCue { Start = 0, End = 0, Text = "And so my fellow Americans" }
                };
                return Task.FromResult(cues);
            }, null, null, CancellationToken.None);
            Assert.True(result.Processed >= 1);
            Assert.NotEmpty(result.Cues);
            Assert.Contains(result.Cues, c => c.Text.Contains("Americans"));
            Assert.True(result.Cues[0].End - result.Cues[0].Start >= 0.04);
        }

        [Fact]
        public async Task JfkLivePipeline_BlankAudioIsDropped()
        {
            var pcm = AudioPcm.Mono16kFromWavFile(JfkPath());
            var result = await LiveTranscribePipeline.RunAsync(pcm, (chunk, prev, ct) =>
                Task.FromResult(new List<TranscriptCue>
                {
                    new TranscriptCue { Start = 0, End = 1, Text = "[BLANK_AUDIO]" }
                }), null, null, CancellationToken.None);
            Assert.Empty(result.Cues);
        }

        [Fact]
        public void DebugDump_Writes16kHzMonoWav()
        {
            var pcm = new short[AudioPcm.WhisperRate];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(i % 2 == 0 ? 8000 : -8000);
            var dir = Path.Combine(Path.GetTempPath(), "bs-dbg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = WhisperDebugChunks.Write(1, pcm, dir);
                Assert.True(File.Exists(path));
                using var fs = File.OpenRead(path);
                Assert.True(AudioPcm.TryReadWav(fs, out int rate, out int ch, out int bits, out _, out int data, out bool ieee));
                Assert.Equal(AudioPcm.WhisperRate, rate);
                Assert.Equal(1, ch);
                Assert.Equal(16, bits);
                Assert.False(ieee);
                Assert.Equal(pcm.Length * 2, data);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
