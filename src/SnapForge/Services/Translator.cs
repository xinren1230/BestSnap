using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SnapForge.Models;

namespace SnapForge.Services
{
    /// <summary>Online translation of OCR text (DeepL / Azure Translator / LibreTranslate). Offline default = none.</summary>
    public static class Translator
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public static bool Configured => (App.Settings?.Current?.Translate?.Provider ?? TranslateProvider.None) != TranslateProvider.None;

        /// <param name="target">"zh" or "en"</param>
        public static async Task<string> TranslateAsync(string text, string target, CancellationToken ct = default)
        {
            var t = App.Settings.Current.Translate ?? new TranslateSettings();
            if (string.IsNullOrWhiteSpace(text)) return "";
            string key = Secrets.Unprotect(t.KeyProtected);
            bool zh = target == "zh";
            HttpRequestMessage req;
            switch (t.Provider)
            {
                case TranslateProvider.DeepL:
                {
                    if (string.IsNullOrEmpty(key)) throw new InvalidOperationException(L.T("未设置 DeepL API 密钥"));
                    string ep = !string.IsNullOrWhiteSpace(t.Endpoint) ? t.Endpoint.TrimEnd('/') : key.EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";
                    req = new HttpRequestMessage(HttpMethod.Post, ep + "/v2/translate");
                    req.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
                    var body = new JsonObject { ["text"] = new JsonArray(text), ["target_lang"] = zh ? "ZH" : "EN-US" };
                    req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                    var j = await Send(req, ct);
                    return (string)j["translations"][0]["text"];
                }
                case TranslateProvider.Azure:
                {
                    if (string.IsNullOrEmpty(key)) throw new InvalidOperationException(L.T("未设置 Azure Translator 密钥"));
                    string ep = !string.IsNullOrWhiteSpace(t.Endpoint) ? t.Endpoint.TrimEnd('/') : "https://api.cognitive.microsofttranslator.com";
                    req = new HttpRequestMessage(HttpMethod.Post, ep + "/translate?api-version=3.0&to=" + (zh ? "zh-Hans" : "en"));
                    req.Headers.Add("Ocp-Apim-Subscription-Key", key);
                    if (!string.IsNullOrWhiteSpace(t.Region)) req.Headers.Add("Ocp-Apim-Subscription-Region", t.Region.Trim());
                    var body = new JsonArray(new JsonObject { ["Text"] = text });
                    req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                    var j = await Send(req, ct);
                    return (string)j[0]["translations"][0]["text"];
                }
                case TranslateProvider.LibreTranslate:
                {
                    if (string.IsNullOrWhiteSpace(t.Endpoint)) throw new InvalidOperationException(L.T("未设置 LibreTranslate 地址"));
                    req = new HttpRequestMessage(HttpMethod.Post, t.Endpoint.TrimEnd('/') + "/translate");
                    var body = new JsonObject { ["q"] = text, ["source"] = "auto", ["target"] = zh ? "zh" : "en", ["format"] = "text" };
                    if (!string.IsNullOrEmpty(key)) body["api_key"] = key;
                    req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                    var j = await Send(req, ct);
                    return (string)j["translatedText"];
                }
                default: throw new InvalidOperationException(L.T("未配置翻译服务（设置 → 截图 → 翻译服务设置）"));
            }
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
