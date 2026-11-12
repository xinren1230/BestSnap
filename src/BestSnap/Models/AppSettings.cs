using System.Collections.Generic;
using BestSnap.Plugins;

namespace BestSnap.Models
{
    public enum AfterCaptureAction { OpenEditor, CopyToClipboard, AutoSave, AutoSaveAndClipboard, QuickBar }

    public class HotkeyBinding
    {
        public string Action { get; set; }
        public string Gesture { get; set; } // e.g. "Ctrl+Shift+PrintScreen"
    }

    public enum RecordSourceKind { Monitor, Region, Window, VirtualScreen }

    public class RecordingSettings
    {
        public RecordSourceKind Source { get; set; } = RecordSourceKind.Monitor;
        public string MonitorDevice { get; set; }
        public bool SystemAudio { get; set; } = true;
        public bool Microphone { get; set; } = false;
        public string MicrophoneDevice { get; set; }
        public string SystemAudioDevice { get; set; }
        public bool ShowCursor { get; set; } = true;
        public bool HighlightClicks { get; set; } = false;
        public int Fps { get; set; } = 30;
        public int Quality { get; set; } = 70;   // 0-100
        public int Countdown { get; set; } = 3;
        public string SaveFolder { get; set; }
        public RecordFormat Format { get; set; } = RecordFormat.Mp4;
        public int GifFps { get; set; } = 10;
        public int GifMaxWidth { get; set; } = 960;
        public double SystemVolume { get; set; } = 1.0;   // 0..1
        public double MicVolume { get; set; } = 1.0;      // 0..1
        public bool ShowRegionBorder { get; set; } = true;
        public bool TrimAfterRecording { get; set; } = true;
        // webcam picture-in-picture
        public bool WebcamEnabled { get; set; }
        public string WebcamDevice { get; set; }      // ScreenRecorderLib device name
        public string WebcamName { get; set; }        // friendly name (used by the plugin bubble)
        public WebcamCorner WebcamCorner { get; set; } = WebcamCorner.BottomRight;
        public int WebcamSize { get; set; } = 200;    // px (height / diameter)
        public bool WebcamCircle { get; set; } = true;
        // keystroke / click overlay (visible in the recording)
        public bool KeystrokeOverlay { get; set; }
        public bool KeystrokeShowClicks { get; set; } = true;
    }

    public enum WebcamCorner { BottomRight, BottomLeft, TopRight, TopLeft }
    public enum TranslateProvider { None, DeepL, Azure, LibreTranslate }

    public class TranslateSettings
    {
        public TranslateProvider Provider { get; set; } = TranslateProvider.None;
        public string Endpoint { get; set; }          // optional override / LibreTranslate URL
        public string Region { get; set; }            // Azure region
        public string KeyProtected { get; set; }      // DPAPI (CurrentUser), base64
    }

    public enum FormulaEngine { Local, OpenAI, Mathpix }

    public class FormulaSettings
    {
        public FormulaEngine Engine { get; set; } = FormulaEngine.Local;
        public string ApiBase { get; set; } = "https://api.openai.com/v1"; // any OpenAI-compatible endpoint
        public string ApiModel { get; set; } = "gpt-4o-mini";
        public string ApiKeyProtected { get; set; }     // DPAPI
        public string MathpixAppId { get; set; }
        public string MathpixKeyProtected { get; set; } // DPAPI
    }

    public enum RecordFormat { Mp4, Gif }
    public enum TimedCaptureMode { FullScreen, Region, Window }

    public class TimedCaptureSettings
    {
        public TimedCaptureMode Mode { get; set; } = TimedCaptureMode.FullScreen;
        public int IntervalSeconds { get; set; } = 10;
        public int Count { get; set; } = 0;              // 0 = until stopped
        public string Folder { get; set; }               // null = SaveFolder\Timed
        public string Template { get; set; } = "Timed_{yyyy}{MM}{dd}_{HH}{mm}{ss}_{n}";
        public string Format { get; set; } = "png";
        public bool IncludeCursor { get; set; }
    }
    public enum TrayClickAction { Menu, Region, Toolbar }

    public class WatermarkSettings
    {
        public bool AutoApply { get; set; }
        public bool IsImage { get; set; }
        public string Text { get; set; } = AppIdentity.Name;
        public string ImagePath { get; set; }
        public string FontFamily { get; set; } = "Microsoft YaHei UI";
        public double FontSize { get; set; } = 28;
        public string Color { get; set; } = "auto";
        public double? PosX { get; set; }
        public double? PosY { get; set; }
        public int Opacity { get; set; } = 35;
        public double Rotation { get; set; } = -30;
        public bool Tiled { get; set; } = true;
        public int Anchor { get; set; } = 8;
        public double Margin { get; set; } = 16;
        public double Spacing { get; set; } = 64;
        public double ImageHeight { get; set; } = 48;
    }

    public class AppSettings
    {
        public RecordingSettings Recording { get; set; } = new RecordingSettings();
        public BestSnap.Plugins.AudioRecordingSettings AudioRecording { get; set; } = new BestSnap.Plugins.AudioRecordingSettings();
        public string SaveFolder { get; set; } = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures), AppIdentity.Name);
        public string FileNameTemplate { get; set; } = "BestSnap_{yyyy}-{MM}-{dd}_{HH}{mm}{ss}_{n}";
        public string DefaultFormat { get; set; } = "png";
        public int JpegQuality { get; set; } = 90;
        public int CaptureDelaySeconds { get; set; } = 0;
        public bool IncludeCursor { get; set; } = false;
        public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.OpenEditor;
        public int FixedRegionWidth { get; set; } = 800;
        public int FixedRegionHeight { get; set; } = 600;
        public int Counter { get; set; } = 1;
        public int RecorderFps { get; set; } = 30;
        public bool RecordAudio { get; set; } = true;
        public bool ExcludeAppWindowsFromCapture { get; set; } = true;
        public bool ShowToolbarOnStartup { get; set; } = true;
        public double? ToolbarLeft { get; set; }
        public double? ToolbarTop { get; set; }
        public int MagnifierZoom { get; set; } = 4;
        /// <summary>Toolbar position in physical pixels (virtual-screen coords) – DPI independent.</summary>
        public int? ToolbarX { get; set; }
        public int? ToolbarY { get; set; }
        public bool HistoryEnabled { get; set; } = true;
        public int HistoryMax { get; set; } = 50;
        public TrayClickAction TrayLeftClick { get; set; } = TrayClickAction.Menu;
        public string Language { get; set; } = "auto"; // auto | zh-CN | zh-TW | en | de | ja | ko | fr | es | any code from lang\*.json
        public TimedCaptureSettings Timed { get; set; } = new TimedCaptureSettings();
        public TranslateSettings Translate { get; set; } = new TranslateSettings();
        public string OcrLanguage { get; set; }        // null/"" = auto (zh/ja/ko recognizers if installed + profile languages, best result wins)
        public FormulaSettings Formula { get; set; } = new FormulaSettings();
        public WatermarkSettings Watermark { get; set; } = new WatermarkSettings();
        public List<UploadTarget> UploadTargets { get; set; } = new List<UploadTarget>();
        public List<HotkeyBinding> Hotkeys { get; set; } = new List<HotkeyBinding>
        {
            new HotkeyBinding { Action = "FullScreen", Gesture = "Shift+PrintScreen" },
            new HotkeyBinding { Action = "ActiveWindow", Gesture = "Alt+PrintScreen" },
            new HotkeyBinding { Action = "WindowObject", Gesture = "Ctrl+Shift+W" },
            new HotkeyBinding { Action = "Region", Gesture = "Ctrl+PrintScreen" },
            new HotkeyBinding { Action = "Freehand", Gesture = "Ctrl+Shift+F" },
            new HotkeyBinding { Action = "FixedRegion", Gesture = "Ctrl+Shift+X" },
            new HotkeyBinding { Action = "Scrolling", Gesture = "Ctrl+Alt+PrintScreen" },
            new HotkeyBinding { Action = "RepeatLast", Gesture = "Ctrl+Shift+R" },
            new HotkeyBinding { Action = "Recorder", Gesture = "Ctrl+Shift+V" },
            new HotkeyBinding { Action = "AudioRecorder", Gesture = "Ctrl+Shift+A" },
            new HotkeyBinding { Action = "OcrRegion", Gesture = "Ctrl+Shift+O" },
            new HotkeyBinding { Action = "PinRegion", Gesture = "Ctrl+Shift+P" },
            new HotkeyBinding { Action = "History", Gesture = "Ctrl+Shift+H" },
            new HotkeyBinding { Action = "TimedCapture", Gesture = "Ctrl+Shift+T" },
            new HotkeyBinding { Action = "QrRegion", Gesture = "Ctrl+Shift+Q" },
            new HotkeyBinding { Action = "FormulaRegion", Gesture = "Ctrl+Shift+L" },
        };
    }
}
