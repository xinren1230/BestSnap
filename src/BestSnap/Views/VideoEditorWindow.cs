using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BestSnap.Plugins;
using BestSnap.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using IOPath = System.IO.Path;

namespace BestSnap.Views
{
    /// <summary>Offline WebView2 host for the vendored AGPL Video Studio editor.</summary>
    public class VideoEditorWindow : Window
    {
        readonly string _source;
        readonly WebView2 _web = new WebView2();
        readonly Dictionary<string, FileStream> _writes = new Dictionary<string, FileStream>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _writeDest = new Dictionary<string, string>(StringComparer.Ordinal);
        string _dist;

        public VideoEditorWindow(string file)
        {
            _source = file ?? "";
            Title = L.T("视频编辑") + " - " + IOPath.GetFileName(_source);
            Width = 1280; Height = 800; MinWidth = 800; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = _web;
            Loaded += async (s, e) =>
            {
                try { await InitAsync().ConfigureAwait(true); }
                catch (Exception ex)
                {
                    Logger.Error("VideoEditor", ex);
                    MessageBox.Show(this, L.T("无法打开视频编辑器：\n") + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
            };
            Closed += (s, e) =>
            {
                foreach (var kv in _writes)
                {
                    try { kv.Value.Dispose(); } catch { }
                }
                _writes.Clear();
                try { _web.Dispose(); } catch { }
            };
        }

        async Task InitAsync()
        {
            if (!RecordingSavePath.IsUsableMedia(_source))
                throw new InvalidOperationException(L.T("无法打开视频：文件不存在或大小为 0。"));
            if (!VideoStudioHost.TryFindDist(out _dist))
                throw new InvalidOperationException(L.T("找不到编辑器文件。"));
            Directory.CreateDirectory(VideoStudioHost.UserDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, VideoStudioHost.UserDataFolder).ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async(env).ConfigureAwait(true);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            // Dist on its own host. Media is a DIFFERENT host that is never folder-mapped;
            // WebView2 does not raise WebResourceRequested for SetVirtualHostNameToFolderMapping
            // (MicrosoftEdge/WebView2Feedback #1847, #2685).
            core.SetVirtualHostNameToFolderMapping(
                RecordingSavePath.VirtualHost, _dist, CoreWebView2HostResourceAccessKind.Allow);
            core.AddWebResourceRequestedFilter(
                "https://" + RecordingSavePath.MediaHost + "/*",
                CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnResourceRequested;
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted += async (s, e) =>
            {
                try
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        type = "openMedia",
                        url = RecordingSavePath.MediaSourceUrl(_source),
                        name = IOPath.GetFileName(_source)
                    });
                    core.PostWebMessageAsJson(payload);
                }
                catch (Exception ex) { Logger.Error("VideoEditorOpen", ex); }
                await Task.CompletedTask.ConfigureAwait(true);
            };
            core.Navigate("https://" + RecordingSavePath.VirtualHost + "/index.html?host=1");
        }

        void OnResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!HostedMediaServe.IsMediaHostUri(e.Request.Uri)) return;
            string range = null;
            try
            {
                var headers = e.Request.Headers;
                if (headers != null && headers.Contains("Range"))
                    range = headers.GetHeader("Range");
            }
            catch { }
            HostedMediaResponse served = null;
            try
            {
                served = HostedMediaServe.Serve(e.Request.Method, e.Request.Uri, range, _source);
                e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(
                    served.Body, served.Status, served.Reason, served.Headers);
                if (served.Status >= 400)
                    Logger.Info("VideoEditorMedia fail " + served.Status + " " + e.Request.Uri);
                else
                    Logger.Info("VideoEditorMedia served " + served.Status + " " + e.Request.Uri + " bytes=" + served.ContentLength);
            }
            catch (Exception ex)
            {
                Logger.Error("VideoEditorMedia", ex);
                try
                {
                    e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(
                        Stream.Null, 500, "Error",
                        "Content-Length: 0\r\nAccess-Control-Allow-Origin: " + RecordingSavePath.EditorOrigin + "\r\n");
                }
                catch { }
                served?.Dispose();
            }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(raw)) return;
            JsonElement root;
            try { root = JsonDocument.Parse(raw).RootElement; }
            catch { return; }
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "request") return;
            int id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            string method = root.TryGetProperty("method", out var mEl) ? mEl.GetString() : "";
            JsonElement args = root.TryGetProperty("params", out var pEl) ? pEl : default;
            _ = HandleAsync(id, method, args);
        }

        async Task HandleAsync(int id, string method, JsonElement args)
        {
            try
            {
                object result = method switch
                {
                    "saveAs" => await SaveAsAsync(args).ConfigureAwait(true),
                    "writeChunk" => WriteChunk(args),
                    "transcribe" => await TranscribeAsync(args).ConfigureAwait(true),
                    "sttAvailable" => new { available = true },
                    _ => throw new InvalidOperationException("Unknown method: " + method)
                };
                Reply(id, result, null);
            }
            catch (Exception ex)
            {
                Logger.Error("VideoEditorBridge", ex);
                Reply(id, null, ex.Message);
            }
        }

        async Task<object> SaveAsAsync(JsonElement args)
        {
            string fileName = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("fileName", out var n) ? n.GetString() : "";
            string ext = IOPath.GetExtension(fileName);
            string suggested = RecordingSavePath.DefaultEditedPath(_source, string.IsNullOrEmpty(ext) ? null : ext);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = IOPath.GetFileName(suggested),
                InitialDirectory = IOPath.GetDirectoryName(suggested),
                Filter = FilterFor(ext),
                OverwritePrompt = true
            };
            if (dlg.ShowDialog(this) != true)
                return new { accepted = false };
            string dest = RecordingSavePath.GuardExportDest(_source, dlg.FileName);
            string token = Guid.NewGuid().ToString("N");
            string tmp = dest + "." + token + ".part";
            var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
            _writes[token] = fs;
            _writeDest[token] = dest;
            await Task.CompletedTask.ConfigureAwait(true);
            return new { accepted = true, token, path = dest };
        }

        object WriteChunk(JsonElement args)
        {
            string token = args.GetProperty("token").GetString();
            bool last = args.TryGetProperty("last", out var lastEl) && lastEl.GetBoolean();
            string data = args.TryGetProperty("data", out var dEl) ? dEl.GetString() : "";
            if (string.IsNullOrEmpty(token) || !_writes.TryGetValue(token, out var fs))
                throw new InvalidOperationException("invalid write token");
            if (!string.IsNullOrEmpty(data))
            {
                byte[] bytes = Convert.FromBase64String(data);
                fs.Write(bytes, 0, bytes.Length);
            }
            if (last)
            {
                string dest = _writeDest[token];
                fs.Dispose();
                _writes.Remove(token);
                _writeDest.Remove(token);
                string tmp = dest + "." + token + ".part";
                if (RecordingSavePath.WouldOverwriteOriginal(_source, dest))
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    throw new InvalidOperationException(L.T("已保存的原片不会被覆盖。请导出为新文件。"));
                }
                if (File.Exists(dest) && !RecordingSavePath.PathsEqual(dest, _source)) File.Delete(dest);
                File.Move(tmp, dest);
                return new { path = dest, done = true };
            }
            return new { ok = true };
        }

        async Task<object> TranscribeAsync(JsonElement args)
        {
            string b64 = args.TryGetProperty("wavBase64", out var b) ? b.GetString() : "";
            string language = args.TryGetProperty("language", out var lang) ? lang.GetString() : "auto";
            if (string.IsNullOrEmpty(b64)) throw new InvalidOperationException("empty audio");
            string tmp = IOPath.Combine(IOPath.GetTempPath(), "bs-vs-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                File.WriteAllBytes(tmp, Convert.FromBase64String(b64));
                var decode = new WhisperDecodeSettings { Language = WhisperDecode.NormalizeLanguage(language) };
                var svc = App.Settings;
                string model = svc?.Current?.WhisperModel ?? "auto";
                var cues = await WhisperTranscriber.TranscribeFileAsync(tmp, model, decode, null, CancellationToken.None).ConfigureAwait(true);
                var words = new List<object>(cues.Count);
                var sb = new StringBuilder();
                foreach (var c in cues)
                {
                    words.Add(new { text = c.Text, start = c.Start, end = c.End });
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(c.Text);
                }
                return new { words, segments = words, text = sb.ToString() };
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        void Reply(int id, object result, string error)
        {
            object payload = error != null
                ? new { type = "response", id, error }
                : new { type = "response", id, result };
            string json = JsonSerializer.Serialize(payload);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { _web.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
            }));
        }

        static string FilterFor(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            if (ext == ".webm") return "WebM|*.webm|All|*.*";
            if (ext == ".gif") return "GIF|*.gif|All|*.*";
            if (ext == ".mov") return "MOV|*.mov|All|*.*";
            if (ext == ".wav") return "WAV|*.wav|All|*.*";
            if (ext == ".mp3") return "MP3|*.mp3|All|*.*";
            return "MP4|*.mp4|All|*.*";
        }
    }
}
