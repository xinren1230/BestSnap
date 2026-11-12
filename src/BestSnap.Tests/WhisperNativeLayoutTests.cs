using System;
using System.IO;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class WhisperNativeLayoutTests
    {
        [Fact]
        public void ResolveSearchRoot_FindsRuntimesWinX64NextToExe()
        {
            var root = Path.Combine(Path.GetTempPath(), "bs-whisper-" + Guid.NewGuid().ToString("N"));
            try
            {
                var win = Path.Combine(root, "runtimes", "win-x64");
                Directory.CreateDirectory(win);
                foreach (var n in WhisperNativeLayout.WinX64Dlls)
                    File.WriteAllBytes(Path.Combine(win, n), new byte[] { 1 });
                Assert.Equal(Path.GetFullPath(root),
                    WhisperNativeLayout.ResolveSearchRoot(new[] { Path.Combine(root, "missing"), root }));
                var probe = WhisperNativeLayout.LibraryPathProbe(root);
                Assert.Equal(Path.GetFullPath(root), Path.GetDirectoryName(probe));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void ResolveSearchRoot_AcceptsWinX64FolderAndExtractLayout()
        {
            var extract = Path.Combine(Path.GetTempPath(), "bs-whisper-ex-" + Guid.NewGuid().ToString("N"));
            try
            {
                var win = Path.Combine(extract, "runtimes", "win-x64");
                Directory.CreateDirectory(win);
                foreach (var n in WhisperNativeLayout.WinX64Dlls)
                    File.WriteAllBytes(Path.Combine(win, n), new byte[] { 1 });
                Assert.Equal(Path.GetFullPath(extract),
                    WhisperNativeLayout.ResolveSearchRoot(new[] { win }));
            }
            finally { TryDelete(extract); }
        }

        [Fact]
        public void ResolveSearchRoot_NullWhenIncomplete()
        {
            var root = Path.Combine(Path.GetTempPath(), "bs-whisper-bad-" + Guid.NewGuid().ToString("N"));
            try
            {
                var win = Path.Combine(root, "runtimes", "win-x64");
                Directory.CreateDirectory(win);
                File.WriteAllBytes(Path.Combine(win, "whisper.dll"), new byte[] { 1 });
                Assert.Null(WhisperNativeLayout.ResolveSearchRoot(new[] { root }));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void PublishLayout_RealRuntimeDllsBesideExe()
        {
            var pkg = FindNugetWinX64();
            Assert.True(pkg != null && Directory.Exists(pkg), "Whisper.net.Runtime win-x64 natives should restore from NuGet");
            foreach (var n in WhisperNativeLayout.WinX64Dlls)
                Assert.True(File.Exists(Path.Combine(pkg, n)), n);
            var pub = Path.Combine(Path.GetTempPath(), "bs-pub-" + Guid.NewGuid().ToString("N"));
            try
            {
                var win = Path.Combine(pub, "runtimes", "win-x64");
                Directory.CreateDirectory(win);
                File.WriteAllText(Path.Combine(pub, "BestSnap.exe"), "fake");
                foreach (var n in WhisperNativeLayout.WinX64Dlls)
                    File.Copy(Path.Combine(pkg, n), Path.Combine(win, n));
                Assert.True(WhisperNativeLayout.HasWinX64Natives(win));
                Assert.Equal(Path.GetFullPath(pub), WhisperNativeLayout.ResolveSearchRoot(new[] { pub }));
            }
            finally { TryDelete(pub); }
        }

        [Fact]
        public void HasGpuRuntime_DetectsCudaOrVulkanTree()
        {
            var root = Path.Combine(Path.GetTempPath(), "bs-gpu-" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.False(WhisperNativeLayout.HasGpuRuntime(root));
                var cuda = Path.Combine(root, "runtimes", "cuda", "win-x64");
                Directory.CreateDirectory(cuda);
                File.WriteAllBytes(Path.Combine(cuda, "ggml-cuda-whisper.dll"), new byte[] { 1 });
                Assert.True(WhisperNativeLayout.HasGpuRuntime(root));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void Csproj_ExcludesWhisperNativesFromSingleFile()
        {
            var csproj = Path.Combine(RepoRoot, "src", "BestSnap", "BestSnap.csproj");
            Assert.True(File.Exists(csproj), csproj);
            var text = File.ReadAllText(csproj);
            Assert.Contains("whisper.dll", text);
            Assert.Contains("ggml-cpu-whisper", text);
            Assert.Contains("ggml-cuda-whisper", text);
            Assert.Contains("ggml-vulkan-whisper", text);
            Assert.Contains("Whisper.net.Runtime.Cuda.Windows", text);
            Assert.Contains("Whisper.net.Runtime.Vulkan", text);
            Assert.Contains("ExcludeFromSingleFile=\"true\"", text);
            Assert.Contains("runtimes/", text);
        }

        static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "BestSnap.sln"))) return dir.FullName;
                    dir = dir.Parent;
                }
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            }
        }

        static string FindNugetWinX64()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var p = Path.Combine(home, ".nuget", "packages", "whisper.net.runtime", "1.8.1", "build", "win-x64");
            if (Directory.Exists(p)) return p;
            var nuget = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (!string.IsNullOrEmpty(nuget))
            {
                p = Path.Combine(nuget, "whisper.net.runtime", "1.8.1", "build", "win-x64");
                if (Directory.Exists(p)) return p;
            }
            return null;
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
