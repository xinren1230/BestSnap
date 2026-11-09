using System;
using System.Collections.Generic;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Geometry for the editor object-eraser: a circular brush removes annotations it covers.
    /// Indices are scanned topmost-first (high index first) so overlapping objects match paint order.
    /// </summary>
    public static class EraserMath
    {
        public static double DistToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double abx = bx - ax, aby = by - ay;
            double len2 = abx * abx + aby * aby;
            if (len2 < 1e-12)
            {
                double dx = px - ax, dy = py - ay;
                return Math.Sqrt(dx * dx + dy * dy);
            }
            double t = ((px - ax) * abx + (py - ay) * aby) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double qx = ax + t * abx, qy = ay + t * aby;
            double dx2 = px - qx, dy2 = py - qy;
            return Math.Sqrt(dx2 * dx2 + dy2 * dy2);
        }

        /// <summary>True when a circle of <paramref name="radius"/> around (px,py) overlaps the axis-aligned box.</summary>
        public static bool HitsRect(double px, double py, double x0, double y0, double x1, double y1, double radius)
        {
            if (x0 > x1) { double t = x0; x0 = x1; x1 = t; }
            if (y0 > y1) { double t = y0; y0 = y1; y1 = t; }
            double r = Math.Max(0, radius);
            return px >= x0 - r && px <= x1 + r && py >= y0 - r && py <= y1 + r;
        }

        public static bool HitsSegment(double px, double py, double ax, double ay, double bx, double by, double radius) =>
            DistToSegment(px, py, ax, ay, bx, by) <= Math.Max(0, radius);

        /// <summary>Luminance-median sample; empty → white (auto-fill fallback).</summary>
        public static (byte R, byte G, byte B) MedianRgb(IReadOnlyList<(byte R, byte G, byte B)> samples)
        {
            if (samples == null || samples.Count == 0) return (255, 255, 255);
            var list = new List<(byte R, byte G, byte B)>(samples.Count);
            for (int i = 0; i < samples.Count; i++) list.Add(samples[i]);
            list.Sort((a, b) => (a.R + a.G + a.B).CompareTo(b.R + b.G + b.B));
            return list[list.Count / 2];
        }

        /// <summary>Pixels in the ring between <paramref name="inner"/> and <paramref name="outer"/> radii, clipped to the image.</summary>
        public static List<(int X, int Y)> RingSamples(int cx, int cy, int inner, int outer, int w, int h, int step = 2)
        {
            var pts = new List<(int, int)>();
            if (w <= 0 || h <= 0) return pts;
            if (inner < 0) inner = 0;
            if (outer < inner) outer = inner;
            if (step < 1) step = 1;
            int x0 = Math.Max(0, cx - outer), x1 = Math.Min(w - 1, cx + outer);
            int y0 = Math.Max(0, cy - outer), y1 = Math.Min(h - 1, cy + outer);
            int inner2 = inner * inner, outer2 = outer * outer;
            for (int y = y0; y <= y1; y += step)
                for (int x = x0; x <= x1; x += step)
                {
                    int dx = x - cx, dy = y - cy, d2 = dx * dx + dy * dy;
                    if (d2 >= inner2 && d2 <= outer2) pts.Add((x, y));
                }
            return pts;
        }

        public static bool IsTransparent(byte a) => a == 0;

        /// <summary>
        /// Unique item indices covered by the brush along <paramref name="samples"/> (topmost first at each sample).
        /// The same index is reported at most once.
        /// </summary>
        public static List<int> SweepHits(IReadOnlyList<(double X, double Y)> samples, int count, Func<int, double, double, bool> hits)
        {
            var order = new List<int>();
            if (samples == null || count <= 0 || hits == null) return order;
            var seen = new bool[count];
            for (int s = 0; s < samples.Count; s++)
            {
                double x = samples[s].X, y = samples[s].Y;
                for (int i = count - 1; i >= 0; i--)
                {
                    if (seen[i]) continue;
                    if (hits(i, x, y)) { seen[i] = true; order.Add(i); }
                }
            }
            return order;
        }
    }
}
