using System;
using System.IO;
using System.Text;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class HostedMediaServeTests
    {
        static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "src", "BestSnap", "Views", "VideoEditorWindow.cs")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            }
        }

        [Fact]
        public void Serve_200_HasTypeLengthAndCorsOrigin()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "clip.mp4");
                var payload = Encoding.ASCII.GetBytes("0123456789abcdefghij");
                File.WriteAllBytes(file, payload);
                string uri = RecordingSavePath.MediaSourceUrl(file);
                using var res = HostedMediaServe.Serve("GET", uri, null, file);
                Assert.Equal(200, res.Status);
                Assert.Equal("OK", res.Reason);
                Assert.Contains("Content-Type: video/mp4", res.Headers);
                Assert.Contains("Content-Length: " + payload.Length, res.Headers);
                Assert.Contains("Access-Control-Allow-Origin: https://vs.bestsnap.localhost", res.Headers);
                Assert.DoesNotContain("Allow-Origin: *", res.Headers);
                Assert.Equal(payload.Length, res.ContentLength);
                var got = new byte[payload.Length];
                Assert.Equal(payload.Length, res.Body.Read(got, 0, got.Length));
                Assert.Equal(payload, got);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Serve_404_WhenMissingOrEmptyOrWrongName()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string missing = Path.Combine(dir, "gone.mp4");
                using (var res = HostedMediaServe.Serve("GET", RecordingSavePath.MediaSourceUrl(missing), null, missing))
                {
                    Assert.Equal(404, res.Status);
                    Assert.Contains("Access-Control-Allow-Origin: https://vs.bestsnap.localhost", res.Headers);
                }

                string empty = Path.Combine(dir, "empty.mp4");
                File.WriteAllBytes(empty, Array.Empty<byte>());
                using (var res = HostedMediaServe.Serve("GET", RecordingSavePath.MediaSourceUrl(empty), null, empty))
                    Assert.Equal(404, res.Status);

                string ok = Path.Combine(dir, "ok.mp4");
                File.WriteAllBytes(ok, new byte[] { 1, 2, 3 });
                string wrong = "https://media.bestsnap.localhost/source/other.mp4";
                using (var res = HostedMediaServe.Serve("GET", wrong, null, ok))
                    Assert.Equal(404, res.Status);

                using (var res = HostedMediaServe.Serve("GET", "https://vs.bestsnap.localhost/source/ok.mp4", null, ok))
                    Assert.Equal(404, res.Status);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Serve_Range_Returns206AndSlice()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "clip.mp4");
                var payload = Encoding.ASCII.GetBytes("ABCDEFGHIJ");
                File.WriteAllBytes(file, payload);
                string uri = RecordingSavePath.MediaSourceUrl(file);
                using var res = HostedMediaServe.Serve("GET", uri, "bytes=2-5", file);
                Assert.Equal(206, res.Status);
                Assert.Equal("Partial Content", res.Reason);
                Assert.Contains("Content-Length: 4", res.Headers);
                Assert.Contains("Content-Range: bytes 2-5/10", res.Headers);
                Assert.Contains("Access-Control-Allow-Origin: https://vs.bestsnap.localhost", res.Headers);
                var got = new byte[4];
                Assert.Equal(4, res.Body.Read(got, 0, 4));
                Assert.Equal(Encoding.ASCII.GetBytes("CDEF"), got);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void Serve_UnsatisfiableRange_416()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bs-media-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "clip.mp4");
                File.WriteAllBytes(file, new byte[10]);
                using var res = HostedMediaServe.Serve("GET", RecordingSavePath.MediaSourceUrl(file), "bytes=99-120", file);
                Assert.Equal(416, res.Status);
                Assert.Contains("Content-Range: bytes */10", res.Headers);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void ParseByteRange_SuffixAndOpenEnd()
        {
            Assert.True(HostedMediaServe.TryParseByteRange("bytes=0-0", 10, out long s, out long e));
            Assert.Equal(0, s); Assert.Equal(0, e);
            Assert.True(HostedMediaServe.TryParseByteRange("bytes=8-", 10, out s, out e));
            Assert.Equal(8, s); Assert.Equal(9, e);
            Assert.True(HostedMediaServe.TryParseByteRange("bytes=-3", 10, out s, out e));
            Assert.Equal(7, s); Assert.Equal(9, e);
            Assert.False(HostedMediaServe.TryParseByteRange("bytes=10-12", 10, out _, out _));
        }

        [Fact]
        public void RecorderFailure_DetectsMfWriteErrorAndDefersStop()
        {
            Assert.True(RecorderFailure.ShouldDeferStop(hasStarted: false, isRecording: true));
            Assert.False(RecorderFailure.ShouldDeferStop(hasStarted: true, isRecording: true));
            Assert.False(RecorderFailure.ShouldDeferStop(hasStarted: false, isRecording: false));
            Assert.True(RecorderFailure.IsEncoderWriteError("Write error (0xc00d36bb)"));
            Assert.True(RecorderFailure.IsEncoderWriteError("MF_E_UNEXPECTED"));
            Assert.False(RecorderFailure.IsEncoderWriteError("disk full"));
        }

        [Fact]
        public void DeleteEmptyTemp_OnlyTouchesTempRecordings()
        {
            var root = Path.Combine(Path.GetTempPath(), "bs-empty-" + Guid.NewGuid().ToString("N"));
            var temp = Path.Combine(root, "temp", "recordings");
            var user = Path.Combine(root, "New folder", "SnapForge");
            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(user);
            try
            {
                string emptyTemp = Path.Combine(temp, "Rec_fail.mp4");
                File.WriteAllBytes(emptyTemp, Array.Empty<byte>());
                File.WriteAllText(emptyTemp + ".meta.json", "{}");
                TempRecordingStore.DeleteEmptyTemp(emptyTemp);
                Assert.False(File.Exists(emptyTemp));
                Assert.False(File.Exists(emptyTemp + ".meta.json"));

                string userZero = Path.Combine(user, "Rec_keep.mp4");
                File.WriteAllBytes(userZero, Array.Empty<byte>());
                TempRecordingStore.DeleteEmptyTemp(userZero);
                Assert.True(File.Exists(userZero), "0-byte file in the save folder must not be deleted by temp cleanup");

                string filled = Path.Combine(temp, "Rec_ok.mp4");
                File.WriteAllBytes(filled, new byte[] { 1 });
                TempRecordingStore.DeleteEmptyTemp(filled);
                Assert.True(File.Exists(filled));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public void VideoEditorWindow_MapsEditorHost_ServesMediaOnSeparateHost()
        {
            string text = File.ReadAllText(Path.Combine(RepoRoot, "src", "BestSnap", "Views", "VideoEditorWindow.cs"));
            Assert.Contains("SetVirtualHostNameToFolderMapping", text);
            Assert.Contains("RecordingSavePath.VirtualHost", text);
            Assert.Contains("RecordingSavePath.MediaHost", text);
            Assert.Contains("CoreWebView2WebResourceContext.All", text);
            Assert.Contains("https://\" + RecordingSavePath.MediaHost + \"/*\"", text);
            Assert.DoesNotContain("/session/*", text);
            Assert.Contains("MediaSourceUrl", text);
            Assert.Contains("VideoEditorMedia served", text);
            Assert.Contains("VideoEditorMedia fail", text);
            Assert.Contains("WouldOverwriteOriginal", text);
            Assert.Contains("GuardExportDest", text);
        }

        [Fact]
        public void ToolbarAndTray_OpenVideoFolder()
        {
            string toolbar = File.ReadAllText(Path.Combine(RepoRoot, "src", "BestSnap", "Views", "CaptureToolbar.cs"));
            Assert.Contains("打开视频文件夹", toolbar);
            Assert.Contains("OpenVideoFolder", toolbar);
            string tray = File.ReadAllText(Path.Combine(RepoRoot, "src", "BestSnap", "Services", "AppController.cs"));
            Assert.Contains("打开视频文件夹", tray);
            Assert.Contains("public void OpenVideoFolder()", tray);
            Assert.Contains("EnsureVideoFolder", tray);
            Assert.Contains("DeleteEmptyTemp", tray);
            Assert.Contains("IsEncoderWriteError", tray);
            string commit = File.ReadAllText(Path.Combine(RepoRoot, "src", "BestSnap", "Services", "RecordingCommit.cs"));
            Assert.Contains("IsUsableMedia", commit);
            Assert.Contains("protectOriginal: true", commit);
        }
    }
}
