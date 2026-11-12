using System;
using System.Collections.Generic;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Place the live-caption window on the monitor of the toolbar / recorder / cursor,
    /// bottom-center of that working area. Session-remembered position wins if still on a screen.
    /// </summary>
    public static class CaptionWindowMath
    {
        public const int DefaultWidth = 640;
        public const int DefaultHeight = 360;
        public const int Margin = 16;

        public static int MonitorIndexForPoint(int x, int y, IReadOnlyList<MonitorRect> monitors)
        {
            if (monitors == null || monitors.Count == 0) return -1;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                if (x >= m.X && y >= m.Y && x < m.X + m.Width && y < m.Y + m.Height)
                    return i;
            }
            int best = 0;
            long bestD = long.MaxValue;
            for (int i = 0; i < monitors.Count; i++)
            {
                long cx = monitors[i].X + monitors[i].Width / 2L;
                long cy = monitors[i].Y + monitors[i].Height / 2L;
                long dx = cx - x, dy = cy - y;
                long d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static bool ContainsPoint(IntRect r, int x, int y) =>
            x >= r.X && y >= r.Y && x < r.Right && y < r.Bottom;

        public static IntRect ClampTo(IntRect win, IntRect work)
        {
            int w = Math.Min(Math.Max(1, win.Width), Math.Max(1, work.Width));
            int h = Math.Min(Math.Max(1, win.Height), Math.Max(1, work.Height));
            int x = win.X, y = win.Y;
            if (x < work.X) x = work.X;
            if (y < work.Y) y = work.Y;
            if (x + w > work.Right) x = work.Right - w;
            if (y + h > work.Bottom) y = work.Bottom - h;
            return new IntRect(x, y, w, h);
        }

        public static IntRect BottomCenter(IntRect work, int width, int height, int margin = Margin)
        {
            if (margin < 0) margin = 0;
            int maxW = Math.Max(1, work.Width - 2 * margin);
            int maxH = Math.Max(1, work.Height - 2 * margin);
            int w = width < 1 ? DefaultWidth : Math.Min(width, maxW);
            int h = height < 1 ? DefaultHeight : Math.Min(height, maxH);
            int x = work.X + Math.Max(0, (work.Width - w) / 2);
            int y = work.Y + work.Height - margin - h;
            if (y < work.Y + margin) y = work.Y + margin;
            return ClampTo(new IntRect(x, y, w, h), work);
        }

        /// <summary>
        /// <paramref name="remembered"/> is last session position (physical pixels).
        /// <paramref name="anchorX"/>/<paramref name="anchorY"/> is the owner window or cursor.
        /// </summary>
        public static IntRect Place(IReadOnlyList<MonitorRect> workAreas, int width, int height, IntRect? remembered, int anchorX, int anchorY)
        {
            if (workAreas == null || workAreas.Count == 0)
                return new IntRect(anchorX, anchorY, Math.Max(1, width), Math.Max(1, height));

            if (remembered is IntRect last && last.Width > 0 && last.Height > 0)
            {
                int cx = last.X + last.Width / 2, cy = last.Y + last.Height / 2;
                int mi = MonitorIndexForPoint(cx, cy, workAreas);
                if (mi >= 0 && ContainsPoint(workAreas[mi].Bounds, cx, cy))
                    return ClampTo(last, workAreas[mi].Bounds);
            }
            int i = MonitorIndexForPoint(anchorX, anchorY, workAreas);
            if (i < 0) i = 0;
            return BottomCenter(workAreas[i].Bounds, width, height);
        }
    }
}
