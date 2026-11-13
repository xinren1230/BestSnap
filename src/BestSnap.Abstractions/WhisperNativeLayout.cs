using System;
using System.Collections.Generic;
using System.IO;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Whisper.net looks for natives at <c>{searchRoot}/runtimes/win-x64/whisper.dll</c>
    /// (see NativeLibraryLoader.GetRuntimePaths). <c>RuntimeOptions.LibraryPath</c> is passed
    /// through <c>Path.GetDirectoryName</c>, so it must be a file under that search root,
    /// not the win-x64 folder itself.
    /// </summary>
    public static class WhisperNativeLayout
    {
        public const string WinX64Folder = "win-x64";
        public static readonly string[] WinX64Dlls =
        {
            "whisper.dll",
            "ggml-whisper.dll",
            "ggml-base-whisper.dll",
            "ggml-cpu-whisper.dll",
        };

        public static readonly string[] GpuRuntimeFolders = { "cuda", "vulkan" };

        public static string WinX64Relative => Path.Combine("runtimes", WinX64Folder);

        /// <summary>True when optional CUDA or Vulkan natives sit under runtimes/{cuda|vulkan}/win-x64.</summary>
        public static bool HasGpuRuntime(string searchRoot) =>
            HasCudaRuntime(searchRoot) || HasVulkanRuntime(searchRoot);

        public static bool HasCudaRuntime(string searchRoot) =>
            HasGpuFolder(searchRoot, "cuda", "ggml-cuda-whisper.dll");

        public static bool HasVulkanRuntime(string searchRoot) =>
            HasGpuFolder(searchRoot, "vulkan", "ggml-vulkan-whisper.dll");

        public static bool HasGpuFolder(string searchRoot, string folder, string dll)
        {
            if (string.IsNullOrWhiteSpace(searchRoot) || string.IsNullOrWhiteSpace(folder)) return false;
            try
            {
                string dir = Path.Combine(searchRoot, "runtimes", folder, WinX64Folder);
                if (!string.IsNullOrEmpty(dll))
                    return File.Exists(Path.Combine(dir, dll));
                if (!Directory.Exists(dir)) return false;
                foreach (var _ in Directory.EnumerateFiles(dir, "*.dll"))
                    return true;
            }
            catch { }
            return false;
        }

        public static bool HasWinX64Natives(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            try
            {
                if (!Directory.Exists(dir)) return false;
                foreach (var n in WinX64Dlls)
                    if (!File.Exists(Path.Combine(dir, n))) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns the directory that contains <c>runtimes/win-x64/*.dll</c>, or null.
        /// Candidates may be the app folder, the win-x64 folder, or the single-file extract dir.
        /// </summary>
        public static string ResolveSearchRoot(IEnumerable<string> candidates)
        {
            if (candidates == null) return null;
            foreach (var raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string c;
                try { c = Path.GetFullPath(raw.Trim().Trim('"')); }
                catch { continue; }
                if (HasWinX64Natives(Path.Combine(c, "runtimes", WinX64Folder)))
                    return c;
                if (HasWinX64Natives(c))
                {
                    var name = Path.GetFileName(c.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.Equals(name, WinX64Folder, StringComparison.OrdinalIgnoreCase))
                    {
                        var runtimes = Directory.GetParent(c);
                        var root = runtimes?.Parent;
                        if (root != null) return root.FullName;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Value for <c>RuntimeOptions.LibraryPath</c>. Must be a file path so GetDirectoryName
        /// yields <paramref name="searchRoot"/> (which then has <c>runtimes/win-x64</c>).
        /// </summary>
        public static string LibraryPathProbe(string searchRoot)
        {
            if (string.IsNullOrWhiteSpace(searchRoot)) return null;
            return Path.Combine(searchRoot, "BestSnap.whisper.probe");
        }
    }
}
