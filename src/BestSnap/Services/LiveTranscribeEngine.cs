using System;
using System.Threading;
using System.Threading.Tasks;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Headless live-sim: same ToMono16k / VAD / TranscribeChunk / Stitch path as captions.</summary>
    public static class LiveTranscribeEngine
    {
        public static async Task<LivePipelineResult> RunFileAsync(
            string media, string modelId, WhisperBackend? forceBackend, WhisperDecodeSettings decode,
            bool dumpWav, Action<string> log, CancellationToken ct, double stretchMinutes = 0)
        {
            decode ??= new WhisperDecodeSettings();
            decode.Live = true;
            decode.ReuseProcessor = true;
            var pick = WhisperTranscriber.Resolve(modelId, live: true);
            if (forceBackend is WhisperBackend b)
            {
                pick.Backend = b;
                log?.Invoke("backend forced " + b);
            }
            await WhisperTranscriber.WarmupAsync(pick.ModelId, live: true, forceBackend, null, ct).ConfigureAwait(false);
            string id = WhisperTranscriber.LastPick?.ModelId ?? pick.ModelId;
            var backend = forceBackend ?? WhisperTranscriber.LastPick?.Backend ?? WhisperBackend.Cpu;
            log?.Invoke("warmup model=" + id + " backend=" + backend);
            short[] pcm = await WhisperTranscriber.LoadPcmAsync(media, null, ct).ConfigureAwait(false);
            if (stretchMinutes > 0)
            {
                pcm = AudioPcm.RepeatWithSilence(pcm, 2, stretchMinutes);
                log?.Invoke($"stretch minutes={stretchMinutes:F1} samples={pcm.Length} dur={pcm.Length / (double)AudioPcm.WhisperRate:F1}s");
            }
            log?.Invoke($"pcm samples={pcm.Length} rate={AudioPcm.WhisperRate} dur={pcm.Length / (double)AudioPcm.WhisperRate:F2}s rms={AudioPcm.Rms(pcm):F4}");
            var result = await LiveTranscribePipeline.RunDetailedAsync(pcm, async (chunk, prev, token) =>
            {
                var d = new WhisperDecodeSettings
                {
                    Language = decode.Language,
                    Prompt = decode.Prompt,
                    PreviousText = prev,
                    Live = true,
                    ReuseProcessor = true
                };
                return await WhisperTranscriber.TranscribeSamplesDetailedAsync(chunk.Pcm, id, backend, d, null, token).ConfigureAwait(false);
            }, log, dumpWav ? (i, samples) => WhisperDebugChunks.Write(i, samples) : null, ct).ConfigureAwait(false);
            log?.Invoke($"live-sim processed={result.Processed} skipped={result.Skipped} timedOut={result.TimedOut} dropped={result.Dropped} cues={result.Cues.Count} maxDecode={result.MaxDecodeSeconds:F2}s ratio={result.MaxDecodeRatio:F2}");
            return result;
        }
    }
}
