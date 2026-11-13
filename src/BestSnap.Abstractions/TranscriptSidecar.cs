using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Transcript next to the audio file: Rec_xxx.wav → Rec_xxx.txt, UTF-8 with BOM (Notepad / Chinese).
    /// Follows the temp-then-Save flow: stay beside the temp media; copy on Save; delete on discard.
    /// </summary>
    public static class TranscriptSidecar
    {
        public static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        public static string PathFor(string media)
        {
            if (string.IsNullOrWhiteSpace(media)) return "";
            return Path.ChangeExtension(media, ".txt");
        }

        public static bool IsTranscriptPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Audio-only recordings auto-transcribe when the setting is on. Screen video does not.</summary>
        public static bool ShouldAutoTranscribe(bool settingOn, string mediaPath) =>
            settingOn && TrimRangeMath.IsAudioPath(mediaPath);

        public static void WriteUtf8Bom(string path, string text)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text ?? "", Utf8Bom);
        }

        public static void WriteForMedia(string media, string text) =>
            WriteUtf8Bom(PathFor(media), text);

        public static void WriteCues(string media, IReadOnlyList<TranscriptCue> cues) =>
            WriteForMedia(media, TranscriptFormat.PlainText(cues));

        public static bool CopyWithMedia(string fromMedia, string toMedia)
        {
            if (string.IsNullOrWhiteSpace(fromMedia) || string.IsNullOrWhiteSpace(toMedia)) return false;
            string src = PathFor(fromMedia);
            string dst = PathFor(toMedia);
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) return false;
            try
            {
                if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                    return true;
                var dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(src, dst, overwrite: true);
                return true;
            }
            catch { return false; }
        }

        public static void DeleteFor(string media)
        {
            string p = PathFor(media);
            if (string.IsNullOrEmpty(p)) return;
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        public static bool HasUtf8Bom(byte[] bytes) =>
            bytes != null && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }
}
