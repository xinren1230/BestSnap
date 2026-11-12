using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace BestSnap.Plugins
{
    public sealed class WhisperModelInfo
    {
        public WhisperModelInfo(string id, string fileName, long sizeBytes, string sha256, string label)
        {
            Id = id; FileName = fileName; SizeBytes = sizeBytes; Sha256 = sha256; Label = label;
        }
        public string Id { get; }
        public string FileName { get; }
        public long SizeBytes { get; }
        public string Sha256 { get; }
        public string Label { get; }
    }

    /// <summary>
    /// ggml weights from huggingface.co/ggerganov/whisper.cpp. Sizes and SHA-256 are the LFS oids
    /// from that repo (exact file bytes). Default is large-v3-turbo; q5_0 variants save download size.
    /// </summary>
    public static class WhisperModels
    {
        public const string HfBase = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
        public const string AutoId = "auto";
        public const string DefaultId = AutoId;

        public static readonly WhisperModelInfo[] All =
        {
            new WhisperModelInfo("tiny", "ggml-tiny.bin", 77_691_713,
                "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21", "tiny"),
            new WhisperModelInfo("base", "ggml-base.bin", 147_951_465,
                "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", "base"),
            new WhisperModelInfo("small", "ggml-small.bin", 487_601_967,
                "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b", "small"),
            new WhisperModelInfo("medium-q5_0", "ggml-medium-q5_0.bin", 539_212_467,
                "19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f", "medium q5_0"),
            new WhisperModelInfo("medium", "ggml-medium.bin", 1_533_763_059,
                "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208", "medium"),
            new WhisperModelInfo("large-v3-turbo-q5_0", "ggml-large-v3-turbo-q5_0.bin", 574_041_195,
                "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", "large-v3-turbo q5_0"),
            new WhisperModelInfo("large-v3-turbo", "ggml-large-v3-turbo.bin", 1_624_555_275,
                "1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", "large-v3-turbo"),
        };

        public static string[] Ids => All.Select(m => m.Id).ToArray();

        public static string[] ChoiceIds
        {
            get
            {
                var a = new string[All.Length + 1];
                a[0] = AutoId;
                for (int i = 0; i < All.Length; i++) a[i + 1] = All[i].Id;
                return a;
            }
        }

        public static bool IsAuto(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return true;
            id = id.Trim().ToLowerInvariant().Replace(' ', '-');
            return id == AutoId || id == "recommended" || id == "auto-(recommended)" || id == "auto-recommended";
        }

        public static WhisperModelInfo Get(string id)
        {
            id = NormalizeWeights(id);
            foreach (var m in All)
                if (m.Id == id) return m;
            return All.First(m => m.Id == "base");
        }

        /// <summary>Settings combo value: auto or a ggml id.</summary>
        public static string Normalize(string id)
        {
            if (IsAuto(id)) return AutoId;
            id = id.Trim().ToLowerInvariant().Replace(' ', '-');
            if (id == "turbo" || id == "large" || id == "large-v3" || id == "large-v3turbo") return "large-v3-turbo";
            if (id == "large-v3-turbo-q5" || id == "turbo-q5_0" || id == "turbo-q5") return "large-v3-turbo-q5_0";
            if (id == "medium-q5" || id == "mediumq5_0") return "medium-q5_0";
            foreach (var m in All)
                if (m.Id == id) return m.Id;
            return AutoId;
        }

        /// <summary>ggml file id; never auto.</summary>
        public static string NormalizeWeights(string id)
        {
            if (IsAuto(id)) return "base";
            string n = Normalize(id);
            return n == AutoId ? "base" : n;
        }

        public static string FileName(string id) => Get(id).FileName;
        public static long SizeBytes(string id) => Get(id).SizeBytes;
        public static string Sha256(string id) => Get(id).Sha256;
        public static string DownloadUrl(string id) => HfBase + FileName(id);

        public static string SizeLabel(string id)
        {
            double mb = SizeBytes(id) / (1024.0 * 1024.0);
            if (mb >= 900) return (mb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            return mb.ToString("0") + " MB";
        }

        public static string DisplayLabel(string id)
        {
            var m = Get(id);
            string def = m.Id == DefaultId ? " · default" : "";
            return m.Label + "  (" + SizeLabel(m.Id) + ")" + def;
        }

        /// <summary>Offset to resume a .part file. 0 means start over (missing, empty, or already complete/corrupt).</summary>
        public static long ResumeOffset(string partPath, long expectedBytes)
        {
            if (string.IsNullOrWhiteSpace(partPath) || !File.Exists(partPath)) return 0;
            long n = new FileInfo(partPath).Length;
            if (n <= 0) return 0;
            if (expectedBytes > 0 && n >= expectedBytes) return 0;
            return n;
        }

        public static bool FileLooksComplete(string path, WhisperModelInfo info)
        {
            if (string.IsNullOrWhiteSpace(path) || info == null || !File.Exists(path)) return false;
            long n = new FileInfo(path).Length;
            if (info.SizeBytes > 0) return n == info.SizeBytes;
            return n > 1_000_000;
        }

        public static string Sha256Hex(byte[] data)
        {
            if (data == null) return "";
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(data));
        }

        public static string Sha256File(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(fs));
        }

        public static bool Sha256Matches(string path, string expectedHex)
        {
            if (string.IsNullOrWhiteSpace(expectedHex) || !File.Exists(path)) return false;
            return string.Equals(Sha256File(path), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        static string ToHex(byte[] hash)
        {
            var c = new char[hash.Length * 2];
            for (int i = 0; i < hash.Length; i++)
            {
                byte b = hash[i];
                c[i * 2] = HexNibble(b >> 4);
                c[i * 2 + 1] = HexNibble(b & 0xF);
            }
            return new string(c);
        }

        static char HexNibble(int v) => (char)(v < 10 ? '0' + v : 'a' + (v - 10));
    }

    public sealed class WhisperDecodeSettings
    {
        public string Language { get; set; } = "auto";
        public string Prompt { get; set; }
        public string PreviousText { get; set; }
        /// <summary>Live chunks skip beam search (greedy is reliable on CUDA q5_0).</summary>
        public bool Live { get; set; }
        /// <summary>Reuse one WhisperProcessor across windows (live + sliced files).</summary>
        public bool ReuseProcessor { get; set; }
    }

    public static class WhisperDecode
    {
        public const string SimplifiedChineseHint = "以下使用简体中文转写，标点用，。？！、；";
        public const int PromptMaxChars = 400;

        public static readonly (string Id, string Label)[] Languages =
        {
            ("auto", "Auto"),
            ("zh", "中文"),
            ("en", "English"),
            ("ja", "日本語"),
            ("ko", "한국어"),
            ("de", "Deutsch"),
            ("fr", "Français"),
            ("es", "Español"),
            ("ru", "Русский"),
            ("yue", "粵語"),
        };

        public static string NormalizeLanguage(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "auto";
            lang = lang.Trim().ToLowerInvariant();
            if (lang == "auto" || lang == "detect") return "auto";
            if (lang == "yue" || lang == "cantonese" || lang == "zh-hk" || lang == "zh_hk" || lang == "zh-yue") return "yue";
            if (lang.StartsWith("zh") || lang == "chinese" || lang == "cn" || lang == "cmn") return "zh";
            if (lang == "english" || lang.StartsWith("en")) return "en";
            if (lang.StartsWith("ja") || lang == "jp" || lang == "japanese") return "ja";
            if (lang.StartsWith("ko") || lang == "kr" || lang == "korean") return "ko";
            if (lang.StartsWith("de") || lang == "german") return "de";
            if (lang.StartsWith("fr") || lang == "french") return "fr";
            if (lang.StartsWith("es") || lang == "spanish") return "es";
            if (lang.StartsWith("ru") || lang == "russian") return "ru";
            if (lang.Length == 2) return lang;
            return "auto";
        }

        public static bool IsChinese(string lang)
        {
            lang = NormalizeLanguage(lang);
            return lang == "zh" || lang == "yue";
        }

        /// <summary>
        /// whisper.cpp <c>detect_language=true</c> means detect-only (no segments).
        /// Auto transcription must pass language "auto" with this left false.
        /// </summary>
        public const bool DetectLanguageOnly = false;

        /// <summary>Value for Whisper.net <c>WithLanguage</c>. "auto" detects AND transcribes.</summary>
        public static string ProcessorLanguage(string lang) => NormalizeLanguage(lang);

        /// <summary>Beam search is CPU file-transcribe only; live/GPU uses greedy.</summary>
        public static bool PreferBeamSearch(WhisperBackend backend, bool live) =>
            !live && backend == WhisperBackend.Cpu;

        public static bool IsJunkCue(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            text = text.Trim();
            if (text.Length >= 2 && text[0] == '[' && text[text.Length - 1] == ']')
            {
                string inner = text.Substring(1, text.Length - 2).Trim();
                if (inner.Equals("BLANK_AUDIO", StringComparison.OrdinalIgnoreCase)) return true;
                if (inner.Equals("silence", StringComparison.OrdinalIgnoreCase)) return true;
                if (inner.Equals("inaudible", StringComparison.OrdinalIgnoreCase)) return true;
                if (inner.Equals("music", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string NormalizeCueText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || IsJunkCue(text)) return "";
            return text;
        }

        public static string Trunc(string s, int max)
        {
            s ??= "";
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            if (max < 8) max = 8;
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        public static string ComposePrompt(string language, string userPrompt, string previousText, int maxChars = PromptMaxChars)
        {
            if (maxChars < 8) maxChars = PromptMaxChars;
            language = NormalizeLanguage(language);
            string hint = IsChinese(language) ? SimplifiedChineseHint : "";
            string user = (userPrompt ?? "").Trim();
            string prev = (previousText ?? "").Trim();
            string head = JoinNonEmpty(" ", hint, user);
            if (head.Length >= maxChars) return head.Substring(0, maxChars);
            int room = maxChars - head.Length - (head.Length > 0 && prev.Length > 0 ? 1 : 0);
            if (prev.Length > room) prev = room <= 0 ? "" : prev.Substring(prev.Length - room);
            return JoinNonEmpty(" ", head, prev);
        }

        static string JoinNonEmpty(string sep, params string[] parts)
        {
            string s = "";
            foreach (var p in parts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (s.Length > 0) s += sep;
                s += p;
            }
            return s;
        }
    }
}
