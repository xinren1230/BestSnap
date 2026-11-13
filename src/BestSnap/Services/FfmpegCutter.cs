using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Runs ffmpeg (if present on PATH or next to the exe) to cut/concat keep-segments.</summary>
    public static class FfmpegCutter
    {
        static string _cached;

        public static string Find()
        {
            if (_cached != null) return _cached.Length == 0 ? null : _cached;
            string env = Environment.GetEnvironmentVariable("BESTSNAP_FFMPEG") ?? Environment.GetEnvironmentVariable("FFMPEG");
            var names = new[] { "ffmpeg.exe", "ffmpeg" };
            var hits = new List<string>();
            if (!string.IsNullOrWhiteSpace(env)) hits.Add(env);
            foreach (var n in names)
            {
                hits.Add(Path.Combine(AppContext.BaseDirectory, n));
                hits.Add(Path.Combine(AppPaths.LocalData, "ffmpeg", n));
                try { hits.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", n)); } catch { }
                try { hits.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", n)); } catch { }
            }
            foreach (var p in hits)
            {
                try { if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) { _cached = p; return p; } } catch { }
            }
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var n in names)
                {
                    try
                    {
                        var p = Path.Combine(dir.Trim().Trim('"'), n);
                        if (File.Exists(p)) { _cached = p; return p; }
                    }
                    catch { }
                }
            }
            _cached = "";
            return null;
        }

        public static bool Available => Find() != null;

        public static async Task ExtractWav16kAsync(string input, string output, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
                throw new FileNotFoundException(input);
            var ff = Find() ?? throw new InvalidOperationException(L.T("未找到 ffmpeg。请安装 ffmpeg 并将其加入 PATH 后再剪辑 MP4/MP3。"));
            var dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var args = new List<string> { "-y", "-i", input, "-vn", "-ac", "1", "-ar", "16000", "-f", "wav", output };
            await RunAsync(ff, args, ct).ConfigureAwait(false);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException(L.T("未能从录音提取音频。"));
        }

        static async Task RunAsync(string ff, List<string> args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ff,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = new Process { StartInfo = psi };
            var err = new StringBuilder();
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) err.AppendLine(e.Data); };
            if (!p.Start()) throw new InvalidOperationException("ffmpeg start failed");
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
                await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0)
                throw new InvalidOperationException("ffmpeg: " + err.ToString().Trim());
        }

        public static async Task CutAsync(string input, string output, IReadOnlyList<TimeRange> keep, bool video, bool audio, IProgress<double> progress, CancellationToken ct)
        {
            if (keep == null || keep.Count == 0) throw new InvalidOperationException(L.T("没有可保留的片段。"));
            var ff = Find() ?? throw new InvalidOperationException(L.T("未找到 ffmpeg。请安装 ffmpeg 并将其加入 PATH 后再剪辑 MP4/MP3。"));
            var dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var args = TrimRangeMath.BuildArgs(input, output, keep, video, audio);
            // -progress pipe:1 for a rough percent (out_time_ms / duration)
            args.Insert(0, "pipe:1");
            args.Insert(0, "-progress");
            var psi = new ProcessStartInfo
            {
                FileName = ff,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var err = new StringBuilder();
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) err.AppendLine(e.Data); };
            if (!p.Start()) throw new InvalidOperationException("ffmpeg start failed");
            p.BeginErrorReadLine();
            var stdout = p.StandardOutput;
            _ = Task.Run(async () =>
            {
                try
                {
                    string line;
                    while ((line = await stdout.ReadLineAsync()) != null)
                    {
                        if (line.StartsWith("out_time_ms=", StringComparison.Ordinal) && long.TryParse(line.Substring(12), out long ms))
                        {
                            double total = TrimRangeMath.TotalDuration(keep) * 1000.0;
                            if (total > 0) progress?.Report(Math.Clamp(ms / 1000.0 / (total / 1000.0), 0, 0.99));
                        }
                    }
                }
                catch { }
            }, ct);
            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
            {
                await p.WaitForExitAsync(ct);
            }
            if (p.ExitCode != 0)
                throw new InvalidOperationException("ffmpeg: " + err.ToString().Trim());
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidOperationException(L.T("剪辑未生成文件。"));
            progress?.Report(1);
        }
    }

    public static class ShellUtil
    {
        public static void RevealInExplorer(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
            try { Process.Start("explorer.exe", "/select,\"" + file + "\""); }
            catch (Exception ex) { Logger.Error("Reveal", ex); }
        }

        public static void OpenFile(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Error("OpenFile", ex); }
        }
    }
}
