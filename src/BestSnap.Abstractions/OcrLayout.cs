using System;
using System.Collections.Generic;
using System.Text;

namespace BestSnap.Plugins
{
    /// <summary>One recognized snippet with an axis-aligned box in source-image pixels.</summary>
    public readonly struct OcrSpan
    {
        public OcrSpan(string text, double x0, double y0, double x1, double y1)
        {
            Text = text ?? "";
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1;
        }
        public string Text { get; }
        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public double Width => X1 - X0;
        public double Height => Y1 - Y0;
        public double Cy => (Y0 + Y1) / 2;
    }

    /// <summary>
    /// Turns raw OCR boxes into readable plain text: reading order, line merge, paragraph
    /// gaps, rough indent, simple columns, and wrap-join inside a paragraph.
    /// </summary>
    public static class OcrLayout
    {
        public static string Format(IReadOnlyList<OcrSpan> boxes)
        {
            if (boxes == null || boxes.Count == 0) return "";
            var items = new List<OcrSpan>(boxes.Count);
            foreach (var b in boxes)
            {
                var t = (b.Text ?? "").Trim();
                if (t.Length == 0) continue;
                items.Add(new OcrSpan(t, b.X0, b.Y0, b.X1, b.Y1));
            }
            if (items.Count == 0) return "";
            if (items.Count == 1) return items[0].Text;

            var heights = new List<double>(items.Count);
            double pageLeft = double.MaxValue;
            foreach (var b in items) { heights.Add(Math.Max(1, b.Height)); if (b.X0 < pageLeft) pageLeft = b.X0; }
            double medianH = Median(heights);
            if (medianH <= 0) medianH = 16;

            var lines = ClusterLines(items);
            var built = new List<Line>(lines.Count);
            foreach (var cells in lines)
            {
                var line = JoinLine(cells, medianH);
                int ind = IndentSpaces(line.X0, pageLeft, medianH);
                if (ind > 0 && line.Text.IndexOf('\t') < 0)
                    line.Text = new string(' ', ind) + line.Text;
                built.Add(line);
            }

            double typical = TypicalLineSpacing(built, medianH);
            var parts = new List<string>();
            for (int i = 0; i < built.Count; i++)
            {
                if (i == 0) { parts.Add(built[i].Text); continue; }
                if (IsParagraphBreak(built[i - 1], built[i], typical, medianH, built.Count))
                {
                    if (parts.Count == 0 || parts[parts.Count - 1].Length != 0) parts.Add("");
                    parts.Add(built[i].Text);
                    continue;
                }
                if (ShouldJoin(built[i - 1], built[i], medianH))
                    parts[parts.Count - 1] = JoinWrapped(parts[parts.Count - 1], built[i].Text);
                else
                    parts.Add(built[i].Text);
            }
            return string.Join("\n", parts).TrimEnd();
        }

        static bool IsParagraphBreak(Line prev, Line next, double typical, double medianH, int lineCount)
        {
            double delta = next.Cy - prev.Cy;
            double ws = next.Y0 - prev.Y1; // true vertical whitespace (negative if unclip boxes overlap)
            if (lineCount <= 2)
                return ws > medianH * 2.0 && delta > medianH * 2.5;
            return delta > typical * 1.8 && ws > medianH * 1.0;
        }

        struct Line
        {
            public string Text;
            public double X0, Y0, Y1, Cy;
            public bool Tab;
        }

        /// <summary>Typical single-line leading from the tight cluster of Cy deltas (ignores large paragraph gaps).</summary>
        static double TypicalLineSpacing(List<Line> lines, double medianH)
        {
            if (lines == null || lines.Count < 2) return Math.Max(1, medianH);
            var d = new List<double>();
            for (int i = 1; i < lines.Count; i++)
            {
                double g = lines[i].Cy - lines[i - 1].Cy;
                if (g > 1) d.Add(g);
            }
            if (d.Count == 0) return Math.Max(1, medianH);
            double min = d[0];
            foreach (var x in d) if (x < min) min = x;
            var tight = new List<double>();
            foreach (var x in d) if (x <= min * 1.35) tight.Add(x);
            double t = Median(tight.Count > 0 ? tight : d);
            return t > 0 ? t : Math.Max(1, medianH);
        }

        static List<List<OcrSpan>> ClusterLines(List<OcrSpan> items)
        {
            var ordered = new List<OcrSpan>(items);
            ordered.Sort((a, b) =>
            {
                int c = a.Cy.CompareTo(b.Cy);
                return c != 0 ? c : a.X0.CompareTo(b.X0);
            });
            var lines = new List<List<OcrSpan>>();
            foreach (var b in ordered)
            {
                List<OcrSpan> hit = null;
                double best = double.MaxValue;
                foreach (var line in lines)
                {
                    double ly0 = 0, ly1 = 0;
                    foreach (var x in line) { ly0 += x.Y0; ly1 += x.Y1; }
                    ly0 /= line.Count; ly1 /= line.Count;
                    double overlap = Math.Min(b.Y1, ly1) - Math.Max(b.Y0, ly0);
                    double mh = Math.Min(Math.Max(1, b.Height), Math.Max(1, ly1 - ly0));
                    if (overlap <= 0.45 * mh) continue;
                    double d = Math.Abs(b.Cy - (ly0 + ly1) / 2);
                    if (d < best) { best = d; hit = line; }
                }
                if (hit == null) lines.Add(new List<OcrSpan> { b });
                else hit.Add(b);
            }
            lines.Sort((a, b) => AvgY(a).CompareTo(AvgY(b)));
            return lines;
        }

        static double AvgY(List<OcrSpan> line)
        {
            double s = 0; foreach (var x in line) s += x.Cy; return s / line.Count;
        }

        static Line JoinLine(List<OcrSpan> cells, double medianH)
        {
            cells.Sort((a, b) => a.X0.CompareTo(b.X0));
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    double gap = cells[i].X0 - cells[i - 1].X1;
                    char prev = LastChar(cells[i - 1].Text), next = FirstChar(cells[i].Text);
                    if (gap > medianH * 1.5) sb.Append('\t');
                    else if (prev != '\0' && next != '\0' && (OcrText.IsCjkPunct(prev) || OcrText.IsCjkPunct(next)))
                    { /* no space before/after ：，。 “” */ }
                    else if (prev != '\0' && next != '\0' && OcrText.IsCjk(prev) && OcrText.IsCjk(next))
                    { /* CJK words sit flush */ }
                    else if (gap > medianH * 0.12) sb.Append(' ');
                    else if (prev != '\0' && next != '\0' && !OcrText.IsCjk(prev) && !OcrText.IsCjk(next))
                        sb.Append(' ');
                }
                sb.Append(cells[i].Text);
            }
            double x0 = cells[0].X0, y0 = cells[0].Y0, y1 = cells[0].Y1, cy = 0;
            foreach (var c in cells) { if (c.Y0 < y0) y0 = c.Y0; if (c.Y1 > y1) y1 = c.Y1; cy += c.Cy; }
            cy /= cells.Count;
            string text = sb.ToString();
            return new Line { Text = text, X0 = x0, Y0 = y0, Y1 = y1, Cy = cy, Tab = text.IndexOf('\t') >= 0 };
        }

        static int IndentSpaces(double x0, double pageLeft, double medianH)
        {
            double dx = x0 - pageLeft;
            if (dx < medianH * 0.8) return 0;
            int n = (int)Math.Round(dx / (medianH * 0.55));
            if (n < 1) n = 1;
            if (n > 16) n = 16;
            return n;
        }

        static bool ShouldJoin(Line prev, Line next, double medianH)
        {
            if (prev.Tab || next.Tab) return false;
            if (LooksLikeList(next.Text)) return false;
            if (next.X0 > prev.X0 + medianH * 0.8) return false;
            if (next.X0 < prev.X0 - medianH * 0.8) return false;
            string a = prev.Text.TrimEnd(), b = next.Text.TrimStart();
            if (a.Length == 0 || b.Length == 0) return false;
            char last = a[a.Length - 1], first = b[0];
            if (last == '-') return true;
            if (",;:，、".IndexOf(last) >= 0) return true;
            if (".!?。！？…".IndexOf(last) >= 0) return false;
            if (OcrText.IsCjk(last) && OcrText.IsCjk(first)) return true;
            if (char.IsLower(first) && first < 0x2E80) return true;
            return false;
        }

        static string JoinWrapped(string prev, string next)
        {
            string a = prev.TrimEnd(), b = next.TrimStart();
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            char last = a[a.Length - 1], first = b[0];
            if (last == '-' && char.IsLetter(first)) return a.Substring(0, a.Length - 1) + b;
            if (OcrText.IsCjk(last) && OcrText.IsCjk(first)) return a + b;
            if (char.IsWhiteSpace(last) || char.IsWhiteSpace(first)) return a + b;
            return a + " " + b;
        }

        static bool LooksLikeList(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int k = 0; while (k < s.Length && char.IsWhiteSpace(s[k])) k++;
            if (k >= s.Length) return false;
            if (s[k] == '•' || s[k] == '·') return true;
            if (k + 1 < s.Length && (s[k] == '-' || s[k] == '*') && s[k + 1] == ' ') return true;
            int i = k; while (i < s.Length && char.IsDigit(s[i])) i++;
            return i > k && i < s.Length && (s[i] == '.' || s[i] == ')') && (i + 1 == s.Length || s[i + 1] == ' ');
        }

        static char FirstChar(string s)
        {
            if (string.IsNullOrEmpty(s)) return '\0';
            for (int i = 0; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) return s[i];
            return '\0';
        }
        static char LastChar(string s)
        {
            if (string.IsNullOrEmpty(s)) return '\0';
            for (int i = s.Length - 1; i >= 0; i--) if (!char.IsWhiteSpace(s[i])) return s[i];
            return '\0';
        }

        static double Median(List<double> xs)
        {
            int n = xs.Count;
            if (n == 0) return 0;
            xs.Sort();
            if ((n & 1) == 1) return xs[n / 2];
            return (xs[n / 2 - 1] + xs[n / 2]) / 2.0;
        }
    }
}
