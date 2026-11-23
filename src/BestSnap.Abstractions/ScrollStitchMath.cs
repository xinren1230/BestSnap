using System;
using System.Collections.Generic;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Vertical long-screenshot stitching: sticky header/footer, central-band overlap, end-of-content, max height.
    /// Operates on BGRA (or BGRX) buffers; no UI types.
    /// </summary>
    public static class ScrollStitchMath
    {
        public const int DefaultMaxHeight = 30000;
        public const int MinFrame = 48;

        public readonly struct ShiftResult
        {
            public ShiftResult(int shift, int header, int footer, bool unchanged)
            {
                Shift = shift; Header = header; Footer = footer; Unchanged = unchanged;
            }
            public int Shift { get; }
            public int Header { get; }
            public int Footer { get; }
            public bool Unchanged { get; }
        }

        public readonly struct Slice
        {
            public Slice(byte[] frame, int fromRow, int rowCount)
            {
                Frame = frame; FromRow = fromRow; RowCount = rowCount;
            }
            public byte[] Frame { get; }
            public int FromRow { get; }
            public int RowCount { get; }
        }

        /// <summary>How far the content moved up (new rows at the bottom). 0 if unchanged or not found.</summary>
        public static ShiftResult FindShift(byte[] prev, byte[] next, int width, int height, int stride)
        {
            if (prev == null || next == null || width < 8 || height < MinFrame || stride < width * 4)
                return new ShiftResult(0, 0, 0, true);

            int x0 = Math.Max(0, width / 5);
            int x1 = Math.Min(width, width - width / 5);
            if (x1 - x0 < 8) { x0 = 0; x1 = width; }

            int header = StaticRows(prev, next, stride, height, x0, x1, fromTop: true);
            int footer = StaticRows(prev, next, stride, height, x0, x1, fromTop: false);
            if (header + footer > height - MinFrame) { header = 0; footer = 0; }

            int top = header, bottom = height - footer;
            int region = bottom - top;
            if (region < 32) return new ShiftResult(0, header, footer, true);

            if (RegionUnchanged(prev, next, stride, top, bottom, x0, x1))
                return new ShiftResult(0, header, footer, true);

            int bestS = 0, bestScore = int.MaxValue;
            int maxS = Math.Max(1, region * 3 / 4);
            for (int s = 1; s <= maxS; s++)
            {
                int score = OverlapScore(prev, next, stride, top, bottom, x0, x1, s);
                if (score < bestScore) { bestScore = score; bestS = s; }
            }

            // identical overlap should score near 0; reject weak matches (busy / repeating chrome)
            int ident = OverlapScore(prev, next, stride, top, bottom, x0, x1, 0);
            if (bestS <= 0 || bestScore > 45 || bestScore * 3 > ident + 30)
                return new ShiftResult(0, header, footer, false);
            return new ShiftResult(bestS, header, footer, false);
        }

        public static Slice ToSlice(byte[] frame, int height, int footer, int shift)
        {
            int n = Math.Max(0, shift);
            int from = height - footer - n;
            if (from < 0) { n += from; from = 0; }
            return new Slice(frame, from, n);
        }

        /// <summary>First frame + new bottom slices + last footer. Clips at <paramref name="maxHeight"/>.</summary>
        public static byte[] Compose(int width, int height, int stride, byte[] first,
            IReadOnlyList<Slice> slices, int footer, byte[] last, int maxHeight, out int outHeight, out bool hitMax)
        {
            if (maxHeight < 1) maxHeight = DefaultMaxHeight;
            int extra = 0;
            if (slices != null)
                for (int i = 0; i < slices.Count; i++) extra += Math.Max(0, slices[i].RowCount);
            int foot = Math.Max(0, footer);
            int firstBody = Math.Max(0, height - foot);
            int full = firstBody + extra + foot;
            hitMax = full > maxHeight;
            outHeight = hitMax ? maxHeight : full;
            if (width < 1 || outHeight < 1 || stride < width * 4) { outHeight = 0; return Array.Empty<byte>(); }

            var dst = new byte[stride * outHeight];
            int y = 0;
            y += CopyRows(first, dst, stride, 0, Math.Min(firstBody, outHeight), y);
            if (slices != null)
            {
                for (int i = 0; i < slices.Count && y < outHeight; i++)
                {
                    var s = slices[i];
                    int n = Math.Max(0, s.RowCount);
                    int room = outHeight - y;
                    // keep room for footer when we still have one
                    if (foot > 0 && i == slices.Count - 1) room = Math.Max(0, outHeight - y - Math.Min(foot, outHeight - y));
                    else if (foot > 0) room = Math.Max(0, outHeight - y - foot);
                    n = Math.Min(n, room);
                    if (n <= 0) continue;
                    y += CopyRows(s.Frame, dst, stride, s.FromRow, n, y);
                }
            }
            if (foot > 0 && y < outHeight && last != null)
            {
                int n = Math.Min(foot, outHeight - y);
                CopyRows(last, dst, stride, Math.Max(0, height - foot), n, y);
                y += n;
            }
            if (y < outHeight) outHeight = y;
            return dst;
        }

        static int CopyRows(byte[] src, byte[] dst, int stride, int srcY, int rows, int dstY)
        {
            if (src == null || rows <= 0) return 0;
            int maxSrc = src.Length / stride;
            if (srcY < 0) { rows += srcY; srcY = 0; }
            if (srcY >= maxSrc) return 0;
            rows = Math.Min(rows, maxSrc - srcY);
            if (rows <= 0) return 0;
            Buffer.BlockCopy(src, srcY * stride, dst, dstY * stride, rows * stride);
            return rows;
        }

        static int StaticRows(byte[] a, byte[] b, int stride, int h, int x0, int x1, bool fromTop)
        {
            int n = 0, cap = h / 2;
            for (int i = 0; i < cap; i++)
            {
                int y = fromTop ? i : h - 1 - i;
                if (RowSad(a, b, y, y, stride, x0, x1) > 18) break;
                n++;
            }
            return n;
        }

        static bool RegionUnchanged(byte[] a, byte[] b, int stride, int top, int bottom, int x0, int x1)
        {
            int sad = 0, n = 0;
            for (int y = top; y < bottom; y += 3)
            {
                sad += RowSad(a, b, y, y, stride, x0, x1);
                n++;
            }
            return n > 0 && sad / n <= 18;
        }

        static int OverlapScore(byte[] prev, byte[] next, int stride, int top, int bottom, int x0, int x1, int shift)
        {
            // prev[y] should match next[y - shift] in the scrolling region
            int sad = 0, n = 0;
            int y0 = top + shift;
            int y1 = bottom;
            if (y1 - y0 < 8) return int.MaxValue / 4;
            for (int y = y0; y < y1; y += 2)
            {
                sad += RowSad(prev, next, y, y - shift, stride, x0, x1);
                n++;
            }
            return n == 0 ? int.MaxValue / 4 : sad / n;
        }

        /// <summary>Mean per-channel abs diff on a subsampled central span. ~0 for identical rows.</summary>
        public static int RowSad(byte[] a, byte[] b, int ya, int yb, int stride, int x0, int x1)
        {
            int oa = ya * stride + x0 * 4;
            int ob = yb * stride + x0 * 4;
            if (oa < 0 || ob < 0) return 255;
            int endA = a.Length - 4, endB = b.Length - 4;
            int sad = 0, n = 0;
            for (int x = x0; x < x1; x += 2, oa += 8, ob += 8)
            {
                if (oa > endA || ob > endB) break;
                sad += Math.Abs(a[oa] - b[ob]) + Math.Abs(a[oa + 1] - b[ob + 1]) + Math.Abs(a[oa + 2] - b[ob + 2]);
                n++;
            }
            return n == 0 ? 255 : sad / n;
        }
    }
}
