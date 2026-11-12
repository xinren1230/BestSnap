using System;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class TranscribeTestArgsTests
    {
        [Fact]
        public void Parse_LiveSimAndOut()
        {
            var a = TranscribeTestArgs.Parse(new[]
            {
                "--transcribe-test", @"C:\a.wav", "--model", "tiny", "--backend", "cpu",
                "--live-sim", "--debug-chunks", "--out", @"C:\out.txt"
            });
            Assert.Equal(@"C:\a.wav", a.Input);
            Assert.Equal("tiny", a.Model);
            Assert.Equal("cpu", a.Backend);
            Assert.Equal(WhisperBackend.Cpu, a.ParseBackend());
            Assert.True(a.LiveSim);
            Assert.True(a.DebugChunks);
            Assert.Equal(@"C:\out.txt", a.Out);
            Assert.Equal(0, a.StretchMinutes);
        }

        [Fact]
        public void Parse_StretchMinutes()
        {
            var a = TranscribeTestArgs.Parse(new[] { "--transcribe-test", "a.wav", "--stretch-minutes", "10", "--live-sim" });
            Assert.Equal(10, a.StretchMinutes);
            Assert.True(a.LiveSim);
            Assert.Throws<ArgumentException>(() => TranscribeTestArgs.Parse(new[] { "--transcribe-test", "a.wav", "--stretch-minutes", "-1" }));
        }

        [Fact]
        public void Parse_DefaultsAutoBackend()
        {
            var a = TranscribeTestArgs.Parse(new[] { "--transcribe-test", "speech.mp4" });
            Assert.Equal("speech.mp4", a.Input);
            Assert.Equal("auto", a.Model);
            Assert.Null(a.ParseBackend());
            Assert.False(a.LiveSim);
        }

        [Fact]
        public void Parse_RejectsUnknownAndMissingFile()
        {
            Assert.Throws<ArgumentException>(() => TranscribeTestArgs.Parse(new[] { "--fullscreen" }));
            Assert.Throws<ArgumentException>(() => TranscribeTestArgs.Parse(new[] { "--transcribe-test" }));
            Assert.Throws<ArgumentException>(() => TranscribeTestArgs.Parse(new[] { "--transcribe-test", "a.wav", "--nope" }));
            var a = TranscribeTestArgs.Parse(new[] { "--transcribe-test", "a.wav", "--backend", "cuda" });
            Assert.Equal(WhisperBackend.Cuda, a.ParseBackend());
            a.Backend = "nope";
            Assert.Throws<ArgumentException>(() => a.ParseBackend());
        }

        [Fact]
        public void LooksLike_IsCaseInsensitive()
        {
            Assert.True(TranscribeTestArgs.LooksLike(new[] { "--Transcribe-Test", "x.wav" }));
            Assert.False(TranscribeTestArgs.LooksLike(new[] { "--record" }));
        }
    }
}
