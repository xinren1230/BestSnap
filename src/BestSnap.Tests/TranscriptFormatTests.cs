using System.Collections.Generic;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class TranscriptFormatTests
    {
        [Fact]
        public void SrtTimestamp_FormatsHoursMinutesMillis()
        {
            Assert.Equal("00:00:01,500", TranscriptFormat.SrtTimestamp(1.5));
            Assert.Equal("00:01:01,000", TranscriptFormat.SrtTimestamp(61));
            Assert.Equal("01:00:00,000", TranscriptFormat.SrtTimestamp(3600));
            Assert.Equal("00:00:00,000", TranscriptFormat.SrtTimestamp(-3));
        }

        [Fact]
        public void ToSrt_NumbersCuesAndDropsEmpty()
        {
            var srt = TranscriptFormat.ToSrt(new[]
            {
                new TranscriptCue { Start = 0, End = 1.2, Text = "Hello" },
                new TranscriptCue { Start = 1.2, End = 2, Text = "  " },
                new TranscriptCue { Start = 2, End = 3.5, Text = "世界" },
            });
            Assert.Contains("1\r\n00:00:00,000 --> 00:00:01,200\r\nHello", srt);
            Assert.Contains("2\r\n00:00:02,000 --> 00:00:03,500\r\n世界", srt);
            Assert.DoesNotContain("3\r\n", srt);
        }

        [Fact]
        public void ToTxt_IncludesBracketTimestamps()
        {
            var t = TranscriptFormat.ToTxt(new[] { new TranscriptCue { Start = 5, End = 6, Text = "hi" } });
            Assert.StartsWith("[00:00:05.000] hi", t);
        }

        [Fact]
        public void LiveChunk_WarmedWindowIsTenSecondsWithTwoSecondOverlap()
        {
            Assert.Equal(3.5, LiveChunkMath.FirstWindowSeconds);
            Assert.Equal(10, LiveChunkMath.WindowSeconds);
            Assert.Equal(2, LiveChunkMath.OverlapSeconds);
            Assert.Equal(8, LiveChunkMath.HopSeconds);
        }

        [Fact]
        public void LiveChunk_FirstWindowThenHop()
        {
            Assert.Equal(0, LiveChunkMath.ChunkAbsStart(0));
            Assert.Equal(LiveChunkMath.FirstWindowSeconds, LiveChunkMath.FireAt(0));
            Assert.Equal(LiveChunkMath.FirstWindowSeconds + LiveChunkMath.HopSeconds, LiveChunkMath.FireAt(1));
            double a1 = LiveChunkMath.ChunkAbsStart(1);
            Assert.Equal(LiveChunkMath.FireAt(1) - LiveChunkMath.WindowSeconds, a1);
            Assert.True(a1 > 0 && a1 < LiveChunkMath.FirstWindowSeconds);
            Assert.Equal(LiveChunkMath.FirstWindowSeconds, LiveChunkMath.WindowForIndex(0));
            Assert.Equal(LiveChunkMath.WindowSeconds, LiveChunkMath.WindowForIndex(1));
        }

        [Fact]
        public void Stitch_DropsOverlapAndClipsStraddle()
        {
            var incoming = new[]
            {
                new TranscriptCue { Start = 0, End = 1, Text = "old" },
                new TranscriptCue { Start = 1.2, End = 2.5, Text = "keep" },
                new TranscriptCue { Start = 0.8, End = 1.6, Text = "straddle" },
            };
            // chunk starts at 6.5s; committed through 8.0s → local 1.5s is the overlap edge
            var stitched = LiveChunkMath.Stitch(incoming, absStart: 6.5, committedThrough: 8.0);
            Assert.DoesNotContain(stitched, c => c.Text == "old"); // 6.5–7.5 fully before 8
            var keep = Assert.Single(stitched, c => c.Text == "keep");
            Assert.Equal(8.0, keep.Start, 3); // 7.7 was inside the overlap, clipped to committedThrough
            var straddle = Assert.Single(stitched, c => c.Text == "straddle");
            Assert.Equal(8.0, straddle.Start, 3);
            Assert.Equal(6.5 + 1.6, straddle.End, 3);
        }

        [Fact]
        public void StitchThenSrt_UsesAbsoluteTimes()
        {
            var added = LiveChunkMath.Stitch(
                new[] { new TranscriptCue { Start = 2, End = 3, Text = "Hello" } },
                absStart: 10, committedThrough: 0);
            var srt = TranscriptFormat.ToSrt(added);
            Assert.Contains("00:00:12,000 --> 00:00:13,000", srt);
        }

        [Fact]
        public void ExtendCommitted_TakesMaxEnd()
        {
            double t = LiveChunkMath.ExtendCommitted(new[]
            {
                new TranscriptCue { Start = 1, End = 2, Text = "a" },
                new TranscriptCue { Start = 2, End = 4.5, Text = "b" },
            }, 1.5);
            Assert.Equal(4.5, t);
        }

        [Fact]
        public void Stitch_KeepsZeroDurationTextUsingChunkLength()
        {
            var added = LiveChunkMath.Stitch(
                new[] { new TranscriptCue { Start = 0, End = 0, Text = "hello" } },
                absStart: 3.5, committedThrough: 0, chunkDuration: 3.5);
            var cue = Assert.Single(added);
            Assert.Equal("hello", cue.Text);
            Assert.Equal(3.5, cue.Start, 3);
            Assert.Equal(7.0, cue.End, 3);
        }

        [Fact]
        public void Stitch_DropsBlankAudio()
        {
            var added = LiveChunkMath.Stitch(
                new[] { new TranscriptCue { Start = 0, End = 1, Text = "[BLANK_AUDIO]" } },
                absStart: 0, committedThrough: 0, chunkDuration: 3.5);
            Assert.Empty(added);
        }
    }
}
