import { app, BrowserWindow, clipboard, desktopCapturer, globalShortcut, ipcMain, Menu, nativeImage, screen, shell, Tray } from "electron";
import { spawn, type ChildProcess } from "node:child_process";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { captureFull, captureRect, captureWindow, listWindows, screenSize } from "../core/capture.js";
import { autoSave, saveImage } from "../core/exporter.js";
import { addHistory, historyFile, loadHistory } from "../core/history.js";
import { blurRegion, pixelate, sizeOf, stitch } from "../core/image-ops.js";
import { loadSettings, saveSettings, setAutostart, type AppSettings } from "../core/settings.js";
import { Recorder } from "../core/recorder.js";
import { uploadFile } from "../core/uploaders.js";
import { appRoot } from "../core/paths.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const renderer = path.join(here, "renderer", "index.html");
const iconPng = path.join(appRoot(), "assets", "logo_64.png");
const isDemo = process.argv.includes("--demo");

app.commandLine.appendSwitch("no-sandbox");
app.commandLine.appendSwitch("disable-gpu");
app.disableHardwareAcceleration();

let tray: Tray | null = null;
let toolbar: BrowserWindow | null = null;
let overlay: BrowserWindow | null = null;
let editor: BrowserWindow | null = null;
let settingsWin: BrowserWindow | null = null;
let captionWin: BrowserWindow | null = null;
let backdrop: BrowserWindow | null = null;
let demoServer: http.Server | null = null;
let toolbarMenuOpen = false;
let worker: ChildProcess | null = null;
let workerReady = false;
let reqId = 1;
const pending = new Map<number, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
let settings: AppSettings = loadSettings();
let lastCapture: { png: Buffer; rect?: { x: number; y: number; w: number; h: number }; mode: string } | null = null;
let recorder: Recorder | null = null;
let timedTimer: NodeJS.Timeout | null = null;
let timedLeft = 0;

function spawnWorker(): void {
  const workerJs = path.join(here, "worker.js");
  const workerTs = path.join(here, "worker.ts");
  const execPath = process.env.npm_node_execpath || process.env.NODE || "node";
  const entry = fs.existsSync(workerJs) ? workerJs : workerTs;
  const args = entry.endsWith(".ts") ? ["--import", "tsx", entry] : [entry];
  worker = spawn(execPath, args, {
    stdio: ["pipe", "pipe", "inherit"],
    env: { ...process.env, SNAPFORGE_ROOT: appRoot() },
  });
  let buf = "";
  worker.stdout?.on("data", (d: Buffer) => {
    buf += d.toString();
    let n: number;
    while ((n = buf.indexOf("\n")) >= 0) {
      const line = buf.slice(0, n);
      buf = buf.slice(n + 1);
      try {
        const msg = JSON.parse(line) as { id: number; ok: boolean; result?: unknown; error?: string };
        const p = pending.get(msg.id);
        if (!p) continue;
        pending.delete(msg.id);
        if (msg.ok) p.resolve(msg.result);
        else p.reject(new Error(msg.error || "worker error"));
      } catch {
        /* ignore */
      }
    }
  });
  worker.on("exit", () => {
    workerReady = false;
    worker = null;
  });
  workerReady = true;
}

function callWorker(cmd: string, args: Record<string, unknown> = {}): Promise<unknown> {
  if (!worker) spawnWorker();
  const id = reqId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    worker!.stdin!.write(JSON.stringify({ id, cmd, args }) + "\n");
  });
}

function frameless(opts: Electron.BrowserWindowConstructorOptions): BrowserWindow {
  const { webPreferences: extraWp, ...rest } = opts;
  const w = new BrowserWindow({
    show: false,
    autoHideMenuBar: true,
    icon: iconPng,
    webPreferences: {
      preload: path.join(here, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
      ...extraWp,
    },
    ...rest,
  });
  const locked = rest.title;
  if (locked) {
    w.on("page-title-updated", (e) => {
      e.preventDefault();
      if (w.getTitle() !== locked) w.setTitle(locked);
    });
  }
  return w;
}

function createToolbar(): void {
  toolbar = frameless({
    width: 440,
    height: 48,
    frame: false,
    transparent: false,
    backgroundColor: "#1c1c1e",
    alwaysOnTop: true,
    skipTaskbar: true,
    resizable: false,
    title: "SnapForge",
  });
  toolbar.loadFile(renderer, { query: { view: "toolbar" } });
  const { width } = screen.getPrimaryDisplay().workAreaSize;
  const x = isDemo ? Math.round(width / 2 - 220) : (settings.toolbarX ?? Math.round(width / 2 - 220));
  const y = isDemo ? 10 : (settings.toolbarY ?? 12);
  toolbar.setPosition(x, y);
  toolbar.setMinimumSize(440, 48);
  toolbar.setMaximumSize(440, 48);
  toolbar.setAlwaysOnTop(true, "screen-saver");
  if (settings.showToolbarOnStartup) toolbar.show();
  toolbar.on("moved", () => {
    if (!toolbar) return;
    const [tx, ty] = toolbar.getPosition();
    settings.toolbarX = tx;
    settings.toolbarY = ty;
  });
  toolbar.on("closed", () => {
    toolbar = null;
  });
}

function toggleToolbar(): void {
  if (!toolbar) createToolbar();
  else if (toolbar.isVisible()) toolbar.hide();
  else toolbar.show();
}

function createTray(): void {
  const img = nativeImage.createFromPath(iconPng);
  tray = new Tray(img.isEmpty() ? nativeImage.createEmpty() : img);
  const menu = () =>
    Menu.buildFromTemplate([
      { label: "显示/隐藏工具栏", click: toggleToolbar },
      { type: "separator" },
      { label: "矩形区域", click: () => startCapture("region") },
      { label: "窗口/对象", click: () => startCapture("window") },
      { label: "全屏", click: () => startCapture("full") },
      { label: "活动窗口", click: () => startCapture("active") },
      { type: "separator" },
      { label: "OCR", click: () => startCapture("ocr") },
      { label: "公式 → LaTeX", click: () => startCapture("latex") },
      { label: "二维码", click: () => startCapture("qr") },
      { type: "separator" },
      { label: "历史", click: () => openView("history") },
      { label: "设置", click: () => openView("settings") },
      { type: "separator" },
      { label: "退出", click: () => app.quit() },
    ]);
  tray.setToolTip("SnapForge");
  tray.setContextMenu(menu());
  tray.on("click", toggleToolbar);
}

type ScreenRect = { x: number; y: number; w: number; h: number };

function placeView(w: BrowserWindow, view: string, avoid?: ScreenRect): void {
  if (view === "editor") {
    w.setPosition(40, 72);
    return;
  }
  const b = w.getBounds();
  const disp = screen.getPrimaryDisplay().bounds;
  if (isDemo) {
    // Park result/history on the right so the demo page cards stay visible.
    w.setPosition(Math.max(920, disp.width - b.width - 36), 72);
    return;
  }
  if (!avoid) {
    w.setPosition(48, 72);
    return;
  }
  const margin = 20;
  const right = avoid.x + avoid.w + margin;
  const left = avoid.x - b.width - margin;
  let x = 48;
  if (right + b.width <= disp.width - 8) x = right;
  else if (left >= 8) x = left;
  else x = Math.max(8, disp.width - b.width - 8);
  const y = Math.max(60, Math.min(avoid.y, disp.height - b.height - 60));
  w.setPosition(Math.round(x), Math.round(y));
}

function openView(view: string, extra: Record<string, string> = {}, avoid?: ScreenRect): BrowserWindow {
  const titles: Record<string, string> = {
    editor: "SnapForge Editor",
    history: "SnapForge History",
    result: "SnapForge Result",
    settings: "SnapForge Settings",
    stitch: "SnapForge Stitch",
    compare: "SnapForge Compare",
  };
  const w = frameless({
    width: view === "editor" ? 1100 : view === "result" ? 700 : 820,
    height: view === "editor" ? 680 : view === "result" ? 500 : 560,
    frame: true,
    backgroundColor: "#f6f6f7",
    title: titles[view] || "SnapForge",
  });
  const q = new URLSearchParams({ view, ...extra }).toString();
  w.loadFile(renderer, { query: Object.fromEntries(new URLSearchParams(q)) });
  placeView(w, view, avoid);
  w.show();
  if (view === "editor") editor = w;
  if (view === "settings") settingsWin = w;
  raiseToolbar();
  return w;
}

function applyToolbarMenu(open: boolean): void {
  toolbarMenuOpen = open;
  if (!toolbar || toolbar.isDestroyed()) return;
  const [x, y] = toolbar.getPosition();
  if (open) {
    toolbar.setMinimumSize(440, 48);
    toolbar.setMaximumSize(440, 900);
    toolbar.setBounds({ x, y, width: 440, height: 480 });
  } else {
    toolbar.setMaximumSize(440, 48);
    toolbar.setMinimumSize(440, 48);
    toolbar.setBounds({ x, y, width: 440, height: 48 });
  }
}

function collapseToolbarMenu(): void {
  applyToolbarMenu(false);
}

function raiseToolbar(): void {
  if (captionWin && !captionWin.isDestroyed()) {
    captionWin.setAlwaysOnTop(true, "screen-saver");
    captionWin.show();
  }
  if (!toolbar || toolbar.isDestroyed()) return;
  if (!toolbarMenuOpen) applyToolbarMenu(false);
  toolbar.setAlwaysOnTop(true, "screen-saver");
  toolbar.show();
}

async function grabScreenPng(): Promise<Buffer> {
  try {
    const { w, h } = screenSize();
    const sources = await desktopCapturer.getSources({ types: ["screen"], thumbnailSize: { width: w, height: h } });
    const img = sources[0]?.thumbnail;
    if (img && !img.isEmpty()) return img.toPNG();
  } catch {
    /* fallback */
  }
  return captureFull(settings.includeCursor);
}

function sleep(ms: number): Promise<void> {
  return new Promise((r) => setTimeout(r, ms));
}

async function cropPng(png: Buffer, x: number, y: number, w: number, h: number): Promise<Buffer> {
  const { default: sharp } = await import("sharp");
  const meta = await sharp(png).metadata();
  const mw = meta.width || 1;
  const mh = meta.height || 1;
  const left = Math.max(0, Math.min(Math.round(x), Math.max(0, mw - 1)));
  const top = Math.max(0, Math.min(Math.round(y), Math.max(0, mh - 1)));
  const width = Math.max(1, Math.min(Math.round(w), mw - left));
  const height = Math.max(1, Math.min(Math.round(h), mh - top));
  return Buffer.from(await sharp(png).extract({ left, top, width, height }).png().toBuffer());
}

let overlayPainted: (() => void) | null = null;
let overlayReady: Promise<void> | null = null;
let overlayShot = "";
let overlayArmed = false;
let demoReady = !isDemo;

function noteOverlayPainted(): void {
  overlayPainted?.();
  overlayPainted = null;
}

async function dimmedHex(png: Buffer): Promise<string> {
  const { default: sharp } = await import("sharp");
  const { data, info } = await sharp(png).resize(16, 9, { fit: "fill" }).removeAlpha().raw().toBuffer({ resolveWithObject: true });
  let r = 0, g = 0, b = 0, n = 0;
  const ch = info.channels || 3;
  for (let i = 0; i < data.length; i += ch) {
    r += data[i];
    g += data[i + 1];
    b += data[i + 2];
    n++;
  }
  const dim = 0.55;
  const hx = (v: number) => Math.round((v / Math.max(1, n)) * dim).toString(16).padStart(2, "0");
  return `#${hx(r)}${hx(g)}${hx(b)}`;
}

async function waitOverlayCanvas(win: BrowserWindow, timeout = 2500): Promise<void> {
  const t0 = Date.now();
  while (Date.now() - t0 < timeout) {
    if (win.isDestroyed()) return;
    try {
      const ok = await win.webContents.executeJavaScript(`
        (() => {
          const cv = document.getElementById("cv");
          if (!cv || cv.width < 16 || cv.height < 16) return false;
          const p = cv.getContext("2d").getImageData(Math.floor(cv.width / 3), Math.floor(cv.height / 3), 1, 1).data;
          return p[0] + p[1] + p[2] > 40;
        })()
      `);
      if (ok) return;
    } catch {
      /* renderer not ready */
    }
    await sleep(32);
  }
}

function ensureOverlay(): BrowserWindow {
  if (overlay && !overlay.isDestroyed()) return overlay;
  const disp = screen.getPrimaryDisplay();
  overlay = frameless({
    x: disp.bounds.x,
    y: disp.bounds.y,
    width: disp.bounds.width,
    height: disp.bounds.height,
    frame: false,
    transparent: false,
    backgroundColor: "#4a4d55",
    alwaysOnTop: true,
    skipTaskbar: true,
    fullscreen: false,
    paintWhenInitiallyHidden: true,
    title: "SnapForge Capture",
  });
  overlayReady = new Promise((resolve) => {
    overlay!.webContents.once("did-finish-load", () => resolve());
  });
  overlay.loadFile(renderer, { query: { view: "overlay" } });
  overlay.setAlwaysOnTop(true, "screen-saver");
  overlay.on("closed", () => {
    overlay = null;
    overlayReady = null;
    noteOverlayPainted();
  });
  return overlay;
}

function lowerOverlay(): void {
  overlayArmed = false;
  if (!overlay || overlay.isDestroyed()) return;
  overlay.setAlwaysOnTop(false);
  if (backdrop && !backdrop.isDestroyed()) {
    backdrop.moveTop();
    backdrop.show();
  }
  raiseToolbar();
}

async function warmupOverlay(): Promise<void> {
  const win = ensureOverlay();
  await overlayReady;
  const disp = screen.getPrimaryDisplay().bounds;
  win.setBounds({ x: disp.x, y: disp.y, width: disp.width, height: disp.height });
  win.setAlwaysOnTop(false);
  win.show();
  if (backdrop && !backdrop.isDestroyed()) {
    backdrop.moveTop();
    backdrop.show();
  }
  try {
    const png = await grabScreenPng();
    const { w, h } = await sizeOf(png);
    const tmp = path.join(os.tmpdir(), `sf-overlay-warm-${process.pid}.png`);
    fs.writeFileSync(tmp, png);
    overlayShot = tmp;
    const painted = new Promise<void>((resolve) => {
      overlayPainted = resolve;
    });
    win.webContents.send("overlay:prepare", { mode: "region", shot: tmp, sw: w, sh: h });
    await Promise.race([painted, waitOverlayCanvas(win), sleep(1500)]);
  } catch {
    await sleep(160);
  }
  lowerOverlay();
}

async function closeOverlay(): Promise<void> {
  noteOverlayPainted();
  lowerOverlay();
  if (backdrop && !backdrop.isDestroyed()) {
    try {
      backdrop.webContents.invalidate();
    } catch {
      /* ignore */
    }
  }
}

async function startCapture(mode: string): Promise<void> {
  if (settings.captureDelaySeconds > 0) await sleep(settings.captureDelaySeconds * 1000);
  const barWasVisible = !!toolbar?.isVisible();
  collapseToolbarMenu();
  toolbar?.hide();
  await sleep(40);
  try {
  if (mode === "full") {
    const png = await grabScreenPng();
    await deliver(png, "full");
    return;
  }
  if (mode === "active") {
    const png = await captureWindow(undefined, settings.includeCursor);
    await deliver(png, "active");
    return;
  }
  const png = await grabScreenPng();
  const { w, h } = await sizeOf(png);
  const disp = screen.getPrimaryDisplay();
  const bg = await dimmedHex(png).catch(() => "#4a4d55");
  const painted = new Promise<void>((resolve) => {
    overlayPainted = resolve;
  });
  const win = ensureOverlay();
  await overlayReady;
  const tmp = path.join(os.tmpdir(), `sf-overlay-${process.pid}-${Date.now()}.png`);
  fs.writeFileSync(tmp, png);
  if (overlayShot && overlayShot !== tmp) fs.rmSync(overlayShot, { force: true });
  overlayShot = tmp;
  try {
    win.setBackgroundColor(bg);
  } catch {
    /* ignore */
  }
  win.setBounds({ x: disp.bounds.x, y: disp.bounds.y, width: disp.bounds.width, height: disp.bounds.height });
  win.webContents.send("overlay:prepare", { mode, shot: tmp, sw: w, sh: h });
  await Promise.race([painted, waitOverlayCanvas(win), sleep(2000)]);
  await sleep(50);
  if (!win.isDestroyed()) {
    win.setBounds({ x: disp.bounds.x, y: disp.bounds.y, width: disp.bounds.width, height: disp.bounds.height });
    overlayArmed = true;
    win.setAlwaysOnTop(true, "screen-saver");
    win.moveTop();
    win.show();
  }
  } finally {
    if (barWasVisible) raiseToolbar();
  }
}

async function deliver(png: Buffer, mode: string, rect?: { x: number; y: number; w: number; h: number }, followup?: string): Promise<void> {
  lastCapture = { png, rect, mode };
  const { w, h } = await sizeOf(png);
  if (settings.historyEnabled) addHistory(png, w, h, settings.historyMax);
  const action = followup || settings.afterCapture;
  if (action === "ocr" || followup === "ocr") {
    await recognizeAndShow("ocr", png, rect);
    return;
  }
  if (action === "latex" || followup === "latex") {
    await recognizeAndShow("latex", png, rect);
    return;
  }
  if (action === "qr" || followup === "qr") {
    await recognizeAndShow("qr", png, rect);
    return;
  }
  if (settings.afterCapture === "CopyToClipboard") {
    clipboard.writeImage(nativeImage.createFromBuffer(png));
    return;
  }
  if (settings.afterCapture === "AutoSave" || settings.afterCapture === "AutoSaveAndClipboard") {
    const p = await autoSave(png, settings);
    saveSettings(settings);
    if (settings.afterCapture === "AutoSaveAndClipboard") clipboard.writeImage(nativeImage.createFromBuffer(png));
    shell.showItemInFolder(p);
    return;
  }
  const tmp = path.join(os.tmpdir(), `sf-edit-${Date.now()}.png`);
  fs.writeFileSync(tmp, png);
  const wined = openView("editor", { file: tmp, follow: followup || "" });
  wined.on("closed", () => fs.rmSync(tmp, { force: true }));
}

function sendResult(w: BrowserWindow, kind: string, text: string): void {
  const payload = { kind, text };
  const send = () => {
    if (!w.isDestroyed()) w.webContents.send("result", payload);
  };
  if (w.webContents.isLoading()) w.webContents.once("did-finish-load", send);
  send();
  setTimeout(send, 300);
  setTimeout(send, 900);
}

function openResult(kind: string, text: string, avoid?: ScreenRect): BrowserWindow {
  const w = openView("result", { kind }, avoid);
  sendResult(w, kind, text);
  return w;
}

async function recognizeAndShow(kind: "ocr" | "latex" | "qr", png: Buffer, rect?: ScreenRect): Promise<void> {
  const pending = kind === "latex" ? "识别中（首次加载约 112MB 模型）…" : "识别中…";
  const w = openResult(kind, pending, rect);
  try {
    const r = (await callWorker(kind, { image: png.toString("base64") })) as { text?: string; hits?: { text: string; format: string }[] };
    const text = kind === "qr" ? JSON.stringify(r.hits || []) : (r.text || "");
    sendResult(w, kind, text);
  } catch (e) {
    sendResult(w, kind, String(e instanceof Error ? e.message : e));
  }
}

function registerHotkeys(): void {
  globalShortcut.unregisterAll();
  const map: Record<string, () => void> = {
    FullScreen: () => startCapture("full"),
    ActiveWindow: () => startCapture("active"),
    WindowObject: () => startCapture("window"),
    Region: () => startCapture("region"),
    Freehand: () => startCapture("freehand"),
    FixedRegion: () => startCapture("fixed"),
    Scrolling: () => startCapture("scroll"),
    RepeatLast: () => {
      if (lastCapture) void deliver(lastCapture.png, lastCapture.mode, lastCapture.rect);
      else void startCapture("region");
    },
    Recorder: () => void toggleRecord(),
    OcrRegion: () => startCapture("ocr"),
    FormulaRegion: () => startCapture("latex"),
    PinRegion: () => startCapture("pin"),
    History: () => openView("history"),
    TimedCapture: () => toggleTimed(),
    QrRegion: () => startCapture("qr"),
  };
  for (const h of settings.hotkeys) {
    const accel = toAccel(h.gesture);
    if (!accel) continue;
    try {
      globalShortcut.register(accel, map[h.action] ?? (() => undefined));
    } catch {
      /* occupied */
    }
  }
}

function toAccel(g: string): string {
  return g
    .replace(/PrintScreen/gi, "PrintScreen")
    .replace(/Ctrl/gi, "CommandOrControl");
}

async function toggleRecord(): Promise<void> {
  if (recorder) {
    await recorder.stop();
    recorder = null;
    toolbar?.webContents.send("recording", { on: false });
    return;
  }
  const folder = settings.recording.saveFolder || path.join(os.homedir(), "Videos", "SnapForge");
  fs.mkdirSync(folder, { recursive: true });
  const ext = settings.recording.format === "Gif" ? ".gif" : ".mp4";
  const out = path.join(folder, `Rec_${Date.now()}${ext}`);
  recorder = new Recorder();
  await recorder.start({
    out,
    fps: settings.recording.fps,
    quality: settings.recording.quality,
    showCursor: settings.recording.showCursor,
    gif: ext === ".gif",
    gifMaxWidth: settings.recording.gifMaxWidth,
    mic: settings.recording.microphone,
    // Demo: grab a page region so the outer x11grab (the walkthrough video) is not
    // competing with a second fullscreen grab that freezes the Xvfb picture.
    region: isDemo ? { x: 36, y: 60, w: 640, h: 480 } : undefined,
  });
  toolbar?.webContents.send("recording", { on: true, file: out });
}

function toggleTimed(): void {
  if (timedTimer) {
    clearInterval(timedTimer);
    timedTimer = null;
    return;
  }
  timedLeft = 0;
  const folder = path.join(settings.saveFolder, "Timed");
  fs.mkdirSync(folder, { recursive: true });
  timedTimer = setInterval(() => {
    void (async () => {
      const png = await grabScreenPng();
      await saveImage(png, path.join(folder, `Timed_${Date.now()}.png`), settings.jpegQuality);
      timedLeft++;
    })();
  }, 10_000);
}

if (!isDemo) {
  const gotLock = app.requestSingleInstanceLock();
  if (!gotLock) app.quit();
  else {
    app.on("second-instance", () => {
      toolbar?.show();
    });
  }
}

function createCaptionBar(): void {
  const disp = screen.getPrimaryDisplay();
  const width = disp.bounds.width || 1920;
  const height = 56;
  captionWin = frameless({
    x: 0,
    y: Math.max(0, (disp.bounds.height || 1080) - height),
    width,
    height,
    frame: false,
    transparent: false,
    backgroundColor: "#1a1030",
    alwaysOnTop: true,
    skipTaskbar: true,
    resizable: false,
    focusable: false,
    title: "SnapForge Caption",
  });
  captionWin.setAlwaysOnTop(true, "status");
  captionWin.loadFile(renderer, { query: { view: "caption" } });
  captionWin.show();
}

function createBackdrop(): void {
  const disp = screen.getPrimaryDisplay();
  const bottom = 56;
  backdrop = frameless({
    x: 0,
    y: 0,
    width: disp.bounds.width || 1920,
    height: Math.max(400, (disp.bounds.height || 1080) - bottom),
    frame: false,
    transparent: false,
    backgroundColor: "#eef0f4",
    alwaysOnTop: false,
    skipTaskbar: true,
    title: "SnapForge Demo Page",
    webPreferences: { webSecurity: false },
  });
  const page = path.join(appRoot(), "demo", "page.html");
  backdrop.loadFile(page);
  backdrop.show();
}

async function demoLayout(): Promise<Record<string, unknown>> {
  const out: Record<string, unknown> = {};
  for (const w of BrowserWindow.getAllWindows()) {
    if (w.isDestroyed() || !w.isVisible()) continue;
    if (overlay && w === overlay && !overlayArmed) continue;
    const title = w.getTitle() || "untitled";
    const cb = w.getContentBounds();
    let buttons: Record<string, { x: number; y: number; w: number; h: number; sx: number; sy: number }> = {};
    try {
      const raw = (await w.webContents.executeJavaScript(`
        (() => {
          const nodes = [...document.querySelectorAll('[id],[data-m],[data-a],[data-t]')];
          const o = {};
          for (const el of nodes) {
            const key = el.id || el.getAttribute('data-m') || el.getAttribute('data-a') || el.getAttribute('data-t');
            if (!key) continue;
            const r = el.getBoundingClientRect();
            if (r.width < 2 || r.height < 2) continue;
            o[key] = { x: r.left, y: r.top, w: r.width, h: r.height };
          }
          return o;
        })()
      `)) as Record<string, { x: number; y: number; w: number; h: number }>;
      for (const [k, r] of Object.entries(raw || {})) {
        buttons[k] = { ...r, sx: cb.x + r.x + r.w / 2, sy: cb.y + r.y + r.h / 2 };
      }
    } catch {
      buttons = {};
    }
    let text = "";
    try {
      text = String(
        (await w.webContents.executeJavaScript(`(document.body && document.body.innerText) ? document.body.innerText.slice(0, 800) : ""`)) || "",
      );
    } catch {
      text = "";
    }
    out[title] = { x: cb.x, y: cb.y, width: cb.width, height: cb.height, buttons, text };
  }
  return out;
}

function startDemoServer(): void {
  demoServer = http.createServer((req, res) => {
    const u = new URL(req.url || "/", "http://127.0.0.1");
    res.setHeader("access-control-allow-origin", "*");
    if (!demoReady && u.pathname === "/") {
      res.statusCode = 503;
      res.end("warming");
      return;
    }
    if (u.pathname === "/caption") {
      const t = u.searchParams.get("t") || "";
      captionWin?.webContents.send("caption", t);
      res.end("ok");
      return;
    }
    if (u.pathname === "/layout") {
      void demoLayout().then((j) => {
        res.setHeader("content-type", "application/json");
        res.end(JSON.stringify(j));
      });
      return;
    }
    if (u.pathname === "/clip") {
      clipboard.writeText(u.searchParams.get("t") || "");
      res.end("ok");
      return;
    }
    if (u.pathname === "/quit") {
      res.end("ok");
      setTimeout(() => app.quit(), 200);
      return;
    }
    res.end("snapforge-demo");
  });
  demoServer.listen(38471, "127.0.0.1");
}

function prepareLiveDemo(): void {
  settings.afterCapture = "OpenEditor";
  settings.showToolbarOnStartup = true;
  settings.includeCursor = true;
  settings.historyEnabled = true;
  settings.captureDelaySeconds = 0;
  settings.recording.saveFolder = path.join(os.tmpdir(), "snapforge-demo-rec");
  settings.recording.microphone = false;
  fs.mkdirSync(settings.recording.saveFolder, { recursive: true });
  createBackdrop();
  createCaptionBar();
  createToolbar();
  startDemoServer();
  raiseToolbar();
}

process.on("uncaughtException", (e) => console.error("uncaught", e));
process.on("unhandledRejection", (e) => console.error("unhandled", e));

app.whenReady().then(async () => {
  console.log("SnapForge ready demo=", isDemo, "argv=", process.argv.join(" "));
  spawnWorker();
  if (isDemo) {
    prepareLiveDemo();
    await warmupOverlay();
    demoReady = true;
    raiseToolbar();
    return;
  }
  createToolbar();
  createTray();
  registerHotkeys();
  await warmupOverlay();
});

app.on("will-quit", () => {
  globalShortcut.unregisterAll();
  worker?.kill();
  try { demoServer?.close(); } catch { /* ignore */ }
  if (!isDemo) saveSettings(settings);
});

ipcMain.handle("settings:get", () => settings);
ipcMain.handle("settings:save", (_e, s: AppSettings) => {
  settings = { ...settings, ...s };
  saveSettings(settings);
  setAutostart(!!settings.autostart);
  registerHotkeys();
  return settings;
});
ipcMain.handle("overlay:painted", () => {
  noteOverlayPainted();
});
ipcMain.handle("capture:start", (_e, mode: string) => startCapture(mode));
ipcMain.handle("capture:result", async (_e, payload: { file: string; x: number; y: number; w: number; h: number; mode: string; action?: string }) => {
  let png: Buffer;
  try {
    png = fs.readFileSync(payload.file);
    if (payload.w > 0 && payload.h > 0) png = await cropPng(png, payload.x, payload.y, payload.w, payload.h);
  } catch {
    png =
      payload.w > 0 && payload.h > 0
        ? await captureRect({ x: payload.x, y: payload.y, w: payload.w, h: payload.h }, settings.includeCursor)
        : await grabScreenPng();
  }
  await closeOverlay();
  await sleep(80);
  if (payload.mode === "pin") {
    const w = frameless({ width: payload.w || 400, height: payload.h || 300, frame: false, alwaysOnTop: true, skipTaskbar: true, transparent: false });
    const tmp = path.join(os.tmpdir(), `sf-pin-${Date.now()}.png`);
    fs.writeFileSync(tmp, png);
    w.loadFile(renderer, { query: { view: "pin", file: tmp } });
    w.show();
    return;
  }
  await deliver(png, payload.mode, { x: payload.x, y: payload.y, w: payload.w, h: payload.h }, payload.action || payload.mode);
});
ipcMain.handle("capture:cancel", () => {
  void closeOverlay();
});
ipcMain.handle("toolbar:hide", () => toolbar?.hide());
ipcMain.handle("toolbar:drag", (_e, dx: number, dy: number) => {
  if (!toolbar) return;
  const [x, y] = toolbar.getPosition();
  toolbar.setPosition(x + dx, y + dy);
});
ipcMain.handle("worker", (_e, cmd: string, args: Record<string, unknown>) => callWorker(cmd, args));
ipcMain.handle("history:list", () => loadHistory().map((i) => ({ ...i, path: historyFile(i) })));
ipcMain.handle("history:open", (_e, file: string) => {
  openView("editor", { file });
});
ipcMain.handle("save:image", async (_e, file: number[] | string, dest: string) => {
  const png = typeof file === "string" ? fs.readFileSync(file) : Buffer.from(file);
  return saveImage(png, dest, settings.jpegQuality);
});
ipcMain.handle("clipboard:write", (_e, file: string) => {
  clipboard.writeImage(nativeImage.createFromPath(file));
});
ipcMain.handle("clipboard:writeText", (_e, text: string) => clipboard.writeText(text));
ipcMain.handle("open:external", (_e, url: string) => {
  if (url.startsWith("http://") || url.startsWith("https://")) void shell.openExternal(url);
});
ipcMain.handle("windows:list", () => listWindows());
ipcMain.handle("record:toggle", () => toggleRecord());
ipcMain.handle("fx:pixelate", (_e, file: string, x: number, y: number, w: number, h: number) => pixelate(fs.readFileSync(file), x, y, w, h));
ipcMain.handle("fx:blur", (_e, file: string, x: number, y: number, w: number, h: number) => blurRegion(fs.readFileSync(file), x, y, w, h));
ipcMain.handle("stitch", async (_e, files: string[], vertical: boolean) => {
  const buf = await stitch(files.map((f) => fs.readFileSync(f)), vertical);
  const tmp = path.join(os.tmpdir(), `sf-stitch-${Date.now()}.png`);
  fs.writeFileSync(tmp, buf);
  return tmp;
});
ipcMain.handle("upload", (_e, file: string, target: { kind: string; url: string; user?: string; secret?: string; publicUrlPrefix?: string }) =>
  uploadFile(file, path.basename(file), target),
);
ipcMain.handle("open:folder", (_e, p: string) => shell.showItemInFolder(p));
ipcMain.handle("root", () => appRoot());
ipcMain.handle("demo:file", (_e, rel: string) => {
  const p = path.join(appRoot(), "demo", rel);
  if (!p.startsWith(path.join(appRoot(), "demo"))) throw new Error("invalid path");
  return fs.readFileSync(p).toString("base64");
});
ipcMain.handle("demo:path", (_e, rel: string) => path.join(appRoot(), "demo", rel));
ipcMain.handle("view:open", (_e, view: string) => {
  openView(view);
});
ipcMain.handle("result:show", (_e, payload: { kind: string; text: string }) => {
  openResult(payload.kind, payload.text);
});
ipcMain.handle("toolbar:menu", (_e, open: boolean) => {
  applyToolbarMenu(!!open);
  raiseToolbar();
});
ipcMain.handle("quit", () => app.quit());
ipcMain.handle("color:at", async (_e, x: number, y: number) => {
  const png = await grabScreenPng();
  const { default: sharp } = await import("sharp");
  const { data } = await sharp(png).extract({ left: x, top: y, width: 1, height: 1 }).raw().toBuffer({ resolveWithObject: true });
  return { r: data[0], g: data[1], b: data[2] };
});
