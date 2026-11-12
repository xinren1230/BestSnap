using System.Collections.Generic;
using System.IO;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class TrimRangeMathTests
    {
        [Fact]
        public void Merge_OverlappingAndTouchingBecomeOne()
        {
            var m = TrimRangeMath.Merge(new[]
            {
                new TimeRange(1, 3),
                new TimeRange(2.5, 4),
                new TimeRange(4, 5),
                new TimeRange(8, 9),
            }, 20);
            Assert.Equal(2, m.Count);
            Assert.Equal(1, m[0].Start);
            Assert.Equal(5, m[0].End);
            Assert.Equal(8, m[1].Start);
        }

        [Fact]
        public void Merge_ClampsAndDropsEmpty()
        {
            var m = TrimRangeMath.Merge(new[]
            {
                new TimeRange(-2, 1),
                new TimeRange(9, 9),
                new TimeRange(12, 15),
            }, 10);
            Assert.Single(m);
            Assert.Equal(0, m[0].Start);
            Assert.Equal(1, m[0].End);
        }

        [Fact]
        public void KeepFromRemove_CutsMiddleAndEdges()
        {
            var keep = TrimRangeMath.KeepFromRemove(10, new[] { new TimeRange(0, 1), new TimeRange(4, 6), new TimeRange(9, 10) });
            Assert.Equal(2, keep.Count);
            Assert.Equal(new TimeRange(1, 4), keep[0]);
            Assert.Equal(new TimeRange(6, 9), keep[1]);
        }

        [Fact]
        public void KeepFromRemove_NoCutsKeepsAll()
        {
            var keep = TrimRangeMath.KeepFromRemove(5, System.Array.Empty<TimeRange>());
            Assert.Single(keep);
            Assert.Equal(new TimeRange(0, 5), keep[0]);
        }

        [Fact]
        public void KeepFromRemove_FullCoverIsEmpty()
        {
            Assert.Empty(TrimRangeMath.KeepFromRemove(5, new[] { new TimeRange(0, 5) }));
        }

        [Fact]
        public void RemoveFromKeep_Inverts()
        {
            var remove = TrimRangeMath.RemoveFromKeep(10, new[] { new TimeRange(2, 5), new TimeRange(7, 8) });
            Assert.Equal(3, remove.Count);
            Assert.Equal(new TimeRange(0, 2), remove[0]);
            Assert.Equal(new TimeRange(5, 7), remove[1]);
            Assert.Equal(new TimeRange(8, 10), remove[2]);
        }

        [Fact]
        public void TotalDuration_Sums()
        {
            Assert.Equal(5, TrimRangeMath.TotalDuration(new[] { new TimeRange(0, 2), new TimeRange(4, 7) }));
        }

        [Fact]
        public void DefaultOutputPath_UsesTrimmedSuffixAndAvoidsCollision()
        {
            var orig = Path.Combine(Path.GetTempPath(), "clip.mp4");
            var taken = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            string p1 = TrimRangeMath.DefaultOutputPath(orig, exists: taken.Contains);
            Assert.EndsWith("clip_trimmed.mp4", p1);
            taken.Add(p1);
            string p2 = TrimRangeMath.DefaultOutputPath(orig, exists: taken.Contains);
            Assert.EndsWith("clip_trimmed-2.mp4", p2);
            Assert.NotEqual(orig, p1);
        }

        [Fact]
        public void CanTrimPath_Mp4WavMp3()
        {
            Assert.True(TrimRangeMath.CanTrimPath("a.MP4"));
            Assert.True(TrimRangeMath.CanTrimPath("a.wav"));
            Assert.True(TrimRangeMath.CanTrimPath("a.mp3"));
            Assert.False(TrimRangeMath.CanTrimPath("a.gif"));
            Assert.True(TrimRangeMath.IsAudioPath("n.mp3"));
            Assert.False(TrimRangeMath.IsAudioPath("n.mp4"));
        }

        [Fact]
        public void BuildFilter_ConcatTwoVideoAudioSegments()
        {
            var f = TrimRangeMath.BuildFilter(new[] { new TimeRange(0, 1.5), new TimeRange(3, 5) }, video: true, audio: true);
            Assert.Contains("[0:v]trim=start=0:end=1.5,setpts=PTS-STARTPTS[v0]", f);
            Assert.Contains("[0:a]atrim=start=3:end=5,asetpts=PTS-STARTPTS[a1]", f);
            Assert.Contains("concat=n=2:v=1:a=1[outv][outa]", f);
        }

        [Fact]
        public void BuildFilter_AudioOnly()
        {
            var f = TrimRangeMath.BuildFilter(new[] { new TimeRange(1, 2) }, video: false, audio: true);
            Assert.DoesNotContain("[0:v]", f);
            Assert.Contains("concat=n=1:v=0:a=1[outa]", f);
        }

        [Fact]
        public void BuildArgs_SingleSegmentUsesSsTo()
        {
            var args = TrimRangeMath.BuildArgs("in.mp4", "out_trimmed.mp4", new[] { new TimeRange(1, 4) }, true, true);
            Assert.Contains("-ss", args);
            Assert.Contains("1", args);
            Assert.Contains("-to", args);
            Assert.Contains("libx264", args);
            Assert.Contains("aac", args);
            Assert.DoesNotContain("-filter_complex", args);
        }

        [Fact]
        public void BuildArgs_MultiSegmentUsesFilterComplexAndWavCodec()
        {
            var args = TrimRangeMath.BuildArgs("in.wav", "out.wav", new[] { new TimeRange(0, 1), new TimeRange(2, 3) }, false, true);
            Assert.Contains("-filter_complex", args);
            Assert.Contains("pcm_s16le", args);
            Assert.Contains("[outa]", args);
        }

        [Fact]
        public void TryCreate_RejectsTinyAndNaN()
        {
            Assert.Null(TrimRangeMath.TryCreate(1, 1, 10));
            Assert.Null(TrimRangeMath.TryCreate(double.NaN, 1, 10));
            Assert.Null(TrimRangeMath.TryCreate(0, 1, 0));
            Assert.NotNull(TrimRangeMath.TryCreate(8, 2, 10)); // swapped
        }
    }
}
