export type Lang = "zh-CN" | "en";

const zh = {
  title: "SnapForge 网页版",
  region: "框选区域",
  window: "窗口/标签页",
  full: "屏幕",
  record: "录屏",
  more: "更多 ▾",
  ocr: "识别文字 (OCR)",
  latex: "公式转 LaTeX",
  qr: "识别二维码/条码",
  history: "截图历史",
  lang: "English",
  drop: "粘贴、拖放或上传图片，或从工具栏抓取屏幕",
  upload: "上传图片",
  copy: "复制 PNG",
  download: "下载 PNG",
  arrow: "箭头",
  rect: "矩形",
  text: "文字",
  crop: "框选",
  select: "选择",
  copyLatex: "复制 LaTeX",
  result: "识别结果",
  progress: "下载模型",
  recording: "正在录制… 再次点击停止",
  recSaved: "录屏已下载",
  noImage: "请先截图或上传图片",
  license: "Apache-2.0 · 模型在浏览器本地运行，不会上传你的图",
  hintCrop: "拖选区域 · Esc 取消",
  demoCaption: "Linux 桌面演示 · 1920×1080 · 约 56 秒",
};

const en: typeof zh = {
  title: "SnapForge Web",
  region: "Region",
  window: "Window / tab",
  full: "Screen",
  record: "Record",
  more: "More ▾",
  ocr: "OCR",
  latex: "Formula → LaTeX",
  qr: "QR / barcode",
  history: "History",
  lang: "中文",
  drop: "Paste, drop or upload an image, or capture from the toolbar",
  upload: "Upload",
  copy: "Copy PNG",
  download: "Download PNG",
  arrow: "Arrow",
  rect: "Rect",
  text: "Text",
  crop: "Crop",
  select: "Select",
  copyLatex: "Copy LaTeX",
  result: "Result",
  progress: "Downloading models",
  recording: "Recording… click again to stop",
  recSaved: "Recording downloaded",
  noImage: "Capture or upload an image first",
  license: "Apache-2.0 · Models run locally in your browser; images are not uploaded",
  hintCrop: "Drag a region · Esc to cancel",
  demoCaption: "Linux desktop demo · 1920×1080 · ~56s",
};

let current: Lang = (localStorage.getItem("sf-lang") as Lang) || "zh-CN";

export function lang(): Lang {
  return current;
}

export function t(): typeof zh {
  return current === "en" ? en : zh;
}

export function toggleLang(): Lang {
  current = current === "zh-CN" ? "en" : "zh-CN";
  localStorage.setItem("sf-lang", current);
  return current;
}
