using System;
using System.IO;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Locates the vendored editor dist and the WebView2 runtime.</summary>
    public static class VideoStudioHost
    {
        public static string UserDataFolder => Path.Combine(AppPaths.LocalData, "video-studio", "webview");

        public static bool IsRuntimeAvailable()
        {
            try
            {
                string v = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrWhiteSpace(v);
            }
            catch { return false; }
        }

        public static bool TryFindDist(out string dir)
        {
            dir = null;
            foreach (var candidate in DistCandidates())
            {
                if (File.Exists(Path.Combine(candidate, "index.html")))
                {
                    dir = candidate;
                    return true;
                }
            }
            return false;
        }

        public static bool CanHostEditor() => IsRuntimeAvailable() && TryFindDist(out _);

        static System.Collections.Generic.IEnumerable<string> DistCandidates()
        {
            string baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                yield return Path.Combine(baseDir, "video-studio");
                yield return Path.GetFullPath(Path.Combine(baseDir, "..", "video-studio"));
            }
            string exe = null;
            try { exe = Environment.ProcessPath; } catch { exe = null; }
            if (!string.IsNullOrEmpty(exe))
                yield return Path.Combine(Path.GetDirectoryName(exe) ?? "", "video-studio");

            var walk = new DirectoryInfo(baseDir ?? ".");
            for (int i = 0; i < 8 && walk != null; i++, walk = walk.Parent)
            {
                yield return Path.Combine(walk.FullName, "third_party", "video-studio", "dist");
                yield return Path.Combine(walk.FullName, "publish", "video-studio");
            }
        }
    }
}
