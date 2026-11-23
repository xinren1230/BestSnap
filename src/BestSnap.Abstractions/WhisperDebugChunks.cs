using System;
using System.IO;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Optional 16 kHz mono WAV dumps of live Whisper chunks.
    /// Enable with BESTSNAP_DEBUG_WHISPER=1 (writes %LocalAppData%\BestSnap\debug).
    /// </summary>
    public static class WhisperDebugChunks
    {
        public const int KeepLatest = 8;

        public static bool Enabled
        {
            get
            {
                string v = AppIdentity.GetEnv("DEBUG_WHISPER");
                if (string.IsNullOrWhiteSpace(v)) return false;
                v = v.Trim();
                return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string DirectoryPath => AppPaths.Debug;

        public static string Write(int index, short[] pcm, string dir = null)
        {
            if (pcm == null || pcm.Length == 0) return null;
            dir = string.IsNullOrWhiteSpace(dir) ? DirectoryPath : dir;
            Directory.CreateDirectory(dir);
            string name = "chunk-" + index.ToString("000") + "-" + DateTime.Now.ToString("HHmmss") + ".wav";
            string path = Path.Combine(dir, name);
            using (var fs = File.Create(path))
                AudioPcm.WriteWav(fs, pcm, AudioPcm.WhisperRate, 1);
            Prune(dir);
            return path;
        }

        public static void Prune(string dir, int keep = KeepLatest)
        {
            if (string.IsNullOrWhiteSpace(dir) || keep < 1 || !Directory.Exists(dir)) return;
            string[] files;
            try { files = Directory.GetFiles(dir, "chunk-*.wav"); }
            catch { return; }
            if (files.Length <= keep) return;
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int drop = files.Length - keep;
            for (int i = 0; i < drop; i++)
            {
                try { File.Delete(files[i]); } catch { }
            }
        }
    }
}
