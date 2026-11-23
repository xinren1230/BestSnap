using System;
using System.Collections.Generic;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class ScrollStitchMathTests
    {
        const int W = 40, H = 80;

        static byte[] Canvas(int w, int h, Action<int, int, byte[]> paint)
        {
            var px = new byte[w * h * 4];
            paint(w, h, px);
            return px;
        }

        static void Put(byte[] px, int w, int x, int y, byte r, byte g, byte b)
        {
            int o = (y * w + x) * 4;
            px[o] = b; px[o + 1] = g; px[o + 2] = r; px[o + 3] = 255;
        }

        /// <summary>Each row has a unique color so overlap is unambiguous.</summary>
        static byte[] UniqueRows(int w, int h, int y0)
        {
            return Canvas(w, h, (ww, hh, px) =>
            {
                for (int y = 0; y < hh; y++)
                    for (int x = 0; x < ww; x++)
                    {
                        int id = y0 + y;
                        Put(px, ww, x, y, (byte)(id * 17), (byte)(id * 9), (byte)(40 + x + id));
                    }
            });
        }

        static byte[] Window(byte[] full, int w, int fullH, int y0, int h)
        {
            int stride = w * 4;
            var o = new byte[stride * h];
            Buffer.BlockCopy(full, y0 * stride, o, 0, stride * h);
            return o;
        }

        [Fact]
        public void FindShift_DetectsOverlapOnUniqueRows()
        {
            var full = UniqueRows(W, 200, 0);
            var a = Window(full, W, 200, 0, H);
            var b = Window(full, W, 200, 25, H);
            var r = ScrollStitchMath.FindShift(a, b, W, H, W * 4);
            Assert.False(r.Unchanged);
            Assert.Equal(25, r.Shift);
        }

        [Fact]
        public void FindShift_IgnoresStickyHeader()
        {
            int header = 12;
            var full = Canvas(W, 220, (w, h, px) =>
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (y < header) Put(px, w, x, y, 255, 200, 0);
                        else
                        {
                            int id = y;
                            Put(px, w, x, y, (byte)(id * 13), (byte)(id * 5), (byte)(20 + x));
                        }
                    }
            });
            // two viewports of height H; content below header scrolled by 18
            var a = Window(full, W, 220, 0, H);
            var b = Canvas(W, H, (w, h, px) =>
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (y < header) Put(px, w, x, y, 255, 200, 0);
                        else
                        {
                            int srcY = y + 18; // scrolled
                            int id = srcY;
                            Put(px, w, x, y, (byte)(id * 13), (byte)(id * 5), (byte)(20 + x));
                        }
                    }
            });
            var r = ScrollStitchMath.FindShift(a, b, W, H, W * 4);
            Assert.False(r.Unchanged);
            Assert.True(r.Header >= header - 1, "header=" + r.Header);
            Assert.Equal(18, r.Shift);
        }

        [Fact]
        public void FindShift_EndOfContentIsUnchanged()
        {
            var a = UniqueRows(W, H, 40);
            var b = UniqueRows(W, H, 40);
            var r = ScrollStitchMath.FindShift(a, b, W, H, W * 4);
            Assert.True(r.Unchanged);
            Assert.Equal(0, r.Shift);
        }

        [Fact]
        public void Compose_StopsAtMaxHeight()
        {
            var first = UniqueRows(W, H, 0);
            var next = UniqueRows(W, H, 30);
            var slice = ScrollStitchMath.ToSlice(next, H, footer: 0, shift: 30);
            var px = ScrollStitchMath.Compose(W, H, W * 4, first, new[] { slice }, 0, next, maxHeight: 90, out int outH, out bool hit);
            Assert.True(hit);
            Assert.Equal(90, outH);
            Assert.Equal(W * 4 * 90, px.Length);
        }

        [Fact]
        public void Compose_KeepsOverlapRemovedAndFooter()
        {
            var first = UniqueRows(W, H, 0);
            var next = UniqueRows(W, H, 20);
            const int footer = 6;
            var slice = ScrollStitchMath.ToSlice(next, H, footer, shift: 20);
            var px = ScrollStitchMath.Compose(W, H, W * 4, first, new[] { slice }, footer, next, 10000, out int outH, out bool hit);
            Assert.False(hit);
            Assert.Equal(H + 20, outH);
            // first body row 0 matches unique id 0
            Assert.Equal((byte)0, px[2]);
            // last footer row should come from next's last rows (ids H-footer+… + 20)
            int lastY = outH - 1;
            int expectId = 20 + (H - 1);
            Assert.Equal((byte)(expectId * 17), px[lastY * W * 4 + 2]);
        }
    }
}
