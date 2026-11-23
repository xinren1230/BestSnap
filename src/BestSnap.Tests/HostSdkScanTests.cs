using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace BestSnap.Tests
{
    public class HostSdkScanTests
    {
        static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "third_party", "video-studio", "LICENSE")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            }
        }

        [Fact]
        public void VendoredEditor_HasNoForbiddenHostSdkName()
        {
            string root = Path.Combine(RepoRoot, "third_party", "video-studio");
            Assert.True(Directory.Exists(root), "missing " + root);
            string needle = string.Concat("pu", "ter");
            var re = new Regex("(?<![A-Za-z])" + Regex.Escape(needle) + "(?![A-Za-z])", RegexOptions.IgnoreCase);
            var skipNames = new[] { "LICENSE", "NOTICE", "NOTICE.md" };
            var skipExt = new[] { ".png", ".jpg", ".jpeg", ".webp", ".ico", ".wasm", ".tflite", ".task", ".woff", ".woff2", ".ttf", ".otf" };
            var hits = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (Array.Exists(skipNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) continue;
                string ext = Path.GetExtension(file);
                if (Array.Exists(skipExt, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase))) continue;
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.StartsWith("dist/", StringComparison.OrdinalIgnoreCase)) continue;
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }
                int lineNo = 0;
                foreach (var line in text.Split('\n'))
                {
                    lineNo++;
                    if (!re.IsMatch(line)) continue;
                    hits.Add(rel + ":" + lineNo + ": " + line.Trim().Substring(0, Math.Min(120, line.Trim().Length)));
                    if (hits.Count >= 20) break;
                }
                if (hits.Count >= 20) break;
            }
            Assert.True(hits.Count == 0, "forbidden host-SDK name in vendored editor:\n" + string.Join("\n", hits));
        }

        [Fact]
        public void CheckScript_ConstructsNeedleWithoutSpellingIt()
        {
            string script = Path.Combine(RepoRoot, "third_party", "video-studio", "tools", "check-host-sdk.mjs");
            Assert.True(File.Exists(script), "missing " + script);
            string text = File.ReadAllText(script);
            string needle = string.Concat("pu", "ter");
            Assert.DoesNotContain(needle, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("['pu', 'ter'].join('')", text.Replace("\"", "'"));
        }
    }
}
