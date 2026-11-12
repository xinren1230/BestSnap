using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        static WhisperFactory _gpuFactory;
        static string _gpuPath;
        static WhisperBackend _gpuKind;
        static WhisperFactory _cpuFactory;
        static string _cpuPath;
        static WhisperProcessor _reused;
        static string _reusedKey;
        static int _epoch;

        public static WhisperAutoPick LastPick { get; private set; }

        public static string ModelDir => AppPaths.WhisperModels;

        public static string ModelPath(string id) =>
            Path.Combine(ModelDir, WhisperModels.FileName(WhisperModels.NormalizeWeights(id)));

        public static bool ModelReady(string id)
        {
            if (WhisperModels.IsAuto(id))
            {
                var pick = Resolve(id, live: false);
                return WhisperModels.FileLooksComplete(ModelPath(pick.ModelId), WhisperModels.Get(pick.ModelId));
            }
            return WhisperModels.FileLooksComplete(ModelPath(id), WhisperModels.Get(id));
        }

        public static WhisperAutoPick Resolve(string choice, bool live)
        {
            var hw = WhisperHardwareProbe.Current;
            var pick = WhisperAutoSelect.Resolve(choice, hw, live);
            LastPick = pick;
            Logger.Info("Whisper pick " + pick.Reason + (pick.Manual ? " manual" : " auto") + (live ? " live" : " file"));
            return pick;
        }

        public static async Task WarmupAsync(string id, IProgress<double> progress, CancellationToken ct) =>
            await WarmupAsync(id, live: false, progress, ct).ConfigureAwait(false);

        public static async Task WarmupAsync(string id, bool live, IProgress<double> progress, CancellationToken ct) =>
            await WarmupAsync(id, live, null, progress, ct).ConfigureAwait(false);

        public static async Task WarmupAsync(string id, bool live, WhisperBackend? forceBackend, IProgress<double> progress, CancellationToken ct)
        {
            var pick = Resolve(id, live);
            if (forceBackend is WhisperBackend b)
            {
                pick.Backend = b;
                LastPick = pick;
                Logger.Info("Whisper backend forced " + b);
            }
            await LoadPickAsync(pick, progress, ct).ConfigureAwait(false);
        }

        static async Task LoadPickAsync(WhisperAutoPick pick, IProgress<double> progress, CancellationToken ct)
        {
            string id = pick.ModelId;
            string path = ModelPath(id);
            Logger.Info("Whisper warmup begin model=" + id + " backend=" + pick.Backend);
            await EnsureModelAsync(id, progress, ct).ConfigureAwait(false);
            long bytes = 0;
            try { if (File.Exists(path)) bytes = new FileInfo(path).Length; } catch { }
            Logger.Info($"Whisper model file ready path={path} bytes={bytes}");
            EnsureNativePath();
            try
            {
                GetFactory(path, pick.Backend);
            }
            catch (Exception ex) when (pick.Backend != WhisperBackend.Cpu)
            {
                Logger.Error("Whisper.Gpu", ex);
                string smaller = WhisperAutoSelect.Smaller(id);
                var fallback = WhisperAutoSelect.CpuFallback(pick, smaller);
                LastPick = fallback;
                Logger.Info("Whisper GPU load failed; CPU fallback " + fallback.Reason);
                await EnsureModelAsync(fallback.ModelId, progress, ct).ConfigureAwait(false);
                GetFactory(ModelPath(fallback.ModelId), WhisperBackend.Cpu);
            }
            Logger.Info("Whisper native factory loaded " + (LastPick?.ModelId ?? id));
        }

        public static bool DeleteModel(string id)
        {
            if (WhisperModels.IsAuto(id)) return false;
            id = WhisperModels.NormalizeWeights(id);
            string path = ModelPath(id);
            bool any = DeleteIfExists(path) | DeleteIfExists(path + ".part");
            lock (FactoryGate)
            {
                bool hit = string.Equals(_gpuPath, path, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(_cpuPath, path, StringComparison.OrdinalIgnoreCase);
                if (hit) DisposeFactoriesUnsafe();
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
            if (WhisperModels.IsAuto(id))
                id = Resolve(id, live: false).ModelId;
            id = WhisperModels.NormalizeWeights(id);
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

        public static async Task<List<TranscriptCue>> TranscribeFileAsync(string media, string modelId, WhisperDecodeSettings decode, IProgress<double> progress, CancellationToken ct) =>
            await TranscribeFileAsync(media, modelId, null, decode, progress, ct).ConfigureAwait(false);

        public static async Task<List<TranscriptCue>> TranscribeFileAsync(string media, string modelId, WhisperBackend? forceBackend, WhisperDecodeSettings decode, IProgress<double> progress, CancellationToken ct) =>
            (await TranscribeFileWindowedAsync(media, modelId, forceBackend, decode, progress, null, ct).ConfigureAwait(false)).Cues;

        public static async Task<List<TranscriptCue>> TranscribeFileAsync(string media, string modelId, WhisperDecodeSettings decode, IProgress<double> progress, Action<IReadOnlyList<TranscriptCue>> onCues, CancellationToken ct) =>
            (await TranscribeFileWindowedAsync(media, modelId, null, decode, progress, onCues, ct).ConfigureAwait(false)).Cues;

        public static async Task<LivePipelineResult> TranscribeFileWindowedAsync(
            string media, string modelId, WhisperBackend? forceBackend, WhisperDecodeSettings decode,
            IProgress<double> progress, Action<IReadOnlyList<TranscriptCue>> onCues, CancellationToken ct,
            double stretchMinutes = 0)
        {
            var pick = Resolve(modelId, live: false);
            if (forceBackend is WhisperBackend b)
            {
                pick.Backend = b;
                LastPick = pick;
                Logger.Info("Whisper backend forced " + b);
            }
            await LoadPickAsync(pick, progress, ct).ConfigureAwait(false);
            short[] pcm = await LoadMono16kAsync(media, progress, ct).ConfigureAwait(false);
            if (stretchMinutes > 0)
            {
                pcm = AudioPcm.RepeatWithSilence(pcm, 2, stretchMinutes);
                Logger.Info($"Whisper stretch minutes={stretchMinutes:F1} samples={pcm.Length} dur={pcm.Length / (double)AudioPcm.WhisperRate:F1}s");
            }
            progress?.Report(0.15);
            decode ??= new WhisperDecodeSettings();
            decode.Live = false;
            decode.ReuseProcessor = true;
            return await TranscribePcmWindowedAsync(pcm, pick.ModelId, pick.Backend, decode, progress, onCues, ct, live: false).ConfigureAwait(false);
        }

        public static async Task<LivePipelineResult> TranscribePcmWindowedAsync(
            short[] pcm, string modelId, WhisperBackend backend, WhisperDecodeSettings decode,
            IProgress<double> progress, Action<IReadOnlyList<TranscriptCue>> onCues, CancellationToken ct, bool live)
        {
            decode ??= new WhisperDecodeSettings();
            decode.Live = live;
            decode.ReuseProcessor = true;
            double total = pcm == null || pcm.Length == 0 ? 0 : pcm.Length / (double)AudioPcm.WhisperRate;
            double first = live ? -1 : FileChunkMath.WindowSeconds;
            double window = live ? -1 : FileChunkMath.WindowSeconds;
            double overlap = live ? -1 : FileChunkMath.OverlapSeconds;
            return await LiveTranscribePipeline.RunDetailedAsync(pcm, async (w, prev, token) =>
            {
                var d = new WhisperDecodeSettings
                {
                    Language = decode.Language,
                    Prompt = decode.Prompt,
                    PreviousText = prev,
                    Live = live,
                    ReuseProcessor = true
                };
                var r = await TranscribeSamplesDetailedAsync(w.Pcm, modelId, backend, d, null, token).ConfigureAwait(false);
                if (total > 0)
                    progress?.Report(0.15 + 0.85 * Math.Min(1, (w.AbsStart + w.DurationSeconds) / total));
                return r;
            }, s => Logger.Info((live ? "WhisperLive " : "WhisperFile ") + s), null, ct, first, window, overlap, live, onCues).ConfigureAwait(false);
        }

        public static Task<short[]> LoadPcmAsync(string media, IProgress<double> progress, CancellationToken ct) =>
            LoadMono16kAsync(media, progress, ct);

        public static Task<List<TranscriptCue>> TranscribeSamplesAsync(short[] pcm, string modelId, IProgress<double> progress, CancellationToken ct) =>
            TranscribeSamplesAsync(pcm, modelId, null, progress, ct);

        public static Task<List<TranscriptCue>> TranscribeSamplesAsync(short[] pcm, string modelId, WhisperDecodeSettings decode, IProgress<double> progress, CancellationToken ct) =>
            TranscribeSamplesAsync(pcm, modelId, LastPick?.Backend ?? WhisperBackend.Cpu, decode, progress, ct);

        public static async Task<List<TranscriptCue>> TranscribeSamplesAsync(short[] pcm, string modelId, WhisperBackend backend, WhisperDecodeSettings decode, IProgress<double> progress, CancellationToken ct) =>
            (await TranscribeSamplesDetailedAsync(pcm, modelId, backend, decode, progress, ct).ConfigureAwait(false)).Cues;

        public static async Task<WhisperDecodeResult> TranscribeSamplesDetailedAsync(short[] pcm, string modelId, WhisperBackend backend, WhisperDecodeSettings decode, IProgress<double> progress, CancellationToken ct)
        {
            var empty = new WhisperDecodeResult { Backend = backend, ChunkDuration = pcm == null ? 0 : pcm.Length / (double)AudioPcm.WhisperRate };
            if (pcm == null || pcm.Length < AudioPcm.WhisperRate / 4)
            {
                Logger.Info("Whisper skip short pcm samples=" + (pcm == null ? 0 : pcm.Length));
                return empty;
            }
            if (WhisperModels.IsAuto(modelId))
            {
                var pick = Resolve(modelId, live: decode?.Live == true);
                modelId = pick.ModelId;
                backend = pick.Backend;
            }
            else modelId = WhisperModels.NormalizeWeights(modelId);
            await EnsureModelAsync(modelId, progress, ct).ConfigureAwait(false);
            decode ??= new WhisperDecodeSettings();
            bool live = decode.Live;
            if (live) decode.ReuseProcessor = true;
            double dur = pcm.Length / (double)AudioPcm.WhisperRate;
            double budget = WhisperDecodeBudget.TimeoutSeconds(dur, live);
            empty.ChunkDuration = dur;
            empty.Backend = backend;
            await RunLock.WaitAsync(ct).ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var work = DecodeWithRetriesAsync(pcm, modelId, backend, decode, linked.Token);
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var timeout = Task.Delay(TimeSpan.FromSeconds(budget), delayCts.Token);
                var done = await Task.WhenAny(work, timeout).ConfigureAwait(false);
                if (done != work)
                {
                    Logger.Info($"Whisper TIMEOUT after {sw.Elapsed.TotalSeconds:F2}s budget={budget:F1}s dur={dur:F2}s live={live} backend={backend} model={modelId}");
                    try { linked.Cancel(); } catch { }
                    RecreateBackend(ModelPath(modelId), backend);
                    try { await Task.WhenAny(work, Task.Delay(2000)).ConfigureAwait(false); } catch { }
                    return new WhisperDecodeResult
                    {
                        TimedOut = true,
                        RecreatedFactory = true,
                        ElapsedSeconds = sw.Elapsed.TotalSeconds,
                        ChunkDuration = dur,
                        Backend = backend,
                        RetryPath = "timeout",
                        Epoch = Volatile.Read(ref _epoch)
                    };
                }
                try { delayCts.Cancel(); } catch { }
                var result = await work.ConfigureAwait(false) ?? empty;
                result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
                result.ChunkDuration = dur;
                result.Epoch = Volatile.Read(ref _epoch);
                bool slow = WhisperDecodeBudget.IsSlow(result.ElapsedSeconds, dur, live);
                if (slow)
                {
                    Logger.Info($"Whisper slow decode elapsed={result.ElapsedSeconds:F2}s dur={dur:F2}s live={live} backend={result.Backend} — recreating factory");
                    RecreateBackend(ModelPath(modelId), result.Backend);
                    result.RecreatedFactory = true;
                }
                progress?.Report(1);
                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                RecreateBackend(ModelPath(modelId), backend);
                return new WhisperDecodeResult
                {
                    TimedOut = true,
                    RecreatedFactory = true,
                    ElapsedSeconds = sw.Elapsed.TotalSeconds,
                    ChunkDuration = dur,
                    Backend = backend,
                    RetryPath = "timeout",
                    Epoch = Volatile.Read(ref _epoch)
                };
            }
            finally { RunLock.Release(); }
        }

        static async Task<WhisperDecodeResult> DecodeWithRetriesAsync(short[] pcm, string modelId, WhisperBackend backend, WhisperDecodeSettings decode, CancellationToken ct)
        {
            WhisperFactory factory;
            var sessionBackend = backend;
            try { factory = GetFactory(ModelPath(modelId), backend); }
            catch (Exception ex) when (backend != WhisperBackend.Cpu)
            {
                Logger.Error("Whisper.Gpu", ex);
                string smaller = WhisperAutoSelect.Smaller(modelId);
                var fallback = WhisperAutoSelect.CpuFallback(LastPick ?? new WhisperAutoPick { ModelId = modelId, Backend = backend }, smaller);
                LastPick = fallback;
                Logger.Info("Whisper GPU load failed; CPU fallback " + fallback.Reason);
                await EnsureModelAsync(fallback.ModelId, progress: null, ct).ConfigureAwait(false);
                modelId = fallback.ModelId;
                sessionBackend = WhisperBackend.Cpu;
                factory = GetFactory(ModelPath(modelId), WhisperBackend.Cpu);
            }
            var floats = AudioPcm.ToFloat32(pcm);
            LogFloatStats(floats, pcm.Length);
            var result = await DecodeOnceAsync(factory, floats, pcm, decode, sessionBackend, "float32", ct).ConfigureAwait(false);
            if (result.KeptCues == 0 && !result.TimedOut)
            {
                Logger.Info("Whisper float32 empty; cheap retry 16 kHz PCM WAV backend=" + sessionBackend);
                result = await DecodeOnceAsync(factory, null, pcm, decode, sessionBackend, "wav", ct).ConfigureAwait(false);
                result.RetryPath = "wav";
            }
            if (result.KeptCues == 0 && !result.TimedOut && sessionBackend != WhisperBackend.Cpu)
            {
                bool live = decode?.Live == true;
                bool turbo = (modelId ?? "").IndexOf("turbo", StringComparison.OrdinalIgnoreCase) >= 0;
                if (live || turbo)
                {
                    Logger.Info("Whisper skip CPU re-decode of " + modelId + " live=" + live + " (keeping GPU factory, LastPick unchanged)");
                    result.RetryPath = (result.RetryPath ?? "float32") + "+skip-cpu";
                }
                else
                {
                    Logger.Info("Whisper GPU empty; CPU factory in parallel (GPU kept, LastPick unchanged)");
                    var cpuFactory = GetFactory(ModelPath(modelId), WhisperBackend.Cpu, allowCpuBesideGpu: true);
                    if (cpuFactory != null && !ReferenceEquals(cpuFactory, factory))
                    {
                        result = await DecodeOnceAsync(cpuFactory, floats, pcm, decode, WhisperBackend.Cpu, "float32-cpu", ct).ConfigureAwait(false);
                        result.RetryPath = "float32-cpu";
                    }
                    else
                    {
                        Logger.Info("Whisper CPU side-factory refused; GPU factory kept");
                        result.RetryPath = (result.RetryPath ?? "wav") + "+cpu-refused";
                    }
                }
            }
            result.Backend = sessionBackend;
            return result;
        }

        static void LogFloatStats(float[] floats, int samples)
        {
            if (floats == null || floats.Length == 0)
            {
                Logger.Info("Whisper pcm float32 empty samples=" + samples);
                return;
            }
            float min = floats[0], max = floats[0];
            double acc = 0;
            for (int i = 0; i < floats.Length; i++)
            {
                float v = floats[i];
                if (v < min) min = v;
                if (v > max) max = v;
                acc += (double)v * v;
            }
            double rms = Math.Sqrt(acc / floats.Length);
            Logger.Info($"Whisper pcm float32 n={floats.Length} min={min:F4} max={max:F4} rms={rms:F4} dur={floats.Length / (double)AudioPcm.WhisperRate:F2}s");
            if (max > 1.05f || min < -1.05f)
                Logger.Info("Whisper pcm float32 out of [-1,1] — samples may be int16-as-float");
        }

        static async Task<WhisperDecodeResult> DecodeOnceAsync(WhisperFactory factory, float[] floats, short[] pcm, WhisperDecodeSettings decode, WhisperBackend backend, string path, CancellationToken ct)
        {
            bool own = decode?.ReuseProcessor != true;
            var processor = GetOrBuildProcessor(factory, decode, backend);
            var cues = new List<TranscriptCue>();
            int raw = 0, kept = 0;
            bool timedOut = false;
            MemoryStream wav = null;
            try
            {
                IAsyncEnumerable<SegmentData> segs;
                if (floats != null)
                    segs = processor.ProcessAsync(floats, ct);
                else
                {
                    wav = new MemoryStream();
                    AudioPcm.WriteWav(wav, pcm ?? Array.Empty<short>(), AudioPcm.WhisperRate, 1);
                    wav.Position = 0;
                    Logger.Info($"Whisper WAV stream bytes={wav.Length} rate={AudioPcm.WhisperRate} ch=1 pcm16");
                    segs = processor.ProcessAsync(wav, ct);
                }
                await foreach (var seg in segs.ConfigureAwait(false))
                {
                    raw++;
                    string original = seg.Text ?? "";
                    Logger.Info($"Whisper seg[{raw}] {seg.Start.TotalSeconds:F3}-{seg.End.TotalSeconds:F3} '{WhisperDecode.Trunc(original, 96)}' path={path}");
                    string text = WhisperDecode.NormalizeCueText(original);
                    if (text.Length > 0)
                    {
                        kept++;
                        double start = seg.Start.TotalSeconds;
                        double end = seg.End.TotalSeconds;
                        if (double.IsNaN(start) || double.IsInfinity(start)) start = 0;
                        if (double.IsNaN(end) || double.IsInfinity(end) || end < start) end = start;
                        cues.Add(new TranscriptCue { Start = start, End = end, Text = text });
                    }
                    processor.Return(seg);
                }
            }
            catch (OperationCanceledException)
            {
                timedOut = !ct.IsCancellationRequested;
                if (!timedOut) throw;
                Logger.Info("Whisper decode canceled path=" + path + " (timeout)");
            }
            catch (Exception ex)
            {
                Logger.Error("Whisper.Decode." + path, ex);
            }
            finally
            {
                try { wav?.Dispose(); } catch { }
                if (own)
                {
                    try { processor.Dispose(); } catch { }
                }
            }
            Logger.Info($"Whisper decode done path={path} backend={backend} lang={WhisperDecode.ProcessorLanguage(decode?.Language)} beam={WhisperDecode.PreferBeamSearch(backend, decode?.Live == true)} reuse={!own} raw={raw} kept={kept} timeout={timedOut}");
            return new WhisperDecodeResult
            {
                Cues = cues,
                RawSegments = raw,
                KeptCues = kept,
                Backend = backend,
                RetryPath = path,
                TimedOut = timedOut
            };
        }

        internal static WhisperProcessor BuildProcessor(WhisperFactory factory, WhisperDecodeSettings decode, WhisperBackend backend)
        {
            var builder = factory.CreateBuilder();
            string lang = WhisperDecode.ProcessorLanguage(decode?.Language);
            string prev = decode?.ReuseProcessor == true ? null : decode?.PreviousText;
            string prompt = WhisperDecode.ComposePrompt(lang, decode?.Prompt, prev);
            if (!string.IsNullOrEmpty(prompt)) builder.WithPrompt(prompt);
            // whisper.cpp detect_language=true is detect-ONLY (zero segments). Auto must be WithLanguage("auto").
            builder.WithLanguage(lang);
            bool live = decode?.Live == true;
            if (WhisperDecode.PreferBeamSearch(backend, live))
            {
                try
                {
                    var beam = builder.WithBeamSearchSamplingStrategy() as BeamSearchSamplingStrategyBuilder;
                    beam?.WithBeamSize(5);
                }
                catch { }
            }
            else
            {
                try { builder.WithGreedySamplingStrategy(); }
                catch { }
            }
            Logger.Info("Whisper processor lang=" + lang + " detectOnly=" + WhisperDecode.DetectLanguageOnly + " live=" + live + " backend=" + backend + " reuse=" + (decode?.ReuseProcessor == true));
            return builder.Build();
        }

        static WhisperProcessor GetOrBuildProcessor(WhisperFactory factory, WhisperDecodeSettings decode, WhisperBackend backend)
        {
            if (decode?.ReuseProcessor != true)
                return BuildProcessor(factory, decode, backend);
            string lang = WhisperDecode.ProcessorLanguage(decode?.Language);
            string key = backend + "|" + lang + "|" + (decode.Live ? "live" : "file") + "|" + (decode.Prompt ?? "");
            lock (FactoryGate)
            {
                if (_reused != null && string.Equals(_reusedKey, key, StringComparison.Ordinal))
                    return _reused;
                DisposeProcessorUnsafe();
                _reused = BuildProcessor(factory, decode, backend);
                _reusedKey = key;
                Logger.Info("Whisper processor reused key=" + key);
                return _reused;
            }
        }

        static void RecreateBackend(string path, WhisperBackend backend)
        {
            lock (FactoryGate)
            {
                Interlocked.Increment(ref _epoch);
                DisposeProcessorUnsafe();
                if (backend == WhisperBackend.Cpu)
                {
                    try { _cpuFactory?.Dispose(); } catch { }
                    _cpuFactory = null;
                    _cpuPath = null;
                }
                else
                {
                    try { _gpuFactory?.Dispose(); } catch { }
                    _gpuFactory = null;
                    _gpuPath = null;
                }
                Logger.Info("Whisper factory recreated epoch=" + _epoch + " backend=" + backend);
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    if (backend == WhisperBackend.Cpu && _gpuFactory != null) return;
                    GetFactoryUnsafe(path, backend, recreate: true, allowCpuBesideGpu: backend == WhisperBackend.Cpu && _gpuFactory != null);
                }
                catch (Exception ex) { Logger.Error("Whisper.Recreate", ex); }
            }
        }

        static void DisposeProcessorUnsafe()
        {
            try { _reused?.Dispose(); } catch { }
            _reused = null;
            _reusedKey = null;
        }

        static void DisposeFactoriesUnsafe()
        {
            DisposeProcessorUnsafe();
            try { _gpuFactory?.Dispose(); } catch { }
            try { _cpuFactory?.Dispose(); } catch { }
            _gpuFactory = null;
            _cpuFactory = null;
            _gpuPath = null;
            _cpuPath = null;
        }

        static WhisperFactory GetFactory(string path, WhisperBackend backend, bool allowCpuBesideGpu = false)
        {
            lock (FactoryGate)
                return GetFactoryUnsafe(path, backend, recreate: false, allowCpuBesideGpu);
        }

        static WhisperFactory GetFactoryUnsafe(string path, WhisperBackend backend, bool recreate, bool allowCpuBesideGpu)
        {
            if (backend == WhisperBackend.Cpu)
            {
                if (_gpuFactory != null && !allowCpuBesideGpu)
                {
                    Logger.Info("Whisper refuse CPU factory replace; keeping GPU " + _gpuKind + " " + _gpuPath);
                    return _gpuFactory;
                }
                if (!recreate && _cpuFactory != null && string.Equals(_cpuPath, path, StringComparison.OrdinalIgnoreCase))
                    return _cpuFactory;
                EnsureNativePath();
                DisposeProcessorUnsafe();
                try { _cpuFactory?.Dispose(); } catch { }
                _cpuFactory = null;
                Logger.Info("Whisper FromPath " + path + " backend=Cpu (GPU factory kept=" + (_gpuFactory != null) + ")");
                if (_gpuFactory == null)
                    WhisperHardwareProbe.ApplyBackend(WhisperBackend.Cpu);
                Logger.Info("Whisper UseGpu=False");
                _cpuFactory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
                _cpuPath = path;
                Logger.Info("Whisper CPU factory ready " + path);
                return _cpuFactory;
            }

            if (!recreate && _gpuFactory != null
                && string.Equals(_gpuPath, path, StringComparison.OrdinalIgnoreCase)
                && _gpuKind == backend)
                return _gpuFactory;
            EnsureNativePath();
            DisposeProcessorUnsafe();
            try { _gpuFactory?.Dispose(); } catch { }
            _gpuFactory = null;
            Logger.Info("Whisper FromPath " + path + " backend=" + backend);
            WhisperHardwareProbe.ApplyBackend(backend);
            Logger.Info("Whisper UseGpu=True");
            _gpuFactory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = true });
            _gpuPath = path;
            _gpuKind = backend;
            Logger.Info("Whisper GPU factory ready " + path + " " + backend + " cpuKept=" + (_cpuFactory != null));
            return _gpuFactory;
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
