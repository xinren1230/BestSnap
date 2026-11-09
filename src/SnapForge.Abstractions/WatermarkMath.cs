using System;
using System.Collections.Generic;

namespace SnapForge.Plugins
{
    /// <summary>
    /// Layout math for editor watermarks. Coordinates are image pixels; (0,0) is top-left.
    /// Anchor is a 9-grid, row-major: 0=TL 1=TC 2=TR 3=ML 4=C 5=MR 6=BL 7=BC 8=BR.
    /// Returned positions are the top-left of the unrotated stamp (rotate around its center when drawing).
    /// </summary>
    public static class WatermarkMath
    {
        public const int MaxStamps = 80;
        public const double LightThreshold = 0.55;

        /// <summary>Axis-aligned size of a <paramref name="w"/>×<paramref name="h"/> box rotated by <paramref name="deg"/>.</summary>
        public static (double W, double H) RotatedAabb(double w, double h, double deg)
        {
            w = Finite(w, 0); h = Finite(h, 0);
            w = Math.Max(0, w); h = Math.Max(0, h);
            deg = Finite(deg, 0);
            double rad = deg * Math.PI / 180.0;
            double c = Math.Abs(Math.Cos(rad)), s = Math.Abs(Math.Sin(rad));
            double bw = w * c + h * s, bh = w * s + h * c;
            if (bw < 1e-6) bw = Math.Max(1, w);
            if (bh < 1e-6) bh = Math.Max(1, h);
            return (bw, bh);
        }

        /// <summary>Top-left of the unrotated stamp so its rotated AABB sits on the 9-grid with <paramref name="margin"/>.</summary>
        public static (double X, double Y) Place(double canvasW, double canvasH, double itemW, double itemH, int anchor, double margin, double rotationDeg = 0)
        {
            if (canvasW < 1 || canvasH < 1) return (0, 0);
            int a = anchor < 0 ? 0 : (anchor > 8 ? 8 : anchor);
            int col = a % 3, row = a / 3;
            double m = Math.Max(0, Finite(margin, 0));
            var (bw, bh) = RotatedAabb(itemW, itemH, rotationDeg);
            double x = col == 0 ? m : col == 1 ? (canvasW - bw) / 2.0 : canvasW - bw - m;
            double y = row == 0 ? m : row == 1 ? (canvasH - bh) / 2.0 : canvasH - bh - m;
            return (x + (bw - itemW) / 2.0, y + (bh - itemH) / 2.0);
        }

        /// <summary>Keep at least 8 px of the rotated AABB on the canvas.</summary>
        public static (double X, double Y) ClampPos(double canvasW, double canvasH, double itemW, double itemH, double x, double y, double rotationDeg)
        {
            if (canvasW < 1 || canvasH < 1) return (0, 0);
            x = Finite(x, 0); y = Finite(y, 0);
            var (bw, bh) = RotatedAabb(itemW, itemH, rotationDeg);
            double ox = (bw - itemW) / 2.0, oy = (bh - itemH) / 2.0;
            double aabbX = Math.Clamp(x - ox, 8 - bw, canvasW - 8);
            double aabbY = Math.Clamp(y - oy, 8 - bh, canvasH - 8);
            return (aabbX + ox, aabbY + oy);
        }

        /// <summary>Top-left positions for a tiled grid. Step is rotated AABB plus <paramref name="spacing"/>.</summary>
        public static List<(double X, double Y)> Tile(double canvasW, double canvasH, double itemW, double itemH, double spacing, double rotationDeg)
        {
            var list = new List<(double X, double Y)>();
            if (canvasW < 1 || canvasH < 1 || itemW < 1 || itemH < 1) return list;
            spacing = Finite(spacing, 0);
            rotationDeg = Finite(rotationDeg, 0);
            var (bw, bh) = RotatedAabb(itemW, itemH, rotationDeg);
            double gap = Math.Max(0, spacing);
            int stepX = Step(bw, gap), stepY = Step(bh, gap);
            double ox = (bw - itemW) / 2.0, oy = (bh - itemH) / 2.0;
            int x0 = (int)Math.Min(Math.Max(0, gap), Math.Max(0, canvasW - 1));
            int y0 = (int)Math.Min(Math.Max(0, gap), Math.Max(0, canvasH - 1));
            int guard = 0;
            for (int y = y0; y < canvasH && list.Count < MaxStamps && guard < MaxStamps * 4; y += stepY)
                for (int x = x0; x < canvasW && list.Count < MaxStamps && guard < MaxStamps * 4; x += stepX, guard++)
                    list.Add((x + ox, y + oy));
            if (list.Count == 0) list.Add(Place(canvasW, canvasH, itemW, itemH, 4, gap, rotationDeg));
            return list;
        }

        /// <summary>sRGB luma in 0..1 from 0..255 channels.</summary>
        public static double Luma(double r, double g, double b) =>
            (0.299 * Finite(r, 0) + 0.587 * Finite(g, 0) + 0.114 * Finite(b, 0)) / 255.0;

        public static bool IsLightSurface(double luma) => Finite(luma, 0.5) >= LightThreshold;

        /// <summary>Dark ink on light surfaces, light ink on dark. Mid-gray when luma is unknown.</summary>
        public static (byte R, byte G, byte B) AutoInk(double luma)
        {
            if (double.IsNaN(luma) || double.IsInfinity(luma)) return (0x80, 0x80, 0x80);
            return IsLightSurface(luma) ? ((byte)0x22, (byte)0x22, (byte)0x22) : ((byte)0xF2, (byte)0xF2, (byte)0xF2);
        }

        public static bool IsAutoColor(string color) =>
            string.IsNullOrWhiteSpace(color) || color.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase);

        static int Step(double aabb, double gap)
        {
            double s = aabb + gap;
            if (double.IsNaN(s) || double.IsInfinity(s) || s < 1) s = 1;
            if (s > 1_000_000) s = 1_000_000;
            return Math.Max(1, (int)Math.Ceiling(s));
        }

        static double Finite(double v, double fallback) => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;
    }
}
