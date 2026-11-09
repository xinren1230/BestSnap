using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Finds sensitive regions: e-mails, phone numbers, ID / card numbers, URLs (via OCR word boxes) and faces.</summary>
    public static class AutoRedact
    {
        private static readonly (string Kind, Regex Rx)[] Patterns =
        {
            ("email", new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)),
            ("url",   new Regex(@"(?i)\b(?:https?://|www\.)[^\s]+|\b[a-z0-9\-]+(?:\.[a-z0-9\-]+)*\.(?:com|net|org|cn|io|dev|app|gov|edu|co|me|info)(?:/[^\s]*)?\b", RegexOptions.Compiled)),
            ("id",    new Regex(@"(?<!\d)\d{6}\s?(?:19|20)\d{2}\s?\d{2}\s?\d{2}\s?\d{3}[\dXx](?!\d)", RegexOptions.Compiled)),   // CN resident ID
            ("phone", new Regex(@"(?<!\d)1[3-9]\d[\s\-]?\d{4}[\s\-]?\d{4}(?!\d)", RegexOptions.Compiled)),                       // CN mobile
            ("phone", new Regex(@"(?<![\w.])\+?\(?\d[\d\s\-().]{6,}\d(?![\w.])", RegexOptions.Compiled)),                     // generic phone / long numbers
            ("id",    new Regex(@"(?<!\d)(?:\d[\s\-]?){12,19}(?!\d)", RegexOptions.Compiled)),                                  // card / account numbers
        };

        public sealed class Hit { public string Kind; public Rect Rect; }

        public static bool Available => PluginHost.OcrLayout != null || PluginHost.Faces != null;

        public static async Task<List<Hit>> DetectAsync(BitmapSource img, bool text = true, bool faces = true, CancellationToken ct = default)
        {
            byte[] png;
            using (var ms = new MemoryStream()) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); enc.Save(ms); png = ms.ToArray(); }
            var hits = new List<Hit>();
            var ocr = PluginHost.OcrLayout;
            if (text && ocr != null)
            {
                var lang = App.Settings.Current.OcrLanguage;
                var words = await Task.Run(() => ocr.RecognizeWordsAsync(png, lang, ct));
                hits.AddRange(FindInWords(words));
            }
            var fd = PluginHost.Faces;
            if (faces && fd != null)
            {
                var fs = await Task.Run(() => fd.DetectFacesAsync(png, ct));
                foreach (var f in fs) { var r = new Rect(f[0], f[1], f[2], f[3]); r.Inflate(f[2] * 0.15, f[3] * 0.2); hits.Add(new Hit { Kind = "face", Rect = r }); }
            }
            var bounds = new Rect(0, 0, img.PixelWidth, img.PixelHeight);
            foreach (var h in hits) h.Rect = Rect.Intersect(h.Rect, bounds);
            return hits.Where(h => !h.Rect.IsEmpty && h.Rect.Width > 1 && h.Rect.Height > 1).ToList();
        }

        /// <summary>Joins each OCR line's words with spaces, runs the patterns, and maps matches back to word boxes.</summary>
        public static List<Hit> FindInWords(IReadOnlyList<OcrWordBox> words)
        {
            var hits = new List<Hit>();
            foreach (var line in words.GroupBy(w => w.Line))
            {
                var ws = line.ToList();
                var sb = new StringBuilder(); var starts = new List<int>();
                foreach (var w in ws) { if (sb.Length > 0) sb.Append(' '); starts.Add(sb.Length); sb.Append(w.Text); }
                var s = sb.ToString();
                var used = new List<(int, int)>();
                foreach (var (kind, rx) in Patterns)
                    foreach (Match m in rx.Matches(s))
                    {
                        int a = m.Index, b = m.Index + m.Length;
                        if (kind == "phone" && m.Value.Count(char.IsDigit) < 7) continue;
                        if (used.Any(u => a < u.Item2 && b > u.Item1)) continue; // already covered by an earlier pattern
                        used.Add((a, b));
                        Rect r = Rect.Empty;
                        for (int i = 0; i < ws.Count; i++)
                        {
                            int ws0 = starts[i], we = starts[i] + ws[i].Text.Length;
                            if (ws0 < b && we > a) r.Union(new Rect(ws[i].X, ws[i].Y, ws[i].Width, ws[i].Height));
                        }
                        if (!r.IsEmpty) { r.Inflate(3, 3); hits.Add(new Hit { Kind = kind, Rect = r }); }
                    }
            }
            return hits;
        }
    }
}
