using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using SnapForge.Plugins;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnapForge.Ocr
{
    internal static class Img
    {
        public static async Task<SoftwareBitmap> DecodeAsync(byte[] png, uint maxDim = 0)
        {
            using var ms = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(ms.GetOutputStreamAt(0))) { w.WriteBytes(png); await w.StoreAsync(); await w.FlushAsync(); w.DetachStream(); }
            var dec = await BitmapDecoder.CreateAsync(ms);
            if (maxDim > 0 && (dec.PixelWidth > maxDim || dec.PixelHeight > maxDim))
            {
                double s = (double)maxDim / Math.Max(dec.PixelWidth, dec.PixelHeight);
                var t = new BitmapTransform { ScaledWidth = (uint)(dec.PixelWidth * s), ScaledHeight = (uint)(dec.PixelHeight * s) };
                return await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, t, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            }
            return await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
    }

    /// <summary>Windows.Media.Ocr with word bounding boxes (coordinates scaled back to the source image).</summary>
    public sealed class WindowsOcrLayoutEngine : IOcrLayoutEngine
    {
        public string Id => "windows-ocr-layout";
        public string DisplayName => "Windows OCR (layout)";
        public bool IsAvailable { get { try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; } catch { return false; } } }

        public async Task<IReadOnlyList<OcrWordBox>> RecognizeWordsAsync(byte[] png, string language, CancellationToken ct)
        {
            var engine = WindowsOcrEngine.EnginesFor(language).LastOrDefault(); // auto: profile recognizer (best for e-mails/URLs)
            if (engine == null) throw new InvalidOperationException("OCR language pack not installed: " + language);
            using var full = await Img.DecodeAsync(png);
            int fw = full.PixelWidth;
            using var sb = await Img.DecodeAsync(png, OcrEngine.MaxImageDimension);
            double k = (double)fw / sb.PixelWidth;
            ct.ThrowIfCancellationRequested();
            var res = await engine.RecognizeAsync(sb);
            var list = new List<OcrWordBox>();
            int li = 0;
            foreach (var line in res.Lines)
            {
                foreach (var w in line.Words)
                    list.Add(new OcrWordBox { Text = w.Text, Line = li, X = w.BoundingRect.X * k, Y = w.BoundingRect.Y * k, Width = w.BoundingRect.Width * k, Height = w.BoundingRect.Height * k });
                li++;
            }
            return list;
        }
    }

    /// <summary>Windows.Media.FaceAnalysis.FaceDetector.</summary>
    public sealed class WindowsFaceDetector : IFaceDetector
    {
        public string Id => "windows-face";
        public string DisplayName => "Windows Face Detector";
        public bool IsAvailable { get { try { return FaceDetector.IsSupported; } catch { return false; } } }

        public async Task<IReadOnlyList<int[]>> DetectFacesAsync(byte[] png, CancellationToken ct)
        {
            var det = await FaceDetector.CreateAsync();
            using var full = await Img.DecodeAsync(png);
            int fw = full.PixelWidth;
            using var sb = await Img.DecodeAsync(png, 2560);
            double k = (double)fw / sb.PixelWidth;
            var fmt = FaceDetector.GetSupportedBitmapPixelFormats().FirstOrDefault();
            using var gray = SoftwareBitmap.Convert(sb, fmt);
            ct.ThrowIfCancellationRequested();
            var faces = await det.DetectFacesAsync(gray);
            return faces.Select(f => new[] { (int)(f.FaceBox.X * k), (int)(f.FaceBox.Y * k), (int)(f.FaceBox.Width * k), (int)(f.FaceBox.Height * k) }).ToList();
        }
    }

    /// <summary>MediaCapture + MediaFrameReader webcam frames (BGRA32).</summary>
    public sealed class WindowsWebcamSource : IWebcamSource
    {
        public string Id => "windows-webcam";
        public string DisplayName => "Windows Camera";

        public async Task<IReadOnlyList<string[]>> ListCamerasAsync()
        {
            var devs = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            return devs.Select(d => new[] { d.Id, d.Name }).ToList();
        }

        public async Task<IDisposable> StartAsync(string cameraIdOrName, Action<byte[], int, int> onFrame)
        {
            var devs = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var dev = devs.FirstOrDefault(d => d.Id == cameraIdOrName || d.Name == cameraIdOrName) ?? devs.FirstOrDefault();
            if (dev == null) throw new InvalidOperationException("No camera found");
            var cap = new MediaCapture();
            await cap.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = dev.Id, StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu, SharingMode = MediaCaptureSharingMode.SharedReadOnly
            });
            var src = cap.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color) ?? cap.FrameSources.Values.First();
            var reader = await cap.CreateFrameReaderAsync(src, "BGRA8");
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            int busy = 0;
            reader.FrameArrived += (r, e) =>
            {
                if (Interlocked.Exchange(ref busy, 1) == 1) return;
                try
                {
                    using var fr = r.TryAcquireLatestFrame();
                    var bmp = fr?.VideoMediaFrame?.SoftwareBitmap;
                    if (bmp == null) return;
                    using var bgra = bmp.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? SoftwareBitmap.Copy(bmp) : SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    var buf = new Windows.Storage.Streams.Buffer((uint)(bgra.PixelWidth * bgra.PixelHeight * 4));
                    bgra.CopyToBuffer(buf);
                    onFrame(buf.ToArray(), bgra.PixelWidth, bgra.PixelHeight);
                }
                catch { }
                finally { Interlocked.Exchange(ref busy, 0); }
            };
            await reader.StartAsync();
            return new Session(cap, reader);
        }

        private sealed class Session : IDisposable
        {
            private MediaCapture _cap; private MediaFrameReader _r;
            public Session(MediaCapture c, MediaFrameReader r) { _cap = c; _r = r; }
            public void Dispose()
            {
                var r = _r; var c = _cap; _r = null; _cap = null;
                if (r == null) return;
                Task.Run(async () => { try { await r.StopAsync(); } catch { } try { r.Dispose(); } catch { } try { c.Dispose(); } catch { } });
            }
        }
    }
}
