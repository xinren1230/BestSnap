using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BestSnap.Services
{
    /// <summary>Pixel helpers for stitching and comparing (Bgra32 buffers).</summary>
    public static class ImageOps
    {
        public sealed class Px { public int W, H; public byte[] D; public int Stride => W * 4; }

        public static Px Read(BitmapSource src)
        {
            var b = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var p = new Px { W = b.PixelWidth, H = b.PixelHeight };
            p.D = new byte[p.W * p.H * 4];
            b.CopyPixels(p.D, p.Stride, 0);
            return p;
        }

        public static BitmapSource Write(Px p, double dpi = 96)
        {
            var bmp = BitmapSource.Create(p.W, p.H, dpi, dpi, PixelFormats.Bgra32, null, p.D, p.Stride);
            bmp.Freeze(); return bmp;
        }

        private static bool RowEq(Px a, int ya, int xa, Px b, int yb, int xb, int len, int tol)
        {
            // sampled compare of a horizontal run
            int step = Math.Max(1, len / 64);
            for (int i = 0; i < len; i += step)
            {
                int ia = (ya * a.W + xa + i) * 4, ib = (yb * b.W + xb + i) * 4;
                if (Math.Abs(a.D[ia] - b.D[ib]) > tol || Math.Abs(a.D[ia + 1] - b.D[ib + 1]) > tol || Math.Abs(a.D[ia + 2] - b.D[ib + 2]) > tol) return false;
            }
            return true;
        }
        private static bool ColEq(Px a, int xa, Px b, int xb, int len, int tol)
        {
            int step = Math.Max(1, len / 64);
            for (int i = 0; i < len; i += step)
            {
                int ia = (i * a.W + xa) * 4, ib = (i * b.W + xb) * 4;
                if (Math.Abs(a.D[ia] - b.D[ib]) > tol || Math.Abs(a.D[ia + 1] - b.D[ib + 1]) > tol || Math.Abs(a.D[ia + 2] - b.D[ib + 2]) > tol) return false;
            }
            return true;
        }

        /// <summary>Largest k such that the last k rows (cols) of a equal the first k rows (cols) of b. 0 = no overlap found.</summary>
        public static int FindOverlap(Px a, Px b, bool vertical, int tol = 8)
        {
            if (vertical)
            {
                int w = Math.Min(a.W, b.W), max = Math.Min(a.H, b.H) - 1;
                for (int k = max; k >= 8; k--)
                {
                    bool ok = true;
                    // check every 4th row of the candidate overlap (plus last), early out
                    for (int r = 0; r < k && ok; r += Math.Max(1, k / 48)) ok = RowEq(a, a.H - k + r, 0, b, r, 0, w, tol);
                    if (ok && RowEq(a, a.H - 1, 0, b, k - 1, 0, w, tol) && !Uniform(b, k)) return k;
                }
            }
            else
            {
                int h = Math.Min(a.H, b.H), max = Math.Min(a.W, b.W) - 1;
                for (int k = max; k >= 8; k--)
                {
                    bool ok = true;
                    for (int c = 0; c < k && ok; c += Math.Max(1, k / 48)) ok = ColEq(a, a.W - k + c, b, c, h, tol);
                    if (ok && ColEq(a, a.W - 1, b, k - 1, h, tol)) return k;
                }
            }
            return 0;
        }

        // Avoid "overlap" made only of a flat colour band (would glue images wrongly).
        private static bool Uniform(Px b, int rows)
        {
            byte r0 = b.D[2], g0 = b.D[1], b0 = b.D[0];
            for (int y = 0; y < rows; y += Math.Max(1, rows / 16))
                for (int x = 0; x < b.W; x += Math.Max(1, b.W / 32))
                {
                    int i = (y * b.W + x) * 4;
                    if (Math.Abs(b.D[i] - b0) > 8 || Math.Abs(b.D[i + 1] - g0) > 8 || Math.Abs(b.D[i + 2] - r0) > 8) return false;
                }
            return true;
        }

        public static BitmapSource Stitch(IList<BitmapSource> imgs, bool vertical, bool detectOverlap, Color bg)
        {
            var px = new List<Px>(); foreach (var i in imgs) px.Add(Read(i));
            var ov = new int[px.Count];
            if (detectOverlap) for (int i = 1; i < px.Count; i++) ov[i] = FindOverlap(px[i - 1], px[i], vertical);
            int W, H;
            if (vertical) { W = 0; H = 0; for (int i = 0; i < px.Count; i++) { W = Math.Max(W, px[i].W); H += px[i].H - ov[i]; } }
            else { W = 0; H = 0; for (int i = 0; i < px.Count; i++) { H = Math.Max(H, px[i].H); W += px[i].W - ov[i]; } }
            if ((long)W * H > 400_000_000) throw new InvalidOperationException("Result too large");
            var o = new Px { W = Math.Max(1, W), H = Math.Max(1, H) }; o.D = new byte[o.W * o.H * 4];
            for (int i = 0; i < o.D.Length; i += 4) { o.D[i] = bg.B; o.D[i + 1] = bg.G; o.D[i + 2] = bg.R; o.D[i + 3] = bg.A; }
            int off = 0;
            for (int n = 0; n < px.Count; n++)
            {
                var p = px[n]; off -= ov[n];
                for (int y = 0; y < p.H; y++)
                {
                    int dy = vertical ? off + y : y, dx = vertical ? 0 : off;
                    Buffer.BlockCopy(p.D, y * p.Stride, o.D, (dy * o.W + dx) * 4, p.Stride);
                }
                off += vertical ? p.H : p.W;
            }
            return Write(o);
        }

        /// <summary>Difference image: b dimmed to grey, changed pixels painted red. Returns changed-pixel count.</summary>
        public static BitmapSource Difference(BitmapSource a, BitmapSource b, int tol, out long changed, out Int32Rect bounds)
        {
            var A = Read(a); var B = Read(b);
            int W = Math.Max(A.W, B.W), H = Math.Max(A.H, B.H);
            var o = new Px { W = W, H = H }; o.D = new byte[W * H * 4];
            changed = 0; int minX = W, minY = H, maxX = -1, maxY = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int oi = (y * W + x) * 4;
                    bool inA = x < A.W && y < A.H, inB = x < B.W && y < B.H;
                    bool diff;
                    byte grey = 255;
                    if (inA && inB)
                    {
                        int ia = (y * A.W + x) * 4, ib = (y * B.W + x) * 4;
                        diff = Math.Abs(A.D[ia] - B.D[ib]) > tol || Math.Abs(A.D[ia + 1] - B.D[ib + 1]) > tol || Math.Abs(A.D[ia + 2] - B.D[ib + 2]) > tol;
                        grey = (byte)(180 + (A.D[ia] * 11 + A.D[ia + 1] * 59 + A.D[ia + 2] * 30) / 100 * 75 / 255);
                    }
                    else diff = true;
                    if (diff)
                    {
                        o.D[oi] = 40; o.D[oi + 1] = 40; o.D[oi + 2] = 255; o.D[oi + 3] = 255; changed++;
                        if (x < minX) minX = x; if (y < minY) minY = y; if (x > maxX) maxX = x; if (y > maxY) maxY = y;
                    }
                    else { o.D[oi] = grey; o.D[oi + 1] = grey; o.D[oi + 2] = grey; o.D[oi + 3] = 255; }
                }
            bounds = maxX < 0 ? Int32Rect.Empty : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return Write(o);
        }
    }
}
