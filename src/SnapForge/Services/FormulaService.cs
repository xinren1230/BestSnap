using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SnapForge.Models;
using SnapForge.Plugins;

namespace SnapForge.Services
{
    /// <summary>
    /// Formula → LaTeX. Offline: pix2text-mfr ONNX (https://huggingface.co/breezedeus/pix2text-mfr, MIT).
    /// Prefers models shipped next to the exe (installer); otherwise downloads on first use into
    /// %LocalAppData%\BestSnap\models\pix2text-mfr and verifies SHA-256. Online (optional, user's own key):
    /// any OpenAI-compatible vision chat API, or Mathpix.
    /// </summary>
    public static class FormulaService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        public const string ModelRepo = "https://huggingface.co/breezedeus/pix2text-mfr";
        private const string Rev = "main";
        public static readonly (string Name, string Sha256, long Size)[] ModelFiles = BundledModels.FormulaFiles;
        public static string ModelDir => BundledModels.ResolveFormulaDir(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        public static FormulaSettings Cfg => App.Settings.Current.Formula ?? (App.Settings.Current.Formula = new FormulaSettings());
        public static IFormulaRecognizer Local => PluginHost.All<IFormulaRecognizer>().FirstOrDefault(o => { try { return o.IsAvailable; } catch { return false; } });
        public static bool ModelsReady => BundledModels.IsComplete(ModelDir);

        /// <summary>Downloads missing model files into the LocalAppData cache (never into Program Files) and checks SHA-256.</summary>
        public static async Task EnsureModelsAsync(IProgress<double> progress, CancellationToken ct)
        {
            if (BundledModels.IsComplete(BundledModels.BundledFormulaDir(AppContext.BaseDirectory)))
            {
                progress?.Report(1);
                return;
            }
            var dest = BundledModels.CacheFormulaDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Directory.CreateDirectory(dest);
            long total = ModelFiles.Sum(f => f.Size), done = 0;
            foreach (var f in ModelFiles)
            {
                var path = Path.Combine(dest, f.Name);
                if (File.Exists(path) && new FileInfo(path).Length == f.Size) { done += f.Size; progress?.Report((double)done / total); continue; }
                var tmp = path + ".part";
                using (var resp = await Http.GetAsync($"{ModelRepo}/resolve/{Rev}/{f.Name}", HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    using var src = await resp.Content.ReadAsStreamAsync(ct);
                    using var dst = File.Create(tmp);
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buf = new byte[1 << 16]; int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n, ct); sha.AppendData(buf, 0, n); done += n; progress?.Report(Math.Min(1, (double)done / total));
                    }
                    var hex = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    if (hex != f.Sha256) { dst.Close(); File.Delete(tmp); throw new InvalidOperationException(L.T("模型文件校验失败") + ": " + f.Name); }
                }
                File.Move(tmp, path, true);
            }
        }

        public static async Task<string> RecognizeAsync(byte[] png, IProgress<double> download, CancellationToken ct)
        {
            var c = Cfg;
            switch (c.Engine)
            {
                case FormulaEngine.OpenAI: return Clean(await OpenAiAsync(png, c, ct));
                case FormulaEngine.Mathpix: return Clean(await MathpixAsync(png, c, ct));
                default:
                    var loc = Local ?? throw new InvalidOperationException(L.T("离线公式识别需要 OCR 插件（plugins\\SnapForge.Ocr）。"));
                    if (!ModelsReady) await EnsureModelsAsync(download, ct);
                    return await loc.RecognizeLatexAsync(png, ModelDir, ct);
            }
        }

        /// <summary>Strip $…$, \[…\], ```latex fences that chat models like to add.</summary>
        public static string Clean(string s)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("```")) { s = s.Substring(s.IndexOf('\n') + 1); int e = s.LastIndexOf("```"); if (e >= 0) s = s.Substring(0, e); s = s.Trim(); }
            foreach (var (a, b) in new[] { ("$$", "$$"), ("\\[", "\\]"), ("\\(", "\\)"), ("$", "$") })
                if (s.Length > a.Length + b.Length && s.StartsWith(a) && s.EndsWith(b)) { s = s.Substring(a.Length, s.Length - a.Length - b.Length).Trim(); break; }
            return s;
        }

        private static async Task<string> OpenAiAsync(byte[] png, FormulaSettings c, CancellationToken ct)
        {
            var key = Secrets.Unprotect(c.ApiKeyProtected);
            if (string.IsNullOrWhiteSpace(c.ApiBase)) throw new InvalidOperationException(L.T("未设置 API 地址"));
            var url = c.ApiBase.TrimEnd('/'); if (!url.EndsWith("/chat/completions")) url += "/chat/completions";
            var body = new JsonObject
            {
                ["model"] = string.IsNullOrWhiteSpace(c.ApiModel) ? "gpt-4o-mini" : c.ApiModel.Trim(),
                ["temperature"] = 0,
                ["messages"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "text", ["text"] = "Transcribe the mathematical formula in this image to LaTeX. Reply with the LaTeX only, no $ delimiters, no explanation." },
                        new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(png) } })
                })
            };
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            if (!string.IsNullOrEmpty(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var j = await Send(req, ct);
            return (string)j["choices"][0]["message"]["content"];
        }

        private static async Task<string> MathpixAsync(byte[] png, FormulaSettings c, CancellationToken ct)
        {
            var key = Secrets.Unprotect(c.MathpixKeyProtected);
            if (string.IsNullOrWhiteSpace(c.MathpixAppId) || string.IsNullOrEmpty(key)) throw new InvalidOperationException(L.T("未设置 Mathpix app_id / app_key"));
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mathpix.com/v3/text");
            req.Headers.Add("app_id", c.MathpixAppId.Trim()); req.Headers.Add("app_key", key);
            var body = new JsonObject { ["src"] = "data:image/png;base64," + Convert.ToBase64String(png), ["formats"] = new JsonArray("latex_styled", "text") };
            req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            var j = await Send(req, ct);
            if (j["error"] != null) throw new InvalidOperationException("Mathpix: " + j["error"]);
            return (string)j["latex_styled"] ?? (string)j["text"];
        }

        private static async Task<JsonNode> Send(HttpRequestMessage req, CancellationToken ct)
        {
            using (req)
            using (var resp = await Http.SendAsync(req, ct))
            {
                var s = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)resp.StatusCode + ": " + (s.Length > 300 ? s.Substring(0, 300) : s));
                return JsonNode.Parse(s);
            }
        }
    }
}
