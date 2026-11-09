using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BestSnap.Services
{
    /// <summary>
    /// JSON-based i18n. Source strings (keys) are the Chinese UI texts. Languages are loaded from embedded
    /// resources (Lang/*.json) and then from &lt;exe dir&gt;\lang\*.json, which may override or add languages.
    /// File format: { "_name": "Deutsch", "_code": "de", "strings": { "中文原文": "Übersetzung", ... } }.
    /// Lookup: current language → English → key.
    /// </summary>
    public static class L
    {
        public sealed class Lang { public string Code, Name; public Dictionary<string, string> Strings = new Dictionary<string, string>(); }

        private static Dictionary<string, Lang> _langs;
        private static Lang _cur, _en;
        private static bool _resolvedWithSettings;
        public static event Action LanguageChanged;

        public static string CurrentCode { get { Ensure(); return _cur?.Code ?? "zh-CN"; } }
        public static bool English => CurrentCode == "en";

        public static IReadOnlyList<Lang> Available { get { Load(); return _langs.Values.OrderBy(Order).ThenBy(l => l.Code).ToList(); } }

        private static readonly string[] Builtin = { "zh-CN", "zh-TW", "en", "de", "ja", "ko", "fr", "es" };
        private static int Order(Lang l) { int i = Array.IndexOf(Builtin, l.Code); return i < 0 ? 100 : i; }

        /// <summary>Choices for menus: auto first, then each language by native name.</summary>
        public static List<KeyValuePair<string, string>> Choices()
        {
            var r = new List<KeyValuePair<string, string>> { new("auto", T("跟随系统") + " (auto)") };
            foreach (var l in Available) r.Add(new(l.Code, l.Name));
            return r;
        }

        public static string T(string key)
        {
            if (key == null) return null;
            Ensure();
            if (_cur != null && _cur.Strings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            if (_en != null && _cur?.Code != "zh-CN" && _en.Strings.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return key;
        }

        /// <summary>Re-read the setting (and lang folder) and notify listeners.</summary>
        public static void Reload()
        {
            _langs = null; _cur = null; _resolvedWithSettings = false;
            Ensure();
            try { LanguageChanged?.Invoke(); } catch (Exception ex) { Logger.Error("LanguageChanged", ex); }
        }

        private static void Ensure()
        {
            if (_cur != null && _resolvedWithSettings) return;
            Load();
            var setting = App.Settings?.Current?.Language;
            _cur = Resolve(setting);
            _en = _langs.TryGetValue("en", out var e) ? e : null;
            _resolvedWithSettings = App.Settings?.Current != null;
        }

        private static Lang Resolve(string setting)
        {
            string code = (setting ?? "auto").Trim();
            if (code.Equals("zh", StringComparison.OrdinalIgnoreCase)) code = "zh-CN";
            if (code.Length == 0 || code.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var ci = CultureInfo.CurrentUICulture;
                string n = ci.Name;
                if (ci.TwoLetterISOLanguageName == "zh")
                    code = (n.Contains("Hant") || n.EndsWith("TW") || n.EndsWith("HK") || n.EndsWith("MO")) ? "zh-TW" : "zh-CN";
                else if (_langs.ContainsKey(n)) code = n;
                else code = ci.TwoLetterISOLanguageName;
            }
            var hit = _langs.Values.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                   ?? _langs.Values.FirstOrDefault(l => l.Code.Split('-')[0].Equals(code.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            return hit ?? (_langs.TryGetValue("en", out var en) ? en : null);
        }

        private static void Load()
        {
            if (_langs != null) return;
            var d = new Dictionary<string, Lang>(StringComparer.OrdinalIgnoreCase);
            var asm = typeof(L).Assembly;
            foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith("lang.") && n.EndsWith(".json")))
                try { using var st = asm.GetManifestResourceStream(res); Merge(d, Parse(st, res.Substring(5, res.Length - 10))); }
                catch (Exception ex) { Logger.Error("lang resource " + res, ex); }
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "lang");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                        try { using var st = File.OpenRead(f); Merge(d, Parse(st, Path.GetFileNameWithoutExtension(f))); }
                        catch (Exception ex) { Logger.Error("lang file " + f, ex); }
            }
            catch { }
            if (!d.ContainsKey("zh-CN")) d["zh-CN"] = new Lang { Code = "zh-CN", Name = "简体中文" };
            _langs = d;
        }

        private static void Merge(Dictionary<string, Lang> d, Lang l)
        {
            if (l == null || string.IsNullOrWhiteSpace(l.Code)) return;
            if (!d.TryGetValue(l.Code, out var ex)) { d[l.Code] = l; return; }
            if (!string.IsNullOrWhiteSpace(l.Name)) ex.Name = l.Name;
            foreach (var kv in l.Strings) ex.Strings[kv.Key] = kv.Value;
        }

        private static Lang Parse(Stream st, string fallbackCode)
        {
            using var doc = JsonDocument.Parse(st, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            var l = new Lang { Code = fallbackCode, Name = fallbackCode };
            if (root.TryGetProperty("_code", out var c) && c.ValueKind == JsonValueKind.String) l.Code = c.GetString();
            if (root.TryGetProperty("_name", out var n) && n.ValueKind == JsonValueKind.String) l.Name = n.GetString();
            var strings = root.TryGetProperty("strings", out var s) && s.ValueKind == JsonValueKind.Object ? s : root;
            foreach (var p in strings.EnumerateObject())
                if (!p.Name.StartsWith("_") && p.Value.ValueKind == JsonValueKind.String) l.Strings[p.Name] = p.Value.GetString();
            return l;
        }
    }
}
