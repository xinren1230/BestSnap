using System;
using System.IO;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class TempRecordingStoreTests
    {
        [Fact]
        public void Folder_IsTempRecordingsUnderLocalData()
        {
            var p = TempRecordingStore.Folder(Path.Combine("C:", "data"));
            Assert.Contains("temp", p.Replace('\\', '/'));
            Assert.Contains("recordings", p);
        }

        [Fact]
        public void NewMediaPath_WritesSidecarAndAvoidsCollision()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t0 = new DateTime(2026, 11, 12, 13, 0, 0, DateTimeKind.Utc);
                string a = TempRecordingStore.NewMediaPath(dir, "Rec", ".mp4", t0);
                File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
                Assert.False(TempRecordingStore.IsReady(a));
                TempRecordingStore.MarkComplete(a, t0);
                Assert.True(TempRecordingStore.IsReady(a));
                Assert.True(File.Exists(TempRecordingStore.SidecarPath(a)));
                string b = TempRecordingStore.NewMediaPath(dir, "Rec", ".mp4", t0);
                Assert.NotEqual(a, b);
                Assert.True(TempRecordingStore.IsUnder(a, dir));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Cleanup_DeletesOnlyOlderThanMaxAge()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var oldF = Path.Combine(dir, "old.wav");
                var newF = Path.Combine(dir, "new.wav");
                File.WriteAllText(oldF, "x");
                File.WriteAllText(newF, "y");
                File.SetLastWriteTimeUtc(oldF, DateTime.UtcNow.AddDays(-8));
                File.SetLastWriteTimeUtc(newF, DateTime.UtcNow);
                var gone = TempRecordingStore.Cleanup(dir, TimeSpan.FromDays(7), DateTime.UtcNow);
                Assert.Contains(oldF, gone);
                Assert.False(File.Exists(oldF));
                Assert.True(File.Exists(newF));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void ListReady_SkipsIncompleteAndMeta()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t = DateTime.UtcNow;
                string ready = TempRecordingStore.NewMediaPath(dir, "Audio", ".wav", t);
                File.WriteAllBytes(ready, new byte[] { 9, 9 });
                TempRecordingStore.MarkComplete(ready, t);
                string open = TempRecordingStore.NewMediaPath(dir, "Rec", ".mp4", t);
                File.WriteAllBytes(open, new byte[] { 1 });
                var list = TempRecordingStore.ListReady(dir);
                Assert.Contains(ready, list);
                Assert.DoesNotContain(open, list);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void DeletePair_RemovesMediaSidecarAnd16k()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var m = Path.Combine(dir, "a.mp4");
                File.WriteAllText(m, "x");
                File.WriteAllText(m + ".meta.json", "{}");
                File.WriteAllText(m + ".16k.wav", "y");
                File.WriteAllText(Path.ChangeExtension(m, ".txt"), "z");
                TempRecordingStore.DeletePair(m);
                Assert.False(File.Exists(m));
                Assert.False(File.Exists(m + ".meta.json"));
                Assert.False(File.Exists(m + ".16k.wav"));
                Assert.False(File.Exists(Path.ChangeExtension(m, ".txt")));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
