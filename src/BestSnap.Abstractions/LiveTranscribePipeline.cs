using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BestSnap.Plugins
{
    public sealed class LivePcmWindow
    {
        public int Index { get; set; }
        public double AbsStart { get; set; }
        public short[] Pcm { get; set; }
        public bool IsTail { get; set; }
        public double DurationSeconds =>
            Pcm == null || Pcm.Length == 0 ? 0 : Pcm.Length / (double)AudioPcm.WhisperRate;
    }

    public sealed class LivePipelineResult
    {
        public List<TranscriptCue> Cues { get; set; } = new List<TranscriptCue>();
        public int Processed { get; set; }
        public int Skipped { get; set; }
        public int TimedOut { get; set; }
        public int Dropped { get; set; }
        public double CommittedThrough { get; set; }
        public double MaxDecodeSeconds { get; set; }
        public double MaxDecodeRatio { get; set; }
        public double DurationSeconds { get; set; }
        public List<WindowDecodeReport> Reports { get; set; } = new List<WindowDecodeReport>();
    }

    /// <summary>
    /// Same windowing / VAD / stitch as the live caption mixer: first 3.5 s, then 10 s / 2 s overlap.
    /// Transcribe is injected so unit tests can feed speech PCM without native Whisper.
    /// </summary>
    public static class LiveTranscribePipeline
    {
        public static List<LivePcmWindow> SlicePcm(short[] pcm, int sampleRate = AudioPcm.WhisperRate, bool includeTail = true,
            double firstWindow = -1, double window = -1, double overlap = -1)
        {
            var list = new List<LivePcmWindow>();
            if (pcm == null || pcm.Length == 0 || sampleRate < 1) return list;
            if (firstWindow <= 0) firstWindow = LiveChunkMath.FirstWindowSeconds;
            if (window <= 0) window = LiveChunkMath.WindowSeconds;
            if (overlap < 0) overlap = LiveChunkMath.OverlapSeconds;
            int maxRing = (int)(window * sampleRate) + sampleRate;
            int index = 0;
            while (true)
            {
                long fire = (long)Math.Round(LiveChunkMath.FireAt(index, firstWindow, window, overlap) * sampleRate);
                if (fire > pcm.Length) break;
                int elapsed = (int)fire;
                int want = (int)Math.Round(LiveChunkMath.WindowForIndex(index, firstWindow, window) * sampleRate);
                if (want < sampleRate / 4) want = sampleRate / 4;
                int ringLen = Math.Min(elapsed, maxRing);
                int take = Math.Min(want, ringLen);
                var snap = new short[take];
                Array.Copy(pcm, elapsed - take, snap, 0, take);
                list.Add(new LivePcmWindow
                {
                    Index = index,
                    AbsStart = LiveChunkMath.ChunkAbsStart(index, window, overlap, firstWindow),
                    Pcm = snap
                });
                index++;
                if (index > 100000) break;
            }
            if (includeTail && pcm.Length >= sampleRate / 2)
            {
                int ringLen = Math.Min(pcm.Length, maxRing);
                if (ringLen >= sampleRate / 2)
                {
                    var snap = new short[ringLen];
                    Array.Copy(pcm, pcm.Length - ringLen, snap, 0, ringLen);
                    list.Add(new LivePcmWindow
                    {
                        Index = index,
                        AbsStart = LiveChunkMath.ChunkAbsStart(Math.Max(0, index), window, overlap, firstWindow),
                        Pcm = snap,
                        IsTail = true
                    });
                }
            }
            return list;
        }

        public static bool EvaluateVad(short[] pcm, ref double noiseFloor, out double rms, out double thresh)
        {
            rms = AudioPcm.Rms(pcm);
            thresh = AudioPcm.VoiceGate.Threshold(noiseFloor);
            bool voiced = rms >= thresh;
            noiseFloor = AudioPcm.VoiceGate.UpdateFloor(noiseFloor, rms, voiced);
            return voiced;
        }

        public static string LastCueText(IReadOnlyList<TranscriptCue> cues, int maxCues = 4)
        {
            if (cues == null || cues.Count == 0) return "";
            int from = cues.Count > maxCues ? cues.Count - maxCues : 0;
            string s = "";
            for (int i = from; i < cues.Count; i++)
            {
                string t = WhisperDecode.NormalizeCueText(cues[i].Text);
                if (t.Length == 0) continue;
                if (s.Length > 0) s += " ";
                s += t;
            }
            return s;
        }

        public static async Task<LivePipelineResult> RunAsync(
            short[] pcm,
            Func<LivePcmWindow, string, CancellationToken, Task<List<TranscriptCue>>> transcribe,
            Action<string> log,
            Func<int, short[], string> dumpWav,
            CancellationToken ct,
            double firstWindow = -1, double window = -1, double overlap = -1, bool live = true,
            Action<IReadOnlyList<TranscriptCue>> onCues = null)
        {
            async Task<WhisperDecodeResult> Wrap(LivePcmWindow w, string prev, CancellationToken token)
            {
                var cues = await transcribe(w, prev, token).ConfigureAwait(false);
                return new WhisperDecodeResult { Cues = cues ?? new List<TranscriptCue>(), KeptCues = cues == null ? 0 : cues.Count };
            }
            return await RunDetailedAsync(pcm, Wrap, log, dumpWav, ct, firstWindow, window, overlap, live, onCues).ConfigureAwait(false);
        }

        public static async Task<LivePipelineResult> RunDetailedAsync(
            short[] pcm,
            Func<LivePcmWindow, string, CancellationToken, Task<WhisperDecodeResult>> transcribe,
            Action<string> log,
            Func<int, short[], string> dumpWav,
            CancellationToken ct,
            double firstWindow = -1, double window = -1, double overlap = -1, bool live = true,
            Action<IReadOnlyList<TranscriptCue>> onCues = null)
        {
            var result = new LivePipelineResult();
            if (pcm == null || pcm.Length == 0 || transcribe == null) return result;
            result.DurationSeconds = pcm.Length / (double)AudioPcm.WhisperRate;
            var windows = SlicePcm(pcm, AudioPcm.WhisperRate, includeTail: true, firstWindow, window, overlap);
            var state = new DecodeState();
            foreach (var w in windows)
            {
                ct.ThrowIfCancellationRequested();
                await DecodeWindowAsync(w, transcribe, log, dumpWav, ct, live, result, state, 0, 0, onCues).ConfigureAwait(false);
            }
            return result;
        }

        sealed class DecodeState
        {
            public double Floor;
            public double Committed;
        }

        static async Task DecodeWindowAsync(
            LivePcmWindow w,
            Func<LivePcmWindow, string, CancellationToken, Task<WhisperDecodeResult>> transcribe,
            Action<string> log,
            Func<int, short[], string> dumpWav,
            CancellationToken ct,
            bool live,
            LivePipelineResult result,
            DecodeState state,
            int queueBefore, int queueAfter,
            Action<IReadOnlyList<TranscriptCue>> onCues)
        {
            var report = new WindowDecodeReport
            {
                Index = w.Index, AbsStart = w.AbsStart, Duration = w.DurationSeconds,
                QueueBefore = queueBefore, QueueAfter = queueAfter
            };
            bool voiced = EvaluateVad(w.Pcm, ref state.Floor, out double rms, out double thresh);
            if (!voiced)
            {
                result.Skipped++;
                report.Voiced = false;
                result.Reports.Add(report);
                log?.Invoke($"chunk {w.Index} skipped silent rms={rms:F5} thresh={thresh:F5} samples={w.Pcm.Length} abs={w.AbsStart:F2} q={queueBefore}->{queueAfter}");
                return;
            }
            result.Processed++;
            report.Voiced = true;
            if (dumpWav != null)
            {
                string path = dumpWav(w.Index, w.Pcm);
                if (!string.IsNullOrEmpty(path))
                    log?.Invoke($"chunk {w.Index} dump {path}");
            }
            log?.Invoke($"chunk {w.Index} process rms={rms:F5} thresh={thresh:F5} samples={w.Pcm.Length} abs={w.AbsStart:F2} dur={w.DurationSeconds:F2} q={queueBefore}->{queueAfter}");
            string previous = LastCueText(result.Cues);
            WhisperDecodeResult dec;
            var sw = Stopwatch.StartNew();
            try
            {
                dec = await transcribe(w, previous, ct).ConfigureAwait(false) ?? new WhisperDecodeResult();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                dec = new WhisperDecodeResult { TimedOut = true, RetryPath = "timeout" };
            }
            if (dec.ElapsedSeconds <= 0) dec.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            dec.Cues ??= new List<TranscriptCue>();
            report.ElapsedSeconds = dec.ElapsedSeconds;
            report.TimedOut = dec.TimedOut;
            report.Raw = dec.RawSegments;
            report.Kept = dec.KeptCues;
            report.Backend = dec.Backend.ToString();
            report.RetryPath = dec.RetryPath;
            if (dec.ElapsedSeconds > result.MaxDecodeSeconds) result.MaxDecodeSeconds = dec.ElapsedSeconds;
            double ratio = w.DurationSeconds > 0 ? dec.ElapsedSeconds / w.DurationSeconds : 0;
            if (ratio > result.MaxDecodeRatio) result.MaxDecodeRatio = ratio;
            if (dec.TimedOut)
            {
                result.TimedOut++;
                log?.Invoke($"chunk {w.Index} TIMEOUT elapsed={dec.ElapsedSeconds:F2}s budget={WhisperDecodeBudget.TimeoutSeconds(w.DurationSeconds, live):F1}s backend={dec.Backend} path={dec.RetryPath}");
                result.Reports.Add(report);
                return;
            }
            log?.Invoke($"chunk {w.Index} decode cues={dec.Cues.Count} raw={dec.RawSegments} kept={dec.KeptCues} elapsed={dec.ElapsedSeconds:F2}s backend={dec.Backend} path={dec.RetryPath}");
            for (int i = 0; i < dec.Cues.Count; i++)
                log?.Invoke($"  cue[{i}] {dec.Cues[i].Start:F3}-{dec.Cues[i].End:F3} '{WhisperDecode.Trunc(dec.Cues[i].Text, 80)}'");
            var added = LiveChunkMath.Stitch(dec.Cues, w.AbsStart, state.Committed, w.DurationSeconds);
            report.Stitched = added.Count;
            log?.Invoke($"chunk {w.Index} stitch in={dec.Cues.Count} out={added.Count} committed={state.Committed:F2}");
            result.Cues.AddRange(added);
            state.Committed = LiveChunkMath.ExtendCommitted(added, state.Committed);
            result.CommittedThrough = state.Committed;
            result.Reports.Add(report);
            if (added.Count > 0) onCues?.Invoke(added);
        }
    }
}
