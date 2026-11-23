using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class CaptionWindowMathTests
    {
        static MonitorRect M(int x, int y, int w, int h) => new MonitorRect(x, y, w, h);

        [Fact]
        public void MonitorIndexForPoint_UsesContainingThenNearest()
        {
            var mons = new[] { M(-1920, 0, 1920, 1080), M(0, 0, 1920, 1080) };
            Assert.Equal(0, CaptionWindowMath.MonitorIndexForPoint(-1127, 12, mons));
            Assert.Equal(1, CaptionWindowMath.MonitorIndexForPoint(100, 10, mons));
            Assert.Equal(0, CaptionWindowMath.MonitorIndexForPoint(-2000, 10, mons));
        }

        [Fact]
        public void BottomCenter_OnLeftMonitor()
        {
            var work = new IntRect(-1920, 0, 1920, 1080);
            var r = CaptionWindowMath.BottomCenter(work, 640, 360);
            Assert.Equal(640, r.Width);
            Assert.Equal(360, r.Height);
            Assert.Equal(-1920 + (1920 - 640) / 2, r.X);
            Assert.Equal(1080 - CaptionWindowMath.Margin - 360, r.Y);
        }

        [Fact]
        public void Place_UsesOwnerMonitor_NotPrimary()
        {
            var mons = new[] { M(-1920, 0, 1920, 1080), M(0, 0, 1920, 1080) };
            var r = CaptionWindowMath.Place(mons, 640, 360, remembered: null, anchorX: -1127, anchorY: 12);
            Assert.True(r.X < 0);
            Assert.True(r.Right <= 0);
            Assert.True(r.Y > 500);
        }

        [Fact]
        public void Place_RememberedOnSameMonitor_Wins()
        {
            var mons = new[] { M(-1920, 0, 1920, 1080), M(0, 0, 1920, 1080) };
            var last = new IntRect(-1800, 40, 640, 360);
            var r = CaptionWindowMath.Place(mons, 640, 360, last, anchorX: 100, anchorY: 10);
            Assert.Equal(-1800, r.X);
            Assert.Equal(40, r.Y);
        }

        [Fact]
        public void Place_RememberedOffscreen_FallsBackToAnchor()
        {
            var mons = new[] { M(0, 0, 1920, 1080) };
            var last = new IntRect(-1800, 40, 640, 360);
            var r = CaptionWindowMath.Place(mons, 640, 360, last, anchorX: 100, anchorY: 10);
            Assert.True(r.X >= 0);
            Assert.Equal(CaptionWindowMath.BottomCenter(mons[0].Bounds, 640, 360), r);
        }

        [Fact]
        public void ClampTo_KeepsWindowInsideWorkArea()
        {
            var work = new IntRect(0, 0, 800, 600);
            var r = CaptionWindowMath.ClampTo(new IntRect(700, 500, 640, 360), work);
            Assert.Equal(800 - 640, r.X);
            Assert.Equal(600 - 360, r.Y);
            Assert.Equal(640, r.Width);
        }
    }
}
