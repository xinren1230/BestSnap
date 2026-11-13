using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ScreenRecorderLib;
using BestSnap.Models;
using BestSnap.Native;
using BestSnap.Plugins;

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
        readonly CaptureSessionState _session = new CaptureSessionState();
        public bool IsRecording { get; private set; }
        public bool IsPaused { get; private set; }
        public CaptureSessionPhase SessionPhase => _session.Phase;
        public int SessionCycle => _session.Cycle;
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

        private static Int32Rect RecordArea(RecordingRequest req) => ResolveArea(req);

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
            try { foreach (var d in Recorder.GetSystemAudioDevices(AudioDeviceSource.InputDevices)) list.Add((d.DeviceName, d.FriendlyName)); } catch (Exception ex) { Logger.Error("GetMicrophones", ex); }
            return list;
        }

        public static List<(string Device, string Name)> GetSystemOutputs()
        {
            var list = new List<(string, string)>();
            try { foreach (var d in Recorder.GetSystemAudioDevices(AudioDeviceSource.OutputDevices)) list.Add((d.DeviceName, d.FriendlyName)); } catch (Exception ex) { Logger.Error("GetSystemOutputs", ex); }
            return list;
        }

        public static List<MonitorRect> ListMonitors()
        {
            var list = new List<MonitorRect>();
            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                list.Add(new MonitorRect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, s.DeviceName));
            return list;
        }

        /// <summary>Physical-pixel rectangle that will actually be captured (after even-size / monitor clip).</summary>
        public static Int32Rect ResolveArea(RecordingRequest req)
        {
            if (req == null) return new Int32Rect(0, 0, 0, 0);
            return ToInt32(ResolveAreaI(req));
        }

        static IntRect ResolveAreaI(RecordingRequest req)
        {
            var o = req.Options ?? new RecordingSettings();
            if (req.WindowHandle != IntPtr.Zero)
            {
                var r = NativeMethods.GetWindowBounds(req.WindowHandle);
                return RecordRectMath.EvenSize(new IntRect(r.Left, r.Top, Math.Max(0, r.Width), Math.Max(0, r.Height)));
            }
            var monitors = ListMonitors();
            if (req.Region.HasValue)
            {
                var r = req.Region.Value;
                var ir = new IntRect(r.X, r.Y, r.Width, r.Height);
                if (RecordRectMath.FitsOneMonitor(ir, monitors, out int idx) && idx >= 0)
                {
                    var crop = RecordRectMath.MonitorLocalEvenCrop(ir, monitors[idx]);
                    var m = monitors[idx];
                    return new IntRect(m.X + crop.X, m.Y + crop.Y, crop.Width, crop.Height);
                }
                var vcrop = RecordRectMath.VirtualLocalEvenCrop(ir, monitors);
                var vs = RecordRectMath.VirtualScreen(monitors);
                return new IntRect(vs.X + vcrop.X, vs.Y + vcrop.Y, vcrop.Width, vcrop.Height);
            }
            if (o.Source == RecordSourceKind.VirtualScreen)
                return RecordRectMath.EvenSize(RecordRectMath.VirtualScreen(monitors));
            var scr = System.Windows.Forms.Screen.PrimaryScreen;
            foreach (var sc in System.Windows.Forms.Screen.AllScreens)
                if (sc.DeviceName == o.MonitorDevice) scr = sc;
            var b = scr.Bounds;
            return RecordRectMath.EvenSize(new IntRect(b.X, b.Y, b.Width, b.Height));
        }

        static Int32Rect ToInt32(IntRect r) => new Int32Rect(r.X, r.Y, r.Width, r.Height);

        public void Start(RecordingRequest req, string defaultFolder)
        {
            if (IsRecording && (_rec != null || _gif != null))
            {
                Logger.Info("Recorder start ignored: already recording phase=" + _session.Phase);
                return;
            }
            if (IsRecording)
            {
                Logger.Info("Recorder stuck IsRecording with no backend; resetting");
                IsRecording = false; IsPaused = false; HasStarted = false;
                _session.Reset("stuck");
            }
            if (!_session.TryBeginStart(out string why))
            {
                if (IsRecording && (_rec != null || _gif != null))
                {
                    Logger.Info("Recorder start ignored: " + why);
                    return;
                }
                Logger.Info("Recorder start recovering " + why);
                _session.Reset("recover");
                if (!_session.TryBeginStart(out why))
                {
                    Logger.Info("Recorder start ignored: " + why);
                    return;
                }
            }
            Release();
            _gif = null;
            Logger.Info("Recorder session Starting cycle=" + _session.Cycle);
            HasStarted = false;
            try
            {
            var o = req.Options;
            bool gif = o.Format == RecordFormat.Gif;
            if (!string.IsNullOrWhiteSpace(req.OutputPath))
                CurrentFile = req.OutputPath;
            else
            {
                string folder = TempRecordingStore.EnsureFolder(AppPaths.LocalData);
                CurrentFile = TempRecordingStore.NewMediaPath(folder, "Rec", gif ? ".gif" : ".mp4", DateTime.UtcNow);
            }
            var outDir = Path.GetDirectoryName(CurrentFile); if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
            if (gif) { StartGif(req); return; }

            var sources = new List<RecordingSourceBase>();
            ScreenRect sourceRect = null;
            if (req.WindowHandle != IntPtr.Zero)
                sources.Add(new WindowRecordingSource(req.WindowHandle) { IsCursorCaptureEnabled = o.ShowCursor });
            else if (req.Region.HasValue)
            {
                var r = req.Region.Value;
                var ir = new IntRect(r.X, r.Y, r.Width, r.Height);
                var monitors = ListMonitors();
                if (RecordRectMath.FitsOneMonitor(ir, monitors, out int idx) && idx >= 0)
                {
                    var m = monitors[idx];
                    sources.Add(new DisplayRecordingSource(m.DeviceName) { IsCursorCaptureEnabled = o.ShowCursor });
                    var c = RecordRectMath.MonitorLocalEvenCrop(ir, m);
                    sourceRect = new ScreenRect(c.X, c.Y, c.Width, c.Height);
                }
                else
                {
                    foreach (var m in monitors)
                        sources.Add(new DisplayRecordingSource(m.DeviceName) { IsCursorCaptureEnabled = o.ShowCursor });
                    var c = RecordRectMath.VirtualLocalEvenCrop(ir, monitors);
                    sourceRect = new ScreenRect(c.X, c.Y, c.Width, c.Height);
                    if (RecordRectMath.PickMonitorIndex(ir, monitors, out bool spans) >= 0 && spans)
                        RegionClipped?.Invoke("virtual");
                }
            }
            else if (o.Source == RecordSourceKind.VirtualScreen)
            {
                foreach (var m in ListMonitors())
                    sources.Add(new DisplayRecordingSource(m.DeviceName) { IsCursorCaptureEnabled = o.ShowCursor });
                if (sources.Count == 0)
                    sources.Add(new DisplayRecordingSource(DisplayRecordingSource.MainMonitor.DeviceName) { IsCursorCaptureEnabled = o.ShowCursor });
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
                    Framerate = RecordRectMath.NormalizeFps(o.Fps > 0 ? o.Fps : 30), Quality = Math.Clamp(o.Quality, 1, 100), IsHardwareEncodingEnabled = true,
                    Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.High }
                },
                AudioOptions = new AudioOptions
                {
                    IsAudioEnabled = o.SystemAudio || o.Microphone,
                    IsOutputDeviceEnabled = o.SystemAudio,
                    IsInputDeviceEnabled = o.Microphone,
                    AudioInputDevice = o.Microphone && !string.IsNullOrWhiteSpace(o.MicrophoneDevice) ? o.MicrophoneDevice : null,
                    AudioOutputDevice = o.SystemAudio && !string.IsNullOrWhiteSpace(o.SystemAudioDevice) ? o.SystemAudioDevice : null,
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
                    IsRecording = false; IsPaused = false; HasStarted = false; StopExtras();
                    _session.MarkFinished();
                    Logger.Info("Recorder session Finished cycle=" + _session.Cycle);
                    Completed?.Invoke(e.FilePath);
                    if (ReferenceEquals(_rec, rec)) Release(); // dispose only after the file has been finalized and handed off
                }));
            };
            rec.OnRecordingFailed += (s, e) =>
            {
                Logger.Info("Recorder: FAILED " + e.Error + " (file " + e.FilePath + ")");
                ui.BeginInvoke(new Action(() =>
                {
                    IsRecording = false; IsPaused = false; HasStarted = false; StopExtras();
                    _session.MarkFailed();
                    Logger.Info("Recorder session Failed cycle=" + _session.Cycle);
                    if (ReferenceEquals(_rec, rec)) Release();
                    Failed?.Invoke(e.Error);
                }));
            };
            StartExtras(req);
            _rec.Record(CurrentFile);
            IsRecording = true;
            _session.TryMarkRunning(out _);
            Logger.Info("Recorder session Running cycle=" + _session.Cycle);
            }
            catch
            {
                IsRecording = false; IsPaused = false; HasStarted = false;
                try { Release(); } catch { }
                _gif = null;
                StopExtras();
                _session.MarkFailed();
                Logger.Info("Recorder session Failed cycle=" + _session.Cycle + " start-throw");
                throw;
            }
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
            else if (req.Region.HasValue)
            {
                var area = ResolveArea(req);
                rect = () => area;
            }
            else if (o.Source == RecordSourceKind.VirtualScreen)
            {
                var vs = CaptureService.VirtualScreen;
                rect = () => vs;
            }
            else
            {
                var scr = System.Windows.Forms.Screen.PrimaryScreen;
                foreach (var sc in System.Windows.Forms.Screen.AllScreens) if (sc.DeviceName == o.MonitorDevice) scr = sc;
                var b = scr.Bounds; rect = () => new Int32Rect(b.X, b.Y, b.Width, b.Height);
            }
            _gif = new GifRecorder(CurrentFile, rect, o.GifFps, o.GifMaxWidth, o.ShowCursor);
            _gif.Completed += f =>
            {
                IsRecording = false; IsPaused = false; HasStarted = false; _gif = null; StopExtras();
                _session.MarkFinished();
                Logger.Info("Recorder session Finished cycle=" + _session.Cycle + " gif");
                Completed?.Invoke(f);
            };
            _gif.Failed += e =>
            {
                IsRecording = false; IsPaused = false; HasStarted = false; _gif = null; StopExtras();
                _session.MarkFailed();
                Logger.Info("Recorder session Failed cycle=" + _session.Cycle + " gif");
                Failed?.Invoke(e);
            };
            StartExtras(req);
            _gif.Start();
            IsRecording = true;
            HasStarted = true;
            _session.TryMarkRunning(out _);
            Logger.Info("Recorder session Running cycle=" + _session.Cycle + " gif");
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => Started?.Invoke()));
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
            var monitors = ListMonitors();
            int i = RecordRectMath.PickMonitorIndex(new IntRect(r.X, r.Y, r.Width, r.Height), monitors, out spansMonitors);
            if (i < 0) return System.Windows.Forms.Screen.PrimaryScreen;
            string name = monitors[i].DeviceName;
            foreach (var sc in System.Windows.Forms.Screen.AllScreens)
                if (sc.DeviceName == name) return sc;
            return System.Windows.Forms.Screen.PrimaryScreen;
        }

        public void Pause()
        {
            if (!_session.TryPause(out string why)) { Logger.Info("Recorder pause ignored: " + why); return; }
            _rec?.Pause(); _gif?.Pause(); IsPaused = true;
            Logger.Info("Recorder session Paused");
        }
        public void Resume()
        {
            if (!_session.TryResume(out string why)) { Logger.Info("Recorder resume ignored: " + why); return; }
            _rec?.Resume(); _gif?.Resume(); IsPaused = false;
            Logger.Info("Recorder session Running");
        }
        public void Stop()
        {
            if (!_session.TryBeginStop(out string why))
            {
                if (!IsRecording && _rec == null && _gif == null)
                {
                    Logger.Info("Recorder stop ignored: " + why);
                    return;
                }
                Logger.Info("Recorder stop leftover (" + why + ")");
            }
            Logger.Info("Recorder session Stopping cycle=" + _session.Cycle);
            _rec?.Stop();
            _gif?.Stop();
        }

        /// <summary>Free native Media Foundation / D3D resources held by the recorder.</summary>
        private void Release()
        {
            var r = _rec; _rec = null;
            try { r?.Dispose(); } catch (Exception ex) { Logger.Error("Recorder.Dispose", ex); }
        }
    }
}
