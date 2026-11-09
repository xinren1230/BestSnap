using System;
using System.IO;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class AppPathsTests
    {
        [Fact]
        public void ProductName_IsBestSnap_WithLegacySnapForgeIds()
        {
            Assert.Equal("BestSnap", AppIdentity.Name);
            Assert.Equal("SnapForge", AppIdentity.LegacyName);
            Assert.Equal("BestSnap.exe", AppIdentity.ExeFileName);
            Assert.Equal("BestSnap.Ocr", AppIdentity.PluginOcrFolder);
            Assert.Equal("SnapForge.Ocr", AppIdentity.LegacyPluginOcrFolder);
            Assert.Equal("SnapForge_SingleInstance_Mutex", AppIdentity.MutexName);
            Assert.Equal("SnapForge_ShowToolbar_Event", AppIdentity.ShowEventName);
            Assert.Equal("SnapForge_Cli_Pipe_v1", AppIdentity.CliPipeName);
            Assert.Equal("SNAPFORGE_", AppIdentity.LegacyEnvPrefix);
            Assert.Equal("BestSnap.bestsnap.ico", AppIdentity.TrayIconResource);
        }

        [Fact]
        public void TryMigrateTree_MovesWhenDestMissing()
        {
            var root = Path.Combine(Path.GetTempPath(), "sf-mig-" + Guid.NewGuid().ToString("N"));
            var from = Path.Combine(root, "SnapForge");
            var to = Path.Combine(root, "BestSnap");
            try
            {
                Directory.CreateDirectory(from);
                File.WriteAllText(Path.Combine(from, "settings.json"), "{}");
                Directory.CreateDirectory(Path.Combine(from, "logs"));
                File.WriteAllText(Path.Combine(from, "logs", "a.log"), "x");
                AppPaths.TryMigrateTree(from, to);
                Assert.True(File.Exists(Path.Combine(to, "settings.json")));
                Assert.True(File.Exists(Path.Combine(to, "logs", "a.log")));
                Assert.False(Directory.Exists(from));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void TryMigrateTree_CopiesMissingFilesWhenDestExists()
        {
            var root = Path.Combine(Path.GetTempPath(), "sf-mig-" + Guid.NewGuid().ToString("N"));
            var from = Path.Combine(root, "SnapForge");
            var to = Path.Combine(root, "BestSnap");
            try
            {
                Directory.CreateDirectory(from);
                Directory.CreateDirectory(to);
                File.WriteAllText(Path.Combine(from, "old.json"), "old");
                File.WriteAllText(Path.Combine(from, "keep.json"), "from");
                File.WriteAllText(Path.Combine(to, "keep.json"), "newer");
                AppPaths.TryMigrateTree(from, to);
                Assert.Equal("old", File.ReadAllText(Path.Combine(to, "old.json")));
                Assert.Equal("newer", File.ReadAllText(Path.Combine(to, "keep.json")));
            }
            finally { TryDelete(root); }
        }

        static void TryDelete(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
