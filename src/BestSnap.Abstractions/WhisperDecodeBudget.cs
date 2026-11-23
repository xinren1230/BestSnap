using System;
using System.Collections.Generic;

namespace BestSnap.Plugins
{
    /// <summary>Live/file decode timeouts and self-test rules. Pure math for unit tests.</summary>
    public static class WhisperDecodeBudget
    {
        public const double LiveTimeoutFactor = 2.5;
        public const double LiveTimeoutDefault = 25;
        public const double LiveTimeoutMin = 8;
        public const double LiveTimeoutMax = 45;
        public const double FileTimeoutFactor = 2.5;
        public const double FileTimeoutMin = 20;
        public const double FileTimeoutMax = 90;
        public const double SlowFactor = 2;
        public const double FailFactor = 3;
        public const int LiveMaxPending = 2;
        public const int LiveMaxLagHops = 2;

        public static double TimeoutSeconds(double chunkDuration, bool live)
        {
            if (double.IsNaN(chunkDuration) || chunkDuration < 0.4) chunkDuration = live ? LiveChunkMath.WindowSeconds : FileChunkMath.WindowSeconds;
            if (live)
            {
                double t = Math.Max(LiveTimeoutMin, chunkDuration * LiveTimeoutFactor);
                if (chunkDuration >= 8 && t < LiveTimeoutDefault) t = LiveTimeoutDefault;
                if (t > LiveTimeoutMax) t = LiveTimeoutMax;
                return t;
            }
            double f = Math.Max(FileTimeoutMin, chunkDuration * FileTimeoutFactor);
            if (f > FileTimeoutMax) f = FileTimeoutMax;
            return f;
        }

        public static bool IsSlow(double elapsedSeconds, double chunkDuration, bool live = true)
        {
            if (elapsedSeconds <= 0) return false;
            double dur = chunkDuration > 0.4 ? chunkDuration : (live ? LiveChunkMath.WindowSeconds : FileChunkMath.WindowSeconds);
            return elapsedSeconds > TimeoutSeconds(dur, live) || elapsedSeconds > dur * SlowFactor;
        }

        public static bool ExceedsDurationFactor(double elapsedSeconds, double chunkDuration, double factor = FailFactor)
        {
            if (factor < 1) factor = FailFactor;
            if (chunkDuration <= 0) return elapsedSeconds > LiveTimeoutDefault;
            return elapsedSeconds > chunkDuration * factor;
        }

        public static int DropOldestCount(int pending, int maxPending = LiveMaxPending)
        {
            if (pending < 0) pending = 0;
            if (maxPending < 1) maxPending = 1;
            return pending > maxPending ? pending - maxPending : 0;
        }

        public static bool LagExceedsHops(double oldestAbsStart, double newestAbsStart, double hopSeconds = -1, int maxHops = LiveMaxLagHops)
        {
            if (hopSeconds <= 0) hopSeconds = LiveChunkMath.HopSeconds;
            if (maxHops < 1) maxHops = LiveMaxLagHops;
            return newestAbsStart - oldestAbsStart > maxHops * hopSeconds - 1e-6;
        }

        public static bool CuesMissingInSecondHalf(IReadOnlyList<TranscriptCue> cues, double durationSeconds)
        {
            if (durationSeconds <= 0) return cues == null || cues.Count == 0;
            double mid = durationSeconds / 2;
            if (cues == null) return true;
            foreach (var c in cues)
            {
                if (c == null) continue;
                if (c.Start >= mid || c.End >= mid) return false;
            }
            return true;
        }

        /// <summary>
        /// Headless self-test: fail on hung/slow chunks or captions that never reach the second half.
        /// </summary>
        public static string SelfTestFailure(IReadOnlyList<WindowDecodeReport> reports, IReadOnlyList<TranscriptCue> cues, double durationSeconds)
        {
            if (reports != null)
            {
                foreach (var r in reports)
                {
                    if (r == null || !r.Voiced) continue;
                    if (r.TimedOut || ExceedsDurationFactor(r.ElapsedSeconds, r.Duration))
                        return $"chunk {r.Index} decode {r.ElapsedSeconds:F2}s exceeded 3x duration {r.Duration:F2}s (timeout={r.TimedOut})";
                }
            }
            if (CuesMissingInSecondHalf(cues, durationSeconds))
                return "cues stopped in the second half";
            if (cues == null || cues.Count == 0)
                return "empty transcript";
            return null;
        }
    }

    public sealed class WhisperDecodeResult
    {
        public List<TranscriptCue> Cues { get; set; } = new List<TranscriptCue>();
        public int RawSegments { get; set; }
        public int KeptCues { get; set; }
        public double ElapsedSeconds { get; set; }
        public double ChunkDuration { get; set; }
        public WhisperBackend Backend { get; set; }
        public string RetryPath { get; set; } = "float32";
        public bool TimedOut { get; set; }
        public bool RecreatedFactory { get; set; }
        public int Epoch { get; set; }
    }

    public sealed class WindowDecodeReport
    {
        public int Index { get; set; }
        public double AbsStart { get; set; }
        public double Duration { get; set; }
        public double ElapsedSeconds { get; set; }
        public bool Voiced { get; set; }
        public bool TimedOut { get; set; }
        public int Raw { get; set; }
        public int Kept { get; set; }
        public int Stitched { get; set; }
        public int QueueBefore { get; set; }
        public int QueueAfter { get; set; }
        public int Dropped { get; set; }
        public string Backend { get; set; }
        public string RetryPath { get; set; }
    }
}
