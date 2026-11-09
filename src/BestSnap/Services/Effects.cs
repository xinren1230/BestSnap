using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BestSnap.Services
{
    public static class Effects
    {
        private static (byte[] px, int stride) Pixels(BitmapSource s)
        {
            int stride = s.PixelWidth * 4; var px = new byte[stride * s.PixelHeight]; s.CopyPixels(px, stride, 0); return (px, stride);
        }
        private static BitmapSource Make(BitmapSource like, byte[] px, int stride)
        {
            var b = BitmapSource.Create(like.PixelWidth, like.PixelHeight, 96, 96, PixelFormats.Bgra32, null, px, stride); b.Freeze(); return b;
        }

        public static BitmapSource Pixelate(BitmapSource src, Int32Rect r, int block)
        {
            var (px, stride) = Pixels(src);
            for (int by = r.Y; by < r.Y + r.Height; by += block)
            for (int bx = r.X; bx < r.X + r.Width; bx += block)
            {
                int ex = Math.Min(bx + block, r.X + r.Width), ey = Math.Min(by + block, r.Y + r.Height);
                long b = 0, g = 0, rr = 0, n = 0;
                for (int y = by; y < ey; y++) for (int x = bx; x < ex; x++) { int o = y * stride + x * 4; b += px[o]; g += px[o + 1]; rr += px[o + 2]; n++; }
                if (n == 0) continue;
                for (int y = by; y < ey; y++) for (int x = bx; x < ex; x++) { int o = y * stride + x * 4; px[o] = (byte)(b / n); px[o + 1] = (byte)(g / n); px[o + 2] = (byte)(rr / n); }
            }
            return Make(src, px, stride);
        }

        /// <summary>Box blur (3 passes ≈ gaussian) restricted to region.</summary>
        /// <summary>
        /// Approximate Gaussian blur: 3 passes of a separable sliding-window box blur, O(1) per pixel regardless of radius.
        /// Thread-safe for frozen sources (call from Task.Run).
        /// </summary>
        public static BitmapSource Blur(BitmapSource src, Int32Rect r, int radius)
        {
            r = CaptureService.Clip(r, src.PixelWidth, src.PixelHeight);
            var (px, stride) = Pixels(src);
            if (r.Width < 2 || r.Height < 2 || radius < 1) return Make(src, px, stride);
            int w = r.Width, h = r.Height;
            var ch = new byte[3][];
            for (int c = 0; c < 3; c++) ch[c] = new byte[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int o = (r.Y + y) * stride + (r.X + x) * 4; int i = y * w + x; ch[0][i] = px[o]; ch[1][i] = px[o + 1]; ch[2][i] = px[o + 2]; }
            System.Threading.Tasks.Parallel.For(0, 3, c =>
            {
                var a = ch[c]; var tmp = new byte[a.Length];
                for (int pass = 0; pass < 3; pass++) { BoxH(a, tmp, w, h, radius); BoxV(tmp, a, w, h, radius); }
            });
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int o = (r.Y + y) * stride + (r.X + x) * 4; int i = y * w + x; px[o] = ch[0][i]; px[o + 1] = ch[1][i]; px[o + 2] = ch[2][i]; }
            return Make(src, px, stride);
        }

        private static void BoxH(byte[] s, byte[] d, int w, int h, int r)
        {
            int win = 2 * r + 1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w, sum = 0;
                for (int k = -r; k <= r; k++) sum += s[row + Math.Clamp(k, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    d[row + x] = (byte)(sum / win);
                    sum += s[row + Math.Min(x + r + 1, w - 1)] - s[row + Math.Max(x - r, 0)];
                }
            }
        }

        private static void BoxV(byte[] s, byte[] d, int w, int h, int r)
        {
            int win = 2 * r + 1;
            for (int x = 0; x < w; x++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++) sum += s[Math.Clamp(k, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    d[y * w + x] = (byte)(sum / win);
                    sum += s[Math.Min(y + r + 1, h - 1) * w + x] - s[Math.Max(y - r, 0) * w + x];
                }
            }
        }
    }
}
