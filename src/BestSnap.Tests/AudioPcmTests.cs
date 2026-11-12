using System;
using System.IO;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class AudioPcmTests
    {
        [Fact]
        public void Mix_AddsWithGainAndClips()
        {
            var a = new short[] { 20000, -20000 };
            var b = new short[] { 20000, -20000 };
            var m = AudioPcm.Mix(a, b, 1, 1);
            Assert.Equal(short.MaxValue, m[0]);
            Assert.Equal(short.MinValue, m[1]);
            var half = AudioPcm.Mix(a, b, 0.5, 0.5);
            Assert.Equal(20000, half[0]);
        }

        [Fact]
        public void Mix_PadsShorterBufferWithSilence()
        {
            var m = AudioPcm.Mix(new short[] { 1000 }, new short[] { 0, 2000 }, 1, 1);
            Assert.Equal(new short[] { 1000, 2000 }, m);
        }

        [Fact]
        public void ClampVolume_HandlesNaNAndRange()
        {
            Assert.Equal(0, AudioPcm.ClampVolume(double.NaN));
            Assert.Equal(0, AudioPcm.ClampVolume(-2));
            Assert.Equal(1, AudioPcm.ClampVolume(9));
            Assert.Equal(0.25, AudioPcm.ClampVolume(0.25));
        }

        [Fact]
        public void Rms_SilentIsZero_FullScaleIsOne()
        {
            Assert.Equal(0, AudioPcm.Rms(new short[] { 0, 0, 0, 0 }));
            Assert.InRange(AudioPcm.Rms(new short[] { short.MaxValue, short.MinValue }), 0.99, 1.01);
        }

        [Fact]
        public void HasVoice_UsesRmsThreshold()
        {
            Assert.False(AudioPcm.HasVoice(new short[1600]));
            var loud = new short[1600];
            for (int i = 0; i < loud.Length; i++) loud[i] = (short)(i % 2 == 0 ? 8000 : -8000);
            Assert.True(AudioPcm.HasVoice(loud, 0.012));
        }

        [Fact]
        public void ToMono16k_DownmixesAndResamples()
        {
            var src = new byte[8];
            src[0] = 0x00; src[1] = 0x10; src[2] = 0x00; src[3] = 0x10;
            var m = AudioPcm.ToMono16k(src, src.Length, 32000, 2, 16);
            Assert.True(m.Length >= 1);
        }

        [Fact]
        public void IsIeeeFloatWave_AcceptsEncoding3AndExtensibleGuid()
        {
            Assert.True(AudioPcm.IsIeeeFloatWave(AudioPcm.WaveFormatIeeeFloat));
            Assert.True(AudioPcm.IsIeeeFloatWave(AudioPcm.WaveFormatExtensible, AudioPcm.IeeeFloatSubType));
            Assert.False(AudioPcm.IsIeeeFloatWave(AudioPcm.WaveFormatExtensible, AudioPcm.PcmSubType));
            Assert.False(AudioPcm.IsIeeeFloatWave(1));
            Assert.False(AudioPcm.IsIeeeFloatWave(AudioPcm.WaveFormatExtensible));
        }

        [Fact]
        public void RepeatWithSilence_ReachesTargetMinutes()
        {
            var speech = new short[AudioPcm.WhisperRate]; // 1 s
            for (int i = 0; i < speech.Length; i++) speech[i] = 8000;
            var stretched = AudioPcm.RepeatWithSilence(speech, gapSeconds: 2, targetMinutes: 0.1);
            Assert.Equal((int)(0.1 * 60 * AudioPcm.WhisperRate), stretched.Length);
            Assert.Equal(8000, stretched[0]);
            Assert.Equal(0, stretched[AudioPcm.WhisperRate]); // gap
        }

        [Fact]
        public void ToFloat32_NormalizesInt16ToUnitRange()
        {
            var f = AudioPcm.ToFloat32(new short[] { 0, 32767, -32768, 16384 });
            Assert.Equal(0, f[0]);
            Assert.InRange(f[1], 0.99f, 1f);
            Assert.Equal(-1f, f[2]);
            Assert.InRange(f[3], 0.49f, 0.51f);
        }

        [Fact]
        public void ToMono16k_IeeeFloat32()
        {
            var src = BitConverter.GetBytes(0.5f);
            var m = AudioPcm.ToMono16k(src, src.Length, 16000, 1, 32, ieeeFloat: true);
            Assert.Single(m);
            Assert.InRange(m[0], 16000, 17000);
            var asInt = AudioPcm.ToMono16k(src, src.Length, 16000, 1, 32, ieeeFloat: false);
            Assert.NotEqual(m[0], asInt[0]);
        }

        [Fact]
        public void Meter_MapsDbToUnitInterval()
        {
            Assert.Equal(0, AudioPcm.Meter(0));
            Assert.InRange(AudioPcm.Meter(1), 0.99, 1.0);
            Assert.True(AudioPcm.Meter(0.01) > 0 && AudioPcm.Meter(0.01) < AudioPcm.Meter(0.1));
        }

        [Fact]
        public void ToStereo16_DuplicatesMonoAndResamples()
        {
            // 8 frames of 16-bit mono at 22050 → 16 frames stereo at 44100
            var src = new byte[16];
            src[0] = 0x00; src[1] = 0x10; // 4096
            var s = AudioPcm.ToStereo16(src, src.Length, 22050, 1, 16);
            Assert.True(s.Length >= 2);
            Assert.Equal(s[0], s[1]); // L=R for mono
        }

        [Fact]
        public void WavWriter_RoundtripsPcmAndPatchesHeader()
        {
            var samples = new short[] { 0, 1000, -1000, 2000 };
            using var ms = new MemoryStream();
            using (var w = new WavPcmWriter(ms, 44100, 2, leaveOpen: true))
                w.Write(samples);
            Assert.True(ms.Length >= 44 + 8);
            var bytes = ms.ToArray();
            Assert.Equal((byte)'R', bytes[0]);
            Assert.Equal((byte)'W', bytes[8]);
            int data = bytes[40] | (bytes[41] << 8) | (bytes[42] << 16) | (bytes[43] << 24);
            Assert.Equal(8, data);
        }

        [Fact]
        public void WriteWav_ThenCutWavFile_KeepsSelectedSeconds()
        {
            // 1 second of stereo silence with a spike in the second half
            var samples = new short[AudioPcm.TargetRate * 2];
            for (int i = AudioPcm.TargetRate; i < samples.Length; i++) samples[i] = 12345;
            var dir = Path.Combine(Path.GetTempPath(), "bs-wav-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var src = Path.Combine(dir, "a.wav");
                var dst = Path.Combine(dir, "b.wav");
                using (var fs = File.Create(src)) AudioPcm.WriteWav(fs, samples);
                AudioPcm.CutWavFile(src, dst, new[] { new TimeRange(0.5, 1.0) });
                var wav = File.ReadAllBytes(dst);
                int data = wav[40] | (wav[41] << 8) | (wav[42] << 16) | (wav[43] << 24);
                Assert.Equal(AudioPcm.TargetRate * 2, data); // 0.5 s * 44100 * 2 ch * 2 bytes
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void NormalizeSourceAndFormat()
        {
            Assert.Equal("System", AudioPcm.NormalizeSource("loopback"));
            Assert.Equal("Mix", AudioPcm.NormalizeSource("Both"));
            Assert.Equal("Microphone", AudioPcm.NormalizeSource(null));
            Assert.Equal("Mp3", AudioPcm.NormalizeFormat("MP3"));
            Assert.Equal("Wav", AudioPcm.NormalizeFormat("x"));
        }

        [Fact]
        public void AudioRecordingSettings_JsonRoundtripAndNormalize()
        {
            var s = new AudioRecordingSettings
            {
                Source = AudioRecordSourceKind.Mix,
                MicrophoneDevice = " mic1 ",
                SystemDevice = "",
                Format = AudioRecordFileFormat.Mp3,
                SaveFolder = @"C:\Rec",
                MicVolume = 1.5,
                SystemVolume = -1,
            };
            var json = s.ToJson();
            var back = AudioRecordingSettings.FromJson(json, @"D:\fallback");
            Assert.Equal(AudioRecordSourceKind.Mix, back.Source);
            Assert.Equal("mic1", back.MicrophoneDevice);
            Assert.Null(back.SystemDevice);
            Assert.Equal(AudioRecordFileFormat.Mp3, back.Format);
            Assert.Equal(@"C:\Rec", back.SaveFolder);
            Assert.Equal(1, back.MicVolume);
            Assert.Equal(0, back.SystemVolume);
            var empty = AudioRecordingSettings.FromJson("not-json", "/tmp/out");
            Assert.Equal("/tmp/out", empty.SaveFolder);
            Assert.Equal(AudioRecordSourceKind.Microphone, empty.Source);
            Assert.Null(empty.LiveSource);
            s.LiveSource = AudioRecordSourceKind.Mix;
            var liveBack = AudioRecordingSettings.FromJson(s.ToJson(), @"D:\fallback");
            Assert.Equal(AudioRecordSourceKind.Mix, liveBack.LiveSource);
        }
    }
}
