using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Native;

namespace BestSnap.Services
{
    /// <summary>
    /// Animated GIF recorder: own GDI capture loop at a fixed fps on a background thread, optional downscale,
    /// adaptive 256-colour palette per frame, frames streamed to disk (memory stays flat).
    /// </summary>
    public sealed class GifRecorder
    {
        private Thread _thread;
        private volatile bool _stop, _paused;
        public string File { get; }
        public event Action<string> Completed;
        public event Action<string> Failed;

        private readonly Func<Int32Rect?> _rect; private readonly int _fps, _maxWidth; private readonly bool _cursor;

        public GifRecorder(string file, Func<Int32Rect?> rectProvider, int fps, int maxWidth, bool cursor)
        { File = file; _rect = rectProvider; _fps = Math.Clamp(fps, 1, 30); _maxWidth = maxWidth; _cursor = cursor; }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "GifRecorder", Priority = ThreadPriority.AboveNormal };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
        public void Pause() => _paused = true;
        public void Resume() => _paused = false;
        public void Stop() => _stop = true;

        private void Run()
        {
            try
            {
                var first = _rect() ?? throw new InvalidOperationException(L.T("录制区域无效"));
                int w = first.Width, h = first.Height;
                double scale = _maxWidth > 0 && w > _maxWidth ? (double)_maxWidth / w : 1;
                int ow = Math.Max(1, (int)Math.Round(w * scale)), oh = Math.Max(1, (int)Math.Round(h * scale));
                using var fs = new FileStream(File, FileMode.Create, FileAccess.Write);
                var gw = new GifStreamWriter(fs, ow, oh);
                var sw = Stopwatch.StartNew(); long frameMs = 1000 / _fps; long next = 0;
                byte[] pending = null; long pendingAt = 0;
                while (!_stop)
                {
                    long now = sw.ElapsedMilliseconds;
                    if (now < next) { Thread.Sleep((int)Math.Min(next - now, 20)); continue; }
                    next = now + frameMs;
                    if (_paused) { if (pending != null) { pendingAt += frameMs; } continue; } // paused time is not shown
                    var r = _rect(); if (r == null) continue;
                    var rr = new Int32Rect(r.Value.X, r.Value.Y, w, h);
                    BitmapSource img = CaptureService.CaptureRect(rr, _cursor);
                    if (img == null) continue;
                    if (scale != 1) img = new TransformedBitmap(img, new ScaleTransform(scale, scale));
                    var frame = GifStreamWriter.EncodeFrame(img);
                    if (pending != null) gw.WriteFrame(pending, (int)Math.Max(2, (now - pendingAt) / 10));
                    pending = frame; pendingAt = now;
                }
                if (pending != null) gw.WriteFrame(pending, (int)Math.Max(2, frameMs / 10));
                gw.Finish();
                Raise(Completed, File);
            }
            catch (Exception ex) { Logger.Error("GifRecorder", ex); Raise(Failed, ex.Message); }
        }

        private static void Raise(Action<string> a, string v) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => a?.Invoke(v)));
    }

    /// <summary>Minimal GIF89a muxer: takes single-frame GIFs from WPF's GifBitmapEncoder and joins them into an animation.</summary>
    public sealed class GifStreamWriter
    {
        private readonly Stream _s;
        public GifStreamWriter(Stream s, int w, int h)
        {
            _s = s;
            Write(new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
            WriteU16(w); WriteU16(h); _s.WriteByte(0x70); _s.WriteByte(0); _s.WriteByte(0); // no global colour table
            // NETSCAPE2.0 loop forever
            Write(new byte[] { 0x21, 0xFF, 0x0B }); Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); Write(new byte[] { 3, 1, 0, 0, 0 });
        }

        /// <summary>Encodes one frame with an adaptive palette; returns a complete single-image GIF.</summary>
        public static byte[] EncodeFrame(BitmapSource img)
        {
            var bgr = new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0);
            BitmapPalette pal;
            try { pal = new BitmapPalette(bgr, 256); } catch { pal = BitmapPalettes.Halftone256; }
            var idx = new FormatConvertedBitmap(bgr, PixelFormats.Indexed8, pal, 0);
            var enc = new GifBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(idx));
            using var ms = new MemoryStream(); enc.Save(ms); return ms.ToArray();
        }

        public void WriteFrame(byte[] gif, int delayCs)
        {
            int p = 6;
            int packed = gif[p + 4]; p += 7;
            byte[] gct = null; int gctBits = 0;
            if ((packed & 0x80) != 0) { gctBits = packed & 7; int n = 3 << (gctBits + 1); gct = new byte[n]; Array.Copy(gif, p, gct, 0, n); p += n; }
            while (p < gif.Length)
            {
                byte b = gif[p];
                if (b == 0x21) { p += 2; while (gif[p] != 0) p += gif[p] + 1; p++; continue; }
                if (b != 0x2C) break;
                // graphic control extension: disposal 1 (leave), delay
                Write(new byte[] { 0x21, 0xF9, 4, 0x04 }); WriteU16(Math.Clamp(delayCs, 2, 65535)); Write(new byte[] { 0, 0 });
                var desc = new byte[10]; Array.Copy(gif, p, desc, 0, 10); p += 10;
                int lp = desc[9];
                byte[] lct = null;
                if ((lp & 0x80) != 0) { int n = 3 << ((lp & 7) + 1); lct = new byte[n]; Array.Copy(gif, p, lct, 0, n); p += n; }
                else if (gct != null) { lct = gct; desc[9] = (byte)((lp & 0x40) | 0x80 | gctBits); }
                Write(desc); if (lct != null) Write(lct);
                int start = p; p++; // LZW min code size
                while (gif[p] != 0) p += gif[p] + 1;
                p++;
                _s.Write(gif, start, p - start);
                break;
            }
        }

        public void Finish() { _s.WriteByte(0x3B); _s.Flush(); }
        private void Write(byte[] b) => _s.Write(b, 0, b.Length);
        private void WriteU16(int v) { _s.WriteByte((byte)(v & 0xFF)); _s.WriteByte((byte)((v >> 8) & 0xFF)); }
    }
}
