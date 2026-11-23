using System;
using System.IO;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Immediate save after a recording stops, and a default name for editor export
    /// that does not overwrite the original.
    /// Editor static files are folder-mapped on <see cref="VirtualHost"/>;
    /// the recording is served from <see cref="MediaHost"/> (never folder-mapped).
    /// </summary>
    public static class RecordingSavePath
    {
        public const string EditedSuffix = "-edited";
        public const string VirtualHost = "vs.bestsnap.localhost";
        public const string MediaHost = "media.bestsnap.localhost";
        public const string MediaSourcePrefix = HostedMediaServe.SourcePrefix;

        public static string EditorOrigin => "https://" + VirtualHost;
        public static string MediaOrigin => "https://" + MediaHost;

        public static string MediaSourceUrl(string filePath)
        {
            string name = string.IsNullOrWhiteSpace(filePath) ? "recording.mp4" : Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(name)) name = "recording.mp4";
            return MediaOrigin + MediaSourcePrefix + Uri.EscapeDataString(name);
        }

        public static bool IsUsableMedia(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try { return new FileInfo(path).Length > 0; }
            catch { return false; }
        }

        public static bool WouldOverwriteOriginal(string original, string dest)
        {
            return PathsEqual(original, dest);
        }

        /// <summary>If <paramref name="dest"/> is the original recording, return a unique -edited path instead.</summary>
        public static string GuardExportDest(string original, string dest)
        {
            if (string.IsNullOrWhiteSpace(dest)) return DefaultEditedPath(original);
            dest = Path.GetFullPath(dest);
            if (WouldOverwriteOriginal(original, dest)) return DefaultEditedPath(original);
            return dest;
        }

        public static string EnsureVideoFolder(string recordingFolder, string defaultFolder)
        {
            string dir = ResolveDestFolder(false, recordingFolder, null, defaultFolder);
            Directory.CreateDirectory(dir);
            return Path.GetFullPath(dir);
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
            // Only delete the source if it is still in temp/recordings. Never delete a file
            // already in the user's save folder (path-canonicalization / OneDrive junctions
            // can make srcDir != destDir even when both are the same library).
            if (!TempRecordingStore.IsTempRecordingPath(fullSrc))
                return dest;
            try { if (File.Exists(fullSrc + ".meta.json")) File.Delete(fullSrc + ".meta.json"); } catch { }
            try { if (File.Exists(fullSrc + ".16k.wav")) File.Delete(fullSrc + ".16k.wav"); } catch { }
            TranscriptSidecar.DeleteFor(fullSrc);
            try { File.Delete(fullSrc); } catch { }
            return dest;
        }
    }
}
