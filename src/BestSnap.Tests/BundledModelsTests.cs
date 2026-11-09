using System;
using System.IO;
using System.Linq;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class BundledModelsTests
    {
        [Fact]
        public void FormulaFiles_MatchPublishedSha256AndSizes()
        {
            Assert.Equal(3, BundledModels.FormulaFiles.Length);
            Assert.Equal(87496990L, BundledModels.FormulaFiles.Single(f => f.Name == "encoder_model.onnx").Size);
            Assert.Equal("bd8d5c322792e9ec45793af5569e9748f82a3d728a9e00213dbfc56c1486f37d",
                BundledModels.FormulaFiles.Single(f => f.Name == "encoder_model.onnx").Sha256);
            Assert.Equal(30114937L, BundledModels.FormulaFiles.Single(f => f.Name == "decoder_model.onnx").Size);
            Assert.Equal("fd0f92d7a012f3dae41e1ac79421aea0ea888b5a66cb3f9a004e424f82f3daed",
                BundledModels.FormulaFiles.Single(f => f.Name == "decoder_model.onnx").Sha256);
            Assert.Equal(39161L, BundledModels.FormulaFiles.Single(f => f.Name == "tokenizer.json").Size);
            Assert.Equal("3e2ab757277d22639bec28c9d7972e352d3d1dba223051fa674002dc5ab64df3",
                BundledModels.FormulaFiles.Single(f => f.Name == "tokenizer.json").Sha256);
        }

        [Fact]
        public void ResolveFormulaDir_PrefersCompleteInstallDirOverCache()
        {
            var root = Path.Combine(Path.GetTempPath(), "sf-models-" + Guid.NewGuid().ToString("N"));
            var exeDir = Path.Combine(root, "app");
            var local = Path.Combine(root, "local");
            try
            {
                var bundled = BundledModels.BundledFormulaDir(exeDir);
                WriteComplete(bundled);
                WriteComplete(BundledModels.CacheFormulaDir(local));
                Assert.Equal(bundled, BundledModels.ResolveFormulaDir(exeDir, local));
                Assert.True(BundledModels.IsComplete(bundled));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void ResolveFormulaDir_FallsBackToCacheWhenInstallDirIncomplete()
        {
            var root = Path.Combine(Path.GetTempPath(), "sf-models-" + Guid.NewGuid().ToString("N"));
            var exeDir = Path.Combine(root, "app");
            var local = Path.Combine(root, "local");
            try
            {
                Directory.CreateDirectory(BundledModels.BundledFormulaDir(exeDir));
                File.WriteAllText(Path.Combine(BundledModels.BundledFormulaDir(exeDir), "tokenizer.json"), "nope");
                var cache = BundledModels.CacheFormulaDir(local);
                WriteComplete(cache);
                Assert.Equal(cache, BundledModels.ResolveFormulaDir(exeDir, local));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void ResolveFormulaDir_FallsBackToLegacySnapForgeCache()
        {
            var root = Path.Combine(Path.GetTempPath(), "sf-models-" + Guid.NewGuid().ToString("N"));
            var exeDir = Path.Combine(root, "app");
            var local = Path.Combine(root, "local");
            try
            {
                var legacy = BundledModels.LegacyCacheFormulaDir(local);
                WriteComplete(legacy);
                Assert.Equal(legacy, BundledModels.ResolveFormulaDir(exeDir, local));
                Assert.Contains(AppIdentity.LegacyName, legacy);
                Assert.Contains(AppIdentity.Name, BundledModels.CacheFormulaDir(local));
            }
            finally { TryDelete(root); }
        }

        [Fact]
        public void ResolveFormulaDir_UsesCachePathWhenNothingIsPresent()
        {
            var exeDir = Path.Combine(Path.GetTempPath(), "sf-missing-app-" + Guid.NewGuid().ToString("N"));
            var local = Path.Combine(Path.GetTempPath(), "sf-missing-local-" + Guid.NewGuid().ToString("N"));
            Assert.Equal(BundledModels.CacheFormulaDir(local), BundledModels.ResolveFormulaDir(exeDir, local));
            Assert.False(BundledModels.IsComplete(BundledModels.BundledFormulaDir(exeDir)));
        }

        [Fact]
        public void IsComplete_FalseWhenAFileHasWrongSize()
        {
            var dir = Path.Combine(Path.GetTempPath(), "sf-models-" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteComplete(dir);
                File.WriteAllBytes(Path.Combine(dir, "tokenizer.json"), new byte[10]);
                Assert.False(BundledModels.IsComplete(dir));
            }
            finally { TryDelete(dir); }
        }

        static void WriteComplete(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var f in BundledModels.FormulaFiles)
                File.WriteAllBytes(Path.Combine(dir, f.Name), new byte[f.Size]);
        }

        static void TryDelete(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
