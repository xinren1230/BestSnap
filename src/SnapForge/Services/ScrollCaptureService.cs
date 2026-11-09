using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Native;
using SnapForge.Views;

namespace SnapForge.Services
{
    /// <summary>
    /// Scrolling capture: real wheel input via SendInput to the window under the cursor, frame grabbing,
    /// static header/footer detection (rows that do not move) and overlap search on the scrolling part only.
    /// Shows a live preview panel with manual 完成/取消.
    /// </summary>
    public static class ScrollCaptureService
    {
        private const int MaxFrames = 80, MaxHeight = 32000;

        public static async Task<BitmapSource> CaptureAsync(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            var r = NativeMethods.GetWindowBounds(hwnd);
            var vs = CaptureService.VirtualScreen;
            var rect = CaptureService.Clip(new Int32Rect(r.Left - vs.X, r.Top - vs.Y, r.Width, r.Height), vs.Width, vs.Height);
            rect = new Int32Rect(rect.X + vs.X, rect.Y + vs.Y, rect.Width, rect.Height);
            int w = rect.Width, h = rect.Height, stride = w * 4;
            if (w < 16 || h < 64) return null;

            NativeMethods.SetForegroundWindow(hwnd);
            var ui = new ScrollCaptureWindow();
            ui.Show();
            await Task.Delay(300);

            byte[] Grab(out BitmapSource bmp) { bmp = CaptureService.CaptureRect(rect); var px = new byte[stride * h]; bmp.CopyPixels(px, stride, 0); return px; }

            var first = Grab(out var firstBmp);
            var slices = new List<(byte[] px, int from, int count)>();
            int header = -1, footer = 0, total = 0, still = 0;
            byte[] prev = first, last = first;
            ui.Update(firstBmp, 1, h);
            try
            {
                for (int i = 0; i < MaxFrames && !ui.StopRequested; i++)
                {
                    // Only scroll while the cursor is over the target window.
                    NativeMethods.GetCursorPos(out var cur);
                    if (NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(cur), NativeMethods.GA_ROOT) != hwnd)
                    {
                        ui.Update(null, slices.Count + 1, total + h, L.T("⚠ 请把鼠标放回要滚动的窗口内容上"));
                        await Task.Delay(300); i--; continue;
                    }
                    for (int k = 0; k < 3; k++) NativeMethods.SendWheel(-120);
                    await Task.Delay(350);
                    if (ui.StopRequested) break;
                    var next = Grab(out var nextBmp);
                    var p0 = prev; int hd = header, ft = footer;
                    var res = await Task.Run(() =>
                    {
                        int hh = hd, ff = ft;
                        if (hh < 0) { hh = StaticRows(p0, next, stride, h, fromTop: true); ff = StaticRows(p0, next, stride, h, fromTop: false); if (hh + ff > h - 48) { hh = 0; ff = 0; } }
                        return (shift: FindShift(p0, next, stride, h, hh, ff), header: hh, footer: ff);
                    });
                    if (res.shift <= 0)
                    {
                        if (++still >= 2) break; // reached the end
                        continue;
                    }
                    still = 0;
                    if (header < 0) { header = res.header; footer = res.footer; }
                    slices.Add((next, h - footer - res.shift, res.shift));
                    total += res.shift; prev = next; last = next;
                    ui.Update(nextBmp, slices.Count + 1, total + h);
                    if (total + h >= MaxHeight) break;
                }
            }
            finally { ui.Close(); }
            if (ui.Cancelled) return null;
            if (header < 0) { header = 0; footer = 0; }

            // Stitch: first frame without footer, every new slice, then footer from the last frame.
            int outH = h + total;
            var outPx = new byte[stride * outH];
            Buffer.BlockCopy(first, 0, outPx, 0, stride * (h - footer));
            int y = h - footer;
            foreach (var s in slices) { Buffer.BlockCopy(s.px, stride * s.from, outPx, stride * y, stride * s.count); y += s.count; }
            if (footer > 0) Buffer.BlockCopy(last, stride * (h - footer), outPx, stride * y, stride * footer);
            var bmp = BitmapSource.Create(w, outH, 96, 96, PixelFormats.Bgra32, null, outPx, stride);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Number of identical rows at the top (or bottom) of two frames = fixed header/footer (toolbars, title bar).</summary>
        private static int StaticRows(byte[] a, byte[] b, int stride, int h, bool fromTop)
        {
            int n = 0;
            for (int i = 0; i < h / 2; i++)
            {
                int y = fromTop ? i : h - 1 - i;
                if (!RowEq(a, b, stride, y, y)) break;
                n++;
            }
            return n;
        }

        /// <summary>Rows the scrolling region moved up between a and b (0 if unchanged / not found).</summary>
        private static int FindShift(byte[] a, byte[] b, int stride, int h, int header, int footer)
        {
            int top = header, bottom = h - footer; // scrolling region [top, bottom)
            int region = bottom - top;
            if (region < 32) return 0;
            bool same = true;
            for (int y = top; y < bottom && same; y += 3) same = RowEq(a, b, stride, y, y);
            if (same) return 0;

            // Probe band in frame a, near the bottom of the region, preferring rows with visible detail.
            const int band = 16;
            int probe = bottom - band - 4;
            while (probe > top + region / 3 && Flat(a, stride, probe, band)) probe -= 8;
            for (int s = 1; s <= probe - top; s++)
            {
                bool ok = true;
                for (int k = 0; k < band && ok; k++) ok = RowEq(a, b, stride, probe + k, probe + k - s);
                if (ok)
                {
                    // verify a larger overlap to avoid false matches on repetitive content
                    int ov = Math.Min(region - s, 200);
                    for (int k = 0; k < ov && ok; k += 2) ok = RowEq(a, b, stride, top + s + k, top + k);
                    if (ok) return s;
                }
            }
            return 0;
        }

        private static bool Flat(byte[] a, int stride, int y, int band)
        {
            for (int k = 0; k < band; k++)
            {
                int o = (y + k) * stride;
                for (int x = 4; x < stride; x += 4) if (a[o + x] != a[o] || a[o + x + 1] != a[o + 1] || a[o + x + 2] != a[o + 2]) return false;
            }
            return true;
        }

        private static bool RowEq(byte[] a, byte[] b, int stride, int ya, int yb) =>
            new ReadOnlySpan<byte>(a, ya * stride, stride).SequenceEqual(new ReadOnlySpan<byte>(b, yb * stride, stride));
    }
}
