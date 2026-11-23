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
            var temp = Path.Combine(root, "temp", "recordings");
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
        public void PromoteToFolder_DoesNotDeleteOriginalOutsideTempEvenIfDestDiffers()
        {
            var root = Path.Combine(Path.GetTempPath(), "bs-keep-" + Guid.NewGuid().ToString("N"));
            var srcDir = Path.Combine(root, "New folder", "OneDrive", "Pictures", "SnapForge");
            var destDir = Path.Combine(root, "Videos");
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(destDir);
            try
            {
                string src = Path.Combine(srcDir, "Rec_20261123_222047.mp4");
                File.WriteAllBytes(src, new byte[] { 7, 7, 7, 7 });
                string saved = RecordingSavePath.PromoteToFolder(src, destDir);
                Assert.True(File.Exists(src), "saved original in a user folder must not be deleted");
                Assert.True(File.Exists(saved));
                Assert.False(RecordingSavePath.PathsEqual(src, saved));
                Assert.Equal(4, new FileInfo(src).Length);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void Hosts_AreSeparatedAndUseLocalhost()
        {
            Assert.Equal("vs.bestsnap.localhost", RecordingSavePath.VirtualHost);
            Assert.Equal("media.bestsnap.localhost", RecordingSavePath.MediaHost);
            Assert.NotEqual(RecordingSavePath.VirtualHost, RecordingSavePath.MediaHost);
            Assert.EndsWith(".localhost", RecordingSavePath.VirtualHost);
            Assert.EndsWith(".localhost", RecordingSavePath.MediaHost);
            Assert.Equal("https://vs.bestsnap.localhost", RecordingSavePath.EditorOrigin);
        }

        [Fact]
        public void MediaSourceUrl_EncodesSpacesAndNonAscii_AndStaysOnMediaHost()
        {
            string folder = Path.Combine("D:", "New folder", "OneDrive", "Pictures", "SnapForge");
            string spaced = Path.Combine(folder, "Rec 2026.mp4");
            string url = RecordingSavePath.MediaSourceUrl(spaced);
            Assert.StartsWith("https://media.bestsnap.localhost/source/", url);
            Assert.Contains("%20", url);
            Assert.DoesNotContain("New folder", url);
            Assert.True(HostedMediaServe.TryParseSourceUri(url, out string name));
            Assert.Equal("Rec 2026.mp4", name);
            Assert.True(HostedMediaServe.NameMatchesSource(name, spaced));

            string zh = Path.Combine(folder, "录屏.mp4");
            string zhUrl = RecordingSavePath.MediaSourceUrl(zh);
            Assert.True(HostedMediaServe.TryParseSourceUri(zhUrl, out string zhName));
            Assert.Equal("录屏.mp4", zhName);
            Assert.False(HostedMediaServe.IsMediaHostUri("https://vs.bestsnap.localhost/source/Rec.mp4"));
            Assert.False(HostedMediaServe.TryParseSourceUri("https://vs.bestsnap.localhost/index.html", out _));
        }

        [Fact]
        public void GuardExportDest_NeverReturnsOriginal()
        {
            var orig = Path.Combine(Path.GetTempPath(), "Rec_20261123_222047.mp4");
            Assert.False(RecordingSavePath.WouldOverwriteOriginal(orig, RecordingSavePath.DefaultEditedPath(orig)));
            string guarded = RecordingSavePath.GuardExportDest(orig, orig);
            Assert.False(RecordingSavePath.PathsEqual(orig, guarded));
            Assert.Contains("-edited", Path.GetFileName(guarded));
        }

        [Fact]
        public void IsUsableMedia_RejectsMissingAndZeroByte()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-use-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string missing = Path.Combine(dir, "no.mp4");
                Assert.False(RecordingSavePath.IsUsableMedia(missing));
                string empty = Path.Combine(dir, "empty.mp4");
                File.WriteAllBytes(empty, Array.Empty<byte>());
                Assert.False(RecordingSavePath.IsUsableMedia(empty));
                string ok = Path.Combine(dir, "ok.mp4");
                File.WriteAllBytes(ok, new byte[] { 1, 2 });
                Assert.True(RecordingSavePath.IsUsableMedia(ok));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void EnsureVideoFolder_CreatesMissingDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-vid-" + Guid.NewGuid().ToString("N"), "SnapForge");
            try
            {
                Assert.False(Directory.Exists(dir));
                string got = RecordingSavePath.EnsureVideoFolder(dir, null);
                Assert.True(Directory.Exists(got));
                Assert.Equal(Path.GetFullPath(dir), got);
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(dir), true); } catch { } }
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
