using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Native;
using BestSnap.Plugins;
using BestSnap.Views;

namespace BestSnap.Services
{
    /// <summary>
    /// Scrolling capture: pick a window or region, auto-scroll with SendInput / WM_MOUSEWHEEL (PageDown fallback),
    /// stitch frames via central-band overlap (sticky header/footer). Live preview + Esc / Stop.
    /// </summary>
    public static class ScrollCaptureService
    {
        public const int MaxHeight = ScrollStitchMath.DefaultMaxHeight;
        const int MaxFrames = 200;

        public static async Task<BitmapSource> CaptureAsync()
        {
            var ov = new SelectionOverlay(SelectionKind.Scrolling);
            ov.ShowDialog();
            if (!ov.ResultRect.HasValue) return null;
            var vs = CaptureService.VirtualScreen;
            var rel = ov.ResultRect.Value;
            var rect = new Int32Rect(rel.X + vs.X, rel.Y + vs.Y, rel.Width, rel.Height);
            if (rect.Width < 16 || rect.Height < ScrollStitchMath.MinFrame) return null;

            NativeMethods.POINT center = new NativeMethods.POINT { X = rect.X + rect.Width / 2, Y = rect.Y + rect.Height / 2 };
            var hwnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(center), NativeMethods.GA_ROOT);
            if (hwnd != IntPtr.Zero)
            {
                if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(hwnd);
            }
            await Task.Delay(200);
            return await RunAsync(hwnd, rect);
        }

        /// <summary>Legacy entry: scroll the given top-level window's bounds.</summary>
        public static Task<BitmapSource> CaptureAsync(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return Task.FromResult<BitmapSource>(null);
            var r = NativeMethods.GetWindowBounds(hwnd);
            return RunAsync(hwnd, new Int32Rect(r.Left, r.Top, r.Width, r.Height));
        }

        static async Task<BitmapSource> RunAsync(IntPtr hwnd, Int32Rect rect)
        {
            int w = rect.Width, h = rect.Height, stride = w * 4;
            if (w < 16 || h < ScrollStitchMath.MinFrame) return null;
            int cx = rect.X + w / 2, cy = rect.Y + h / 2;

            var ui = new ScrollCaptureWindow();
            ui.Show();
            await Task.Delay(180);

            byte[] Grab(out BitmapSource bmp)
            {
                bmp = CaptureService.CaptureRect(rect);
                var px = new byte[stride * h];
                bmp.CopyPixels(px, stride, 0);
                return px;
            }

            var first = Grab(out var firstBmp);
            var slices = new List<ScrollStitchMath.Slice>();
            int header = 0, footer = 0, still = 0, usedPage = 0;
            byte[] prev = first, last = first;
            int extra = 0;
            ui.Update(firstBmp, 1, h);
            try
            {
                for (int i = 0; i < MaxFrames && !ui.StopRequested; i++)
                {
                    if (hwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hwnd);
                    NativeMethods.ScrollAt(cx, cy, -120);
                    NativeMethods.ScrollAt(cx, cy, -120);
                    NativeMethods.ScrollAt(cx, cy, -120);
                    await Task.Delay(320);
                    if (ui.StopRequested) break;

                    var next = Grab(out var nextBmp);
                    var p0 = prev;
                    var res = await Task.Run(() => ScrollStitchMath.FindShift(p0, next, w, h, stride)).ConfigureAwait(true);
                    if (res.Unchanged || res.Shift <= 0)
                    {
                        if (usedPage < 2)
                        {
                            usedPage++;
                            NativeMethods.SendPageDown();
                            await Task.Delay(400);
                            continue;
                        }
                        if (++still >= 2) break;
                        continue;
                    }
                    still = 0; usedPage = 0;
                    if (slices.Count == 0) { header = res.Header; footer = res.Footer; _ = header; }
                    var slice = ScrollStitchMath.ToSlice(next, h, footer, res.Shift);
                    if (slice.RowCount <= 0) { if (++still >= 2) break; continue; }
                    slices.Add(slice);
                    extra += slice.RowCount;
                    prev = next; last = next;
                    ui.Update(nextBmp, slices.Count + 1, h + extra);
                    if (h + extra >= MaxHeight) break;
                }
            }
            finally { try { ui.Close(); } catch { } }

            if (ui.Cancelled) return null;
            var outPx = ScrollStitchMath.Compose(w, h, stride, first, slices, footer, last, MaxHeight, out int outH, out _);
            if (outH < 2 || outPx == null || outPx.Length < stride * outH) return firstBmp;
            var bmpOut = BitmapSource.Create(w, outH, 96, 96, PixelFormats.Bgra32, null, outPx, stride);
            bmpOut.Freeze();
            return bmpOut;
        }
    }
}
