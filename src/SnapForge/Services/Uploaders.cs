using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnapForge.Plugins;

namespace SnapForge.Services
{
    public sealed class FolderUploader : IUploader
    {
        public string Id => "folder"; public string DisplayName => L.T("保存到文件夹");
        public Task<string> UploadAsync(string localFile, string remoteName, UploadTarget t, string secret, CancellationToken ct)
        {
            Directory.CreateDirectory(t.Url);
            var dest = Path.Combine(t.Url, remoteName);
            File.Copy(localFile, dest, overwrite: false);
            return Task.FromResult(Uploads.PublicUrl(t, remoteName) ?? dest);
        }
    }

#pragma warning disable SYSLIB0014 // FtpWebRequest is the only FTP client in the BCL; fine for single-file uploads.
    public sealed class FtpUploader : IUploader
    {
        public string Id => "ftp"; public string DisplayName => "FTP / FTPS";
        public async Task<string> UploadAsync(string localFile, string remoteName, UploadTarget t, string secret, CancellationToken ct)
        {
            var baseUrl = t.Url.TrimEnd('/');
            bool ftps = baseUrl.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
            if (ftps) baseUrl = "ftp://" + baseUrl.Substring(7);
            var req = (FtpWebRequest)WebRequest.Create(baseUrl + "/" + Uri.EscapeDataString(remoteName));
            req.Method = WebRequestMethods.Ftp.UploadFile; req.UseBinary = true; req.UsePassive = true; req.EnableSsl = ftps;
            req.Credentials = new NetworkCredential(string.IsNullOrEmpty(t.User) ? "anonymous" : t.User, secret ?? "");
            using (ct.Register(() => req.Abort()))
            {
                using (var rs = await req.GetRequestStreamAsync())
                using (var fs = File.OpenRead(localFile)) await fs.CopyToAsync(rs, ct);
                using var resp = (FtpWebResponse)await req.GetResponseAsync();
                if ((int)resp.StatusCode >= 400) throw new IOException("FTP: " + resp.StatusDescription);
            }
            return Uploads.PublicUrl(t, remoteName) ?? baseUrl + "/" + remoteName;
        }
    }
#pragma warning restore SYSLIB0014

    public sealed class WebDavUploader : IUploader
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        public string Id => "webdav"; public string DisplayName => L.T("WebDAV (Nextcloud/坚果云…)");
        public async Task<string> UploadAsync(string localFile, string remoteName, UploadTarget t, string secret, CancellationToken ct)
        {
            var url = t.Url.TrimEnd('/') + "/" + Uri.EscapeDataString(remoteName);
            using var req = new HttpRequestMessage(HttpMethod.Put, url);
            if (!string.IsNullOrEmpty(t.User))
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(t.User + ":" + (secret ?? ""))));
            using var fs = File.OpenRead(localFile);
            req.Content = new StreamContent(fs);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(Uploads.Mime(remoteName));
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) throw new IOException($"WebDAV {(int)resp.StatusCode} {resp.ReasonPhrase}");
            return Uploads.PublicUrl(t, remoteName) ?? url;
        }
    }

    /// <summary>Built-in uploaders + any IUploader found in plugins.</summary>
    public static class Uploads
    {
        public static List<IUploader> All()
        {
            var l = new List<IUploader> { new FolderUploader(), new FtpUploader(), new WebDavUploader() };
            l.AddRange(PluginHost.All<IUploader>());
            return l;
        }
        public static IUploader Find(string kind) => All().FirstOrDefault(u => string.Equals(u.Id, kind, StringComparison.OrdinalIgnoreCase));
        internal static string PublicUrl(UploadTarget t, string name) => string.IsNullOrWhiteSpace(t.PublicUrlPrefix) ? null : t.PublicUrlPrefix.TrimEnd('/') + "/" + Uri.EscapeDataString(name);
        internal static string Mime(string n) => Path.GetExtension(n).ToLowerInvariant() switch
        { ".png" => "image/png", ".jpg" => "image/jpeg", ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".mp4" => "video/mp4", ".bmp" => "image/bmp", _ => "application/octet-stream" };

        public static async Task<string> UploadAsync(UploadTarget t, string localFile, CancellationToken ct = default)
        {
            var up = Find(t.Kind) ?? throw new InvalidOperationException(L.T("未知的上传方式") + ": " + t.Kind);
            var result = await up.UploadAsync(localFile, Path.GetFileName(localFile), t, Secrets.Unprotect(t.ProtectedSecret), ct);
            Logger.Info("Uploaded " + localFile + " -> " + t.Name);
            return result;
        }
    }
}
