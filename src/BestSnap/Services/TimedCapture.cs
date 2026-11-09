using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using BestSnap.Models;
using BestSnap.Native;

namespace BestSnap.Services
{
    /// <summary>Captures every N seconds, M times (0 = until stopped), saving each shot with its own name template.</summary>
    public sealed class TimedCapture
    {
        private CancellationTokenSource _cts;
        public bool Running => _cts != null;
        public int Taken { get; private set; }
        public int Total { get; private set; }
        public string Folder { get; private set; }
        public event Action StateChanged;
        public event Action<string> Error;

        public static string Expand(string template, int n)
        {
            var now = DateTime.Now;
            string name = Regex.Replace(string.IsNullOrWhiteSpace(template) ? "Timed_{n}" : template, @"\{([\w\-]+)\}", m =>
            {
                var t = m.Groups[1].Value;
                if (Array.IndexOf(ImageExporter.Placeholders, t) < 0) return m.Value;
                return t == "n" ? n.ToString("D4") : t == "date" ? now.ToString("yyyy-MM-dd") : t == "time" ? now.ToString("HH-mm-ss") : now.ToString(t);
            });
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length == 0 ? "Timed" : name;
        }

        /// <param name="region">absolute physical rect for Region mode</param>
        /// <param name="hwnd">window for Window mode</param>
        public void Start(TimedCaptureSettings o, string defaultFolder, Int32Rect? region, IntPtr hwnd)
        {
            Stop();
            Folder = string.IsNullOrWhiteSpace(o.Folder) ? Path.Combine(defaultFolder, "Timed") : o.Folder;
            Directory.CreateDirectory(Folder);
            Taken = 0; Total = Math.Max(0, o.Count);
            var cts = _cts = new CancellationTokenSource();
            int interval = Math.Max(1, o.IntervalSeconds);
            string ext = "." + (string.IsNullOrWhiteSpace(o.Format) ? "png" : o.Format.Trim('.').ToLowerInvariant());
            int q = App.Settings.Current.JpegQuality;
            StateChanged?.Invoke();
            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            Task.Run(async () =>
            {
                int fails = 0;
                while (!cts.IsCancellationRequested && (Total == 0 || Taken < Total))
                {
                    var due = DateTime.UtcNow.AddSeconds(interval);
                    try
                    {
                        BitmapSource img = o.Mode switch
                        {
                            TimedCaptureMode.Region when region.HasValue => CaptureService.CaptureRect(region.Value, o.IncludeCursor),
                            TimedCaptureMode.Window when hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd) => CaptureService.CaptureWindow(hwnd, o.IncludeCursor),
                            TimedCaptureMode.Window => throw new InvalidOperationException(L.T("目标窗口已关闭")),
                            _ => CaptureService.CaptureFullScreen(o.IncludeCursor),
                        };
                        if (!img.IsFrozen) img.Freeze();
                        var baseName = Expand(o.Template, Taken + 1);
                        var path = Path.Combine(Folder, baseName + ext);
                        for (int i = 2; File.Exists(path); i++) path = Path.Combine(Folder, $"{baseName} ({i}){ext}");
                        ImageExporter.Save(img, path, q);
                        Taken++; fails = 0;
                        _ = ui.BeginInvoke(new Action(() => StateChanged?.Invoke()));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("TimedCapture", ex);
                        if (++fails >= 3) { _ = ui.BeginInvoke(new Action(() => Error?.Invoke(ex.Message))); break; }
                    }
                    if (Total != 0 && Taken >= Total) break;
                    var wait = due - DateTime.UtcNow;
                    try { if (wait > TimeSpan.Zero) await Task.Delay(wait, cts.Token); } catch (TaskCanceledException) { break; }
                }
                _ = ui.BeginInvoke(new Action(() => { if (_cts == cts) { _cts = null; StateChanged?.Invoke(); } }));
            });
        }

        public void Stop()
        {
            var c = _cts; _cts = null;
            if (c != null) { c.Cancel(); StateChanged?.Invoke(); }
        }
    }
}
