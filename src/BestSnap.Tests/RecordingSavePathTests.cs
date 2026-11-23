using System;
using System.IO;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class RecordingSavePathTests
    {
        [Fact]
        public void DefaultEditedPath_UsesEditedSuffixAndNeverOriginal()
        {
            var orig = Path.Combine(Path.GetTempPath(), "clip.mp4");
            string p1 = RecordingSavePath.DefaultEditedPath(orig);
            Assert.EndsWith("clip-edited.mp4", p1);
            Assert.False(RecordingSavePath.PathsEqual(orig, p1));
            string webm = RecordingSavePath.DefaultEditedPath(orig, ".webm");
            Assert.EndsWith("clip-edited.webm", webm);
        }

        [Fact]
        public void UniqueInFolder_SkipsExisting()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "Rec.mp4"), new byte[] { 1 });
                string p = RecordingSavePath.UniqueInFolder(dir, "Rec", ".mp4");
                Assert.EndsWith("Rec (2).mp4", p);
                Assert.False(File.Exists(p));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void PromoteToFolder_MovesMediaAndTxt_DoesNotStayInTemp()
        {
            var root = Path.Combine(Path.GetTempPath(), "bs-prom-" + Guid.NewGuid().ToString("N"));
            var temp = Path.Combine(root, "temp");
            var dest = Path.Combine(root, "Videos");
            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(dest);
            try
            {
                string src = Path.Combine(temp, "Rec_20261123_120000.mp4");
                File.WriteAllBytes(src, new byte[] { 9, 9, 9 });
                File.WriteAllText(src + ".meta.json", "{\"complete\":true}");
                TranscriptSidecar.WriteForMedia(src, "hello");
                string saved = RecordingSavePath.PromoteToFolder(src, dest);
                Assert.True(File.Exists(saved));
                Assert.Equal(dest, Path.GetDirectoryName(saved));
                Assert.False(File.Exists(src));
                Assert.False(File.Exists(src + ".meta.json"));
                Assert.False(File.Exists(TranscriptSidecar.PathFor(src)));
                Assert.True(File.Exists(TranscriptSidecar.PathFor(saved)));
                Assert.Equal("hello", File.ReadAllText(TranscriptSidecar.PathFor(saved), TranscriptSidecar.Utf8Bom).Trim('\uFEFF'));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void PromoteToFolder_SameDirectory_ReturnsOriginal()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-same-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string src = Path.Combine(dir, "keep.wav");
                File.WriteAllBytes(src, new byte[] { 1 });
                Assert.Equal(Path.GetFullPath(src), RecordingSavePath.PromoteToFolder(src, dir));
                Assert.True(File.Exists(src));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void SessionSourceUri_MatchesVirtualHost()
        {
            Assert.Equal("vs.bestsnap.local", RecordingSavePath.VirtualHost);
            Assert.Equal("https://vs.bestsnap.local/session/source", RecordingSavePath.SessionSourceUrl);
            Assert.True(RecordingSavePath.IsSessionSourceUri("https://vs.bestsnap.local/session/source"));
            Assert.True(RecordingSavePath.IsSessionSourceUri("https://VS.BESTSNAP.LOCAL/session/source?x=1"));
            Assert.False(RecordingSavePath.IsSessionSourceUri("https://vs.bestsnap.local/index.html"));
            Assert.False(RecordingSavePath.IsSessionSourceUri("https://evil.example/session/source"));
        }

        [Fact]
        public void ResolveDestFolder_PrefersKindSpecificThenDefault()
        {
            Assert.Equal(@"C:\Rec", RecordingSavePath.ResolveDestFolder(false, @"C:\Rec", @"C:\Aud", @"C:\Def"));
            Assert.Equal(@"C:\Aud", RecordingSavePath.ResolveDestFolder(true, @"C:\Rec", @"C:\Aud", @"C:\Def"));
            Assert.Equal(@"C:\Def", RecordingSavePath.ResolveDestFolder(false, null, @"C:\Aud", @"C:\Def"));
        }
    }
}
