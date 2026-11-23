using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BestSnap.Plugins
{
    /// <summary>Half-open time range in seconds: [Start, End).</summary>
    public readonly struct TimeRange : IEquatable<TimeRange>
    {
        public TimeRange(double start, double end)
        {
            Start = start; End = end;
        }
        public double Start { get; }
        public double End { get; }
        public double Duration => End - Start;
        public bool IsEmpty => End <= Start;
        public bool Equals(TimeRange other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object obj) => obj is TimeRange r && Equals(r);
        public override int GetHashCode() => HashCode.Combine(Start, End);
        public override string ToString() => $"{Start:0.###}-{End:0.###}";
        public static bool operator ==(TimeRange a, TimeRange b) => a.Equals(b);
        public static bool operator !=(TimeRange a, TimeRange b) => !a.Equals(b);
    }

    /// <summary>
    /// Range math for the post-recording trim editor: merge overlapping cuts, invert to keep-segments,
    /// default output names, ffmpeg filter_complex plans.
    /// </summary>
    public static class TrimRangeMath
    {
        public const double Epsilon = 1e-4;

        public static TimeRange? TryCreate(double start, double end, double duration)
        {
            if (double.IsNaN(start) || double.IsNaN(end) || double.IsNaN(duration) || duration <= 0) return null;
            double a = Math.Min(start, end), b = Math.Max(start, end);
            a = Math.Clamp(a, 0, duration);
            b = Math.Clamp(b, 0, duration);
            if (b - a < Epsilon) return null;
            return new TimeRange(a, b);
        }

        /// <summary>Clamp, drop empties, sort, merge overlapping or touching ranges.</summary>
        public static List<TimeRange> Merge(IEnumerable<TimeRange> ranges, double duration, double minGap = 0)
        {
            var list = new List<TimeRange>();
            if (ranges == null || duration <= 0) return list;
            if (minGap < 0) minGap = 0;
            foreach (var r in ranges)
            {
                var n = TryCreate(r.Start, r.End, duration);
                if (n.HasValue) list.Add(n.Value);
            }
            list.Sort((x, y) => x.Start.CompareTo(y.Start));
            var merged = new List<TimeRange>();
            foreach (var r in list)
            {
                if (merged.Count == 0) { merged.Add(r); continue; }
                var last = merged[merged.Count - 1];
                if (r.Start <= last.End + minGap + Epsilon)
                    merged[merged.Count - 1] = new TimeRange(last.Start, Math.Max(last.End, r.End));
                else merged.Add(r);
            }
            return merged;
        }

        /// <summary>Keep segments = complement of merged remove ranges in [0, duration].</summary>
        public static List<TimeRange> KeepFromRemove(double duration, IEnumerable<TimeRange> remove)
        {
            var cuts = Merge(remove, duration);
            var keep = new List<TimeRange>();
            if (duration <= 0) return keep;
            double t = 0;
            foreach (var c in cuts)
            {
                if (c.Start > t + Epsilon) keep.Add(new TimeRange(t, c.Start));
                if (c.End > t) t = c.End;
            }
            if (duration > t + Epsilon) keep.Add(new TimeRange(t, duration));
            return keep;
        }

        /// <summary>Remove segments = complement of merged keep ranges.</summary>
        public static List<TimeRange> RemoveFromKeep(double duration, IEnumerable<TimeRange> keep) =>
            KeepFromRemove(duration, Merge(keep, duration));

        public static double TotalDuration(IEnumerable<TimeRange> ranges)
        {
            double t = 0;
            if (ranges == null) return 0;
            foreach (var r in ranges)
            {
                double d = r.End - r.Start;
                if (d > 0 && !double.IsNaN(d) && !double.IsInfinity(d)) t += d;
            }
            return t;
        }

        /// <summary>
        /// <c>name_trimmed.ext</c>; if that path exists, <c>name_trimmed-2.ext</c>, then -3, …
        /// Never returns <paramref name="original"/> itself.
        /// </summary>
        public static string DefaultOutputPath(string original, string suffix = "_trimmed", Func<string, bool> exists = null)
        {
            if (string.IsNullOrWhiteSpace(original)) throw new ArgumentException("original path required", nameof(original));
            exists ??= File.Exists;
            string dir = Path.GetDirectoryName(original);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            string name = Path.GetFileNameWithoutExtension(original);
            string ext = Path.GetExtension(original);
            if (string.IsNullOrEmpty(suffix)) suffix = "_trimmed";
            string path = Path.Combine(dir, name + suffix + ext);
            if (!exists(path) && !PathsEqual(path, original)) return path;
            for (int n = 2; n < 10000; n++)
            {
                path = Path.Combine(dir, name + suffix + "-" + n + ext);
                if (!exists(path) && !PathsEqual(path, original)) return path;
            }
            return Path.Combine(dir, name + suffix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
        }

        static bool PathsEqual(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        public static bool IsAudioPath(string path)
        {
            var e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return e is ".wav" or ".mp3" or ".m4a" or ".aac" or ".flac" or ".wma";
        }

        public static bool CanTrimPath(string path)
        {
            var e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return e is ".mp4" or ".wav" or ".mp3" or ".m4a" or ".mov" or ".mkv";
        }

        static string Ts(double s) => s.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>ffmpeg filter_complex that concatenates keep-segments with timestamps reset (frame-accurate re-encode).</summary>
        public static string BuildFilter(IReadOnlyList<TimeRange> keep, bool video, bool audio)
        {
            if (keep == null || keep.Count == 0) throw new ArgumentException("need at least one keep segment", nameof(keep));
            if (!video && !audio) throw new ArgumentException("need video and/or audio");
            var sb = new StringBuilder();
            for (int i = 0; i < keep.Count; i++)
            {
                var r = keep[i];
                if (video) sb.Append("[0:v]trim=start=").Append(Ts(r.Start)).Append(":end=").Append(Ts(r.End)).Append(",setpts=PTS-STARTPTS[v").Append(i).Append("];");
                if (audio) sb.Append("[0:a]atrim=start=").Append(Ts(r.Start)).Append(":end=").Append(Ts(r.End)).Append(",asetpts=PTS-STARTPTS[a").Append(i).Append("];");
            }
            for (int i = 0; i < keep.Count; i++)
            {
                if (video) sb.Append("[v").Append(i).Append(']');
                if (audio) sb.Append("[a").Append(i).Append(']');
            }
            sb.Append("concat=n=").Append(keep.Count).Append(":v=").Append(video ? 1 : 0).Append(":a=").Append(audio ? 1 : 0);
            if (video) sb.Append("[outv]");
            if (audio) sb.Append("[outa]");
            return sb.ToString();
        }

        /// <summary>ffmpeg argv after the binary name (no process). Re-encodes so cuts are frame-accurate.</summary>
        public static List<string> BuildArgs(string input, string output, IReadOnlyList<TimeRange> keep, bool video, bool audio)
        {
            if (keep == null || keep.Count == 0) throw new ArgumentException("need at least one keep segment", nameof(keep));
            var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", input };
            string ext = (Path.GetExtension(output) ?? "").ToLowerInvariant();
            if (keep.Count == 1)
            {
                args.Add("-ss"); args.Add(Ts(keep[0].Start));
                args.Add("-to"); args.Add(Ts(keep[0].End));
            }
            else
            {
                args.Add("-filter_complex");
                args.Add(BuildFilter(keep, video, audio));
                if (video) { args.Add("-map"); args.Add("[outv]"); }
                if (audio) { args.Add("-map"); args.Add("[outa]"); }
            }
            if (video)
            {
                args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-pix_fmt", "yuv420p" });
            }
            if (audio)
            {
                if (ext == ".wav") args.AddRange(new[] { "-c:a", "pcm_s16le" });
                else if (ext == ".mp3") args.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", "192k" });
                else args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
            }
            if (video) args.AddRange(new[] { "-movflags", "+faststart" });
            args.Add(output);
            return args;
        }
    }
}
