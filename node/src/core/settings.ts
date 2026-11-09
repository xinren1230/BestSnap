import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { ensureDir, userDataDir } from "./paths.js";

export type AfterCaptureAction = "OpenEditor" | "CopyToClipboard" | "AutoSave" | "AutoSaveAndClipboard" | "QuickBar";
export type FormulaEngine = "Local" | "OpenAI" | "Mathpix";
export type TranslateProvider = "None" | "DeepL" | "Azure" | "LibreTranslate";

export interface HotkeyBinding {
  action: string;
  gesture: string;
}

export interface RecordingSettings {
  source: "Monitor" | "Region" | "Window";
  systemAudio: boolean;
  microphone: boolean;
  showCursor: boolean;
  highlightClicks: boolean;
  fps: number;
  quality: number;
  countdown: number;
  saveFolder: string;
  format: "Mp4" | "Gif";
  gifFps: number;
  gifMaxWidth: number;
  showRegionBorder: boolean;
  webcamEnabled: boolean;
  keystrokeOverlay: boolean;
}

export interface AppSettings {
  saveFolder: string;
  fileNameTemplate: string;
  defaultFormat: string;
  jpegQuality: number;
  captureDelaySeconds: number;
  includeCursor: boolean;
  afterCapture: AfterCaptureAction;
  fixedRegionWidth: number;
  fixedRegionHeight: number;
  counter: number;
  excludeAppWindowsFromCapture: boolean;
  showToolbarOnStartup: boolean;
  toolbarX?: number;
  toolbarY?: number;
  magnifierZoom: number;
  historyEnabled: boolean;
  historyMax: number;
  language: string;
  ocrLanguage: string;
  formula: { engine: FormulaEngine; apiBase: string; apiModel: string; apiKey: string; mathpixAppId: string; mathpixKey: string };
  translate: { provider: TranslateProvider; endpoint: string; region: string; key: string };
  recording: RecordingSettings;
  watermark: {
    autoApply: boolean;
    isImage: boolean;
    text: string;
    imagePath: string;
    fontFamily: string;
    fontSize: number;
    color: string;
    opacity: number;
    rotation: number;
    tiled: boolean;
    anchor: number;
    margin: number;
    spacing: number;
    imageHeight: number;
  };
  hotkeys: HotkeyBinding[];
  autostart: boolean;
  uploadTargets: { name: string; kind: string; url: string; user: string; secret: string; publicUrlPrefix: string }[];
}

export const DEFAULT_HOTKEYS: HotkeyBinding[] = [
  { action: "FullScreen", gesture: "Shift+PrintScreen" },
  { action: "ActiveWindow", gesture: "Alt+PrintScreen" },
  { action: "WindowObject", gesture: "Ctrl+Shift+W" },
  { action: "Region", gesture: "Ctrl+PrintScreen" },
  { action: "Freehand", gesture: "Ctrl+Shift+F" },
  { action: "FixedRegion", gesture: "Ctrl+Shift+X" },
  { action: "Scrolling", gesture: "Ctrl+Alt+PrintScreen" },
  { action: "RepeatLast", gesture: "Ctrl+Shift+R" },
  { action: "Recorder", gesture: "Ctrl+Shift+V" },
  { action: "OcrRegion", gesture: "Ctrl+Shift+O" },
  { action: "PinRegion", gesture: "Ctrl+Shift+P" },
  { action: "History", gesture: "Ctrl+Shift+H" },
  { action: "TimedCapture", gesture: "Ctrl+Shift+T" },
  { action: "QrRegion", gesture: "Ctrl+Shift+Q" },
  { action: "FormulaRegion", gesture: "Ctrl+Shift+L" },
];

export function defaults(): AppSettings {
  return {
    saveFolder: path.join(os.homedir(), "Pictures", "SnapForge"),
    fileNameTemplate: "Snap_{yyyy}-{MM}-{dd}_{HH}{mm}{ss}_{n}",
    defaultFormat: "png",
    jpegQuality: 90,
    captureDelaySeconds: 0,
    includeCursor: false,
    afterCapture: "OpenEditor",
    fixedRegionWidth: 800,
    fixedRegionHeight: 600,
    counter: 1,
    excludeAppWindowsFromCapture: true,
    showToolbarOnStartup: true,
    magnifierZoom: 4,
    historyEnabled: true,
    historyMax: 50,
    language: "auto",
    ocrLanguage: "",
    formula: { engine: "Local", apiBase: "https://api.openai.com/v1", apiModel: "gpt-4o-mini", apiKey: "", mathpixAppId: "", mathpixKey: "" },
    translate: { provider: "None", endpoint: "", region: "", key: "" },
    recording: {
      source: "Monitor",
      systemAudio: false,
      microphone: false,
      showCursor: true,
      highlightClicks: false,
      fps: 30,
      quality: 70,
      countdown: 3,
      saveFolder: path.join(os.homedir(), "Videos", "SnapForge"),
      format: "Mp4",
      gifFps: 10,
      gifMaxWidth: 960,
      showRegionBorder: true,
      webcamEnabled: false,
      keystrokeOverlay: false,
    },
    watermark: {
      autoApply: false,
      isImage: false,
      text: "SnapForge",
      imagePath: "",
      fontFamily: "Noto Sans CJK SC",
      fontSize: 28,
      color: "auto",
      opacity: 35,
      rotation: -30,
      tiled: true,
      anchor: 8,
      margin: 16,
      spacing: 64,
      imageHeight: 48,
    },
    hotkeys: DEFAULT_HOTKEYS.map((h) => ({ ...h })),
    autostart: false,
    uploadTargets: [],
  };
}

export function settingsPath(): string {
  return path.join(userDataDir(), "settings.json");
}

export function loadSettings(): AppSettings {
  const p = settingsPath();
  const d = defaults();
  if (!fs.existsSync(p)) return d;
  try {
    const raw = JSON.parse(fs.readFileSync(p, "utf8")) as Partial<AppSettings>;
    return { ...d, ...raw, formula: { ...d.formula, ...raw.formula }, translate: { ...d.translate, ...raw.translate }, recording: { ...d.recording, ...raw.recording }, watermark: { ...d.watermark, ...raw.watermark }, hotkeys: raw.hotkeys?.length ? raw.hotkeys : d.hotkeys };
  } catch {
    const bak = p + ".bak";
    if (fs.existsSync(bak)) {
      try {
        return { ...d, ...(JSON.parse(fs.readFileSync(bak, "utf8")) as AppSettings) };
      } catch {
        /* ignore */
      }
    }
    return d;
  }
}

export function saveSettings(s: AppSettings): void {
  ensureDir(userDataDir());
  const p = settingsPath();
  if (fs.existsSync(p)) {
    try {
      fs.copyFileSync(p, p + ".bak");
    } catch {
      /* ignore */
    }
  }
  const tmp = p + ".tmp";
  fs.writeFileSync(tmp, JSON.stringify(s, null, 2), { mode: 0o600 });
  fs.renameSync(tmp, p);
}

export function setAutostart(on: boolean): void {
  const dir = path.join(os.homedir(), ".config", "autostart");
  const desktop = path.join(dir, "snapforge.desktop");
  if (!on) {
    fs.rmSync(desktop, { force: true });
    return;
  }
  ensureDir(dir);
  const exec = process.env.SNAPFORGE_EXEC || "snapforge-ui";
  fs.writeFileSync(
    desktop,
    `[Desktop Entry]\nType=Application\nName=SnapForge\nExec=${exec}\nX-GNOME-Autostart-enabled=true\n`,
  );
}
