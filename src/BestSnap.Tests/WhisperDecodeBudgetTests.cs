using System.Collections.Generic;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class WhisperDecodeBudgetTests
    {
        [Fact]
        public void LiveTimeout_IsAboutTwoAndAHalfWindowsCapped()
        {
            Assert.Equal(25, WhisperDecodeBudget.TimeoutSeconds(10, live: true));
            Assert.InRange(WhisperDecodeBudget.TimeoutSeconds(3.5, live: true), 8, 25);
            Assert.Equal(45, WhisperDecodeBudget.TimeoutSeconds(30, live: true));
        }

        [Fact]
        public void FileTimeout_UsesThirtySecondWindows()
        {
            Assert.Equal(75, WhisperDecodeBudget.TimeoutSeconds(FileChunkMath.WindowSeconds, live: false));
            Assert.Equal(90, WhisperDecodeBudget.TimeoutSeconds(60, live: false));
            Assert.Equal(20, WhisperDecodeBudget.TimeoutSeconds(1, live: false));
        }

        [Fact]
        public void DropOldest_KeepsAtMostTwoPending()
        {
            Assert.Equal(0, WhisperDecodeBudget.DropOldestCount(2));
            Assert.Equal(3, WhisperDecodeBudget.DropOldestCount(5));
            Assert.Equal(0, WhisperDecodeBudget.DropOldestCount(0));
        }

        [Fact]
        public void LagExceedsHops_UsesTwoLiveHops()
        {
            Assert.False(WhisperDecodeBudget.LagExceedsHops(0, LiveChunkMath.HopSeconds));
            Assert.True(WhisperDecodeBudget.LagExceedsHops(0, LiveChunkMath.HopSeconds * 2 + 0.1));
        }

        [Fact]
        public void ExceedsDurationFactor_IsThreeTimes()
        {
            Assert.False(WhisperDecodeBudget.ExceedsDurationFactor(29, 10));
            Assert.True(WhisperDecodeBudget.ExceedsDurationFactor(31, 10));
            Assert.True(WhisperDecodeBudget.IsSlow(21, 10, live: true));
        }

        [Fact]
        public void CuesMissingInSecondHalf_DetectsStop()
        {
            var cues = new List<TranscriptCue>
            {
                new TranscriptCue { Start = 1, End = 2, Text = "early" }
            };
            Assert.True(WhisperDecodeBudget.CuesMissingInSecondHalf(cues, 20));
            cues.Add(new TranscriptCue { Start = 11, End = 12, Text = "late" });
            Assert.False(WhisperDecodeBudget.CuesMissingInSecondHalf(cues, 20));
        }

        [Fact]
        public void SelfTest_FailsOnSlowChunkOrMissingSecondHalf()
        {
            var reports = new List<WindowDecodeReport>
            {
                new WindowDecodeReport { Index = 3, Voiced = true, Duration = 10, ElapsedSeconds = 40 }
            };
            var cues = new List<TranscriptCue> { new TranscriptCue { Start = 1, End = 2, Text = "a" }, new TranscriptCue { Start = 80, End = 81, Text = "b" } };
            Assert.Contains("3x", WhisperDecodeBudget.SelfTestFailure(reports, cues, 100));
            reports[0].ElapsedSeconds = 1;
            Assert.Null(WhisperDecodeBudget.SelfTestFailure(reports, cues, 100));
            reports[0].TimedOut = true;
            Assert.Contains("timeout", WhisperDecodeBudget.SelfTestFailure(reports, cues, 100));
            reports[0].TimedOut = false;
            Assert.Contains("second half", WhisperDecodeBudget.SelfTestFailure(reports, new[] { cues[0] }, 100));
        }

        [Fact]
        public void BoundedQueue_DropsOldestWhenOverMaxPending()
        {
            var q = new BoundedJobQueue<int>(2);
            Assert.Equal(1, q.EnqueueNewest(1, out int d0));
            Assert.Equal(0, d0);
            Assert.Equal(2, q.EnqueueNewest(2, out int d1));
            Assert.Equal(0, d1);
            Assert.Equal(2, q.EnqueueNewest(3, out int d2));
            Assert.Equal(1, d2);
            Assert.Equal(2, q.EnqueueNewest(4, out int d3));
            Assert.Equal(1, d3);
            Assert.True(q.TryDequeue(out int a));
            Assert.True(q.TryDequeue(out int b));
            Assert.Equal(3, a);
            Assert.Equal(4, b);
            Assert.False(q.TryDequeue(out _));
        }
    }
}
