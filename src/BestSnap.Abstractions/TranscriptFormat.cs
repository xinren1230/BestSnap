using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BestSnap.Plugins
{
    public sealed class TranscriptCue
    {
        public double Start { get; set; }
        public double End { get; set; }
        public string Text { get; set; }
    }

    public static class TranscriptFormat
    {
        public static string SrtTimestamp(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            var t = TimeSpan.FromSeconds(seconds);
            int h = (int)t.TotalHours;
            if (h > 99) h = 99;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00},{3:000}",
                h, t.Minutes, t.Seconds, t.Milliseconds);
        }

        public static string ToSrt(IReadOnlyList<TranscriptCue> cues)
        {
            var sb = new StringBuilder();
            int n = 0;
            if (cues == null) return "";
            foreach (var c in cues)
            {
                string text = (c.Text ?? "").Trim();
                if (text.Length == 0) continue;
                n++;
                sb.Append(n).Append("\r\n");
                sb.Append(SrtTimestamp(c.Start)).Append(" --> ").Append(SrtTimestamp(Math.Max(c.End, c.Start))).Append("\r\n");
                sb.Append(text).Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        public static string ToTxt(IReadOnlyList<TranscriptCue> cues)
        {
            var sb = new StringBuilder();
            if (cues == null) return "";
            foreach (var c in cues)
            {
                string text = (c.Text ?? "").Trim();
                if (text.Length == 0) continue;
                sb.Append('[').Append(SrtTimestamp(c.Start).Replace(',', '.')).Append("] ").Append(text).Append("\r\n");
            }
            return sb.ToString();
        }

        public static string PlainText(IReadOnlyList<TranscriptCue> cues)
        {
            var sb = new StringBuilder();
            if (cues == null) return "";
            foreach (var c in cues)
            {
                string text = (c.Text ?? "").Trim();
                if (text.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(text);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Rolling ASR windows. First caption is short; later windows are longer with overlap.
    /// </summary>
    public static class LiveChunkMath
    {
        public const double FirstWindowSeconds = 3.5;
        public const double WindowSeconds = 10;
        public const double OverlapSeconds = 2;
        public static double HopSeconds => WindowSeconds - OverlapSeconds;

        public static double WindowForIndex(int index, double firstWindow = FirstWindowSeconds, double window = WindowSeconds) =>
            index <= 0 ? Math.Max(0.4, firstWindow) : Math.Max(0.4, window);

        public static double FireAt(int index, double firstWindow = FirstWindowSeconds, double window = WindowSeconds, double overlap = OverlapSeconds)
        {
            double first = Math.Max(0.4, firstWindow);
            if (index <= 0) return first;
            double hop = Math.Max(0.1, window - overlap);
            return first + index * hop;
        }

        public static double ChunkAbsStart(int index, double window = WindowSeconds, double overlap = OverlapSeconds, double firstWindow = FirstWindowSeconds)
        {
            if (index <= 0) return 0;
            return Math.Max(0, FireAt(index, firstWindow, window, overlap) - WindowForIndex(index, firstWindow, window));
        }

        public static List<TranscriptCue> Stitch(IReadOnlyList<TranscriptCue> incomingRelative, double absStart, double committedThrough, double chunkDuration = 0)
        {
            var list = new List<TranscriptCue>();
            if (incomingRelative == null) return list;
            foreach (var c in incomingRelative)
            {
                string text = WhisperDecode.NormalizeCueText(c.Text);
                if (text.Length == 0) continue;
                double start = c.Start, end = c.End;
                if (double.IsNaN(start) || double.IsInfinity(start)) start = 0;
                if (double.IsNaN(end) || double.IsInfinity(end)) end = start;
                double a = absStart + start;
                double b = absStart + Math.Max(end, start);
                if (b - a < 0.04)
                {
                    // CUDA/q5 often returns 0-duration timestamps with valid text; keep the cue.
                    a = Math.Max(absStart, committedThrough);
                    double span = chunkDuration > 0 ? chunkDuration : 0.5;
                    b = absStart + span;
                    if (b <= a) b = a + 0.04;
                }
                if (b <= committedThrough + 1e-3) continue;
                if (a < committedThrough) a = committedThrough;
                if (b - a < 0.04) continue;
                list.Add(new TranscriptCue { Start = a, End = b, Text = text });
            }
            return list;
        }

        public static double ExtendCommitted(IReadOnlyList<TranscriptCue> added, double previous)
        {
            double t = previous;
            if (added == null) return t;
            foreach (var c in added)
                if (c.End > t) t = c.End;
            return t;
        }
    }
}
