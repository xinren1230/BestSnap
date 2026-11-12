using System;
using System.Collections.Generic;
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
        public double CommittedThrough { get; set; }
    }

    /// <summary>
    /// Same windowing / VAD / stitch as the live caption mixer: first 3.5 s, then 10 s / 2 s overlap.
    /// Transcribe is injected so unit tests can feed speech PCM without native Whisper.
    /// </summary>
    public static class LiveTranscribePipeline
    {
        public static List<LivePcmWindow> SlicePcm(short[] pcm, int sampleRate = AudioPcm.WhisperRate, bool includeTail = true)
        {
            var list = new List<LivePcmWindow>();
            if (pcm == null || pcm.Length == 0 || sampleRate < 1) return list;
            int maxRing = (int)(LiveChunkMath.WindowSeconds * sampleRate) + sampleRate;
            int index = 0;
            while (true)
            {
                long fire = (long)Math.Round(LiveChunkMath.FireAt(index) * sampleRate);
                if (fire > pcm.Length) break;
                int elapsed = (int)fire;
                int want = (int)Math.Round(LiveChunkMath.WindowForIndex(index) * sampleRate);
                if (want < sampleRate / 4) want = sampleRate / 4;
                int ringLen = Math.Min(elapsed, maxRing);
                int take = Math.Min(want, ringLen);
                var snap = new short[take];
                Array.Copy(pcm, elapsed - take, snap, 0, take);
                list.Add(new LivePcmWindow
                {
                    Index = index,
                    AbsStart = LiveChunkMath.ChunkAbsStart(index),
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
                        AbsStart = LiveChunkMath.ChunkAbsStart(Math.Max(0, index)),
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
            CancellationToken ct)
        {
            var result = new LivePipelineResult();
            if (pcm == null || pcm.Length == 0 || transcribe == null) return result;
            double floor = 0, committed = 0;
            foreach (var w in SlicePcm(pcm))
            {
                ct.ThrowIfCancellationRequested();
                bool voiced = EvaluateVad(w.Pcm, ref floor, out double rms, out double thresh);
                if (!voiced)
                {
                    result.Skipped++;
                    log?.Invoke($"chunk {w.Index} skipped silent rms={rms:F5} thresh={thresh:F5} floor={floor:F5} samples={w.Pcm.Length} abs={w.AbsStart:F2}");
                    continue;
                }
                result.Processed++;
                if (dumpWav != null)
                {
                    string path = dumpWav(w.Index, w.Pcm);
                    if (!string.IsNullOrEmpty(path))
                        log?.Invoke($"chunk {w.Index} dump {path}");
                }
                log?.Invoke($"chunk {w.Index} process rms={rms:F5} thresh={thresh:F5} samples={w.Pcm.Length} abs={w.AbsStart:F2} dur={w.DurationSeconds:F2}");
                string previous = LastCueText(result.Cues);
                var rel = await transcribe(w, previous, ct).ConfigureAwait(false) ?? new List<TranscriptCue>();
                log?.Invoke($"chunk {w.Index} decode cues={rel.Count}");
                for (int i = 0; i < rel.Count; i++)
                    log?.Invoke($"  cue[{i}] {rel[i].Start:F3}-{rel[i].End:F3} '{WhisperDecode.Trunc(rel[i].Text, 80)}'");
                var added = LiveChunkMath.Stitch(rel, w.AbsStart, committed, w.DurationSeconds);
                log?.Invoke($"chunk {w.Index} stitch in={rel.Count} out={added.Count} committed={committed:F2}");
                for (int i = 0; i < added.Count; i++)
                    log?.Invoke($"  stitched[{i}] {added[i].Start:F3}-{added[i].End:F3} '{WhisperDecode.Trunc(added[i].Text, 80)}'");
                result.Cues.AddRange(added);
                committed = LiveChunkMath.ExtendCommitted(added, committed);
                result.CommittedThrough = committed;
            }
            return result;
        }
    }
}
