using System.Text;
using System.Text.RegularExpressions;

namespace SnapForge.Plugins
{
    /// <summary>LaTeX token cleanup used by offline formula recognition (pix2text-mfr).</summary>
    public static class FormulaLatex
    {
        /// <summary>The model emits space-separated tokens; keep a space only where LaTeX needs it (after a command word before a letter).</summary>
        public static string Tidy(string s)
        {
            var sb = new StringBuilder(); string prev = "";
            foreach (var t in (s ?? "").Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                if (sb.Length > 0 && Regex.IsMatch(prev, @"\\[A-Za-z]+$") && char.IsLetter(t[0])) sb.Append(' ');
                sb.Append(t); prev = t;
            }
            return sb.ToString();
        }
    }
}
