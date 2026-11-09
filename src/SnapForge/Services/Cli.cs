using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using SnapForge.Models;
using SnapForge.Native;
using SnapForge.Plugins;
using SnapForge.Views;

namespace SnapForge.Services
{
    /// <summary>Exit codes returned by command-line invocations.</summary>
    public static class ExitCodes
    {
        public const int Ok = 0, Error = 1, BadArgs = 2, Cancelled = 3, OcrUnavailable = 4, Busy = 5;
    }

    public sealed class CliOptions
    {
        public string Source;          // fullscreen | window | region | interactive
        public Int32Rect Region;
        public int Delay;
        public string Save, Format, Ocr, Out, Latex, Image;
        public bool Clipboard, Record;
        public int Duration;

        public static bool IsCommand(string[] args) => args.Any(a => a.StartsWith("--") && a != "--tray");

        /// <exception cref="ArgumentException">bad arguments</exception>
        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            string Next(ref int i, string name) { if (i + 1 >= args.Length) throw new ArgumentException(name + " needs a value"); return args[++i]; }
            int Int(string v, string name) { if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0) throw new ArgumentException(name + ": invalid number '" + v + "'"); return n; }
            void Src(string s) { if (o.Source != null) throw new ArgumentException("only one of --fullscreen/--window/--region/--interactive-region"); o.Source = s; }
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i].ToLowerInvariant();
                switch (a)
                {
                    case "--tray": break;
                    case "--fullscreen": Src("fullscreen"); break;
                    case "--window": Src("window"); break;
                    case "--interactive-region": Src("interactive"); break;
                    case "--region":
                        Src("region");
                        var p = Next(ref i, a).Split(',');
                        if (p.Length != 4) throw new ArgumentException("--region expects x,y,w,h");
                        int x = int.Parse(p[0], CultureInfo.InvariantCulture), y = int.Parse(p[1], CultureInfo.InvariantCulture), w = Int(p[2], a), h = Int(p[3], a);
                        if (w < 1 || h < 1) throw new ArgumentException("--region: width/height must be > 0");
                        o.Region = new Int32Rect(x, y, w, h); break;
                    case "--delay": o.Delay = Int(Next(ref i, a), a); break;
                    case "--save": o.Save = Next(ref i, a); break;
                    case "--clipboard": o.Clipboard = true; break;
                    case "--format":
                        o.Format = Next(ref i, a).ToLowerInvariant().TrimStart('.');
                        if (o.Format == "jpeg") o.Format = "jpg";
                        if (o.Format != "png" && o.Format != "jpg") throw new ArgumentException("--format must be png or jpg");
                        break;
                    case "--ocr": o.Ocr = Next(ref i, a); break;
                    case "--latex": o.Latex = Next(ref i, a); break;
                    case "--image": Src("image"); o.Image = Next(ref i, a); break;
                    case "--record": o.Record = true; break;
                    case "--duration": o.Duration = Int(Next(ref i, a), a); break;
                    case "--out": o.Out = Next(ref i, a); break;
                    default: throw new ArgumentException("unknown argument: " + args[i]);
                }
            }
            o.Source ??= "fullscreen";
            if (o.Record)
            {
                if (o.Duration < 1) throw new ArgumentException("--record needs --duration s (>= 1)");
                if (string.IsNullOrWhiteSpace(o.Out)) throw new ArgumentException("--record needs --out file.mp4|.gif");
            }
            else if (o.Duration > 0 || o.Out != null) throw new ArgumentException("--duration/--out are only valid with --record");
            return o;
        }
    }

    /// <summary>Executes a command line inside the running (or a temporary) SnapForge instance. Must run on the UI thread.</summary>
    public static class CliRunner
    {
        public const string PipeName = AppIdentity.CliPipeName;
        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public static async Task<int> RunAsync(string[] args)
        {
            CliOptions o;
            try { o = CliOptions.Parse(args); }
            catch (Exception ex) { Logger.Info("CLI bad args: " + ex.Message); return ExitCodes.BadArgs; }
            if (!await _gate.WaitAsync(0)) return ExitCodes.Busy;
            try { return o.Record ? await RecordAsync(o) : await CaptureAsync(o); }
            catch (Exception ex) { Logger.Error("CLI", ex); return ExitCodes.Error; }
            finally { _gate.Release(); }
        }

        private static async Task<(BitmapSource img, Int32Rect? rect, IntPtr hwnd)> Acquire(CliOptions o, bool forRecording)
        {
            if (o.Delay > 0) await Task.Delay(o.Delay * 1000);
            var S = App.Settings.Current;
            switch (o.Source)
            {
                case "window":
                    var hw = WindowEnumerator.ResolveTargetWindow();
                    if (hw == IntPtr.Zero) return (null, null, IntPtr.Zero);
                    return (forRecording ? null : CaptureService.CaptureWindow(hw, S.IncludeCursor), null, hw);
                case "region":
                    return (forRecording ? null : CaptureService.CaptureRect(o.Region, S.IncludeCursor), o.Region, IntPtr.Zero);
                case "interactive":
                    var ov = new SelectionOverlay(SelectionKind.Rectangle);
                    ov.ShowDialog();
                    if (!ov.ResultRect.HasValue) return (null, null, IntPtr.Zero);
                    var r = ov.ResultRect.Value; var vs = CaptureService.VirtualScreen;
                    return (ov.Result, new Int32Rect(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height), IntPtr.Zero);
                case "image":
                    if (forRecording) throw new ArgumentException("--image cannot be recorded");
                    return (SnapForge.Views.ImagePicker.LoadFile(Path.GetFullPath(Environment.ExpandEnvironmentVariables(o.Image.Trim('"')))), null, IntPtr.Zero);
                default:
                    return (forRecording ? null : CaptureService.CaptureFullScreen(S.IncludeCursor), null, IntPtr.Zero);
            }
        }

        private static async Task<int> CaptureAsync(CliOptions o)
        {
            if (o.Ocr != null && !OcrRunner.Available) return ExitCodes.OcrUnavailable;
            var (img, rect, _) = await Acquire(o, false);
            if (img == null) return ExitCodes.Cancelled;
            var S = App.Settings.Current;
            bool any = false;
            BitmapSource Export() => Watermark.ApplyIfNeeded(img, S);
            if (!string.IsNullOrWhiteSpace(o.Save))
            {
                var path = Environment.ExpandEnvironmentVariables(o.Save.Trim('"'));
                string fmt = o.Format;
                if (Directory.Exists(path) || path.EndsWith("\\") || path.EndsWith("/"))
                {
                    Directory.CreateDirectory(path);
                    var baseName = ImageExporter.ExpandTemplate(S);
                    var ext = "." + (fmt ?? S.DefaultFormat.TrimStart('.'));
                    var p = Path.Combine(path, baseName + ext);
                    for (int i = 2; File.Exists(p); i++) p = Path.Combine(path, $"{baseName} ({i}){ext}");
                    path = p; ImageExporter.CommitCounter(S); App.Settings.Save();
                }
                else
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (fmt != null && ext != "." + fmt && !(fmt == "jpg" && ext == ".jpeg")) path += "." + fmt;
                    else if (ext.Length == 0) path += ".png";
                    var dir = Path.GetDirectoryName(Path.GetFullPath(path)); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                ImageExporter.Save(Export(), Path.GetFullPath(path), S.JpegQuality);
                any = true;
            }
            if (o.Clipboard) { ImageExporter.CopyToClipboard(Export()); any = true; }
            if (o.Ocr != null)
            {
                byte[] png;
                using (var ms = new MemoryStream()) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); enc.Save(ms); png = ms.ToArray(); }
                var text = await Task.Run(() => PluginHost.Ocr.RecognizeAsync(png, S.OcrLanguage, CancellationToken.None));
                var outPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(o.Ocr.Trim('"')));
                var dir = Path.GetDirectoryName(outPath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, text ?? "", new UTF8Encoding(true));
                if (!OcrRunner.UsesBuiltIn(S.OcrLanguage) && !OcrRunner.HasChinese(PluginHost.Ocr)) Logger.Info("OCR: Windows Chinese recognizer not installed (" + OcrRunner.ZhCapability + ")");
                any = true;
            }
            if (o.Latex != null)
            {
                byte[] png;
                using (var ms = new MemoryStream()) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); enc.Save(ms); png = ms.ToArray(); }
                var tex = await FormulaService.RecognizeAsync(png, null, CancellationToken.None);
                var outPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(o.Latex.Trim('"')));
                var dir = Path.GetDirectoryName(outPath); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, tex ?? "", new UTF8Encoding(false));
                any = true;
            }
            if (!any) // no explicit output: normal after-capture handling (only meaningful inside the running app)
            {
                if (App.Controller != null && !App.IsCliOnly) App.Controller.Deliver(img, rect);
                else ImageExporter.CopyToClipboard(img);
            }
            return ExitCodes.Ok;
        }

        private static async Task<int> RecordAsync(CliOptions o)
        {
            var S = App.Settings.Current;
            var outPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(o.Out.Trim('"')));
            bool gif = outPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
            var (_, rect, hwnd) = await Acquire(o, true);
            if (o.Source == "window" && hwnd == IntPtr.Zero) return ExitCodes.Cancelled;
            if (o.Source == "interactive" && !rect.HasValue) return ExitCodes.Cancelled;
            var src = S.Recording ?? new RecordingSettings();
            var opts = new RecordingSettings
            {
                SystemAudio = src.SystemAudio, Microphone = src.Microphone, MicrophoneDevice = src.MicrophoneDevice, ShowCursor = src.ShowCursor, HighlightClicks = src.HighlightClicks,
                Fps = src.Fps, Quality = src.Quality, Countdown = 0, GifFps = src.GifFps, GifMaxWidth = src.GifMaxWidth, SystemVolume = src.SystemVolume, MicVolume = src.MicVolume,
                ShowRegionBorder = false, TrimAfterRecording = false, Format = gif ? RecordFormat.Gif : RecordFormat.Mp4,
                Source = hwnd != IntPtr.Zero ? RecordSourceKind.Window : rect.HasValue ? RecordSourceKind.Region : RecordSourceKind.Monitor,
                SaveFolder = Path.Combine(Path.GetTempPath(), "BestSnapCli"),
            };
            var dir0 = Path.GetDirectoryName(outPath); if (!string.IsNullOrEmpty(dir0)) Directory.CreateDirectory(dir0);
            if (File.Exists(outPath)) File.Delete(outPath);
            var req = new RecordingRequest { Options = opts, Region = rect, WindowHandle = hwnd, OutputPath = outPath };
            var rec = new RecorderService();
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            rec.Started += () => started.TrySetResult(true);
            rec.Completed += f => tcs.TrySetResult(f);
            rec.Failed += e => tcs.TrySetException(new Exception("Recording failed: " + e));
            rec.Start(req, opts.SaveFolder);
            RegionBorderWindow border = null;
            if (rect.HasValue) { border = new RegionBorderWindow(rect.Value); border.Show(); }
            try
            {
                // --duration counts from status Recording, not from Record() (startup can take several seconds)
                var first = await Task.WhenAny(started.Task, tcs.Task, Task.Delay(20000));
                if (first == tcs.Task) await tcs.Task; // failed (throws) or completed early
                else if (first != started.Task)
                {
                    Logger.Info("CLI: recorder did not reach status Recording within 20 s");
                    rec.Stop(); await Task.WhenAny(tcs.Task, Task.Delay(5000));
                    return ExitCodes.Error;
                }
                Logger.Info("CLI: recording started, duration " + o.Duration + " s");
                var finished = await Task.WhenAny(tcs.Task, Task.Delay(o.Duration * 1000));
                if (finished != tcs.Task) rec.Stop();
                var stopTimeout = Task.Delay(30000);
                if (await Task.WhenAny(tcs.Task, stopTimeout) == stopTimeout) { Logger.Info("CLI: recorder did not complete within 30 s after Stop"); return ExitCodes.Error; }
                var file = await tcs.Task; // only OnRecordingComplete gets here; OnRecordingFailed throws -> exit 1
                if (!File.Exists(file) || new FileInfo(file).Length == 0) { Logger.Info("CLI: recorder reported complete but file is empty: " + file); return ExitCodes.Error; }
                return ExitCodes.Ok;
            }
            finally { border?.Close(); }
        }

        // ---------------- named pipe forwarding ----------------

        /// <summary>Client side: send args to the running instance and wait for its exit code. Returns null if no server answered.</summary>
        public static int? TryForward(string[] args)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                pipe.Connect(3000);
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { cwd = Environment.CurrentDirectory, args }));
                var len = BitConverter.GetBytes(payload.Length);
                pipe.Write(len, 0, 4); pipe.Write(payload, 0, payload.Length); pipe.Flush();
                var buf = new byte[4]; int read = 0;
                while (read < 4) { int n = pipe.Read(buf, read, 4 - read); if (n <= 0) return ExitCodes.Error; read += n; }
                return BitConverter.ToInt32(buf, 0);
            }
            catch (TimeoutException) { return null; }
            catch (Exception ex) { Logger.Error("CLI forward", ex); return null; }
        }

        /// <summary>Server side: runs in the primary instance; one client at a time, commands executed on the UI thread.</summary>
        public static void StartServer(System.Windows.Threading.Dispatcher ui, CancellationToken ct)
        {
            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                        await pipe.WaitForConnectionAsync(ct);
                        var lb = new byte[4]; await ReadExact(pipe, lb, ct);
                        int len = BitConverter.ToInt32(lb, 0);
                        if (len <= 0 || len > 1 << 20) continue;
                        var data = new byte[len]; await ReadExact(pipe, data, ct);
                        using var doc = JsonDocument.Parse(data);
                        var cwd = doc.RootElement.GetProperty("cwd").GetString();
                        var args = doc.RootElement.GetProperty("args").EnumerateArray().Select(e => e.GetString()).ToArray();
                        args = ResolvePaths(args, cwd);
                        int code = await await ui.InvokeAsync(() => RunAsync(args));
                        await pipe.WriteAsync(BitConverter.GetBytes(code), 0, 4, ct);
                        await pipe.FlushAsync(ct);
                        try { pipe.WaitForPipeDrain(); } catch { }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { Logger.Error("CLI pipe", ex); await Task.Delay(200); }
                }
            });
        }

        private static async Task ReadExact(Stream s, byte[] buf, CancellationToken ct)
        {
            int r = 0;
            while (r < buf.Length) { int n = await s.ReadAsync(buf, r, buf.Length - r, ct); if (n <= 0) throw new EndOfStreamException(); r += n; }
        }

        /// <summary>Relative --save/--ocr/--out paths are relative to the caller's working directory, not ours.</summary>
        public static string[] ResolvePaths(string[] args, string cwd)
        {
            var r = (string[])args.Clone();
            for (int i = 0; i + 1 < r.Length; i++)
            {
                var a = r[i].ToLowerInvariant();
                if ((a == "--save" || a == "--ocr" || a == "--out" || a == "--latex" || a == "--image") && !string.IsNullOrEmpty(cwd))
                {
                    var p = Environment.ExpandEnvironmentVariables(r[i + 1].Trim('"'));
                    if (!Path.IsPathRooted(p)) r[i + 1] = Path.Combine(cwd, p);
                }
            }
            return r;
        }
    }
}
