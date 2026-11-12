using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BestSnap.Plugins;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace BestSnap.Services
{
    /// <summary>
    /// Offline speech-to-text via Whisper.net (whisper.cpp, MIT). Models live under
    /// %LocalAppData%\BestSnap\models\whisper and are downloaded on first use (resume + SHA-256).
    /// Audio is processed from memory (16 kHz mono); any 16 kHz extract file is deleted afterwards.
    /// </summary>
    public static class WhisperTranscriber
    {
        const int DownloadAttempts = 3;
        static readonly SemaphoreSlim RunLock = new SemaphoreSlim(1, 1);
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        static readonly object FactoryGate = new object();
        static WhisperFactory _factory;
        static string _factoryPath;

        public static string ModelDir => AppPaths.WhisperModels;

        public static string ModelPath(string id) =>
            Path.Combine(ModelDir, WhisperModels.FileName(id));

        public static bool ModelReady(string id) =>
            WhisperModels.FileLooksComplete(ModelPath(id), WhisperModels.Get(id));

        public static async Task WarmupAsync(string id, IProgress<double> progress, CancellationToken ct)
        {
            id = WhisperModels.Normalize(id);
            string path = ModelPath(id);
            Logger.Info("Whisper warmup begin model=" + id);
            await EnsureModelAsync(id, progress, ct).ConfigureAwait(false);
            long bytes = 0;
            try { if (File.Exists(path)) bytes = new FileInfo(path).Length; } catch { }
            Logger.Info($"Whisper model file ready path={path} bytes={bytes}");
            EnsureNativePath();
            GetFactory(path);
            Logger.Info("Whisper native factory loaded " + path);
        }

        public static bool DeleteModel(string id)
        {
            id = WhisperModels.Normalize(id);
            string path = ModelPath(id);
            bool any = DeleteIfExists(path) | DeleteIfExists(path + ".part");
            lock (FactoryGate)
            {
                if (_factory != null && string.Equals(_factoryPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    try { _factory.Dispose(); } catch { }
                    _factory = null;
                    _factoryPath = null;
                }
            }
            return any;
        }

        public static int DeleteAllModels()
        {
            int n = 0;
            if (!Directory.Exists(ModelDir)) return 0;
            foreach (var m in WhisperModels.All)
                if (DeleteModel(m.Id)) n++;
            try
            {
                foreach (var f in Directory.GetFiles(ModelDir, "ggml-*.bin*"))
                {
                    try { File.Delete(f); n++; } catch { }
                }
            }
            catch { }
            return n;
        }

        public static async Task EnsureModelAsync(string id, IProgress<double> progress, CancellationToken ct)
        {
            id = WhisperModels.Normalize(id);
            var info = WhisperModels.Get(id);
            string path = ModelPath(id);
            if (WhisperModels.FileLooksComplete(path, info))
            {
                progress?.Report(1);
                return;
            }
            Directory.CreateDirectory(ModelDir);
            Exception last = null;
            for (int attempt = 1; attempt <= DownloadAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await DownloadOnceAsync(info, path, progress, ct).ConfigureAwait(false);
                    if (!WhisperModels.FileLooksComplete(path, info))
                        throw new InvalidDataException("size mismatch after download of " + info.FileName);
                    if (!string.IsNullOrEmpty(info.Sha256) && !WhisperModels.Sha256Matches(path, info.Sha256))
                    {
                        try { File.Delete(path); } catch { }
                        throw new InvalidDataException("SHA-256 mismatch for " + info.FileName);
                    }
                    progress?.Report(1);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    Logger.Error("Whisper.Download." + attempt, ex);
                    if (attempt < DownloadAttempts)
                        await Task.Delay(1000 * attempt, ct).ConfigureAwait(false);
                }
            }
            throw last ?? new InvalidOperationException("model download failed");
        }

        static async Task DownloadOnceAsync(WhisperModelInfo info, string path, IProgress<double> progress, CancellationToken ct)
        {
            string tmp = path + ".part";
            long expected = info.SizeBytes;
            long offset = WhisperModels.ResumeOffset(tmp, expected);
            if (offset == 0)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }

            string url = WhisperModels.DownloadUrl(info.Id);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "BestSnap");
            if (offset > 0)
                req.Headers.Range = new RangeHeaderValue(offset, null);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (offset > 0 && resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                try { File.Delete(tmp); } catch { }
                offset = 0;
                using var retry = new HttpRequestMessage(HttpMethod.Get, url);
                retry.Headers.TryAddWithoutValidation("User-Agent", "BestSnap");
                using var resp2 = await Http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp2.EnsureSuccessStatusCode();
                await CopyResponseAsync(resp2, tmp, 0, expected, progress, ct).ConfigureAwait(false);
            }
            else
            {
                resp.EnsureSuccessStatusCode();
                bool append = offset > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
                if (!append) offset = 0;
                await CopyResponseAsync(resp, tmp, offset, expected, progress, ct).ConfigureAwait(false);
            }

            if (File.Exists(path)) try { File.Delete(path); } catch { }
            File.Move(tmp, path);
        }

        static async Task CopyResponseAsync(HttpResponseMessage resp, string tmp, long offset, long expected, IProgress<double> progress, CancellationToken ct)
        {
            if (resp.Content.Headers.ContentLength is long remaining && remaining > 0 && expected <= 0)
                expected = offset + remaining;
            var range = resp.Content.Headers.ContentRange;
            if (range?.Length is long total && total > 0) expected = total;

            FileMode mode = offset > 0 ? FileMode.Append : FileMode.Create;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(tmp, mode, FileAccess.Write, FileShare.None, 64 * 1024, true);
            var buf = new byte[64 * 1024];
            long written = offset;
            int n;
            while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                written += n;
                if (expected > 0) progress?.Report(Math.Min(0.99, written / (double)expected));
            }
            await dst.FlushAsync(ct).ConfigureAwait(false);
        }

        public static async Task<List<TranscriptCue>> TranscribeFileAsync(string media, string modelId, IProgress<double> progress, CancellationToken ct) =>
            await TranscribeFileAsync(media, modelId, null, progress, ct).ConfigureAwait(false);

        public static async Task<List<TranscriptCue>> TranscribeFileAsync(string media, string modelId, WhisperDecodeSettings decode, IProgress<double> progress, CancellationToken ct)
        {
            short[] pcm = await LoadMono16kAsync(media, progress, ct).ConfigureAwait(false);
            progress?.Report(0.15);
            return await TranscribeSamplesAsync(pcm, modelId, decode, progress, ct).ConfigureAwait(false);
        }

        public static Task<List<TranscriptCue>> TranscribeSamplesAsync(short[] pcm, string modelId, IProgress<double> progress, CancellationToken ct) =>
            TranscribeSamplesAsync(pcm, modelId, null, progress, ct);

        public static async Task<List<TranscriptCue>> TranscribeSamplesAsync(short[] pcm, string modelId, WhisperDecodeSettings decode, IProgress<double> progress, CancellationToken ct)
        {
            if (pcm == null || pcm.Length < AudioPcm.WhisperRate / 4)
                return new List<TranscriptCue>();
            modelId = WhisperModels.Normalize(modelId);
            await EnsureModelAsync(modelId, progress, ct).ConfigureAwait(false);
            await RunLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var factory = GetFactory(ModelPath(modelId));
                using var processor = BuildProcessor(factory, decode);
                var floats = new float[pcm.Length];
                for (int i = 0; i < pcm.Length; i++) floats[i] = pcm[i] / 32768f;
                var cues = new List<TranscriptCue>();
                await foreach (var seg in processor.ProcessAsync(floats, ct).ConfigureAwait(false))
                {
                    string text = (seg.Text ?? "").Trim();
                    if (text.Length == 0) continue;
                    cues.Add(new TranscriptCue
                    {
                        Start = seg.Start.TotalSeconds,
                        End = seg.End.TotalSeconds,
                        Text = text
                    });
                    processor.Return(seg);
                }
                progress?.Report(1);
                return cues;
            }
            finally { RunLock.Release(); }
        }

        internal static WhisperProcessor BuildProcessor(WhisperFactory factory, WhisperDecodeSettings decode)
        {
            var builder = factory.CreateBuilder();
            string lang = WhisperDecode.NormalizeLanguage(decode?.Language);
            string prompt = WhisperDecode.ComposePrompt(lang, decode?.Prompt, decode?.PreviousText);
            if (!string.IsNullOrEmpty(prompt)) builder.WithPrompt(prompt);
            if (lang == "auto") builder.WithLanguageDetection();
            else builder.WithLanguage(lang);
            try
            {
                var beam = builder.WithBeamSearchSamplingStrategy() as BeamSearchSamplingStrategyBuilder;
                beam?.WithBeamSize(5);
            }
            catch { }
            return builder.Build();
        }

        static WhisperFactory GetFactory(string path)
        {
            lock (FactoryGate)
            {
                if (_factory != null && string.Equals(_factoryPath, path, StringComparison.OrdinalIgnoreCase))
                    return _factory;
                EnsureNativePath();
                _factory?.Dispose();
                Logger.Info("Whisper FromPath " + path);
                string searchRoot = Path.GetDirectoryName(RuntimeOptions.LibraryPath);
                bool gpu = WhisperNativeLayout.HasGpuRuntime(searchRoot);
                Logger.Info("Whisper UseGpu=" + gpu + " searchRoot=" + searchRoot);
                try
                {
                    _factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = gpu });
                }
                catch (Exception ex) when (gpu)
                {
                    Logger.Error("Whisper.Gpu", ex);
                    Logger.Info("Whisper GPU load failed; retrying CPU");
                    _factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
                }
                _factoryPath = path;
                Logger.Info("Whisper factory ready " + path);
                return _factory;
            }
        }

        static bool _nativeReady;

        /// <summary>
        /// Point Whisper.net at {exeDir}/runtimes/win-x64 (or the single-file extract dir).
        /// Must run before the first WhisperFactory is created.
        /// </summary>
        public static void EnsureNativePath()
        {
            if (_nativeReady && !string.IsNullOrEmpty(RuntimeOptions.LibraryPath)) return;
            var candidates = new List<string>();
            void Add(string p) { if (!string.IsNullOrWhiteSpace(p)) candidates.Add(p); }
            Add(AppContext.BaseDirectory);
            try { Add(Path.GetDirectoryName(Environment.ProcessPath)); } catch { }
            try
            {
                var args = Environment.GetCommandLineArgs();
                if (args != null && args.Length > 0) Add(Path.GetDirectoryName(args[0]));
            }
            catch { }
            try
            {
                var nativeSearch = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
                if (!string.IsNullOrEmpty(nativeSearch))
                {
                    foreach (var d in nativeSearch.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                        Add(d);
                }
            }
            catch { }
            try
            {
                var tmp = Path.Combine(Path.GetTempPath(), ".net", AppIdentity.Name);
                if (Directory.Exists(tmp))
                    foreach (var d in Directory.GetDirectories(tmp)) Add(d);
            }
            catch { }
            Add(EmbeddedRuntime.Dir);

            var root = WhisperNativeLayout.ResolveSearchRoot(candidates);
            if (root == null)
            {
                string searched = string.Join("; ", candidates);
                Logger.Error("Whisper.Native", "whisper.dll not found. Searched: " + searched);
                throw new FileNotFoundException(L.T("找不到 Whisper 原生库（whisper.dll）。请重新安装 BestSnap，确保安装目录含 runtimes\\win-x64。") + " " + searched);
            }
            string probe = WhisperNativeLayout.LibraryPathProbe(root);
            RuntimeOptions.LibraryPath = probe;
            Logger.Info("Whisper native LibraryPath=" + probe + " searchRoot=" + root + " gpu=" + WhisperNativeLayout.HasGpuRuntime(root));
            _nativeReady = true;
        }

        static bool DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); return true; }
            }
            catch { }
            return false;
        }

        static async Task<short[]> LoadMono16kAsync(string media, IProgress<double> progress, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(media) || !File.Exists(media))
                throw new FileNotFoundException(media);
            string ext = Path.GetExtension(media);
            if (string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
            {
                var pcm = AudioPcm.Mono16kFromWavFile(media);
                if (pcm.Length > 0) return pcm;
            }
            if (!FfmpegCutter.Available)
                throw new InvalidOperationException(L.T("转写需要 16 kHz 单声道 WAV。请安装 ffmpeg 并加入 PATH，以便从 MP4/MP3 提取音频。"));
            string wav = media + ".16k.wav";
            try
            {
                progress?.Report(0.05);
                await FfmpegCutter.ExtractWav16kAsync(media, wav, ct).ConfigureAwait(false);
                var pcm = AudioPcm.Mono16kFromWavFile(wav);
                if (pcm.Length == 0) throw new InvalidOperationException(L.T("未能从录音提取音频。"));
                return pcm;
            }
            finally
            {
                try { if (File.Exists(wav)) File.Delete(wav); } catch { }
            }
        }
    }
}
