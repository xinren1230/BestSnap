using System;
using System.Collections.Generic;

namespace BestSnap.Plugins
{
    /// <summary>Integer rectangle in virtual-screen physical pixels (origin may be negative on multi-monitor setups).</summary>
    public readonly struct IntRect : IEquatable<IntRect>
    {
        public IntRect(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;
        public bool Equals(IntRect other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is IntRect r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
        public override string ToString() => $"{X},{Y} {Width}x{Height}";
        public static bool operator ==(IntRect a, IntRect b) => a.Equals(b);
        public static bool operator !=(IntRect a, IntRect b) => !a.Equals(b);
    }

    /// <summary>One monitor in the same coordinate space as <see cref="IntRect"/>.</summary>
    public readonly struct MonitorRect
    {
        public MonitorRect(int x, int y, int width, int height, string deviceName = null)
        {
            X = x; Y = y; Width = width; Height = height; DeviceName = deviceName ?? "";
        }
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public string DeviceName { get; }
        public IntRect Bounds => new IntRect(X, Y, Width, Height);
    }

    /// <summary>
    /// Region / monitor geometry for screen recording. Coordinates are physical pixels on the virtual desktop.
    /// H.264 needs even width and height; the crop origin is kept and the size is rounded down.
    /// </summary>
    public static class RecordRectMath
    {
        /// <summary>Positive-size rectangle; flipped drags (negative w/h) are normalized.</summary>
        public static IntRect Normalize(int x, int y, int w, int h)
        {
            if (w < 0) { x += w; w = -w; }
            if (h < 0) { y += h; h = -h; }
            return new IntRect(x, y, w, h);
        }

        public static IntRect Intersect(IntRect a, IntRect b)
        {
            int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
            int x2 = Math.Min(a.Right, b.Right), y2 = Math.Min(a.Bottom, b.Bottom);
            return new IntRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        /// <summary>Axis-aligned union of monitors (the virtual screen). Empty list → 0×0 at origin.</summary>
        public static IntRect VirtualScreen(IReadOnlyList<MonitorRect> monitors)
        {
            if (monitors == null || monitors.Count == 0) return new IntRect(0, 0, 0, 0);
            int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                x1 = Math.Min(x1, m.X); y1 = Math.Min(y1, m.Y);
                x2 = Math.Max(x2, m.X + m.Width); y2 = Math.Max(y2, m.Y + m.Height);
            }
            return new IntRect(x1, y1, x2 - x1, y2 - y1);
        }

        /// <summary>Clip <paramref name="r"/> to [0,w)×[0,h) in the given local space.</summary>
        public static IntRect ClipLocal(IntRect r, int w, int h)
        {
            int x1 = Math.Max(0, r.X), y1 = Math.Max(0, r.Y);
            int x2 = Math.Min(w, r.Right), y2 = Math.Min(h, r.Bottom);
            return new IntRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        /// <summary><paramref name="region"/> expressed relative to <paramref name="originX"/>,<paramref name="originY"/>.</summary>
        public static IntRect RelativeTo(IntRect region, int originX, int originY) =>
            new IntRect(region.X - originX, region.Y - originY, region.Width, region.Height);

        /// <summary>H.264 even dimensions: shrink width/height by at most 1 px; origin unchanged.</summary>
        public static IntRect EvenSize(IntRect r)
        {
            int w = r.Width & ~1, h = r.Height & ~1;
            return new IntRect(r.X, r.Y, w, h);
        }

        /// <summary>
        /// Crop of <paramref name="region"/> on <paramref name="monitor"/>, in monitor-local pixels, even-sized for H.264.
        /// </summary>
        public static IntRect MonitorLocalEvenCrop(IntRect region, MonitorRect monitor)
        {
            var local = RelativeTo(region, monitor.X, monitor.Y);
            return EvenSize(ClipLocal(local, monitor.Width, monitor.Height));
        }

        /// <summary>
        /// Crop of <paramref name="region"/> in virtual-screen-local pixels (origin = bounding box of <paramref name="monitors"/>).
        /// </summary>
        public static IntRect VirtualLocalEvenCrop(IntRect region, IReadOnlyList<MonitorRect> monitors)
        {
            var vs = VirtualScreen(monitors);
            var local = RelativeTo(region, vs.X, vs.Y);
            return EvenSize(ClipLocal(local, vs.Width, vs.Height));
        }

        /// <summary>
        /// Monitor with the largest overlap with <paramref name="region"/>.
        /// <paramref name="spansMonitors"/> is true when two or more monitors overlap the region.
        /// Returns -1 when <paramref name="monitors"/> is empty.
        /// </summary>
        public static int PickMonitorIndex(IntRect region, IReadOnlyList<MonitorRect> monitors, out bool spansMonitors)
        {
            spansMonitors = false;
            if (monitors == null || monitors.Count == 0) return -1;
            int best = 0; long bestArea = -1; int hits = 0;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                var inter = Intersect(region, m.Bounds);
                long a = (long)inter.Width * inter.Height;
                if (a > 0) hits++;
                if (a > bestArea) { bestArea = a; best = i; }
            }
            spansMonitors = hits > 1;
            return best;
        }

        public static bool Contains(MonitorRect monitor, IntRect region)
        {
            if (region.IsEmpty) return false;
            return region.X >= monitor.X && region.Y >= monitor.Y
                && region.Right <= monitor.X + monitor.Width && region.Bottom <= monitor.Y + monitor.Height;
        }

        /// <summary>True when the region sits fully inside exactly one of <paramref name="monitors"/>.</summary>
        public static bool FitsOneMonitor(IntRect region, IReadOnlyList<MonitorRect> monitors, out int index)
        {
            index = PickMonitorIndex(region, monitors, out bool spans);
            if (index < 0) return false;
            if (spans) return false;
            return Contains(monitors[index], region);
        }

        /// <summary>Frame drawn just outside the capture rect so the stroke is not inside the recorded pixels.</summary>
        public static IntRect BorderOutside(IntRect capture, int pad)
        {
            if (pad < 0) pad = 0;
            return new IntRect(capture.X - pad, capture.Y - pad, capture.Width + 2 * pad, capture.Height + 2 * pad);
        }

        /// <summary>Minimum H.264 / GIF capture size after even rounding.</summary>
        public static bool IsValidCapture(IntRect r) => r.Width >= 2 && r.Height >= 2;

        /// <summary>Allowed recording frame rates (video).</summary>
        public static readonly int[] VideoFpsChoices = { 10, 15, 24, 30, 60 };

        public static int NormalizeFps(int fps)
        {
            int best = VideoFpsChoices[0], dist = Math.Abs(fps - best);
            for (int i = 1; i < VideoFpsChoices.Length; i++)
            {
                int d = Math.Abs(fps - VideoFpsChoices[i]);
                if (d < dist) { dist = d; best = VideoFpsChoices[i]; }
            }
            return best;
        }
    }
}
