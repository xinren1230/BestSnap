using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Unsaved recordings live under %LocalAppData%\BestSnap\temp\recordings until the user clicks Save.
    /// Crash-safe: media stays on disk; a sidecar marks completion. Stale files (default 7 days) are deleted on startup.
    /// </summary>
    public static class TempRecordingStore
    {
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

        public static string Folder(string localData) =>
            Path.Combine(localData ?? "", "temp", "recordings");

        public static string EnsureFolder(string localData)
        {
            var d = Folder(localData);
            Directory.CreateDirectory(d);
            return d;
        }

        public static bool IsUnder(string path, string folder)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder)) return false;
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string SidecarPath(string media) => (media ?? "") + ".meta.json";

        public static string NewMediaPath(string folder, string prefix, string ext, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("folder required", nameof(folder));
            Directory.CreateDirectory(folder);
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "Rec";
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";
            if (ext[0] != '.') ext = "." + ext;
            string stamp = utcNow.ToUniversalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string path = Path.Combine(folder, prefix + "_" + stamp + ext);
            if (File.Exists(path))
                path = Path.Combine(folder, prefix + "_" + stamp + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
            WriteSidecar(path, complete: false, utcNow);
            return path;
        }

        public static void MarkComplete(string media, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(media)) return;
            WriteSidecar(media, complete: true, utcNow);
        }

        public static bool IsReady(string media)
        {
            if (string.IsNullOrWhiteSpace(media) || !File.Exists(media)) return false;
            try { if (new FileInfo(media).Length <= 0) return false; } catch { return false; }
            string sc = SidecarPath(media);
            if (!File.Exists(sc)) return true; // leftover file without sidecar is still recoverable
            string t;
            try { t = File.ReadAllText(sc); } catch { return true; }
            return t.IndexOf("\"complete\":true", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("\"complete\": true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<string> ListReady(string folder)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;
            foreach (var f in Directory.GetFiles(folder))
            {
                if (f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (f.EndsWith(".16k.wav", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsReady(f)) list.Add(f);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>Deletes files in <paramref name="folder"/> whose last write is older than <paramref name="maxAge"/>.</summary>
        public static List<string> Cleanup(string folder, TimeSpan maxAge, DateTime utcNow)
        {
            var deleted = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return deleted;
            if (maxAge < TimeSpan.Zero) maxAge = TimeSpan.Zero;
            foreach (var f in Directory.GetFiles(folder))
            {
                DateTime t;
                try { t = File.GetLastWriteTimeUtc(f); } catch { continue; }
                if (utcNow.ToUniversalTime() - t.ToUniversalTime() < maxAge) continue;
                try { File.Delete(f); deleted.Add(f); } catch { }
            }
            return deleted;
        }

        public static void DeletePair(string media)
        {
            if (string.IsNullOrWhiteSpace(media)) return;
            try { if (File.Exists(media)) File.Delete(media); } catch { }
            try
            {
                string sc = SidecarPath(media);
                if (File.Exists(sc)) File.Delete(sc);
            }
            catch { }
            try
            {
                string wav = media + ".16k.wav";
                if (File.Exists(wav)) File.Delete(wav);
            }
            catch { }
        }

        static void WriteSidecar(string media, bool complete, DateTime utc)
        {
            try
            {
                string json = "{\"complete\":" + (complete ? "true" : "false")
                    + ",\"utc\":\"" + utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + "\"}";
                File.WriteAllText(SidecarPath(media), json);
            }
            catch { }
        }
    }
}
