using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class RecordRectMathTests
    {
        static MonitorRect M(int x, int y, int w, int h, string n = "m") => new MonitorRect(x, y, w, h, n);

        [Fact]
        public void Normalize_FlipsNegativeSize()
        {
            var r = RecordRectMath.Normalize(100, 80, -40, -20);
            Assert.Equal(new IntRect(60, 60, 40, 20), r);
        }

        [Fact]
        public void VirtualScreen_UnionsMonitorsWithNegativeOrigin()
        {
            var vs = RecordRectMath.VirtualScreen(new[] { M(-1920, 0, 1920, 1080, "L"), M(0, 0, 1920, 1080, "P") });
            Assert.Equal(new IntRect(-1920, 0, 3840, 1080), vs);
        }

        [Fact]
        public void PickMonitor_LargestOverlap_AndSpansFlag()
        {
            var monitors = new[] { M(0, 0, 1920, 1080, "A"), M(1920, 0, 1920, 1080, "B") };
            int i = RecordRectMath.PickMonitorIndex(new IntRect(1800, 10, 200, 100), monitors, out bool spans);
            Assert.True(spans);
            Assert.Equal(0, i); // 120×100 on A vs 80×100 on B
            i = RecordRectMath.PickMonitorIndex(new IntRect(10, 10, 100, 100), monitors, out spans);
            Assert.False(spans);
            Assert.Equal(0, i);
        }

        [Fact]
        public void FitsOneMonitor_FalseWhenSpanning()
        {
            var monitors = new[] { M(0, 0, 100, 100, "A"), M(100, 0, 100, 100, "B") };
            Assert.False(RecordRectMath.FitsOneMonitor(new IntRect(80, 0, 40, 40), monitors, out _));
            Assert.True(RecordRectMath.FitsOneMonitor(new IntRect(10, 10, 20, 20), monitors, out int i));
            Assert.Equal(0, i);
        }

        [Fact]
        public void MonitorLocalEvenCrop_RelativeAndEven()
        {
            var mon = M(100, 50, 200, 100, "A");
            var crop = RecordRectMath.MonitorLocalEvenCrop(new IntRect(101, 51, 51, 33), mon);
            Assert.Equal(1, crop.X);
            Assert.Equal(1, crop.Y);
            Assert.Equal(50, crop.Width);
            Assert.Equal(32, crop.Height);
        }

        [Fact]
        public void VirtualLocalEvenCrop_UsesBoundingOrigin()
        {
            var monitors = new[] { M(-100, 0, 100, 80, "L"), M(0, 0, 100, 80, "P") };
            var crop = RecordRectMath.VirtualLocalEvenCrop(new IntRect(-20, 0, 40, 40), monitors);
            Assert.Equal(80, crop.X);
            Assert.Equal(0, crop.Y);
            Assert.Equal(40, crop.Width);
            Assert.Equal(40, crop.Height);
        }

        [Fact]
        public void BorderOutside_DrawsPadAroundCapture()
        {
            var b = RecordRectMath.BorderOutside(new IntRect(10, 20, 100, 50), 4);
            Assert.Equal(new IntRect(6, 16, 108, 58), b);
        }

        [Fact]
        public void EvenSize_ShrinksOddSides()
        {
            Assert.Equal(new IntRect(3, 4, 10, 20), RecordRectMath.EvenSize(new IntRect(3, 4, 11, 21)));
            Assert.False(RecordRectMath.IsValidCapture(RecordRectMath.EvenSize(new IntRect(0, 0, 1, 1))));
        }

        [Fact]
        public void NormalizeFps_SnapsToAllowed()
        {
            Assert.Equal(10, RecordRectMath.NormalizeFps(9));
            Assert.Equal(15, RecordRectMath.NormalizeFps(16));
            Assert.Equal(30, RecordRectMath.NormalizeFps(30));
            Assert.Equal(60, RecordRectMath.NormalizeFps(90));
        }

        [Fact]
        public void ClipLocal_ClampsToMonitor()
        {
            var c = RecordRectMath.ClipLocal(new IntRect(-10, -5, 30, 20), 100, 50);
            Assert.Equal(new IntRect(0, 0, 20, 15), c);
        }
    }
}
