using System;
using System.Collections.Generic;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class EraserMathTests
    {
        [Fact]
        public void DistToSegment_IsZeroOnTheLine()
        {
            Assert.Equal(0, EraserMath.DistToSegment(5, 0, 0, 0, 10, 0), 6);
            Assert.Equal(0, EraserMath.DistToSegment(0, 0, 0, 0, 10, 0), 6);
            Assert.Equal(0, EraserMath.DistToSegment(10, 0, 0, 0, 10, 0), 6);
        }

        [Fact]
        public void DistToSegment_UsesNearestEndpointPastTheEnds()
        {
            Assert.Equal(5, EraserMath.DistToSegment(-5, 0, 0, 0, 10, 0), 6);
            Assert.Equal(5, EraserMath.DistToSegment(15, 0, 0, 0, 10, 0), 6);
        }

        [Fact]
        public void HitsRect_UsesInflatedBoxAsBrush()
        {
            Assert.True(EraserMath.HitsRect(50, 50, 0, 0, 40, 40, 12));  // 10 px outside
            Assert.False(EraserMath.HitsRect(50, 50, 0, 0, 40, 40, 8));
            Assert.True(EraserMath.HitsRect(20, 20, 0, 0, 40, 40, 0));
        }

        [Fact]
        public void HitsSegment_MatchesLinePlusRadius()
        {
            Assert.True(EraserMath.HitsSegment(0, 5, 0, 0, 10, 0, 6));
            Assert.False(EraserMath.HitsSegment(0, 5, 0, 0, 10, 0, 4));
        }

        [Fact]
        public void SweepHits_ReportsTopmostFirstAndOnlyOnce()
        {
            // Two overlapping boxes; later index is topmost.
            bool Hit(int i, double x, double y) =>
                i == 0 ? EraserMath.HitsRect(x, y, 0, 0, 40, 40, 8)
                       : EraserMath.HitsRect(x, y, 20, 20, 60, 60, 8);

            var path = new List<(double, double)> { (30, 30), (30, 30), (80, 80) };
            Assert.Equal(new[] { 1, 0 }, EraserMath.SweepHits(path, 2, Hit));
        }

        [Fact]
        public void SweepHits_DragPicksUpASecondObject()
        {
            bool Hit(int i, double x, double y) =>
                i == 0 ? EraserMath.HitsRect(x, y, 0, 0, 20, 20, 4)
                       : EraserMath.HitsRect(x, y, 80, 0, 100, 20, 4);

            var path = new List<(double, double)> { (10, 10), (50, 10), (90, 10) };
            Assert.Equal(new[] { 0, 1 }, EraserMath.SweepHits(path, 2, Hit));
        }

        [Fact]
        public void SweepHits_MissesWhenRadiusTooSmall()
        {
            bool Hit(int i, double x, double y) => EraserMath.HitsRect(x, y, 0, 0, 10, 10, 1);
            var path = new List<(double, double)> { (40, 40) };
            Assert.Empty(EraserMath.SweepHits(path, 1, Hit));
        }

        [Fact]
        public void MedianRgb_EmptyFallsBackToWhite()
        {
            Assert.Equal((255, 255, 255), EraserMath.MedianRgb(Array.Empty<(byte, byte, byte)>()));
            Assert.Equal((255, 255, 255), EraserMath.MedianRgb(null));
        }

        [Fact]
        public void MedianRgb_PicksMiddleLuminance()
        {
            var samples = new List<(byte, byte, byte)>
            {
                (0, 0, 0), (10, 10, 10), (200, 200, 200), (255, 255, 255), (30, 30, 30)
            };
            Assert.Equal((30, 30, 30), EraserMath.MedianRgb(samples));
        }

        [Fact]
        public void RingSamples_StaysInBoundsAndExcludesInnerDisk()
        {
            var pts = EraserMath.RingSamples(cx: 5, cy: 5, inner: 3, outer: 6, w: 20, h: 20, step: 1);
            Assert.NotEmpty(pts);
            foreach (var (x, y) in pts)
            {
                Assert.InRange(x, 0, 19);
                Assert.InRange(y, 0, 19);
                int d2 = (x - 5) * (x - 5) + (y - 5) * (y - 5);
                Assert.InRange(d2, 9, 36);
            }
        }

        [Fact]
        public void RingSamples_EmptyWhenImageDegenerate()
        {
            Assert.Empty(EraserMath.RingSamples(0, 0, 1, 4, 0, 10, 1));
        }

        [Fact]
        public void IsTransparent_ZeroAlphaOnly()
        {
            Assert.True(EraserMath.IsTransparent(0));
            Assert.False(EraserMath.IsTransparent(1));
            Assert.False(EraserMath.IsTransparent(255));
        }
    }
}
