using System;
using SnapForge.Plugins;
using Xunit;

namespace SnapForge.Tests
{
    public class WatermarkMathTests
    {
        [Fact]
        public void Place_NineGridCornersAndCenter()
        {
            var tl = WatermarkMath.Place(200, 100, 40, 20, 0, 10);
            Assert.Equal(10, tl.X, 6); Assert.Equal(10, tl.Y, 6);

            var tr = WatermarkMath.Place(200, 100, 40, 20, 2, 10);
            Assert.Equal(150, tr.X, 6); Assert.Equal(10, tr.Y, 6);

            var c = WatermarkMath.Place(200, 100, 40, 20, 4, 10);
            Assert.Equal(80, c.X, 6); Assert.Equal(40, c.Y, 6);

            var br = WatermarkMath.Place(200, 100, 40, 20, 8, 10);
            Assert.Equal(150, br.X, 6); Assert.Equal(70, br.Y, 6);

            var bl = WatermarkMath.Place(200, 100, 40, 20, 6, 10);
            Assert.Equal(10, bl.X, 6); Assert.Equal(70, bl.Y, 6);

            var tc = WatermarkMath.Place(200, 100, 40, 20, 1, 10);
            Assert.Equal(80, tc.X, 6); Assert.Equal(10, tc.Y, 6);
            var ml = WatermarkMath.Place(200, 100, 40, 20, 3, 10);
            Assert.Equal(10, ml.X, 6); Assert.Equal(40, ml.Y, 6);
            var mr = WatermarkMath.Place(200, 100, 40, 20, 5, 10);
            Assert.Equal(150, mr.X, 6); Assert.Equal(40, mr.Y, 6);
            var bc = WatermarkMath.Place(200, 100, 40, 20, 7, 10);
            Assert.Equal(80, bc.X, 6); Assert.Equal(70, bc.Y, 6);
        }

        [Fact]
        public void Place_ClampsAnchorAndEmptyCanvas()
        {
            var t = WatermarkMath.Place(200, 100, 10, 10, -3, 0);
            var tl = WatermarkMath.Place(200, 100, 10, 10, 0, 0);
            Assert.Equal(tl, t);
            var b = WatermarkMath.Place(200, 100, 10, 10, 99, 0);
            var br = WatermarkMath.Place(200, 100, 10, 10, 8, 0);
            Assert.Equal(br, b);
            Assert.Equal((0.0, 0.0), WatermarkMath.Place(0, 10, 4, 4, 4, 2));
        }

        [Fact]
        public void RotatedAabb_SwapsOn90And270()
        {
            var (w, h) = WatermarkMath.RotatedAabb(40, 10, 90);
            Assert.Equal(10, w, 6);
            Assert.Equal(40, h, 6);
            var n = WatermarkMath.RotatedAabb(40, 10, 0);
            Assert.Equal(40, n.W, 6);
            Assert.Equal(10, n.H, 6);
            var flip = WatermarkMath.RotatedAabb(40, 10, 270);
            Assert.Equal(10, flip.W, 6);
            Assert.Equal(40, flip.H, 6);
            var nan = WatermarkMath.RotatedAabb(40, 10, double.NaN);
            Assert.Equal(40, nan.W, 6);
        }

        [Fact]
        public void Place_RotatedStampStaysInsideMargin()
        {
            var p = WatermarkMath.Place(100, 100, 40, 10, 8, 5, 90);
            var (bw, bh) = WatermarkMath.RotatedAabb(40, 10, 90);
            double aabbX = p.X - (bw - 40) / 2, aabbY = p.Y - (bh - 10) / 2;
            Assert.Equal(100 - bw - 5, aabbX, 5);
            Assert.Equal(100 - bh - 5, aabbY, 5);
        }

        [Fact]
        public void Tile_FillsCanvasWithSpacing()
        {
            var pts = WatermarkMath.Tile(100, 40, 10, 10, 10, 0);
            Assert.Equal(10, pts.Count);
            Assert.Equal((10, 10), pts[0]);
            Assert.Contains((10 + 20, 10), pts);
        }

        [Fact]
        public void Tile_LargerRotationUsesBiggerStep()
        {
            var flat = WatermarkMath.Tile(200, 200, 40, 10, 0, 0);
            var rot = WatermarkMath.Tile(200, 200, 40, 10, 0, 45);
            Assert.True(rot.Count < flat.Count);
            Assert.NotEmpty(rot);
        }

        [Fact]
        public void Tile_EmptyWhenCanvasOrStampDegenerate()
        {
            Assert.Empty(WatermarkMath.Tile(0, 100, 10, 10, 8, 0));
            Assert.Empty(WatermarkMath.Tile(100, 100, 0, 10, 8, 0));
        }

        [Fact]
        public void Tile_CapsAtMaxStamps()
        {
            var pts = WatermarkMath.Tile(10000, 10000, 2, 2, 0, 0);
            Assert.Equal(WatermarkMath.MaxStamps, pts.Count);
        }

        [Fact]
        public void Tile_ZeroSpacingAndTinyStampIsFinite()
        {
            var pts = WatermarkMath.Tile(4000, 3000, 1, 1, 0, -30);
            Assert.InRange(pts.Count, 1, WatermarkMath.MaxStamps);
            foreach (var p in pts)
            {
                Assert.False(double.IsNaN(p.X) || double.IsInfinity(p.X));
                Assert.False(double.IsNaN(p.Y) || double.IsInfinity(p.Y));
            }
        }

        [Fact]
        public void Tile_OddAnglesStayFinite()
        {
            foreach (var deg in new[] { -30.0, -90, 180, 359.9, 720, double.NaN, double.PositiveInfinity })
            {
                var pts = WatermarkMath.Tile(320, 240, 48, 16, 8, deg);
                Assert.InRange(pts.Count, 1, WatermarkMath.MaxStamps);
                Assert.All(pts, p =>
                {
                    Assert.False(double.IsNaN(p.X) || double.IsInfinity(p.X));
                    Assert.False(double.IsNaN(p.Y) || double.IsInfinity(p.Y));
                });
            }
        }

        [Fact]
        public void Tile_NegativeSpacingTreatedAsZero()
        {
            var a = WatermarkMath.Tile(120, 80, 20, 10, 0, 0);
            var b = WatermarkMath.Tile(120, 80, 20, 10, -50, 0);
            Assert.Equal(a.Count, b.Count);
        }

        [Fact]
        public void ClampPos_KeepsSomeStampOnCanvas()
        {
            var far = WatermarkMath.ClampPos(200, 100, 40, 20, 5000, -400, 0);
            Assert.InRange(far.X, 8 - 40, 200 - 8);
            Assert.InRange(far.Y, 8 - 20, 100 - 8);
            var mid = WatermarkMath.ClampPos(200, 100, 40, 20, 80, 40, 0);
            Assert.Equal(80, mid.X, 6);
            Assert.Equal(40, mid.Y, 6);
        }

        [Fact]
        public void AutoInk_DarkOnLight_LightOnDark()
        {
            Assert.True(WatermarkMath.IsLightSurface(WatermarkMath.Luma(255, 255, 255)));
            Assert.False(WatermarkMath.IsLightSurface(WatermarkMath.Luma(10, 10, 10)));
            var dark = WatermarkMath.AutoInk(0.9);
            Assert.True(dark.R < 80 && dark.G < 80);
            var light = WatermarkMath.AutoInk(0.1);
            Assert.True(light.R > 180 && light.G > 180);
            var mid = WatermarkMath.AutoInk(double.NaN);
            Assert.Equal(0x80, mid.R);
        }

        [Fact]
        public void IsAutoColor_AcceptsBlankAndAuto()
        {
            Assert.True(WatermarkMath.IsAutoColor("auto"));
            Assert.True(WatermarkMath.IsAutoColor(""));
            Assert.True(WatermarkMath.IsAutoColor(null));
            Assert.False(WatermarkMath.IsAutoColor("#808080"));
            Assert.False(WatermarkMath.IsAutoColor("#FFFFFF"));
        }
    }
}
