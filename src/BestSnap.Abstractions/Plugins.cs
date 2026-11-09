using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BestSnap.Plugins
{
    /// <summary>Marker for every BestSnap plugin. Plugins are discovered in &lt;exe&gt;\plugins\&lt;Name&gt;\*.dll.</summary>
    public interface IBestSnapPlugin
    {
        string Id { get; }
        string DisplayName { get; }
    }

    /// <summary>Text recognition. The image is passed as PNG bytes so the contract has no UI-framework dependency.</summary>
    public interface IOcrEngine : IBestSnapPlugin
    {
        bool IsAvailable { get; }
        /// <summary>BCP-47 language tags installed on this machine (e.g. "zh-Hans-CN", "en-US").</summary>
        IReadOnlyList<string> Languages { get; }
        /// <param name="language">null/empty = user profile languages.</param>
        Task<string> RecognizeAsync(byte[] png, string language, CancellationToken ct);
    }

    /// <summary>Lossless trimming of a recorded MP4.</summary>
    public interface IVideoTrimmer : IBestSnapPlugin
    {
        Task TrimAsync(string input, string output, TimeSpan start, TimeSpan end, IProgress<double> progress, CancellationToken ct);
    }

    /// <summary>One configured upload destination. Secret is already decrypted by the host (stored with DPAPI).</summary>
    public sealed class UploadTarget
    {
        public string Name { get; set; }
        public string Kind { get; set; }          // uploader Id: "folder", "ftp", "webdav", or a plugin id
        public string Url { get; set; }           // folder path, ftp://host/dir, https://host/dav/dir
        public string User { get; set; }
        public string ProtectedSecret { get; set; } // DPAPI (CurrentUser) base64 – never plain text on disk
        public string PublicUrlPrefix { get; set; } // optional: URL returned/copied after upload
    }

    public interface IUploader : IBestSnapPlugin
    {
        /// <returns>Public URL (if PublicUrlPrefix set) or the remote location.</returns>
        Task<string> UploadAsync(string localFile, string remoteName, UploadTarget target, string secret, CancellationToken ct);
    }
}

namespace BestSnap.Plugins
{
    /// <summary>A recognized word with its bounding box in source-image pixels.</summary>
    public sealed class OcrWordBox
    {
        public string Text { get; set; }
        public int Line { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>OCR that also returns word boxes (used by auto-redact).</summary>
    public interface IOcrLayoutEngine : IBestSnapPlugin
    {
        bool IsAvailable { get; }
        System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<OcrWordBox>> RecognizeWordsAsync(byte[] png, string language, System.Threading.CancellationToken ct);
    }

    /// <summary>Face detection; returns rectangles (x, y, w, h) in source-image pixels.</summary>
    public interface IFaceDetector : IBestSnapPlugin
    {
        bool IsAvailable { get; }
        System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<int[]>> DetectFacesAsync(byte[] png, System.Threading.CancellationToken ct);
    }

    /// <summary>Webcam frame source (used for the circular picture-in-picture bubble).</summary>
    public interface IWebcamSource : IBestSnapPlugin
    {
        /// <summary>(id, display name) pairs.</summary>
        System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<string[]>> ListCamerasAsync();
        /// <summary>Starts streaming BGRA32 frames (bytes, width, height) on a background thread. Dispose to stop.</summary>
        System.Threading.Tasks.Task<System.IDisposable> StartAsync(string cameraIdOrName, System.Action<byte[], int, int> onFrame);
    }
}

namespace BestSnap.Plugins
{
    /// <summary>Image → LaTeX (offline formula recognition). Model files live in a host-managed folder.</summary>
    public interface IFormulaRecognizer : IBestSnapPlugin
    {
        /// <summary>True when the runtime is usable; the model files may still need downloading.</summary>
        bool IsAvailable { get; }
        System.Threading.Tasks.Task<string> RecognizeLatexAsync(byte[] png, string modelDir, System.Threading.CancellationToken ct);
    }

    /// <summary>Shared OCR text post-processing.</summary>
    public static class OcrText
    {
        // Only long letter runs + multi-digit: "Windows11" → "Windows 11". Not MP4, Win10, H2O.
        private static readonly Regex LetterDigit = new Regex(
            @"(?<=[A-Za-z]{4,})(?=\d{2,})|(?<=\d{2,})(?=[A-Za-z]{4,})", RegexOptions.Compiled);

        /// <summary>True for CJK letters/kana/hangul/CJK punctuation — not ASCII or fullwidth Latin/digits.</summary>
        public static bool IsCjk(char c)
        {
            int u = c;
            if (u >= 0x4E00 && u <= 0x9FFF) return true; // CJK Unified
            if (u >= 0x3400 && u <= 0x4DBF) return true; // Ext A
            if (u >= 0xF900 && u <= 0xFAFF) return true; // Compatibility ideographs
            if (u >= 0x2E80 && u <= 0x2FFF) return true; // radicals / IDC
            if (u >= 0x3040 && u <= 0x30FF) return true; // hiragana + katakana
            if (u >= 0x3100 && u <= 0x312F) return true; // bopomofo
            if (u >= 0x31A0 && u <= 0x31BF) return true; // bopomofo ext
            if (u >= 0x1100 && u <= 0x11FF) return true; // hangul jamo
            if (u >= 0x3130 && u <= 0x318F) return true; // hangul compatibility
            if (u >= 0xAC00 && u <= 0xD7AF) return true; // hangul syllables
            if (u >= 0x3001 && u <= 0x303F) return true; // CJK punctuation (not U+3000 ideographic space)
            if (u >= 0xFF66 && u <= 0xFF9D) return true; // halfwidth katakana
            if (u >= 0xFF01 && u <= 0xFF0F) return true; // fullwidth punctuation
            if (u >= 0xFF1A && u <= 0xFF20) return true;
            if (u >= 0xFF3B && u <= 0xFF40) return true;
            if (u >= 0xFF5B && u <= 0xFF65) return true;
            return false;
        }

        public static bool IsLatinTokenChar(char c) => c < 0x2E80 && char.IsLetterOrDigit(c);

        /// <summary>Full-width / CJK punctuation and quotes — no space on either side.</summary>
        public static bool IsCjkPunct(char c)
        {
            int u = c;
            if (u >= 0x3001 && u <= 0x303F) return true;
            if (u >= 0xFF01 && u <= 0xFF0F) return true;
            if (u >= 0xFF1A && u <= 0xFF20) return true;
            if (u >= 0xFF3B && u <= 0xFF40) return true;
            if (u >= 0xFF5B && u <= 0xFF65) return true;
            if (u >= 0x2018 && u <= 0x201F) return true; // ‘’“”
            if ("、。；：！？，．「」『』《》〈〉【】（）".IndexOf(c) >= 0) return true;
            return false;
        }
        static bool IsGapSpace(char c) => c == ' ' || c == '\t' || c == '\u00A0' || c == '\u3000';

        /// <summary>Removes spaces only between two CJK characters (Windows OCR inserts one between every glyph). Latin tokens keep their spaces.</summary>
        public static string JoinCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (IsGapSpace(c) && sb.Length > 0 && IsCjk(sb[sb.Length - 1]))
                {
                    int j = i;
                    while (j < s.Length && IsGapSpace(s[j])) j++;
                    if (j < s.Length && IsCjk(s[j])) { i = j - 1; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Re-inserts spaces between Latin tokens using character positions (CTC timesteps or box X).
        /// Typical letter spacing is the median inter-character CTC gap; a Latin–Latin word break is
        /// added only when the gap is clearly larger (&gt; 1.8× median). Prefer leaving packed words
        /// (<c>Thisis</c>) over splitting <c>World</c>. Model-emitted space glyphs are kept. Letter↔digit
        /// inside one token (MP4) needs a still larger gap. CJK↔Latin ignores the threshold except
        /// next to CJK punctuation (no space before/after ：，。).
        /// </summary>
        public static string RestoreLatinSpaces(IReadOnlyList<(string Text, double Position)> glyphs)
            => RestoreLatinSpaces(glyphs, 0);

        public static string RestoreLatinSpaces(IReadOnlyList<(string Text, double Position)> glyphs, double em)
        {
            if (glyphs == null || glyphs.Count == 0) return "";
            if (glyphs.Count == 1) return glyphs[0].Text ?? "";

            var gaps = new double[glyphs.Count - 1];
            var samples = new List<double>();
            for (int i = 1; i < glyphs.Count; i++)
            {
                gaps[i - 1] = glyphs[i].Position - glyphs[i - 1].Position;
                // Median from real character-to-character gaps only (ignore space glyphs so they
                // neither collapse the baseline nor look like extra word breaks).
                if (gaps[i - 1] > 1e-6 && !IsSpaceGlyph(glyphs[i - 1].Text) && !IsSpaceGlyph(glyphs[i].Text))
                    samples.Add(gaps[i - 1]);
            }
            double median = Median(samples);
            if (median <= 0) median = 1;

            var sb = new StringBuilder();
            for (int i = 0; i < glyphs.Count; i++)
            {
                var t = glyphs[i].Text ?? "";
                if (i > 0)
                {
                    var prev = glyphs[i - 1].Text ?? "";
                    // Keep model-emitted spaces; only consider inserting when neither side is already a space.
                    if (!EndsWithSpace(prev) && !StartsWithSpace(t)
                        && ShouldInsertLatinSpace(LastSignificant(prev), FirstSignificant(t), gaps[i - 1], median, em))
                        sb.Append(' ');
                }
                sb.Append(t);
            }
            return sb.ToString();
        }

        static bool IsSentencePunct(char c) => ",.;:!?，。；：！？、".IndexOf(c) >= 0;

        /// <summary>CJK↔Latin (not next to CJK punct), or ASCII comma before a Latin letter.</summary>
        static bool ShouldSpaceScripts(char prev, char next)
        {
            if (prev == '\0' || next == '\0') return false;
            if (IsCjkPunct(prev) || IsCjkPunct(next)) return false;
            if (IsCjk(prev) && IsCjk(next)) return false;
            if ((IsCjk(prev) && IsLatinTokenChar(next)) || (IsLatinTokenChar(prev) && IsCjk(next))) return true;
            // "World,open" → "World, open"; not "1,000"; not full-width ：，。
            if (IsSentencePunct(prev) && !IsCjkPunct(prev) && char.IsLetter(next) && next < 0x2E80) return true;
            return false;
        }

        static bool ShouldInsertLatinSpace(char prev, char next, double gap, double median, double em)
        {
            if (ShouldSpaceScripts(prev, next)) return true;
            if (!IsLatinTokenChar(prev) || !IsLatinTokenChar(next)) return false;
            bool letterDigit = (char.IsLetter(prev) && char.IsDigit(next)) || (char.IsDigit(prev) && char.IsLetter(next));
            // Conservative: CTC letter pitch varies; never treat a modest extra step as a word gap.
            if (letterDigit) return gap > median * 2.4;
            _ = em;
            return gap > median * 1.8;
        }

        /// <summary>Drop spaces immediately before/after full-width punctuation and CJK quotes.</summary>
        public static string SqueezeCjkPunctSpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\n' && c != '\r' && (IsGapSpace(c) || char.IsWhiteSpace(c)))
                {
                    if (sb.Length > 0 && IsCjkPunct(sb[sb.Length - 1])) continue;
                    int j = i;
                    while (j < s.Length && s[j] != '\n' && s[j] != '\r' && (IsGapSpace(s[j]) || char.IsWhiteSpace(s[j]))) j++;
                    if (j < s.Length && IsCjkPunct(s[j])) { i = j - 1; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Inserts spaces at CJK↔Latin boundaries and after ',' before a Latin word. Idempotent.</summary>
        public static string InsertScriptAndPunctSpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && !char.IsWhiteSpace(s[i - 1]) && !char.IsWhiteSpace(c) && ShouldSpaceScripts(s[i - 1], c))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        static bool IsSpaceGlyph(string s) => !string.IsNullOrEmpty(s) && StartsWithSpace(s) && EndsWithSpace(s);

        static double Median(List<double> xs)
        {
            int n = xs == null ? 0 : xs.Count;
            if (n == 0) return 0;
            xs.Sort();
            if ((n & 1) == 1) return xs[n / 2];
            return (xs[n / 2 - 1] + xs[n / 2]) / 2.0;
        }

        static bool EndsWithSpace(string s) { if (string.IsNullOrEmpty(s)) return false; return IsGapSpace(s[s.Length - 1]) || char.IsWhiteSpace(s[s.Length - 1]); }
        static bool StartsWithSpace(string s) { if (string.IsNullOrEmpty(s)) return false; return IsGapSpace(s[0]) || char.IsWhiteSpace(s[0]); }
        static char LastSignificant(string s)
        {
            if (string.IsNullOrEmpty(s)) return '\0';
            for (int i = s.Length - 1; i >= 0; i--) if (!char.IsWhiteSpace(s[i])) return s[i];
            return '\0';
        }
        static char FirstSignificant(string s)
        {
            if (string.IsNullOrEmpty(s)) return '\0';
            for (int i = 0; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) return s[i];
            return '\0';
        }

        /// <summary>"Windows11" → "Windows 11". Short tokens (MP4, Win10, H2O) are left alone.</summary>
        public static string SplitLetterDigit(string s) => string.IsNullOrEmpty(s) ? s : LetterDigit.Replace(s, " ");

        /// <summary>Join CJK-only gaps, split Windows11, space CJK↔Latin and ",word", squeeze CJK punct.</summary>
        public static string PostProcess(string s) => SqueezeCjkPunctSpaces(InsertScriptAndPunctSpaces(SplitLetterDigit(JoinCjk(s))));

        public static int CountCjk(string s)
        {
            int n = 0;
            if (s != null) foreach (var c in s)
                if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x3040 && c <= 0x30FF) || (c >= 0xAC00 && c <= 0xD7AF)) n++;
            return n;
        }
    }
}
