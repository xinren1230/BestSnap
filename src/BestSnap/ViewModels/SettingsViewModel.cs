using System;
using System.Collections.Generic;
using System.Linq;
using BestSnap.Models;
using BestSnap.Services;

namespace BestSnap.ViewModels
{
    public class SettingsViewModel
    {
        private static readonly int[] QualityValues = { 40, 60, 80, 95 };
        private readonly AppSettings _s;
        public SettingsViewModel(AppSettings s)
        {
            _s = s;
            var rec = s.Recording ?? new RecordingSettings();
            SaveFolder = s.SaveFolder; FileNameTemplate = s.FileNameTemplate; DefaultFormat = s.DefaultFormat; JpegQuality = s.JpegQuality;
            CaptureDelaySeconds = s.CaptureDelaySeconds; IncludeCursor = s.IncludeCursor; AfterCapture = s.AfterCapture;
            FixedRegionWidth = s.FixedRegionWidth; FixedRegionHeight = s.FixedRegionHeight;
            RecorderFps = rec.Fps > 0 ? rec.Fps : s.RecorderFps;
            RecordAudio = rec.SystemAudio;
            ShowToolbarOnStartup = s.ShowToolbarOnStartup; ExcludeAppWindows = s.ExcludeAppWindowsFromCapture;
            Hotkeys = s.Hotkeys.Select(h => new HotkeyBinding { Action = h.Action, Gesture = h.Gesture }).ToList();
            HistoryEnabled = s.HistoryEnabled; HistoryMax = s.HistoryMax; TrayLeftClick = s.TrayLeftClick; Language = string.Equals(s.Language, "zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : (s.Language ?? "auto"); OcrLanguage = s.OcrLanguage ?? "";
            AutoStart = Services.AutoStart.IsEnabled; // reflects the real registry state
            RecordFormat = rec.Format;
            RecordShowCursor = rec.ShowCursor;
            RecordCountdown = rec.Countdown;
            RecordQualityIndex = IndexOfQuality(rec.Quality);
            RecordMicrophone = rec.Microphone;
            RecordHighlightClicks = rec.HighlightClicks;
            RecordGifFps = rec.GifFps;
            RecordGifMaxWidth = rec.GifMaxWidth;
            RecordSystemVolume = rec.SystemVolume;
            RecordMicVolume = rec.MicVolume;
            RecordShowRegionBorder = rec.ShowRegionBorder;
            RecordTrimAfter = rec.TrimAfterRecording;
            RecordWebcamEnabled = rec.WebcamEnabled;
            RecordWebcamCorner = rec.WebcamCorner;
            RecordWebcamSize = rec.WebcamSize;
            RecordWebcamCircle = rec.WebcamCircle;
            RecordKeystrokeOverlay = rec.KeystrokeOverlay;
            RecordKeystrokeShowClicks = rec.KeystrokeShowClicks;
            RecordSource = rec.Source;
            RecordSaveFolder = rec.SaveFolder ?? "";
            WatermarkAutoApply = s.Watermark?.AutoApply ?? false;
        }
        public string SaveFolder { get; set; }
        public string FileNameTemplate { get; set; }
        public string DefaultFormat { get; set; }
        public int JpegQuality { get; set; }
        public int CaptureDelaySeconds { get; set; }
        public bool IncludeCursor { get; set; }
        public AfterCaptureAction AfterCapture { get; set; }
        public int FixedRegionWidth { get; set; }
        public int FixedRegionHeight { get; set; }
        public int RecorderFps { get; set; }
        public bool RecordAudio { get; set; }
        public bool ShowToolbarOnStartup { get; set; }
        public bool ExcludeAppWindows { get; set; }
        public bool HistoryEnabled { get; set; }
        public int HistoryMax { get; set; }
        public TrayClickAction TrayLeftClick { get; set; }
        public string Language { get; set; }
        public string OcrLanguage { get; set; }
        public bool AutoStart { get; set; }
        public List<HotkeyBinding> Hotkeys { get; set; }
        public RecordFormat RecordFormat { get; set; }
        public bool RecordShowCursor { get; set; }
        public int RecordCountdown { get; set; }
        public int RecordQualityIndex { get; set; }
        public bool RecordMicrophone { get; set; }
        public bool RecordHighlightClicks { get; set; }
        public int RecordGifFps { get; set; }
        public int RecordGifMaxWidth { get; set; }
        public double RecordSystemVolume { get; set; }
        public double RecordMicVolume { get; set; }
        public bool RecordShowRegionBorder { get; set; }
        public bool RecordTrimAfter { get; set; }
        public bool RecordWebcamEnabled { get; set; }
        public WebcamCorner RecordWebcamCorner { get; set; }
        public int RecordWebcamSize { get; set; }
        public bool RecordWebcamCircle { get; set; }
        public bool RecordKeystrokeOverlay { get; set; }
        public bool RecordKeystrokeShowClicks { get; set; }
        public RecordSourceKind RecordSource { get; set; }
        public string RecordSaveFolder { get; set; }
        public bool WatermarkAutoApply { get; set; }
        public static string[] Formats => new[] { "png", "jpg", "bmp", "gif", "tif", "pdf" };
        public static int[] FpsChoices => new[] { 15, 24, 30, 60 };
        public static int[] CountdownChoices => new[] { 0, 3, 5 };
        public static int[] GifFpsChoices => new[] { 5, 8, 10, 12, 15, 20 };
        public static int[] GifWidthChoices => new[] { 480, 640, 800, 960, 1280, 0 };
        public static int[] WebcamSizeChoices => new[] { 120, 160, 200, 240, 320, 400 };
        public static string[] QualityNames => new[] { L.T("低（文件小）"), L.T("中"), L.T("高"), L.T("极高") };
        public static List<KeyValuePair<AfterCaptureAction, string>> AfterCaptureNames => new List<KeyValuePair<AfterCaptureAction, string>>
        {
            new(AfterCaptureAction.OpenEditor, L.T("打开编辑器")), new(AfterCaptureAction.CopyToClipboard, L.T("复制到剪贴板")),
            new(AfterCaptureAction.AutoSave, L.T("自动保存")), new(AfterCaptureAction.AutoSaveAndClipboard, L.T("自动保存并复制")), new(AfterCaptureAction.QuickBar, L.T("显示快捷操作栏")),
        };
        public static List<KeyValuePair<TrayClickAction, string>> TrayClickNames => new List<KeyValuePair<TrayClickAction, string>>
        {
            new(TrayClickAction.Menu, L.T("弹出菜单")), new(TrayClickAction.Region, L.T("矩形区域截图")), new(TrayClickAction.Toolbar, L.T("显示/隐藏工具栏")),
        };
        public static List<KeyValuePair<string, string>> LanguageNames => L.Choices();
        public static List<KeyValuePair<RecordFormat, string>> RecordFormatNames => new List<KeyValuePair<RecordFormat, string>>
        {
            new(RecordFormat.Mp4, L.T("MP4 视频 (H.264 + 声音)")), new(RecordFormat.Gif, L.T("GIF 动图（无声音）")),
        };
        public static List<KeyValuePair<RecordSourceKind, string>> RecordSourceNames => new List<KeyValuePair<RecordSourceKind, string>>
        {
            new(RecordSourceKind.Monitor, L.T("全屏（选择显示器）")), new(RecordSourceKind.Region, L.T("选择区域（开始后拖动框选）")), new(RecordSourceKind.Window, L.T("指定窗口")),
        };
        public static List<KeyValuePair<WebcamCorner, string>> WebcamCornerNames => new List<KeyValuePair<WebcamCorner, string>>
        {
            new(WebcamCorner.BottomRight, L.T("右下")), new(WebcamCorner.BottomLeft, L.T("左下")),
            new(WebcamCorner.TopRight, L.T("右上")), new(WebcamCorner.TopLeft, L.T("左上")),
        };

        static int IndexOfQuality(int q)
        {
            int i = Array.FindIndex(QualityValues, v => v >= q);
            return i < 0 ? QualityValues.Length - 1 : i;
        }

        public void Apply()
        {
            _s.SaveFolder = SaveFolder; _s.FileNameTemplate = FileNameTemplate; _s.DefaultFormat = DefaultFormat; _s.JpegQuality = Math.Clamp(JpegQuality, 1, 100);
            _s.CaptureDelaySeconds = Math.Clamp(CaptureDelaySeconds, 0, 60); _s.IncludeCursor = IncludeCursor; _s.AfterCapture = AfterCapture;
            _s.FixedRegionWidth = Math.Max(10, FixedRegionWidth); _s.FixedRegionHeight = Math.Max(10, FixedRegionHeight);
            _s.RecorderFps = Math.Clamp(RecorderFps, 5, 60); _s.RecordAudio = RecordAudio; _s.ShowToolbarOnStartup = ShowToolbarOnStartup; _s.ExcludeAppWindowsFromCapture = ExcludeAppWindows; _s.Hotkeys = Hotkeys;
            _s.HistoryEnabled = HistoryEnabled; _s.HistoryMax = Math.Clamp(HistoryMax, 5, 1000); _s.TrayLeftClick = TrayLeftClick; _s.Language = Language; _s.OcrLanguage = OcrLanguage;

            var rec = _s.Recording ?? (_s.Recording = new RecordingSettings());
            rec.Fps = _s.RecorderFps;
            rec.SystemAudio = RecordAudio;
            rec.Format = RecordFormat;
            rec.ShowCursor = RecordShowCursor;
            rec.Countdown = RecordCountdown;
            rec.Quality = QualityValues[Math.Clamp(RecordQualityIndex, 0, QualityValues.Length - 1)];
            rec.Microphone = RecordMicrophone;
            rec.HighlightClicks = RecordHighlightClicks;
            rec.GifFps = RecordGifFps;
            rec.GifMaxWidth = RecordGifMaxWidth;
            rec.SystemVolume = Math.Clamp(RecordSystemVolume, 0, 1);
            rec.MicVolume = Math.Clamp(RecordMicVolume, 0, 1);
            rec.ShowRegionBorder = RecordShowRegionBorder;
            rec.TrimAfterRecording = RecordTrimAfter;
            rec.WebcamEnabled = RecordWebcamEnabled;
            rec.WebcamCorner = RecordWebcamCorner;
            rec.WebcamSize = RecordWebcamSize;
            rec.WebcamCircle = RecordWebcamCircle;
            rec.KeystrokeOverlay = RecordKeystrokeOverlay;
            rec.KeystrokeShowClicks = RecordKeystrokeShowClicks;
            rec.Source = RecordSource;
            rec.SaveFolder = string.IsNullOrWhiteSpace(RecordSaveFolder) ? null : RecordSaveFolder.Trim();
            var wm = _s.Watermark ?? (_s.Watermark = new WatermarkSettings());
            wm.AutoApply = WatermarkAutoApply;
        }
    }
}
