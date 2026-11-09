using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Native;
using Drawing = System.Drawing;

namespace BestSnap.Services
{
    /// <summary>Screen grabbing in physical pixels (process is Per-Monitor-V2 DPI aware).</summary>
    public static class CaptureService
    {
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

        public static Int32Rect VirtualScreen
        {
            get { var r = NativeMethods.VirtualScreen; return new Int32Rect(r.Left, r.Top, r.Width, r.Height); }
        }

        public static BitmapSource CaptureRect(Int32Rect r, bool includeCursor = false)
        {
            if (r.Width <= 0 || r.Height <= 0) return null;
            using (var bmp = new Drawing.Bitmap(r.Width, r.Height, Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Drawing.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(r.X, r.Y, 0, 0, new Drawing.Size(r.Width, r.Height), Drawing.CopyPixelOperation.SourceCopy);
                    if (includeCursor) DrawCursor(g, r.X, r.Y);
                }
                return FromGdiBitmap(bmp, opaque: true);
            }
        }

        /// <summary>Draws the real current cursor (I-beam, hand, resize…) at its hotspot-corrected position.</summary>
        public static void DrawCursor(Drawing.Graphics g, int originX, int originY)
        {
            var ci = new NativeMethods.CURSORINFO { cbSize = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
            if (!NativeMethods.GetCursorInfo(ref ci) || (ci.flags & NativeMethods.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;
            int hx = 0, hy = 0;
            if (NativeMethods.GetIconInfo(ci.hCursor, out var ii))
            {
                hx = ii.xHotspot; hy = ii.yHotspot;
                if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
            }
            IntPtr hdc = g.GetHdc();
            try { NativeMethods.DrawIconEx(hdc, ci.ptScreenPos.X - originX - hx, ci.ptScreenPos.Y - originY - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL); }
            finally { g.ReleaseHdc(hdc); }
        }

        /// <summary>One copy only: GDI bitmap bits -> frozen Bgra32 BitmapSource (no HBITMAP round trip).</summary>
        public static BitmapSource FromGdiBitmap(Drawing.Bitmap bmp, bool opaque)
        {
            var rect = new Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bs = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, opaque ? PixelFormats.Bgr32 : PixelFormats.Bgra32, null,
                                             data.Scan0, data.Stride * bmp.Height, data.Stride);
                bs.Freeze();
                return bs;
            }
            finally { bmp.UnlockBits(data); }
        }

        public static BitmapSource CaptureFullScreen(bool cursor = false) => CaptureRect(VirtualScreen, cursor);

        public static BitmapSource CaptureWindow(IntPtr hwnd, bool cursor = false)
        {
            if (hwnd == IntPtr.Zero) return null;
            var r = NativeMethods.GetWindowBounds(hwnd);
            return CaptureRect(new Int32Rect(r.Left, r.Top, r.Width, r.Height), cursor);
        }

        public static BitmapSource ToBitmapSource(Drawing.Bitmap bmp)
        {
            IntPtr h = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return Normalize(src);
            }
            finally { DeleteObject(h); }
        }

        /// <summary>Converts to Bgra32 @ 96 DPI so that 1 image pixel == 1 WPF unit in the editor.</summary>
        public static BitmapSource Normalize(BitmapSource src)
        {
            if (src == null) return null;
            if ((src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Bgr32) && src.DpiX == 96 && src.DpiY == 96 && src.IsFrozen) return src;
            var conv = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int stride = conv.PixelWidth * 4;
            var px = new byte[stride * conv.PixelHeight];
            conv.CopyPixels(px, stride, 0);
            var bs = BitmapSource.Create(conv.PixelWidth, conv.PixelHeight, 96, 96, PixelFormats.Bgra32, null, px, stride);
            bs.Freeze();
            return bs;
        }

        public static BitmapSource Crop(BitmapSource src, Int32Rect r)
        {
            r = Clip(r, src.PixelWidth, src.PixelHeight);
            if (r.Width <= 0 || r.Height <= 0) return null;
            return Normalize(new CroppedBitmap(src, r));
        }

        public static Int32Rect Clip(Int32Rect r, int w, int h)
        {
            int x1 = Math.Max(0, r.X), y1 = Math.Max(0, r.Y);
            int x2 = Math.Min(w, r.X + r.Width), y2 = Math.Min(h, r.Y + r.Height);
            return new Int32Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        /// <summary>Crop to the bounding box of a polygon and make pixels outside it transparent.</summary>
        public static BitmapSource CropPolygon(BitmapSource src, PointCollection pts)
        {
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = pts[0], IsClosed = true, IsFilled = true };
            fig.Segments.Add(new PolyLineSegment(pts, true));
            geo.Figures.Add(fig);
            var b = geo.Bounds;
            var r = Clip(new Int32Rect((int)b.X, (int)b.Y, (int)Math.Ceiling(b.Width), (int)Math.Ceiling(b.Height)), src.PixelWidth, src.PixelHeight);
            if (r.Width < 2 || r.Height < 2) return null;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-r.X, -r.Y));
                dc.PushClip(geo);
                dc.DrawImage(src, new Rect(0, 0, src.PixelWidth, src.PixelHeight));
            }
            var rtb = new RenderTargetBitmap(r.Width, r.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            return Normalize(rtb);
        }
    }
}
