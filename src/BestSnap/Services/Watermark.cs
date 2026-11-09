using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Models;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Builds a watermark annotation from saved settings and composites it onto a bitmap for save/copy.</summary>
    public static class Watermark
    {
        public static BitmapSource ApplyIfNeeded(BitmapSource img, AppSettings s)
        {
            if (img == null || s?.Watermark == null || !s.Watermark.AutoApply) return img;
            try
            {
                var a = Build(s.Watermark, img.PixelWidth, img.PixelHeight, img);
                if (a == null) return img;
                return Composite(img, a);
            }
            catch (Exception ex) { Logger.Error("Watermark.ApplyIfNeeded", ex); return img; }
        }

        public static Annotation Build(WatermarkSettings w, int canvasW, int canvasH, BitmapSource sample = null)
        {
            if (w == null || canvasW < 1 || canvasH < 1) return null;
            try
            {
                bool img = w.IsImage;
                BitmapSource bmp = null;
                double iw, ih;
                if (img)
                {
                    bmp = Load(w.ImagePath);
                    if (bmp == null) return null;
                    ih = Math.Max(8, w.ImageHeight);
                    iw = ih * bmp.PixelWidth / Math.Max(1, bmp.PixelHeight);
                }
                else
                {
                    string text = string.IsNullOrWhiteSpace(w.Text) ? AppIdentity.Name : w.Text;
                    var sz = Measure(text, w.FontFamily, Math.Max(8, w.FontSize));
                    iw = Math.Max(1, sz.Width); ih = Math.Max(1, sz.Height);
                }
                List<(double X, double Y)> pts;
                if (w.Tiled)
                    pts = WatermarkMath.Tile(canvasW, canvasH, iw, ih, w.Spacing, w.Rotation);
                else if (w.PosX.HasValue && w.PosY.HasValue)
                    pts = new List<(double X, double Y)> { WatermarkMath.ClampPos(canvasW, canvasH, iw, ih, w.PosX.Value, w.PosY.Value, w.Rotation) };
                else
                    pts = new List<(double X, double Y)> { WatermarkMath.Place(canvasW, canvasH, iw, ih, w.Anchor, w.Margin, w.Rotation) };
                if (pts.Count > WatermarkMath.MaxStamps) pts = pts.GetRange(0, WatermarkMath.MaxStamps);
                var color = ResolveColor(w, sample, pts, iw, ih);
                var a = new Annotation
                {
                    Kind = AnnotationKind.Watermark,
                    Text = img ? (w.ImagePath ?? "") : (string.IsNullOrWhiteSpace(w.Text) ? AppIdentity.Name : w.Text),
                    TextBackground = img,
                    FontFamilyName = string.IsNullOrWhiteSpace(w.FontFamily) ? "Microsoft YaHei UI" : w.FontFamily,
                    FontSizeOverride = Math.Max(8, w.FontSize),
                    Color = color,
                    Rotation = Finite(w.Rotation, -30),
                    A = new Point(iw, ih),
                    B = new Point(iw, ih),
                    Number = w.Anchor,
                    Thickness = w.Spacing
                };
                a.Points.Clear();
                foreach (var (x, y) in pts) a.Points.Add(new Point(x, y));
                a.SetWatermarkImage(bmp);
                return a;
            }
            catch (Exception ex) { Logger.Error("Watermark.Build", ex); return null; }
        }

        public static BitmapSource Composite(BitmapSource img, Annotation wm)
        {
            if (img == null) return null;
            if (wm == null) return img;
            try
            {
                int w = img.PixelWidth, h = img.PixelHeight;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(img, new Rect(0, 0, w, h));
                    wm.Render(dc);
                }
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                return CaptureService.Normalize(rtb);
            }
            catch (Exception ex) { Logger.Error("Watermark.Composite", ex); return img; }
        }

        public static bool IsAuto(string color) => WatermarkMath.IsAutoColor(color);

        public static Color ParseColor(string hex, int opacity)
        {
            byte a = OpacityByte(opacity);
            byte r = 128, g = 128, b = 128;
            if (!IsAuto(hex))
            {
                var s = hex.Trim().TrimStart('#');
                if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
                { r = (byte)((n >> 16) & 0xFF); g = (byte)((n >> 8) & 0xFF); b = (byte)(n & 0xFF); }
            }
            return Color.FromArgb(a, r, g, b);
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        static Color ResolveColor(WatermarkSettings w, BitmapSource img, List<(double X, double Y)> pts, double iw, double ih)
        {
            int op = w.Opacity;
            if (!IsAuto(w.Color)) return ParseColor(w.Color, op);
            double luma = SampleLuma(img, pts, iw, ih);
            var (r, g, b) = WatermarkMath.AutoInk(luma);
            return Color.FromArgb(OpacityByte(op), r, g, b);
        }

        static byte OpacityByte(int opacity) =>
            (byte)Math.Clamp((int)Math.Round(Math.Clamp(opacity, 0, 100) * 2.55), 0, 255);

        static double SampleLuma(BitmapSource img, List<(double X, double Y)> pts, double iw, double ih)
        {
            if (img == null || pts == null || pts.Count == 0) return double.NaN;
            try
            {
                var src = img;
                if (img.Format != PixelFormats.Bgra32 && img.Format != PixelFormats.Pbgra32 && img.Format != PixelFormats.Bgr32)
                    src = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
                int w = src.PixelWidth, h = src.PixelHeight;
                if (w < 1 || h < 1) return double.NaN;
                long sr = 0, sg = 0, sb = 0, n = 0;
                int take = Math.Min(8, pts.Count);
                var buf = new byte[4];
                for (int i = 0; i < take; i++)
                {
                    int cx = (int)Math.Clamp(pts[i].X + iw / 2, 0, w - 1);
                    int cy = (int)Math.Clamp(pts[i].Y + ih / 2, 0, h - 1);
                    src.CopyPixels(new Int32Rect(cx, cy, 1, 1), buf, 4, 0);
                    sb += buf[0]; sg += buf[1]; sr += buf[2]; n++;
                }
                if (n == 0) return double.NaN;
                return WatermarkMath.Luma(sr / (double)n, sg / (double)n, sb / (double)n);
            }
            catch (Exception ex) { Logger.Error("Watermark.SampleLuma", ex); return double.NaN; }
        }

        static Size Measure(string text, string font, double size)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(string.IsNullOrWhiteSpace(font) ? "Segoe UI" : font), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                Math.Max(8, size), Brushes.White, 1.0);
            return new Size(Math.Max(1, ft.WidthIncludingTrailingWhitespace), Math.Max(1, ft.Height));
        }

        public static BitmapSource Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        static double Finite(double v, double fallback) => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;
    }
}
