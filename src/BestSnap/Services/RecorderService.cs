using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ScreenRecorderLib;
using BestSnap.Models;
using BestSnap.Native;

namespace BestSnap.Services
{
    public class RecordingRequest
    {
        public RecordingSettings Options;
        public Int32Rect? Region;        // virtual-screen physical pixels (absolute)
        public IntPtr WindowHandle;
        public string OutputPath;        // optional exact output file (CLI --out); otherwise Rec_<time> in the save folder
    }

    /// <summary>MP4 (H.264/AAC) recording via ScreenRecorderLib (Media Foundation, no FFmpeg). Events are raised on the UI thread.</summary>
    public sealed class RecorderService
    {
        private Recorder _rec;
        private GifRecorder _gif;
        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }
        public string CurrentFile { get; private set; }
        public event Action<string> Completed;
        /// <summary>Raised (UI thread) when frames are actually being captured — status Recording, not the Record() call (MF startup can take seconds).</summary>
        public event Action Started;
        public bool HasStarted { get; private set; }
        public event Action<string> Failed;

        public static List<(string Device, string Name)> GetMonitors()
        {
            var list = new List<(string, string)>();
            try { foreach (var d in Recorder.GetDisplays()) list.Add((d.DeviceName, string.IsNullOrEmpty(d.FriendlyName) ? d.DeviceName : d.FriendlyName + " (" + d.DeviceName + ")")); }
            catch { foreach (var s in System.Windows.Forms.Screen.AllScreens) list.Add((s.DeviceName, s.DeviceName + $" {s.Bounds.Width}×{s.Bounds.Height}" + (s.Primary ? " 主显示器" : ""))); }
            return list;
        }

        public static List<(IntPtr Handle, string Title)> GetWindows()
        {
            var list = new List<(IntPtr, string)>();
            foreach (var w in Recorder.GetWindows())
                if (w.IsValidWindow() && !string.IsNullOrWhiteSpace(w.Title) && !NativeMethods.IsOwnWindow(w.Handle))
                    list.Add((w.Handle, w.IsMinmimized() ? w.Title + L.T("  (已最小化，开始时自动还原)") : w.Title));
            return list;
        }

        public static List<(string Device, string Name)> GetCameras()
        {
            var list = new List<(string, string)>();
            try { foreach (var d in Recorder.GetSystemVideoCaptureDevices()) list.Add((d.DeviceName, d.FriendlyName)); } catch (Exception ex) { Logger.Error("GetCameras", ex); }
            return list;
        }

        // ---- recorded overlays (keystrokes, webcam bubble) ----
        private readonly List<Window> _extras = new List<Window>();

        private static Int32Rect RecordArea(RecordingRequest req)
        {
            if (req.WindowHandle != IntPtr.Zero) { var r = NativeMethods.GetWindowBounds(req.WindowHandle); return new Int32Rect(r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height)); }
            if (req.Region.HasValue) return req.Region.Value;
            var scr = System.Windows.Forms.Screen.PrimaryScreen;
            foreach (var sc in System.Windows.Forms.Screen.AllScreens) if (sc.DeviceName == req.Options.MonitorDevice) scr = sc;
            var b = scr.Bounds; return new Int32Rect(b.X, b.Y, b.Width, b.Height);
        }

        /// <summary>Webcam via ScreenRecorderLib overlay (MP4, rectangle) — or via the circular bubble window (needs plugin)?</summary>
        private static bool UseBubble(RecordingRequest req)
        {
            var o = req.Options;
            if (!o.WebcamEnabled || PluginHost.Webcam == null) return false;
            if (o.Format == RecordFormat.Gif) return true;
            return o.WebcamCircle && req.WindowHandle == IntPtr.Zero; // a window source would not include the bubble
        }

        private void StartExtras(RecordingRequest req)
        {
            if (Application.Current == null) return;
            var o = req.Options; var area = RecordArea(req);
            try
            {
                if (o.KeystrokeOverlay) { var k = new Views.KeystrokeOverlay(area, o.KeystrokeShowClicks); k.Show(); _extras.Add(k); }
                if (UseBubble(req)) { var b = new Views.WebcamBubble(area, o); b.Show(); _extras.Add(b); }
            }
            catch (Exception ex) { Logger.Error("RecordingOverlays", ex); }
        }

        private void StopExtras()
        {
            foreach (var w in _extras) { try { w.Close(); } catch { } }
            _extras.Clear();
        }

        public static List<(string Device, string Name)> GetMicrophones()
        {
            var list = new List<(string, string)>();
            foreach (var d in Recorder.GetSystemAudioDevices(AudioDeviceSource.InputDevices)) list.Add((d.DeviceName, d.FriendlyName));
            return list;
        }

        public void Start(RecordingRequest req, string defaultFolder)
        {
            if (IsRecording) return;
            HasStarted = false;
            var o = req.Options;
            string folder = string.IsNullOrWhiteSpace(o.SaveFolder) ? defaultFolder : o.SaveFolder;
            Directory.CreateDirectory(folder);
            bool gif = o.Format == RecordFormat.Gif;
            CurrentFile = !string.IsNullOrWhiteSpace(req.OutputPath) ? req.OutputPath
                : Path.Combine(folder, "Rec_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (gif ? ".gif" : ".mp4"));
            var outDir = Path.GetDirectoryName(CurrentFile); if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
            if (gif) { StartGif(req); return; }

            var sources = new List<RecordingSourceBase>();
            ScreenRect sourceRect = null;
            if (req.WindowHandle != IntPtr.Zero)
                sources.Add(new WindowRecordingSource(req.WindowHandle) { IsCursorCaptureEnabled = o.ShowCursor });
            else if (req.Region.HasValue)
            {
                // Record the monitor that contains the region, cropped via SourceRect (coordinates relative to that monitor).
                var r = req.Region.Value;
                var scr = PickScreen(r, out bool spans);
                if (spans) RegionClipped?.Invoke(scr.DeviceName);
                sources.Add(new DisplayRecordingSource(scr.DeviceName) { IsCursorCaptureEnabled = o.ShowCursor });
                var c = CaptureService.Clip(new Int32Rect(r.X - scr.Bounds.X, r.Y - scr.Bounds.Y, r.Width, r.Height), scr.Bounds.Width, scr.Bounds.Height);
                int w = c.Width & ~1, h = c.Height & ~1; // H.264 needs even dimensions
                sourceRect = new ScreenRect(c.X, c.Y, w, h);
            }
            else
            {
                string dev = string.IsNullOrEmpty(o.MonitorDevice) ? DisplayRecordingSource.MainMonitor.DeviceName : o.MonitorDevice;
                sources.Add(new DisplayRecordingSource(dev) { IsCursorCaptureEnabled = o.ShowCursor });
            }

            var opts = new RecorderOptions
            {
                SourceOptions = new SourceOptions { RecordingSources = sources },
                OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video },
                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Framerate = o.Fps, Quality = Math.Clamp(o.Quality, 1, 100), IsHardwareEncodingEnabled = true,
                    Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.High }
                },
                AudioOptions = new AudioOptions
                {
                    IsAudioEnabled = o.SystemAudio || o.Microphone,
                    IsOutputDeviceEnabled = o.SystemAudio,
                    IsInputDeviceEnabled = o.Microphone,
                    AudioInputDevice = o.Microphone ? o.MicrophoneDevice : null,
                    InputVolume = (float)Math.Clamp(o.MicVolume, 0, 1),
                    OutputVolume = (float)Math.Clamp(o.SystemVolume, 0, 1),
                },
                MouseOptions = new MouseOptions { IsMousePointerEnabled = o.ShowCursor, IsMouseClicksDetected = o.HighlightClicks },
            };
            if (sourceRect != null) opts.OutputOptions.SourceRect = sourceRect;
            if (o.WebcamEnabled && !UseBubble(req))
            {
                string dev = o.WebcamDevice;
                if (string.IsNullOrEmpty(dev)) { var cams = GetCameras(); if (cams.Count > 0) dev = cams[0].Device; }
                if (!string.IsNullOrEmpty(dev))
                {
                    int size = Math.Clamp(o.WebcamSize, 80, 800);
                    var anchor = o.WebcamCorner switch { WebcamCorner.BottomLeft => Anchor.BottomLeft, WebcamCorner.TopLeft => Anchor.TopLeft, WebcamCorner.TopRight => Anchor.TopRight, _ => Anchor.BottomRight };
                    opts.OverlayOptions = new OverLayOptions
                    {
                        Overlays = new List<RecordingOverlayBase>
                        {
                            new VideoCaptureOverlay { DeviceName = dev, AnchorPoint = anchor, Offset = new ScreenSize(16, 16), Size = new ScreenSize(0, size), Stretch = StretchMode.Uniform }
                        }
                    };
                }
            }

            Release();
            _rec = Recorder.CreateRecorder(opts);
            var ui = Application.Current.Dispatcher;
            var rec = _rec;
            Logger.Info("Recorder: created, output " + CurrentFile);
            rec.OnStatusChanged += (s, e) =>
            {
                Logger.Info("Recorder: status " + e.Status);
                if (e.Status == RecorderStatus.Recording)
                    ui.BeginInvoke(new Action(() => { if (!ReferenceEquals(_rec, rec) || HasStarted) return; HasStarted = true; Started?.Invoke(); }));
            };
            rec.OnRecordingComplete += (s, e) =>
            {
                Logger.Info("Recorder: complete " + e.FilePath + (File.Exists(e.FilePath) ? " (" + new FileInfo(e.FilePath).Length + " bytes)" : " (missing)"));
                ui.BeginInvoke(new Action(() =>
                {
                    IsRecording = false; IsPaused = false; StopExtras();
                    Completed?.Invoke(e.FilePath);
                    if (ReferenceEquals(_rec, rec)) Release(); // dispose only after the file has been finalized and handed off
                }));
            };
            rec.OnRecordingFailed += (s, e) =>
            {
                Logger.Info("Recorder: FAILED " + e.Error + " (file " + e.FilePath + ")");
                ui.BeginInvoke(new Action(() => { IsRecording = false; IsPaused = false; StopExtras(); if (ReferenceEquals(_rec, rec)) Release(); Failed?.Invoke(e.Error); }));
            };
            StartExtras(req);
            _rec.Record(CurrentFile);
            IsRecording = true;
        }

        private void StartGif(RecordingRequest req)
        {
            var o = req.Options;
            Func<Int32Rect?> rect;
            if (req.WindowHandle != IntPtr.Zero)
            {
                var h = req.WindowHandle;
                if (NativeMethods.IsIconic(h)) { NativeMethods.ShowWindow(h, 9); System.Threading.Thread.Sleep(250); }
                rect = () => { var r = NativeMethods.GetWindowBounds(h); return r.Width > 0 ? new Int32Rect(r.Left, r.Top, r.Width, r.Height) : (Int32Rect?)null; };
            }
            else if (req.Region.HasValue) { var r = req.Region.Value; rect = () => r; }
            else
            {
                var scr = System.Windows.Forms.Screen.PrimaryScreen;
                foreach (var sc in System.Windows.Forms.Screen.AllScreens) if (sc.DeviceName == o.MonitorDevice) scr = sc;
                var b = scr.Bounds; rect = () => new Int32Rect(b.X, b.Y, b.Width, b.Height);
            }
            _gif = new GifRecorder(CurrentFile, rect, o.GifFps, o.GifMaxWidth, o.ShowCursor);
            _gif.Completed += f => { IsRecording = false; IsPaused = false; _gif = null; StopExtras(); Completed?.Invoke(f); };
            _gif.Failed += e => { IsRecording = false; IsPaused = false; _gif = null; StopExtras(); Failed?.Invoke(e); };
            StartExtras(req);
            _gif.Start();
            IsRecording = true;
            HasStarted = true; Application.Current?.Dispatcher.BeginInvoke(new Action(() => Started?.Invoke()));
        }

        /// <summary>Change mic / system volume while recording (MP4 only).</summary>
        public void SetVolumes(double mic, double system)
        {
            try { _rec?.GetDynamicOptionsBuilder().SetDynamicAudioOptions(new DynamicAudioOptions { InputVolume = (float)mic, OutputVolume = (float)system }).Apply(); }
            catch (Exception ex) { Logger.Error("SetVolumes", ex); }
        }
        public bool SupportsLiveVolume => _rec != null;

        /// <summary>Raised when a region spanning several monitors had to be clipped to one monitor.</summary>
        public event Action<string> RegionClipped;

        /// <summary>Monitor with the largest overlap with the region (desktop duplication records one output per source).</summary>
        public static System.Windows.Forms.Screen PickScreen(Int32Rect r, out bool spansMonitors)
        {
            var rr = new System.Drawing.Rectangle(r.X, r.Y, r.Width, r.Height);
            System.Windows.Forms.Screen best = System.Windows.Forms.Screen.PrimaryScreen; long bestArea = -1; int hits = 0;
            foreach (var sc in System.Windows.Forms.Screen.AllScreens)
            {
                var i = System.Drawing.Rectangle.Intersect(sc.Bounds, rr);
                long a = (long)i.Width * i.Height;
                if (a > 0) hits++;
                if (a > bestArea) { bestArea = a; best = sc; }
            }
            spansMonitors = hits > 1;
            return best;
        }

        public void Pause() { if (IsRecording && !IsPaused) { _rec?.Pause(); _gif?.Pause(); IsPaused = true; } }
        public void Resume() { if (IsRecording && IsPaused) { _rec?.Resume(); _gif?.Resume(); IsPaused = false; } }
        public void Stop() { Logger.Info("Recorder: Stop requested"); _rec?.Stop(); _gif?.Stop(); }

        /// <summary>Free native Media Foundation / D3D resources held by the recorder.</summary>
        private void Release()
        {
            var r = _rec; _rec = null;
            try { r?.Dispose(); } catch (Exception ex) { Logger.Error("Recorder.Dispose", ex); }
        }
    }
}
