using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using BestSnap.Models;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>
    /// Pluggable transcript summarizer: OpenAI-compatible chat-completions (Chat settings, else Formula OpenAI),
    /// otherwise an on-device TextRank extractive fallback. Never sends audio; text leaves the machine only
    /// when the user configured a provider and confirmed the first-time notice.
    /// </summary>
    public static class ChatSummarizer
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };

        public static bool HasNetworkProvider(AppSettings s)
        {
            if (s == null) return false;
            if (!string.IsNullOrWhiteSpace(s.Chat?.ApiBase)) return true;
            return s.Formula?.Engine == FormulaEngine.OpenAI && !string.IsNullOrWhiteSpace(s.Formula.ApiBase);
        }

        public static (string Base, string Model, string Key) Resolve(AppSettings s)
        {
            if (s?.Chat != null && !string.IsNullOrWhiteSpace(s.Chat.ApiBase))
                return (s.Chat.ApiBase.Trim(), string.IsNullOrWhiteSpace(s.Chat.ApiModel) ? "gpt-4o-mini" : s.Chat.ApiModel.Trim(), Secrets.Unprotect(s.Chat.ApiKeyProtected));
            var f = s?.Formula;
            if (f != null && f.Engine == FormulaEngine.OpenAI && !string.IsNullOrWhiteSpace(f.ApiBase))
                return (f.ApiBase.Trim(), string.IsNullOrWhiteSpace(f.ApiModel) ? "gpt-4o-mini" : f.ApiModel.Trim(), Secrets.Unprotect(f.ApiKeyProtected));
            return (null, null, null);
        }

        public static async Task<string> SummarizeAsync(IReadOnlyList<TranscriptCue> cues, AppSettings settings, bool allowNetwork, IProgress<string> status, CancellationToken ct)
        {
            cues ??= Array.Empty<TranscriptCue>();
            if (cues.Count == 0) return SummarizeMath.ExtractiveMarkdown(cues);
            if (!allowNetwork || !HasNetworkProvider(settings))
            {
                status?.Report(L.T("正在生成本机摘要…"));
                return await Task.Run(() => SummarizeMath.ExtractiveMarkdown(cues), ct).ConfigureAwait(false);
            }
            var (apiBase, model, key) = Resolve(settings);
            bool zh = SummarizeMath.LooksChinese(TranscriptFormat.PlainText(cues));
            var chunks = SummarizeMath.ChunkCues(cues, SummarizeMath.DefaultChunkChars);
            var partials = new List<string>();
            for (int i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                status?.Report(string.Format(L.T("正在摘要 {0}/{1}…"), i + 1, chunks.Count));
                string body = TranscriptFormat.ToTxt(chunks[i]);
                string sys = zh
                    ? "你是会议记录助手。用中文输出 Markdown，包含：概述、要点（带 [mm:ss] 时间戳）、待办。不要编造原文没有的内容。"
                    : "You summarize transcripts. Reply in the transcript language as Markdown with Overview, Key points (include [mm:ss] timestamps), and Action items. Do not invent facts.";
                partials.Add(await ChatAsync(apiBase, model, key, sys, body, ct).ConfigureAwait(false));
            }
            if (partials.Count == 1 && chunks.Count == 1)
                return FormatLlmMarkdown(partials[0], zh);
            status?.Report(L.T("正在合并摘要…"));
            string reduceSys = zh
                ? "把下面多段部分摘要合并成一份 Markdown：# 摘要，## 概述，## 要点，## 待办。保留时间戳，删除重复。"
                : "Merge the partial notes into one Markdown document with # Summary, ## Overview, ## Key points, ## Action items. Keep timestamps, drop duplicates.";
            string merged = await ChatAsync(apiBase, model, key, reduceSys, string.Join("\n\n---\n\n", partials), ct).ConfigureAwait(false);
            return FormatLlmMarkdown(merged, zh);
        }

        static string FormatLlmMarkdown(string md, bool zh)
        {
            md = (md ?? "").Trim();
            if (md.Length == 0) return SummarizeMath.FormatMarkdown(zh, false, "", Array.Empty<string>(), Array.Empty<string>());
            if (!md.StartsWith("#", StringComparison.Ordinal))
                md = (zh ? "# 摘要\r\n\r\n" : "# Summary\r\n\r\n") + md;
            return md.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
        }

        static async Task<string> ChatAsync(string apiBase, string model, string key, string system, string user, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiBase)) throw new InvalidOperationException(L.T("未设置 API 地址"));
            var url = apiBase.TrimEnd('/');
            if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) url += "/chat/completions";
            var body = new JsonObject
            {
                ["model"] = model,
                ["temperature"] = 0.2,
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = user })
            };
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using (req)
            using (var resp = await Http.SendAsync(req, ct).ConfigureAwait(false))
            {
                var s = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException("HTTP " + (int)resp.StatusCode + ": " + (s.Length > 300 ? s.Substring(0, 300) : s));
                var j = JsonNode.Parse(s);
                return (string)j?["choices"]?[0]?["message"]?["content"] ?? "";
            }
        }
    }
}
