using System;
using System.IO;
using System.Text;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class TranscriptSidecarTests
    {
        [Theory]
        [InlineData("Rec_20261113_120000.wav", "Rec_20261113_120000.txt")]
        [InlineData("Rec_20261113_120000.mp3", "Rec_20261113_120000.txt")]
        [InlineData("Audio_1.WAV", "Audio_1.txt")]
        public void PathFor_SameBaseNameTxt(string media, string expectedName)
        {
            Assert.Equal(expectedName, Path.GetFileName(TranscriptSidecar.PathFor(media)));
        }

        [Fact]
        public void ShouldAutoTranscribe_OnlyWhenSettingOnAndAudio()
        {
            Assert.True(TranscriptSidecar.ShouldAutoTranscribe(true, "Rec.wav"));
            Assert.True(TranscriptSidecar.ShouldAutoTranscribe(true, "Rec.mp3"));
            Assert.False(TranscriptSidecar.ShouldAutoTranscribe(false, "Rec.wav"));
            Assert.False(TranscriptSidecar.ShouldAutoTranscribe(false, "Rec.mp3"));
            Assert.False(TranscriptSidecar.ShouldAutoTranscribe(true, "Rec.mp4"));
            Assert.False(TranscriptSidecar.ShouldAutoTranscribe(true, ""));
        }

        [Fact]
        public void WriteUtf8Bom_IncludesBomAndChinese()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-txt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string media = Path.Combine(dir, "Rec_zh.wav");
                File.WriteAllBytes(media, new byte[] { 1 });
                TranscriptSidecar.WriteForMedia(media, "你好，世界");
                string txt = TranscriptSidecar.PathFor(media);
                byte[] bytes = File.ReadAllBytes(txt);
                Assert.True(TranscriptSidecar.HasUtf8Bom(bytes));
                string body = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                Assert.Equal("你好，世界", body);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void CopyWithMedia_MovesTxtOnSave()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-txt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string temp = Path.Combine(dir, "Rec_tmp.wav");
                string dest = Path.Combine(dir, "saved", "Rec_final.wav");
                File.WriteAllBytes(temp, new byte[] { 9 });
                TranscriptSidecar.WriteForMedia(temp, "cue");
                Assert.True(TranscriptSidecar.CopyWithMedia(temp, dest));
                Assert.True(File.Exists(TranscriptSidecar.PathFor(dest)));
                Assert.Equal("cue", File.ReadAllText(TranscriptSidecar.PathFor(dest), TranscriptSidecar.Utf8Bom));
                File.Copy(temp, dest, overwrite: true);
                TempRecordingStore.DeletePair(temp);
                Assert.False(File.Exists(temp));
                Assert.False(File.Exists(TranscriptSidecar.PathFor(temp)));
                Assert.True(File.Exists(dest));
                Assert.True(File.Exists(TranscriptSidecar.PathFor(dest)));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void DeletePair_RemovesTranscriptTxt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-txt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string m = Path.Combine(dir, "Rec_disc.mp3");
                File.WriteAllText(m, "x");
                File.WriteAllText(m + ".meta.json", "{}");
                TranscriptSidecar.WriteForMedia(m, "丢弃");
                TempRecordingStore.DeletePair(m);
                Assert.False(File.Exists(m));
                Assert.False(File.Exists(m + ".meta.json"));
                Assert.False(File.Exists(TranscriptSidecar.PathFor(m)));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void ListReady_SkipsTranscriptTxt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-txt-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t = DateTime.UtcNow;
                string ready = TempRecordingStore.NewMediaPath(dir, "Audio", ".wav", t);
                File.WriteAllBytes(ready, new byte[] { 9, 9 });
                TempRecordingStore.MarkComplete(ready, t);
                TranscriptSidecar.WriteForMedia(ready, "text");
                var list = TempRecordingStore.ListReady(dir);
                Assert.Contains(ready, list);
                Assert.DoesNotContain(TranscriptSidecar.PathFor(ready), list);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
