using System;
using System.IO;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Immediate save after a recording stops, and a default name for editor export
    /// that does not overwrite the original unless the user picks that path.
    /// </summary>
    public static class RecordingSavePath
    {
        public const string EditedSuffix = "-edited";
        public const string VirtualHost = "vs.bestsnap.local";
        public const string SessionSourcePath = "/session/source";

        public static string SessionSourceUrl => "https://" + VirtualHost + SessionSourcePath;

        public static bool IsSessionSourceUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return false;
            try
            {
                var u = new Uri(uri, UriKind.Absolute);
                return string.Equals(u.Host, VirtualHost, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(u.AbsolutePath.TrimEnd('/'), SessionSourcePath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string UniqueInFolder(string folder, string stem, string ext)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("folder required", nameof(folder));
            Directory.CreateDirectory(folder);
            if (string.IsNullOrWhiteSpace(stem)) stem = "Rec";
            if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";
            if (ext[0] != '.') ext = "." + ext;
            string path = Path.Combine(folder, stem + ext);
            for (int i = 2; File.Exists(path); i++)
                path = Path.Combine(folder, stem + " (" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + ext);
            return Path.GetFullPath(path);
        }

        /// <summary>
        /// <c>name-edited.ext</c> next to <paramref name="original"/>; never the original path itself.
        /// Optional <paramref name="exportExt"/> keeps a WebM/GIF export next to an MP4 recording.
        /// </summary>
        public static string DefaultEditedPath(string original, string exportExt = null)
        {
            if (string.IsNullOrWhiteSpace(original)) throw new ArgumentException("original path required", nameof(original));
            string ext = string.IsNullOrWhiteSpace(exportExt) ? Path.GetExtension(original) : exportExt;
            if (string.IsNullOrEmpty(ext)) ext = ".mp4";
            if (ext[0] != '.') ext = "." + ext;
            string full = Path.GetFullPath(original);
            string dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            string stem = Path.GetFileNameWithoutExtension(full);
            return TrimRangeMath.DefaultOutputPath(Path.Combine(dir, stem + ext), EditedSuffix);
        }

        public static string ResolveDestFolder(bool audio, string recordingFolder, string audioFolder, string defaultFolder)
        {
            if (audio && !string.IsNullOrWhiteSpace(audioFolder)) return audioFolder.Trim();
            if (!audio && !string.IsNullOrWhiteSpace(recordingFolder)) return recordingFolder.Trim();
            if (!string.IsNullOrWhiteSpace(defaultFolder)) return defaultFolder.Trim();
            var special = audio ? Environment.SpecialFolder.MyMusic : Environment.SpecialFolder.MyVideos;
            return Path.Combine(Environment.GetFolderPath(special), AppIdentity.Name);
        }

        /// <summary>
        /// Copy <paramref name="source"/> (and its .txt sidecar) into <paramref name="destFolder"/>
        /// under a unique name, then delete the source pair. No-op when already in that folder.
        /// </summary>
        public static string PromoteToFolder(string source, string destFolder)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return source;
            if (string.IsNullOrWhiteSpace(destFolder)) destFolder = Path.GetDirectoryName(Path.GetFullPath(source));
            Directory.CreateDirectory(destFolder);
            string fullSrc = Path.GetFullPath(source);
            string fullDestDir = Path.GetFullPath(destFolder);
            string srcDir = Path.GetDirectoryName(fullSrc);
            if (string.Equals(srcDir, fullDestDir, StringComparison.OrdinalIgnoreCase))
                return fullSrc;

            string dest = UniqueInFolder(fullDestDir, Path.GetFileNameWithoutExtension(fullSrc), Path.GetExtension(fullSrc));
            File.Copy(fullSrc, dest, overwrite: false);
            TranscriptSidecar.CopyWithMedia(fullSrc, dest);
            try { if (File.Exists(fullSrc + ".meta.json")) File.Delete(fullSrc + ".meta.json"); } catch { }
            try { if (File.Exists(fullSrc + ".16k.wav")) File.Delete(fullSrc + ".16k.wav"); } catch { }
            TranscriptSidecar.DeleteFor(fullSrc);
            try { File.Delete(fullSrc); } catch { }
            return dest;
        }
    }
}
