using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Transcript chunking for map-reduce LLM summaries, plus an offline TextRank-style extractive summarizer
    /// that works for Chinese and English. No network.
    /// </summary>
    public static class SummarizeMath
    {
        public const int DefaultChunkChars = 6000;

        public static bool LooksChinese(string text)
        {
            int cjk = 0, n = 0;
            if (text == null) return false;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch)) continue;
                n++;
                if (ch >= 0x4E00 && ch <= 0x9FFF) cjk++;
            }
            return n > 0 && cjk * 2 >= n;
        }

        public static List<string> SplitSentences(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            var sb = new StringBuilder();
            foreach (var ch in text)
            {
                sb.Append(ch);
                if (ch == '。' || ch == '！' || ch == '？' || ch == '!' || ch == '?' || ch == '.' || ch == '\n')
                {
                    var s = sb.ToString().Trim();
                    if (s.Length > 0) list.Add(s);
                    sb.Clear();
                }
            }
            var tail = sb.ToString().Trim();
            if (tail.Length > 0) list.Add(tail);
            return list;
        }

        public static List<List<TranscriptCue>> ChunkCues(IReadOnlyList<TranscriptCue> cues, int maxChars)
        {
            var chunks = new List<List<TranscriptCue>>();
            if (cues == null || cues.Count == 0) return chunks;
            if (maxChars < 8) maxChars = 8;
            var cur = new List<TranscriptCue>();
            int len = 0;
            foreach (var c in cues)
            {
                int add = (c.Text ?? "").Length + 8;
                if (cur.Count > 0 && len + add > maxChars)
                {
                    chunks.Add(cur);
                    cur = new List<TranscriptCue>();
                    len = 0;
                }
                cur.Add(c);
                len += add;
            }
            if (cur.Count > 0) chunks.Add(cur);
            return chunks;
        }

        public static List<string> ChunkText(string text, int maxChars)
        {
            var chunks = new List<string>();
            var sents = SplitSentences(text ?? "");
            if (sents.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(text)) chunks.Add(text.Trim());
                return chunks;
            }
            if (maxChars < 8) maxChars = 8;
            var sb = new StringBuilder();
            foreach (var s in sents)
            {
                if (sb.Length > 0 && sb.Length + s.Length + 1 > maxChars)
                {
                    chunks.Add(sb.ToString());
                    sb.Clear();
                }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(s);
            }
            if (sb.Length > 0) chunks.Add(sb.ToString());
            return chunks;
        }

        public static string ExtractiveMarkdown(IReadOnlyList<TranscriptCue> cues, int keyCount = 5)
        {
            cues ??= Array.Empty<TranscriptCue>();
            var items = new List<(string Sent, double Start)>();
            foreach (var c in cues)
            {
                foreach (var s in SplitSentences(c.Text ?? ""))
                    items.Add((s, c.Start));
            }
            if (items.Count == 0)
            {
                bool zhEmpty = false;
                return FormatMarkdown(zhEmpty, basic: true, overview: "", keys: Array.Empty<string>(), actions: Array.Empty<string>());
            }

            string all = string.Join(" ", items.ConvertAll(i => i.Sent));
            bool zh = LooksChinese(all);
            int n = items.Count;
            var tokens = new HashSet<string>[n];
            for (int i = 0; i < n; i++) tokens[i] = Tokens(items[i].Sent);
            var sim = new double[n, n];
            var outW = new double[n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double s = Overlap(tokens[i], tokens[j]);
                    sim[i, j] = s;
                    outW[i] += s;
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (outW[i] <= 0) continue;
                for (int j = 0; j < n; j++) sim[i, j] /= outW[i];
            }
            var rank = PageRank(sim);
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => rank[b].CompareTo(rank[a]));

            int overN = n == 1 ? 1 : 2;
            var overviewParts = new List<string>();
            for (int k = 0; k < overN && k < n; k++) overviewParts.Add(items[order[k]].Sent);

            int take = Math.Min(Math.Max(1, keyCount), n);
            var keyIdx = new List<int>();
            for (int k = 0; k < take; k++) keyIdx.Add(order[k]);
            keyIdx.Sort();
            var keys = new List<string>();
            foreach (var i in keyIdx)
                keys.Add(Stamp(items[i].Start) + items[i].Sent);

            var actions = new List<string>();
            foreach (var it in items)
            {
                if (IsAction(it.Sent)) actions.Add(Stamp(it.Start) + it.Sent);
            }

            return FormatMarkdown(zh, basic: true, string.Join(zh ? "" : " ", overviewParts), keys, actions);
        }

        public static string FormatMarkdown(bool zh, bool basic, string overview, IList<string> keys, IList<string> actions)
        {
            var sb = new StringBuilder();
            sb.Append(zh ? "# 摘要" : "# Summary").Append("\r\n");
            if (basic) sb.Append(zh ? "（本机抽取式摘要）" : "(basic on-device extractive summary)").Append("\r\n");
            sb.Append("\r\n").Append(zh ? "## 概述" : "## Overview").Append("\r\n");
            sb.Append(string.IsNullOrWhiteSpace(overview) ? (zh ? "（无）" : "(none)") : overview.Trim()).Append("\r\n\r\n");
            sb.Append(zh ? "## 要点" : "## Key points").Append("\r\n");
            if (keys == null || keys.Count == 0) sb.Append(zh ? "- （无）" : "- (none)").Append("\r\n");
            else foreach (var k in keys) sb.Append("- ").Append(k).Append("\r\n");
            sb.Append("\r\n").Append(zh ? "## 待办" : "## Action items").Append("\r\n");
            if (actions == null || actions.Count == 0) sb.Append(zh ? "- （无）" : "- (none)").Append("\r\n");
            else foreach (var a in actions) sb.Append("- ").Append(a).Append("\r\n");
            return sb.ToString();
        }

        public static string Stamp(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) return "";
            var t = TimeSpan.FromSeconds(seconds);
            return string.Format(CultureInfo.InvariantCulture, "[{0:00}:{1:00}] ", (int)t.TotalMinutes, t.Seconds);
        }

        static readonly Regex ActionEn = new Regex(@"\b(please|should|must|need to|todo|to-do|action item)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex ActionZh = new Regex(@"需要|请|务必|记得|应当|应该|待办");

        public static bool IsAction(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return false;
            return ActionZh.IsMatch(sentence) || ActionEn.IsMatch(sentence);
        }

        static HashSet<string> Tokens(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (s == null) return set;
            var word = new StringBuilder();
            foreach (var ch in s)
            {
                if (ch >= 0x4E00 && ch <= 0x9FFF) { set.Add(ch.ToString()); continue; }
                if (char.IsLetterOrDigit(ch)) word.Append(ch);
                else if (word.Length > 0)
                {
                    if (word.Length > 1) set.Add(word.ToString());
                    word.Clear();
                }
            }
            if (word.Length > 1) set.Add(word.ToString());
            return set;
        }

        static double Overlap(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;
            int n = 0;
            foreach (var x in a) if (b.Contains(x)) n++;
            return n / Math.Sqrt((double)a.Count * b.Count);
        }

        static double[] PageRank(double[,] sim, int iter = 20)
        {
            int n = sim.GetLength(0);
            var r = new double[n];
            if (n == 0) return r;
            for (int i = 0; i < n; i++) r[i] = 1.0 / n;
            const double d = 0.85;
            for (int it = 0; it < iter; it++)
            {
                var nr = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double sum = 0;
                    for (int j = 0; j < n; j++) sum += sim[j, i] * r[j];
                    nr[i] = (1 - d) / n + d * sum;
                }
                r = nr;
            }
            return r;
        }
    }
}
