using System;
using System.IO;
using System.Text;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class WhisperModelsTests
    {
        [Fact]
        public void Catalog_IncludesTurboAndQuantizedWithExactHfSizeAndSha()
        {
            Assert.Equal("auto", WhisperModels.DefaultId);
            Assert.Equal("auto", WhisperModels.ChoiceIds[0]);
            Assert.Contains("tiny", WhisperModels.Ids);
            Assert.Contains("base", WhisperModels.Ids);
            Assert.Contains("small", WhisperModels.Ids);
            Assert.Contains("medium", WhisperModels.Ids);
            Assert.Contains("medium-q5_0", WhisperModels.Ids);
            Assert.Contains("large-v3-turbo", WhisperModels.Ids);
            Assert.Contains("large-v3-turbo-q5_0", WhisperModels.Ids);
            Assert.Equal("ggml-large-v3-turbo.bin", WhisperModels.FileName("large-v3-turbo"));
            Assert.Equal("ggml-medium-q5_0.bin", WhisperModels.FileName("medium-q5_0"));
            Assert.Equal(1_624_555_275, WhisperModels.SizeBytes("large-v3-turbo"));
            Assert.Equal(574_041_195, WhisperModels.SizeBytes("large-v3-turbo-q5_0"));
            Assert.Equal("1fc70f774d38eb169993ac391eea357ef47c88757ef72ee5943879b7e8e2bc69", WhisperModels.Sha256("large-v3-turbo"));
            Assert.Equal("394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", WhisperModels.Sha256("large-v3-turbo-q5_0"));
            Assert.StartsWith(WhisperModels.HfBase, WhisperModels.DownloadUrl("tiny"));
            Assert.Contains("1.5 GB", WhisperModels.SizeLabel("large-v3-turbo"));
            Assert.Contains("547 MB", WhisperModels.SizeLabel("large-v3-turbo-q5_0"));
        }

        [Fact]
        public void Normalize_KeepsKnownIdsAndMapsAliases()
        {
            Assert.Equal("base", WhisperModels.Normalize("base"));
            Assert.Equal("medium-q5_0", WhisperModels.Normalize("medium-q5"));
            Assert.Equal("large-v3-turbo", WhisperModels.Normalize("turbo"));
            Assert.Equal("large-v3-turbo", WhisperModels.Normalize("large"));
            Assert.Equal("large-v3-turbo-q5_0", WhisperModels.Normalize("turbo-q5_0"));
            Assert.Equal("auto", WhisperModels.Normalize(""));
            Assert.Equal("auto", WhisperModels.Normalize("recommended"));
            Assert.Equal("auto", WhisperModels.Normalize("nope"));
            Assert.True(WhisperModels.IsAuto("auto"));
            Assert.False(WhisperModels.IsAuto("base"));
            Assert.Equal("base", WhisperModels.NormalizeWeights("auto"));
        }

        [Fact]
        public void ResumeOffset_RestartsWhenMissingEmptyOrComplete()
        {
            Assert.Equal(0, WhisperModels.ResumeOffset(null, 100));
            var dir = Path.Combine(Path.GetTempPath(), "bs-wh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string part = Path.Combine(dir, "m.bin.part");
                Assert.Equal(0, WhisperModels.ResumeOffset(part, 100));
                File.WriteAllBytes(part, Array.Empty<byte>());
                Assert.Equal(0, WhisperModels.ResumeOffset(part, 100));
                File.WriteAllBytes(part, new byte[40]);
                Assert.Equal(40, WhisperModels.ResumeOffset(part, 100));
                File.WriteAllBytes(part, new byte[100]);
                Assert.Equal(0, WhisperModels.ResumeOffset(part, 100));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void FileLooksComplete_RequiresExactSizeWhenKnown()
        {
            var info = WhisperModels.Get("tiny");
            var dir = Path.Combine(Path.GetTempPath(), "bs-wh2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, info.FileName);
                Assert.False(WhisperModels.FileLooksComplete(path, info));
                File.WriteAllBytes(path, new byte[2_000_000]);
                Assert.False(WhisperModels.FileLooksComplete(path, info));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void Sha256Hex_MatchesKnownVector()
        {
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                WhisperModels.Sha256Hex(Encoding.ASCII.GetBytes("abc")));
            var path = Path.Combine(Path.GetTempPath(), "bs-sha-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));
                Assert.True(WhisperModels.Sha256Matches(path, "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    public class WhisperDecodeTests
    {
        [Fact]
        public void NormalizeLanguage_MapsChineseEnglishAndAuto()
        {
            Assert.Equal("auto", WhisperDecode.NormalizeLanguage(null));
            Assert.Equal("auto", WhisperDecode.NormalizeLanguage("detect"));
            Assert.Equal("zh", WhisperDecode.NormalizeLanguage("zh-CN"));
            Assert.Equal("zh", WhisperDecode.NormalizeLanguage("Chinese"));
            Assert.Equal("en", WhisperDecode.NormalizeLanguage("en-US"));
            Assert.Equal("ja", WhisperDecode.NormalizeLanguage("jp"));
            Assert.Equal("yue", WhisperDecode.NormalizeLanguage("zh-HK"));
            Assert.True(WhisperDecode.IsChinese("zh"));
            Assert.False(WhisperDecode.IsChinese("en"));
        }

        [Fact]
        public void ComposePrompt_PrefersSimplifiedChineseAndKeepsUserVocab()
        {
            string p = WhisperDecode.ComposePrompt("zh", "BestSnap", "上一句。");
            Assert.Contains(WhisperDecode.SimplifiedChineseHint, p);
            Assert.Contains("BestSnap", p);
            Assert.Contains("上一句。", p);
            Assert.DoesNotContain(WhisperDecode.SimplifiedChineseHint, WhisperDecode.ComposePrompt("en", "BestSnap", "hi"));
            string longPrev = new string('a', 2000);
            string clipped = WhisperDecode.ComposePrompt("en", "term", longPrev, 80);
            Assert.True(clipped.Length <= 80);
            Assert.StartsWith("term", clipped);
        }

        [Fact]
        public void AutoLanguage_IsNotDetectOnly_AndGreedyOnGpuLive()
        {
            Assert.False(WhisperDecode.DetectLanguageOnly);
            Assert.Equal("auto", WhisperDecode.ProcessorLanguage("auto"));
            Assert.Equal("auto", WhisperDecode.ProcessorLanguage(null));
            Assert.False(WhisperDecode.PreferBeamSearch(WhisperBackend.Cuda, live: true));
            Assert.False(WhisperDecode.PreferBeamSearch(WhisperBackend.Cuda, live: false));
            Assert.False(WhisperDecode.PreferBeamSearch(WhisperBackend.Cpu, live: true));
            Assert.True(WhisperDecode.PreferBeamSearch(WhisperBackend.Cpu, live: false));
            Assert.True(WhisperDecode.IsJunkCue("[BLANK_AUDIO]"));
            Assert.Equal("", WhisperDecode.NormalizeCueText("  [BLANK_AUDIO]  "));
            Assert.Equal("hello", WhisperDecode.NormalizeCueText(" hello "));
        }
    }
}
