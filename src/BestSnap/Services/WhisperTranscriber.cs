using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BestSnap.Plugins;
using Whisper.net;
using Whisper.net.Ggml;

namespace BestSnap.Services
{
    /// <summary>
    /// Offline speech-to-text via Whisper.net (whisper.cpp, MIT). Models live under
    /// %LocalAppData%\BestSnap\models\whisper and are downloaded on first use.
    /// Audio is processed from memory (16 kHz mono); any 16 kHz extract file is deleted afterwards.
    /// </summary>
    public static class WhisperTranscriber
    {
        static readonly SemaphoreSlim RunLock = new SemaphoreSlim(1, 1);
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        static readonly object FactoryGate = new object();
        static WhisperFactory _factory;
        static string _factoryPath;

        public static string ModelDir => AppPaths.WhisperModels;

        public static string ModelPath(string id) =>
            Path.Combine(ModelDir, WhisperModels.FileName(id));

        public static bool ModelReady(string id) => File.Exists(ModelPath(id)) && new FileInfo(ModelPath(id)).Length > 1_000_000;

        public static GgmlType ToGgml(string id) => WhisperModels.Normalize(id) switch
        {
            "tiny" => GgmlType.Tiny,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            _ => GgmlType.Base
        };

        public static async Task EnsureModelAsync(string id, IProgress<double> progress, CancellationToken ct)
        {
            id = WhisperModels.Normalize(id);
            string path = ModelPath(id);
            if (ModelReady(id)) { progress?.Report(1); return; }
            Directory.CreateDirectory(ModelDir);
            string tmp = path + ".part";
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            long expected = WhisperModels.SizeBytes(id);
            Stream src = null;
            try
            {
                src = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ToGgml(id), QuantizationType.NoQuantization, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error("Whisper.DownloadGgml", ex);
                src = null;
            }
            if (src == null)
            {
                string url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + WhisperModels.FileName(id);
                var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength is long len && len > 0) expected = len;
                src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            }
            await using (src)
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                var buf = new byte[64 * 1024];
                long written = 0;
                int n;
                while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    written += n;
                    if (expected > 0) progress?.Report(Math.Min(0.99, written / (double)expected));
                }
                await dst.FlushAsync(ct).ConfigureAwait(false);
            }
            if (File.Exists(path)) try { File.Delete(path); } catch { }
            File.Move(tmp, path);
            progress?.Report(1);
        }

        public static async Task<List<TranscriptCue>> TranscribeFileAsync(string media, string modelId, IProgress<double> progress, CancellationToken ct)
        {
            short[] pcm = await LoadMono16kAsync(media, progress, ct).ConfigureAwait(false);
            progress?.Report(0.15);
            return await TranscribeSamplesAsync(pcm, modelId, progress, ct).ConfigureAwait(false);
        }

        public static async Task<List<TranscriptCue>> TranscribeSamplesAsync(short[] pcm, string modelId, IProgress<double> progress, CancellationToken ct)
        {
            if (pcm == null || pcm.Length < AudioPcm.WhisperRate / 4)
                return new List<TranscriptCue>();
            modelId = WhisperModels.Normalize(modelId);
            await EnsureModelAsync(modelId, progress, ct).ConfigureAwait(false);
            await RunLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var factory = GetFactory(ModelPath(modelId));
                using var processor = factory.CreateBuilder()
                    .WithLanguageDetection()
                    .Build();
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

        static WhisperFactory GetFactory(string path)
        {
            lock (FactoryGate)
            {
                if (_factory != null && string.Equals(_factoryPath, path, StringComparison.OrdinalIgnoreCase))
                    return _factory;
                _factory?.Dispose();
                var opts = new WhisperFactoryOptions { UseGpu = false };
                _factory = WhisperFactory.FromPath(path, opts);
                _factoryPath = path;
                return _factory;
            }
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
