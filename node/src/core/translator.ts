/** Optional online translation of OCR text. Keys live in settings.json (mode 0600). */
import type { AppSettings } from "./settings.js";

export async function translateText(text: string, target: "zh" | "en", s: AppSettings): Promise<string> {
  if (!text.trim()) return "";
  const t = s.translate;
  const zh = target === "zh";
  if (t.provider === "None") throw new Error("未设置翻译服务");
  if (t.provider === "DeepL") {
    if (!t.key) throw new Error("未设置 DeepL API 密钥");
    const ep = t.endpoint?.trim() ? t.endpoint.replace(/\/$/, "") : t.key.endsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com";
    const resp = await fetch(ep + "/v2/translate", {
      method: "POST",
      headers: { Authorization: "DeepL-Auth-Key " + t.key, "Content-Type": "application/json" },
      body: JSON.stringify({ text: [text], target_lang: zh ? "ZH" : "EN-US" }),
    });
    const j = (await resp.json()) as { translations: { text: string }[] };
    if (!resp.ok) throw new Error("DeepL HTTP " + resp.status);
    return j.translations[0].text;
  }
  if (t.provider === "Azure") {
    if (!t.key) throw new Error("未设置 Azure Translator 密钥");
    const ep = t.endpoint?.trim() ? t.endpoint.replace(/\/$/, "") : "https://api.cognitive.microsofttranslator.com";
    const headers: Record<string, string> = { "Ocp-Apim-Subscription-Key": t.key, "Content-Type": "application/json" };
    if (t.region) headers["Ocp-Apim-Subscription-Region"] = t.region.trim();
    const resp = await fetch(ep + "/translate?api-version=3.0&to=" + (zh ? "zh-Hans" : "en"), {
      method: "POST",
      headers,
      body: JSON.stringify([{ Text: text }]),
    });
    const j = (await resp.json()) as { translations: { text: string }[] }[];
    if (!resp.ok) throw new Error("Azure HTTP " + resp.status);
    return j[0].translations[0].text;
  }
  if (t.provider === "LibreTranslate") {
    const ep = t.endpoint?.trim() ? t.endpoint.replace(/\/$/, "") : "https://libretranslate.com";
    const resp = await fetch(ep + "/translate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ q: text, source: "auto", target: zh ? "zh" : "en", api_key: t.key || undefined }),
    });
    const j = (await resp.json()) as { translatedText?: string };
    if (!resp.ok) throw new Error("LibreTranslate HTTP " + resp.status);
    return j.translatedText ?? "";
  }
  throw new Error("unknown provider");
}

export async function openAiLatex(png: Buffer, s: AppSettings): Promise<string> {
  const c = s.formula;
  if (!c.apiBase) throw new Error("未设置 API 地址");
  let url = c.apiBase.replace(/\/$/, "");
  if (!url.endsWith("/chat/completions")) url += "/chat/completions";
  const body = {
    model: c.apiModel?.trim() || "gpt-4o-mini",
    temperature: 0,
    messages: [
      {
        role: "user",
        content: [
          { type: "text", text: "Transcribe the mathematical formula in this image to LaTeX. Reply with the LaTeX only, no $ delimiters, no explanation." },
          { type: "image_url", image_url: { url: "data:image/png;base64," + png.toString("base64") } },
        ],
      },
    ],
  };
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (c.apiKey) headers.Authorization = "Bearer " + c.apiKey;
  const resp = await fetch(url, { method: "POST", headers, body: JSON.stringify(body) });
  const j = (await resp.json()) as { choices: { message: { content: string } }[] };
  if (!resp.ok) throw new Error("HTTP " + resp.status);
  return j.choices[0].message.content;
}

export async function mathpixLatex(png: Buffer, s: AppSettings): Promise<string> {
  const c = s.formula;
  if (!c.mathpixAppId || !c.mathpixKey) throw new Error("未设置 Mathpix app_id / app_key");
  const resp = await fetch("https://api.mathpix.com/v3/text", {
    method: "POST",
    headers: { app_id: c.mathpixAppId.trim(), app_key: c.mathpixKey, "Content-Type": "application/json" },
    body: JSON.stringify({ src: "data:image/png;base64," + png.toString("base64"), formats: ["latex_styled", "text"] }),
  });
  const j = (await resp.json()) as { error?: string; latex_styled?: string; text?: string };
  if (!resp.ok) throw new Error("Mathpix HTTP " + resp.status);
  if (j.error) throw new Error("Mathpix: " + j.error);
  return j.latex_styled ?? j.text ?? "";
}
