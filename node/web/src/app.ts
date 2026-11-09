import katex from "katex";
import { addHistory, listHistory, getHistory } from "./history.ts";
import { t, toggleLang } from "./i18n.ts";
import { recognizeFromRgba } from "./ocr.ts";
import { recognizeLatexFromRgba } from "./formula.ts";
import { decodeQrFromRgba } from "./qr.ts";
import type { ProgressFn } from "./models.ts";

type Tool = "select" | "crop" | "arrow" | "rect" | "text";
type Ann = { kind: "arrow" | "rect" | "text"; x: number; y: number; x2: number; y2: number; text?: string; color: string };

const COLORS = ["#e11d48", "#f97316", "#22c55e", "#3b82f6", "#7c3aed"];

const app = document.getElementById("app")!;
let image = new Image();
let hasImage = false;
let tool: Tool = "select";
let color = COLORS[0];
let anns: Ann[] = [];
let draft: Ann | null = null;
let cropA: { x: number; y: number } | null = null;
let cropB: { x: number; y: number } | null = null;
let recorder: MediaRecorder | null = null;
let recChunks: Blob[] = [];

function ui() {
  const s = t();
  app.innerHTML = `
    <div class="top">
      <div class="toolbar" id="bar">
        <img class="logo" src="${import.meta.env.BASE_URL}logo_24.png" alt="" />
        <button id="btn-region" title="${s.region}">▢</button>
        <button id="btn-window" title="${s.window}">▣</button>
        <button id="btn-full" title="${s.full}">⛶</button>
        <button id="btn-record" title="${s.record}">●</button>
        <button class="more" id="more">${s.more}</button>
        <button class="wide" id="btn-lang">${s.lang}</button>
        <div class="menu hidden" id="menu">
          <button data-a="ocr" data-testid="ocr">${s.ocr}</button>
          <button data-a="latex" data-testid="latex-btn">${s.latex}</button>
          <button data-a="qr" data-testid="qr">${s.qr}</button>
          <div class="hr"></div>
          <button data-a="upload">${s.upload}</button>
          <button data-a="history">${s.history}</button>
        </div>
      </div>
    </div>
    <div class="intro">
      <video
        controls
        playsinline
        preload="metadata"
        width="1920"
        height="1080"
        poster="${import.meta.env.BASE_URL}snapforge-linux-demo.jpg"
        src="${import.meta.env.BASE_URL}snapforge-linux-demo.mp4"
      ></video>
      <p class="caption">${s.demoCaption}</p>
    </div>
    <div class="layout">
      <div class="stage" id="stage">
        <div class="drop" id="drop">${s.drop}<br/><button type="button" id="btn-upload">${s.upload}</button></div>
        <canvas id="cv" class="hidden"></canvas>
        <div class="overlay-hint hidden" id="hint">${s.hintCrop}</div>
      </div>
      <div class="side">
        <div class="card">
          <h2>${s.title}</h2>
          <div class="tools">
            <button data-t="select">${s.select}</button>
            <button data-t="crop">${s.crop}</button>
            <button data-t="arrow">${s.arrow}</button>
            <button data-t="rect">${s.rect}</button>
            <button data-t="text">${s.text}</button>
            <button class="accent" data-a="copy">${s.copy}</button>
            <button class="accent" data-a="download">${s.download}</button>
          </div>
          <div class="progress" id="progress"></div>
        </div>
        <div class="card">
          <h2>${s.result}</h2>
          <pre id="result" data-testid="result"></pre>
          <div id="katex"></div>
          <pre id="latex-src" class="hidden" data-testid="latex"></pre>
          <button type="button" id="copy-latex" class="hidden">${s.copyLatex}</button>
        </div>
        <div class="card">
          <h2>${s.history}</h2>
          <div class="hist" id="hist"></div>
        </div>
        <div class="foot">${s.license}</div>
      </div>
    </div>
    <input type="file" id="file" accept="image/*" data-testid="file" />
  `;
  bind();
  void refreshHist();
}

function bind() {
  const menu = document.getElementById("menu")!;
  document.getElementById("more")!.onclick = () => menu.classList.toggle("hidden");
  document.getElementById("btn-lang")!.onclick = () => {
    toggleLang();
    ui();
  };
  document.getElementById("btn-region")!.onclick = () => startCrop();
  document.getElementById("btn-window")!.onclick = () => void captureDisplay();
  document.getElementById("btn-full")!.onclick = () => void captureDisplay();
  document.getElementById("btn-record")!.onclick = () => void toggleRecord();
  document.getElementById("btn-upload")!.onclick = () => document.getElementById("file")!.click();
  document.getElementById("file")!.addEventListener("change", onFile);
  app.querySelectorAll("[data-a]").forEach((b) => {
    (b as HTMLButtonElement).onclick = () => void onAction((b as HTMLButtonElement).dataset.a!);
  });
  app.querySelectorAll("[data-t]").forEach((b) => {
    (b as HTMLButtonElement).onclick = () => {
      tool = (b as HTMLButtonElement).dataset.t as Tool;
      app.querySelectorAll("[data-t]").forEach((x) => x.classList.toggle("active", (x as HTMLElement).dataset.t === tool));
      if (tool === "crop") startCrop();
    };
  });
  const stage = document.getElementById("stage")!;
  stage.addEventListener("dragover", (e) => {
    e.preventDefault();
  });
  stage.addEventListener("drop", (e) => {
    e.preventDefault();
    const f = e.dataTransfer?.files?.[0];
    if (f) void loadBlob(f);
  });
  document.addEventListener("paste", (e) => {
    const f = [...(e.clipboardData?.items || [])].find((i) => i.type.startsWith("image/"));
    if (f) void loadBlob(f.getAsFile()!);
  });
  const cv = canvas();
  cv.onmousedown = onDown;
  cv.onmousemove = onMove;
  cv.onmouseup = onUp;
  document.getElementById("copy-latex")!.onclick = async () => {
    const tex = document.getElementById("latex-src")!.textContent || "";
    await navigator.clipboard.writeText(tex);
  };
}

function canvas(): HTMLCanvasElement {
  return document.getElementById("cv") as HTMLCanvasElement;
}

function ctx(): CanvasRenderingContext2D {
  return canvas().getContext("2d")!;
}

function pos(e: MouseEvent): { x: number; y: number } {
  const cv = canvas();
  const r = cv.getBoundingClientRect();
  return { x: ((e.clientX - r.left) * cv.width) / r.width, y: ((e.clientY - r.top) * cv.height) / r.height };
}

function startCrop() {
  if (!hasImage) return;
  tool = "crop";
  cropA = cropB = null;
  document.getElementById("hint")!.classList.remove("hidden");
}

function onDown(e: MouseEvent) {
  if (!hasImage) return;
  const p = pos(e);
  if (tool === "crop") {
    cropA = p;
    cropB = p;
    return;
  }
  if (tool === "text") {
    const text = prompt(t().text, "") || "";
    if (text) anns.push({ kind: "text", x: p.x, y: p.y, x2: p.x, y2: p.y, text, color });
    draw();
    return;
  }
  if (tool === "arrow" || tool === "rect") {
    draft = { kind: tool, x: p.x, y: p.y, x2: p.x, y2: p.y, color };
  }
}

function onMove(e: MouseEvent) {
  const p = pos(e);
  if (tool === "crop" && cropA) {
    cropB = p;
    draw();
    return;
  }
  if (draft) {
    draft.x2 = p.x;
    draft.y2 = p.y;
    draw();
  }
}

function onUp() {
  if (tool === "crop" && cropA && cropB) {
    applyCrop();
    cropA = cropB = null;
    document.getElementById("hint")!.classList.add("hidden");
    tool = "select";
    return;
  }
  if (draft) {
    anns.push(draft);
    draft = null;
    draw();
  }
}

function applyCrop() {
  if (!cropA || !cropB) return;
  const cv = canvas();
  const x = Math.max(0, Math.min(cropA.x, cropB.x));
  const y = Math.max(0, Math.min(cropA.y, cropB.y));
  const w = Math.min(cv.width - x, Math.abs(cropB.x - cropA.x));
  const h = Math.min(cv.height - y, Math.abs(cropB.y - cropA.y));
  if (w < 4 || h < 4) return;
  const tmp = document.createElement("canvas");
  tmp.width = Math.max(1, Math.round(w));
  tmp.height = Math.max(1, Math.round(h));
  tmp.getContext("2d")!.drawImage(cv, x, y, w, h, 0, 0, tmp.width, tmp.height);
  anns = [];
  void loadUrl(tmp.toDataURL("image/png"));
}

function drawAnn(c: CanvasRenderingContext2D, a: Ann) {
  c.save();
  c.strokeStyle = a.color;
  c.fillStyle = a.color;
  c.lineWidth = 3;
  if (a.kind === "arrow") {
    c.beginPath();
    c.moveTo(a.x, a.y);
    c.lineTo(a.x2, a.y2);
    c.stroke();
    const ang = Math.atan2(a.y2 - a.y, a.x2 - a.x);
    c.beginPath();
    c.moveTo(a.x2, a.y2);
    c.lineTo(a.x2 - 14 * Math.cos(ang - 0.4), a.y2 - 14 * Math.sin(ang - 0.4));
    c.lineTo(a.x2 - 14 * Math.cos(ang + 0.4), a.y2 - 14 * Math.sin(ang + 0.4));
    c.closePath();
    c.fill();
  } else if (a.kind === "rect") {
    c.strokeRect(Math.min(a.x, a.x2), Math.min(a.y, a.y2), Math.abs(a.x2 - a.x), Math.abs(a.y2 - a.y));
  } else if (a.kind === "text") {
    c.font = "18px sans-serif";
    c.fillText(a.text || "", a.x, a.y + 16);
  }
  c.restore();
}

function draw() {
  if (!hasImage) return;
  const cv = canvas();
  const c = ctx();
  c.setTransform(1, 0, 0, 1, 0, 0);
  c.clearRect(0, 0, cv.width, cv.height);
  c.drawImage(image, 0, 0);
  for (const a of anns) drawAnn(c, a);
  if (draft) drawAnn(c, draft);
  if (tool === "crop" && cropA && cropB) {
    c.fillStyle = "rgba(0,0,0,.45)";
    c.fillRect(0, 0, cv.width, cv.height);
    const x = Math.min(cropA.x, cropB.x),
      y = Math.min(cropA.y, cropB.y),
      w = Math.abs(cropB.x - cropA.x),
      h = Math.abs(cropB.y - cropA.y);
    c.clearRect(x, y, w, h);
    c.drawImage(image, x, y, w, h, x, y, w, h);
    c.strokeStyle = "#7c3aed";
    c.lineWidth = 2;
    c.strokeRect(x, y, w, h);
  }
}

async function loadUrl(url: string) {
  const img = new Image();
  img.src = url;
  await img.decode();
  image = img;
  hasImage = true;
  const cv = canvas();
  cv.width = img.width;
  cv.height = img.height;
  cv.classList.remove("hidden");
  document.getElementById("drop")!.classList.add("hidden");
  draw();
  cv.toBlob((b) => {
    if (b) void addHistory(b, img.width, img.height).then(refreshHist);
  }, "image/png");
}

async function loadBlob(file: Blob) {
  await loadUrl(URL.createObjectURL(file));
}

function onFile(e: Event) {
  const f = (e.target as HTMLInputElement).files?.[0];
  if (f) void loadBlob(f);
}

async function captureDisplay() {
  const stream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
  const track = stream.getVideoTracks()[0];
  const video = document.createElement("video");
  video.srcObject = stream;
  await video.play();
  await new Promise((r) => setTimeout(r, 120));
  const c = document.createElement("canvas");
  c.width = video.videoWidth;
  c.height = video.videoHeight;
  c.getContext("2d")!.drawImage(video, 0, 0);
  track.stop();
  stream.getTracks().forEach((t) => t.stop());
  await loadUrl(c.toDataURL("image/png"));
}

async function toggleRecord() {
  const btn = document.getElementById("btn-record")!;
  if (recorder && recorder.state !== "inactive") {
    recorder.stop();
    return;
  }
  const stream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: true });
  recChunks = [];
  recorder = new MediaRecorder(stream, { mimeType: MediaRecorder.isTypeSupported("video/webm;codecs=vp9") ? "video/webm;codecs=vp9" : "video/webm" });
  recorder.ondataavailable = (e) => {
    if (e.data.size) recChunks.push(e.data);
  };
  recorder.onstop = () => {
    stream.getTracks().forEach((t) => t.stop());
    const blob = new Blob(recChunks, { type: "video/webm" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = "snapforge.webm";
    a.click();
    btn.classList.remove("rec-on");
    setProgress(t().recSaved);
    recorder = null;
  };
  recorder.start();
  btn.classList.add("rec-on");
  setProgress(t().recording);
}

function setProgress(s: string) {
  document.getElementById("progress")!.textContent = s;
}

const onProg: ProgressFn = (p) => {
  setProgress(`${t().progress}: ${p.file} ${Math.round(p.fraction * 100)}%`);
};

function rgba(): { data: Uint8ClampedArray; w: number; h: number } {
  const cv = canvas();
  const { data } = ctx().getImageData(0, 0, cv.width, cv.height);
  return { data, w: cv.width, h: cv.height };
}

async function onAction(a: string) {
  document.getElementById("menu")!.classList.add("hidden");
  if (a === "upload") {
    document.getElementById("file")!.click();
    return;
  }
  if (a === "copy") {
    await copyPng();
    return;
  }
  if (a === "download") {
    downloadPng();
    return;
  }
  if (a === "history") {
    await refreshHist();
    return;
  }
  if (!hasImage) {
    document.getElementById("result")!.textContent = t().noImage;
    return;
  }
  const { data, w, h } = rgba();
  try {
    if (a === "ocr") {
      setProgress("OCR…");
      const text = await recognizeFromRgba(data, w, h, onProg);
      document.getElementById("result")!.textContent = text;
      document.getElementById("latex-src")!.classList.add("hidden");
      document.getElementById("copy-latex")!.classList.add("hidden");
      document.getElementById("katex")!.innerHTML = "";
    } else if (a === "latex") {
      setProgress("LaTeX…");
      const tex = await recognizeLatexFromRgba(data, w, h, onProg);
      document.getElementById("result")!.textContent = tex;
      const src = document.getElementById("latex-src")!;
      src.textContent = tex;
      src.classList.remove("hidden");
      document.getElementById("copy-latex")!.classList.remove("hidden");
      try {
        katex.render(tex, document.getElementById("katex")!, { throwOnError: false, displayMode: true });
      } catch {
        /* ignore */
      }
    } else if (a === "qr") {
      const hits = decodeQrFromRgba(data, w, h);
      document.getElementById("result")!.textContent = hits.map((h) => `${h.format}: ${h.text}`).join("\n") || "(none)";
    }
    setProgress("");
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    document.getElementById("result")!.textContent = msg;
    setProgress(msg);
  }
}

async function copyPng() {
  const cv = canvas();
  const blob: Blob = await new Promise((resolve) => cv.toBlob((b) => resolve(b!), "image/png"));
  try {
    await navigator.clipboard.write([new ClipboardItem({ "image/png": blob })]);
  } catch {
    downloadPng();
  }
}

function downloadPng() {
  const a = document.createElement("a");
  a.href = canvas().toDataURL("image/png");
  a.download = "snapforge.png";
  a.click();
}

async function refreshHist() {
  const box = document.getElementById("hist");
  if (!box) return;
  const items = await listHistory();
  box.innerHTML = "";
  for (const it of items) {
    const img = document.createElement("img");
    img.src = URL.createObjectURL(it.png);
    img.title = new Date(it.created).toLocaleString();
    img.onclick = async () => {
      const full = await getHistory(it.id);
      if (full) void loadBlob(full.png);
    };
    box.appendChild(img);
  }
}

export function start() {
  ui();
}

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape") {
    cropA = cropB = null;
    document.getElementById("hint")?.classList.add("hidden");
    tool = "select";
    draw();
  }
});
