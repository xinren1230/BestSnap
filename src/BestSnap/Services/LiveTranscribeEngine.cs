using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Headless live-sim: same ToMono16k / VAD / TranscribeChunk / Stitch path as captions.</summary>
    public static class LiveTranscribeEngine
    {
        public static async Task<List<TranscriptCue>> RunFileAsync(
            string media, string modelId, WhisperBackend? forceBackend, WhisperDecodeSettings decode,
            bool dumpWav, Action<string> log, CancellationToken ct)
        {
            decode ??= new WhisperDecodeSettings();
            decode.Live = true;
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
            log?.Invoke($"pcm samples={pcm.Length} rate={AudioPcm.WhisperRate} dur={pcm.Length / (double)AudioPcm.WhisperRate:F2}s rms={AudioPcm.Rms(pcm):F4}");
            var result = await LiveTranscribePipeline.RunAsync(pcm, async (chunk, prev, token) =>
            {
                var d = new WhisperDecodeSettings
                {
                    Language = decode.Language,
                    Prompt = decode.Prompt,
                    PreviousText = prev,
                    Live = true
                };
                return await WhisperTranscriber.TranscribeSamplesAsync(chunk.Pcm, id, backend, d, null, token).ConfigureAwait(false);
            }, log, dumpWav ? (i, samples) => WhisperDebugChunks.Write(i, samples) : null, ct).ConfigureAwait(false);
            log?.Invoke($"live-sim processed={result.Processed} skipped={result.Skipped} cues={result.Cues.Count}");
            return result.Cues;
        }
    }
}
