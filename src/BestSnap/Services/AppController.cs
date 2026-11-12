using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using BestSnap.Models;
using BestSnap.Native;
using BestSnap.Plugins;
using BestSnap.Views;
using WinForms = System.Windows.Forms;

namespace BestSnap.Services
{
    public enum CaptureMode { FullScreen, ActiveWindow, WindowObject, Region, Freehand, FixedRegion, Scrolling }

    /// <summary>Central coordinator: tray icon, hotkeys, capture workflow and output routing.</summary>
    public sealed class AppController : IDisposable
    {
        private readonly SettingsService _settings;
        private readonly HotkeyService _hotkeys = new HotkeyService();
        private readonly RecorderService _recorder = new RecorderService();
        private readonly AudioRecorderService _audio = new AudioRecorderService();
        private readonly LiveTranscribeService _live = new LiveTranscribeService();
        private WinForms.NotifyIcon _tray;
        private CaptureMode? _lastMode;
        private Int32Rect? _lastRect;
        private MagnifierWindow _magnifier;

        public AppController(SettingsService s) { _settings = s; }
        private AppSettings S => _settings.Current;

        public void Start()
        {
            _tray = new WinForms.NotifyIcon { Icon = LoadTrayIcon(), Text = AppIdentity.Name, Visible = true };
            BuildTrayMenu();
            L.LanguageChanged += OnLanguageChanged;
            // Left-click always restores the floating capture toolbar; right-click keeps the context menu.
            _tray.MouseClick += (o, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left) ShowToolbar();
            };

            _timed.StateChanged += UpdateTimedIndicator;
            _timed.Error += err => Notify(L.T("定时截图已停止") + ": " + err);
            _recorder.Completed += OnRecordingCompleted;
            _recorder.Failed += OnRecordingFailed;
            _recorder.RegionClipped += dev => Logger.Info("Recording region clipped to " + dev);
            RegisterHotkeys();
            RecoverTempRecordings();
            if (S.ShowToolbarOnStartup) ShowToolbar();
            UiDemo();
        }

        void RecoverTempRecordings()
        {
            try
            {
                var folder = TempRecordingStore.EnsureFolder(AppPaths.LocalData);
                TempRecordingStore.Cleanup(folder, TempRecordingStore.DefaultMaxAge, DateTime.UtcNow);
                var ready = TempRecordingStore.ListReady(folder);
                if (ready.Count == 0) return;
                Notify(string.Format(L.T("发现 {0} 个未保存的临时录音。"), ready.Count));
                new RecordingDoneWindow(ready[ready.Count - 1], _settings).Show();
            }
            catch (Exception ex) { Logger.Error("TempRecover", ex); }
        }

        /// <summary>Dev aid for UI screenshots: BESTSNAP_UI_DEMO=&lt;png&gt; (or legacy SNAPFORGE_UI_DEMO) opens the editor and the quick bar on that image at startup.</summary>
        private void UiDemo()
        {
            var p = AppIdentity.GetEnv("UI_DEMO");
            if (string.IsNullOrEmpty(p) || !System.IO.File.Exists(p)) return;
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (o, e) =>
            {
                t.Stop();
                try
                {
                    var img = new System.Windows.Media.Imaging.BitmapImage(); img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.UriSource = new Uri(p); img.EndInit(); img.Freeze();
                    var ed = new EditorWindow(img, _settings) { WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = 40, Top = 140, Width = 1500, Height = 640 }; ed.Show();
                    QuickActionBar.ShowFor(img, new System.Windows.Int32Rect(40, 820, img.PixelWidth, 60));
                }
                catch (Exception ex) { Logger.Error("UiDemo", ex); }
            };
            t.Start();
        }

        public void SuspendHotkeys() => _hotkeys.UnregisterAll();

        public void RegisterHotkeys()
        {
            _hotkeys.UnregisterAll();
            foreach (var hk in S.Hotkeys)
            {
                Action a = null;
                if (Enum.TryParse(hk.Action, out CaptureMode mode)) a = () => Capture(mode);
                else if (hk.Action == "RepeatLast") a = RepeatLast;
                else if (hk.Action == "Recorder") a = ToggleRecording;
                else if (hk.Action == "AudioRecorder") a = ShowAudioRecorder;
                else if (hk.Action == "OcrRegion") a = OcrRegion;
                else if (hk.Action == "QrRegion") a = QrRegion;
                else if (hk.Action == "FormulaRegion") a = FormulaRegion;
                else if (hk.Action == "PinRegion") a = PinRegion;
                else if (hk.Action == "History") a = ShowHistory;
                else if (hk.Action == "TimedCapture") a = ToggleTimedCapture;
                if (a != null && !_hotkeys.Register(hk.Gesture, a, out var err)) Notify(err);
            }
        }

        public void Notify(string msg)
        {
            Application.Current.Dispatcher.Invoke(() => { _tray.BalloonTipTitle = AppIdentity.Name; _tray.BalloonTipText = msg; _tray.ShowBalloonTip(3000); });
        }

        private bool _exiting;
        private bool _busy; // a capture flow (overlay/delay/scroll) is in progress
        public bool IsBusy => _busy;

        public async void Capture(CaptureMode mode)
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _lastMode = mode;
                if (S.CaptureDelaySeconds > 0) await Task.Delay(S.CaptureDelaySeconds * 1000);
                else await Task.Delay(150); // let tray menu disappear
                BitmapSource img = null;
                Int32Rect? abs = null;
                Int32Rect? WinRect(IntPtr h) { if (h == IntPtr.Zero || !NativeMethods.GetWindowRect(h, out var wr)) return null; return new Int32Rect(wr.Left, wr.Top, wr.Width, wr.Height); }
                switch (mode)
                {
                    case CaptureMode.FullScreen: img = CaptureService.CaptureFullScreen(S.IncludeCursor); break;
                    case CaptureMode.ActiveWindow: { var h = WindowEnumerator.ResolveTargetWindow(); img = CaptureService.CaptureWindow(h, S.IncludeCursor); abs = WinRect(h); } break;
                    case CaptureMode.Scrolling:
                        Notify(L.T("请在 2 秒内把鼠标移到要滚动的窗口内容上，开始后可在右下角面板点“完成”…"));
                        await Task.Delay(2000);
                        NativeMethods.GetCursorPos(out var p);
                        var hw = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(p), NativeMethods.GA_ROOT);
                        img = await ScrollCaptureService.CaptureAsync(hw);
                        break;
                    default:
                        var kind = mode == CaptureMode.WindowObject ? SelectionKind.WindowObject
                                 : mode == CaptureMode.Freehand ? SelectionKind.Freehand
                                 : mode == CaptureMode.FixedRegion ? SelectionKind.Fixed : SelectionKind.Rectangle;
                        var ov = new SelectionOverlay(kind, S.FixedRegionWidth, S.FixedRegionHeight);
                        ov.ShowDialog();
                        img = ov.Result;
                        if (ov.ResultRect.HasValue) { _lastRect = ov.ResultRect; var r0 = ov.ResultRect.Value; var vs0 = CaptureService.VirtualScreen; abs = new Int32Rect(r0.X + vs0.X, r0.Y + vs0.Y, r0.Width, r0.Height); }
                        break;
                }
                if (img != null) Deliver(img, abs);
            }
            catch (Exception ex) { Logger.Error("Capture", ex); Notify("截图失败: " + ex.Message); }
            finally { _busy = false; }
        }

        public async void RepeatLast()
        {
            if (_busy) return;
            if (_lastRect.HasValue && (_lastMode == CaptureMode.Region || _lastMode == CaptureMode.FixedRegion || _lastMode == CaptureMode.WindowObject))
            {
                _busy = true;
                try
                {
                    await Task.Delay(150 + S.CaptureDelaySeconds * 1000);
                    var r = _lastRect.Value; var vs = CaptureService.VirtualScreen;
                    var ar = new Int32Rect(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height);
                    Deliver(CaptureService.CaptureRect(ar, S.IncludeCursor), ar);
                }
                catch (Exception ex) { Logger.Error("RepeatLast", ex); Notify("截图失败: " + ex.Message); }
                finally { _busy = false; }
            }
            else if (_lastMode.HasValue) Capture(_lastMode.Value);
            else Capture(CaptureMode.Region);
        }

        public void ShowHistory() => HistoryWindow.ShowSingle();

        /// <summary>Lets the user drag a rectangle; returns the image and its absolute physical rect.</summary>
        private async Task<(BitmapSource img, Int32Rect? rect)> SelectRegionAsync()
        {
            if (_busy) return (null, null);
            _busy = true;
            try
            {
                await Task.Delay(150);
                var ov = new SelectionOverlay(SelectionKind.Rectangle);
                ov.ShowDialog();
                Int32Rect? abs = null;
                if (ov.ResultRect.HasValue) { var r = ov.ResultRect.Value; var vs = CaptureService.VirtualScreen; abs = new Int32Rect(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height); }
                return (ov.Result, abs);
            }
            finally { _busy = false; }
        }

        public async void PinRegion()
        {
            try { var (img, r) = await SelectRegionAsync(); if (img != null) { AddHistory(img); new PinWindow(img, r).Show(); } }
            catch (Exception ex) { Logger.Error("PinRegion", ex); Notify(ex.Message); }
        }

        public async void QrRegion()
        {
            try { var (img, _) = await SelectRegionAsync(); if (img != null) CodeReader.Run(img); }
            catch (Exception ex) { Logger.Error("QrRegion", ex); Notify(ex.Message); }
        }

        public async void FormulaRegion()
        {
            try { var (img, _) = await SelectRegionAsync(); if (img != null) FormulaWindow.Run(img); }
            catch (Exception ex) { Logger.Error("FormulaRegion", ex); Notify(ex.Message); }
        }

        /// <summary>Select a region → OCR → translate (to Chinese, or English when the text is mostly Chinese).</summary>
        public async void TranslateRegion()
        {
            if (!OcrRunner.Available) { OcrRunner.Run(null); return; }
            try { var (img, _) = await SelectRegionAsync(); if (img != null) OcrRunner.Run(img, null, "auto"); }
            catch (Exception ex) { Logger.Error("TranslateRegion", ex); Notify(ex.Message); }
        }

        /// <summary>Select a region → editor with the auto-redact pass already applied.</summary>
        public async void RedactRegion()
        {
            try { var (img, _) = await SelectRegionAsync(); if (img != null) { AddHistory(img); var ed = new EditorWindow(img, _settings); ed.Show(); ed.AutoRedactNow(); } }
            catch (Exception ex) { Logger.Error("RedactRegion", ex); Notify(ex.Message); }
        }

        /// <summary>Select a region → editor with the pixelate (mosaic) tool armed.</summary>
        public async void BlurRegion()
        {
            try { var (img, _) = await SelectRegionAsync(); if (img != null) { AddHistory(img); var ed = new EditorWindow(img, _settings); ed.Show(); ed.UseTool(EditorTool.Pixelate); } }
            catch (Exception ex) { Logger.Error("BlurRegion", ex); Notify(ex.Message); }
        }

        public async void OcrRegion()
        {
            if (!OcrRunner.Available) { OcrRunner.Run(null); return; } // shows the "plugin missing" hint
            try { var (img, _) = await SelectRegionAsync(); if (img != null) OcrRunner.Run(img); }
            catch (Exception ex) { Logger.Error("OcrRegion", ex); Notify(ex.Message); }
        }

        private void AddHistory(BitmapSource img)
        {
            if (!S.HistoryEnabled || img == null) return;
            if (!img.IsFrozen) { img = img.Clone(); img.Freeze(); }
            int max = S.HistoryMax; var f = img;
            Task.Run(() => { try { HistoryService.Add(f, max); } catch (Exception ex) { Logger.Error("History", ex); } });
        }

        public void Deliver(BitmapSource img, Int32Rect? absRect = null)
        {
            AddHistory(img);
            switch (S.AfterCapture)
            {
                case AfterCaptureAction.QuickBar: QuickActionBar.ShowFor(img, absRect); break;
                case AfterCaptureAction.OpenEditor: new EditorWindow(img, _settings).Show(); break;
                case AfterCaptureAction.CopyToClipboard: ImageExporter.CopyToClipboard(Watermark.ApplyIfNeeded(img, S)); Notify(L.T("已复制到剪贴板")); break;
                case AfterCaptureAction.AutoSave: Notify(L.T("已保存: ") + ImageExporter.AutoSave(Watermark.ApplyIfNeeded(img, S), S)); _settings.Save(); break;
                case AfterCaptureAction.AutoSaveAndClipboard:
                    var outImg = Watermark.ApplyIfNeeded(img, S);
                    ImageExporter.CopyToClipboard(outImg); Notify(L.T("已保存并复制: ") + ImageExporter.AutoSave(outImg, S)); _settings.Save(); break;
            }
        }

        private void BuildTrayMenu()
        {
            var old = _tray.ContextMenuStrip;
            var m = new WinForms.ContextMenuStrip();
            void Add(string text, Action a) => m.Items.Add(text, null, (o, e) => a());
            Add(L.T("显示/隐藏工具栏"), ToggleToolbar);
            m.Items.Add(new WinForms.ToolStripSeparator());
            Add(L.T("全屏截图"), () => Capture(CaptureMode.FullScreen));
            Add(L.T("活动窗口"), () => Capture(CaptureMode.ActiveWindow));
            Add(L.T("窗口/对象"), () => Capture(CaptureMode.WindowObject));
            Add(L.T("矩形区域"), () => Capture(CaptureMode.Region));
            Add(L.T("手绘区域"), () => Capture(CaptureMode.Freehand));
            Add(L.T("固定大小区域"), () => Capture(CaptureMode.FixedRegion));
            Add(L.T("滚动窗口"), () => Capture(CaptureMode.Scrolling));
            Add(L.T("重复上次截图"), RepeatLast);
            m.Items.Add(new WinForms.ToolStripSeparator());
            Add(L.T("截图并钉到桌面"), PinRegion);
            Add(L.T("识别文字 (OCR)"), OcrRegion);
            Add(L.T("识别二维码/条码"), QrRegion);
            Add(L.T("公式转LaTeX"), FormulaRegion);
            Add(L.T("截图历史…"), ShowHistory);
            Add(L.T("上传目标…"), () => new UploadTargetsWindow().Show());
            Add(L.T("拼接长图…"), () => new StitchWindow().Show());
            Add(L.T("图片对比…"), () => new CompareWindow().Show());
            _timedItem = new WinForms.ToolStripMenuItem(L.T("定时截图…"), null, (o, e) => ToggleTimedCapture());
            m.Items.Add(_timedItem);
            m.Items.Add(new WinForms.ToolStripSeparator());
            Add(L.T("开始/停止录屏"), ToggleRecording);
            Add(L.T("录制音频…"), ShowAudioRecorder);
            Add(L.T("仅转写（不保存音频）…"), ShowLiveTranscribe);
            Add(L.T("屏幕取色器"), ShowColorPicker);
            Add(L.T("屏幕放大镜"), ToggleMagnifier);
            Add(L.T("屏幕标尺"), ShowRuler);
            Add(L.T("打开图片…"), OpenImage);
            m.Items.Add(new WinForms.ToolStripSeparator());
            var langMenu = new WinForms.ToolStripMenuItem(L.T("界面语言"));
            foreach (var kv in L.Choices())
            {
                string code = kv.Key;
                var it = new WinForms.ToolStripMenuItem(kv.Value) { Checked = string.Equals(S.Language ?? "auto", code, StringComparison.OrdinalIgnoreCase) };
                it.Click += (o, e) => SetLanguage(code);
                langMenu.DropDownItems.Add(it);
            }
            m.Items.Add(langMenu);
            Add(L.T("设置…"), ShowSettings);
            Add(L.T("退出"), RequestExit);
            _tray.ContextMenuStrip = m;
            old?.Dispose();
            if (_timed != null) UpdateTimedIndicator();
        }

        public void SetLanguage(string code)
        {
            S.Language = code; _settings.Save();
            L.Reload();
        }

        private void OnLanguageChanged()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                BuildTrayMenu();
                if (_toolbar != null)
                {
                    bool vis = _toolbar.IsVisible; double l = _toolbar.Left, t = _toolbar.Top;
                    var tb = _toolbar; _toolbar = null; tb.Close();
                    if (vis) { ShowToolbar(); _toolbar.Left = l; _toolbar.Top = t; }
                }
                int others = 0;
                foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
                    if (w.IsVisible && !(w is CaptureToolbar) && !App.IsRecordedOverlay(w) && w.GetType().Namespace == "BestSnap.Views") others++;
                if (others > 0)
                    System.Windows.MessageBox.Show(L.T("界面语言已切换。部分已打开的窗口需要重新打开后才会显示新语言。"), AppIdentity.Name);
            });
        }

        private CaptureToolbar _toolbar;
        public void ShowToolbar()
        {
            if (_toolbar == null) { _toolbar = new CaptureToolbar(this, _settings); _toolbar.Closed += (o, e) => _toolbar = null; }
            if (_toolbar.WindowState == WindowState.Minimized) _toolbar.WindowState = WindowState.Normal;
            _toolbar.Show();
            _toolbar.Activate();
            try { _toolbar.Topmost = false; _toolbar.Topmost = true; } catch { }
        }
        public void ToggleToolbar() { if (_toolbar != null && _toolbar.IsVisible) _toolbar.Hide(); else ShowToolbar(); }
        public void ShowColorPicker()
        {
            if (_busy) return;
            _busy = true;
            var ov = new SelectionOverlay(SelectionKind.ColorPick);
            ov.Closed += (o, e) => _busy = false;
            ov.Show();
        }
        public void ShowRuler() => new RulerWindow().Show();
        public void ShowSettings() => new SettingsWindow(_settings, this).Show();

        private static System.Drawing.Icon LoadTrayIcon()
        {
            using var st = typeof(AppController).Assembly.GetManifestResourceStream(AppIdentity.TrayIconResource);
            if (st == null) return System.Drawing.SystemIcons.Application;
            return new System.Drawing.Icon(st, WinForms.SystemInformation.SmallIconSize);
        }

        public void OpenImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff") };
            if (dlg.ShowDialog() == true)
                new EditorWindow(CaptureService.Normalize(new BitmapImage(new Uri(dlg.FileName))), _settings).Show();
        }

        private RecordingControlBar _recBar;
        private bool _recDialogOpen;

        public bool IsAnyRecording => _recorder.IsRecording || _audio.IsRecording || _live.IsRunning;

        public void StopAnyRecording()
        {
            if (_recorder.IsRecording) _recorder.Stop();
            else if (_audio.IsRecording) _audio.Stop();
            else if (_live.IsRunning) _live.Stop();
        }

        public void ShowLiveTranscribe()
        {
            if (_live.IsRunning) { _liveWin?.Activate(); return; }
            ShowAudioRecorder(liveOnly: true);
        }

        public void StartLiveTranscribe(AudioRecordingSettings opts)
        {
            if (_live.IsRunning) { _liveWin?.Activate(); return; }
            if (_recorder.IsRecording || _audio.IsRecording)
                throw new InvalidOperationException(L.T("请先停止当前录制。"));
            _liveWin = new LiveCaptionWindow(_live, _settings);
            _liveWin.Closed += (o, e) => _liveWin = null;
            _liveWin.Show();
            try { _live.Start(opts, S.WhisperModel); }
            catch
            {
                try { _liveWin.Close(); } catch { }
                throw;
            }
        }

        public void ToggleRecording()
        {
            if (_live.IsRunning) { _live.Stop(); return; }
            if (_recorder.IsRecording) { _recorder.Stop(); return; }
            if (_recDialogOpen || _busy) return;
            _recDialogOpen = true;
            try
            {
                var dlg = new RecordingOptionsWindow(S.Recording ?? new RecordingSettings(), S.SaveFolder);
                if (dlg.ShowDialog() != true) return;
                var opts = dlg.Result;
                if (dlg.Remember) { S.Recording = opts; _settings.Save(); }
                var req = new RecordingRequest { Options = opts };
                if (opts.Source == RecordSourceKind.Window) req.WindowHandle = dlg.SelectedWindow;
                else if (opts.Source == RecordSourceKind.Region)
                {
                    var ov = new SelectionOverlay(SelectionKind.Rectangle);
                    ov.ShowDialog();
                    if (!ov.ResultRect.HasValue) return;
                    var r = ov.ResultRect.Value; var vs = CaptureService.VirtualScreen;
                    req.Region = new Int32Rect(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height);
                    var area = RecorderService.ResolveArea(req);
                    if (area.Width < 2 || area.Height < 2) { MessageBox.Show(L.T("录制区域无效"), L.T("BestSnap 录屏")); return; }
                    req.Region = area;
                }
                if (opts.Countdown > 0) new CountdownWindow(opts.Countdown, () => BeginRecording(req)).Show();
                else BeginRecording(req);
            }
            finally { _recDialogOpen = false; }
        }

        private void BeginRecording(RecordingRequest req)
        {
            try
            {
                _recorder.Start(req, S.SaveFolder);
                var area = RecorderService.ResolveArea(req);
                if (req.Options.ShowRegionBorder && area.Width > 0 && area.Height > 0)
                { _border = new RegionBorderWindow(area); _border.Show(); }
                _recBar = new RecordingControlBar(_recorder);
                _recBar.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("无法开始录屏：\n") + ex.Message, L.T("BestSnap 录屏"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private RegionBorderWindow _border;
        private void CloseRecBar() { _recBar?.Close(); _recBar = null; _border?.Close(); _border = null; }

        private void OnRecordingCompleted(string file)
        {
            CloseRecBar();
            try { TempRecordingStore.MarkComplete(file, DateTime.UtcNow); } catch { }
            Notify(L.T("录屏已完成（尚未保存）") + ": " + file);
            if (_exiting) return;
            new RecordingDoneWindow(file, _settings).Show();
        }

        private void OnRecordingFailed(string error)
        {
            CloseRecBar();
            Logger.Info("Recording failed: " + error);
            if (_exiting) return;
            MessageBox.Show(L.T("录屏失败：\n") + error, L.T("BestSnap 录屏"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // ---------------- timed capture ----------------
        private readonly TimedCapture _timed = new TimedCapture();
        private WinForms.ToolStripMenuItem _timedItem;
        private System.Drawing.Icon _normalIcon, _timedIcon;
        public bool TimedRunning => _timed.Running;

        public async void ToggleTimedCapture()
        {
            if (_timed.Running) { _timed.Stop(); Notify(string.Format(L.T("定时截图已停止，共 {0} 张"), _timed.Taken)); return; }
            if (_busy) return;
            var dlg = new TimedCaptureWindow(S.Timed ?? new TimedCaptureSettings(), S.SaveFolder);
            if (dlg.ShowDialog() != true) return;
            var o = dlg.Result; S.Timed = o; _settings.Save();
            Int32Rect? region = null; IntPtr hwnd = IntPtr.Zero;
            if (o.Mode != TimedCaptureMode.FullScreen)
            {
                _busy = true;
                try
                {
                    await Task.Delay(150);
                    var ov = new SelectionOverlay(o.Mode == TimedCaptureMode.Window ? SelectionKind.WindowObject : SelectionKind.Rectangle);
                    ov.ShowDialog();
                    if (!ov.ResultRect.HasValue) return;
                    var r = ov.ResultRect.Value; var vs = CaptureService.VirtualScreen;
                    region = new Int32Rect(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height);
                    if (o.Mode == TimedCaptureMode.Window)
                    {
                        var pt = new NativeMethods.POINT { X = region.Value.X + region.Value.Width / 2, Y = region.Value.Y + region.Value.Height / 2 };
                        hwnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT);
                    }
                }
                finally { _busy = false; }
            }
            try { _timed.Start(o, S.SaveFolder, region, hwnd); Notify(L.T("定时截图已开始") + " → " + _timed.Folder); }
            catch (Exception ex) { Logger.Error("Timed", ex); Notify(ex.Message); }
        }

        /// <summary>Tray indicator: red dot on the icon, tooltip with progress, menu item turns into "stop".</summary>
        private void UpdateTimedIndicator()
        {
            if (_tray == null) return;
            _normalIcon ??= _tray.Icon;
            if (_timed.Running)
            {
                if (_timedIcon == null)
                {
                    using var bmp = _normalIcon.ToBitmap();
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        int d = Math.Max(6, bmp.Width / 2);
                        g.FillEllipse(System.Drawing.Brushes.Red, bmp.Width - d, bmp.Height - d, d - 1, d - 1);
                        g.DrawEllipse(System.Drawing.Pens.White, bmp.Width - d, bmp.Height - d, d - 1, d - 1);
                    }
                    _timedIcon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
                }
                _tray.Icon = _timedIcon;
                var t = string.Format(L.T("BestSnap – 定时截图中 {0}/{1}"), _timed.Taken, _timed.Total == 0 ? "∞" : _timed.Total.ToString());
                _tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
                _timedItem.Text = L.T("停止定时截图") + $" ({_timed.Taken})";
                _timedItem.Font = new System.Drawing.Font(_timedItem.Font, System.Drawing.FontStyle.Bold);
            }
            else
            {
                _tray.Icon = _normalIcon; _tray.Text = AppIdentity.Name;
                _timedItem.Text = L.T("定时截图…");
                _timedItem.Font = new System.Drawing.Font(_timedItem.Font, System.Drawing.FontStyle.Regular);
            }
        }

        public void ToggleMagnifier()
        {
            if (_magnifier != null) { _magnifier.Close(); _magnifier = null; return; }
            _magnifier = new MagnifierWindow(S.MagnifierZoom);
            _magnifier.Closed += (o, e) => _magnifier = null;
            _magnifier.Show();
        }

        /// <summary>Exit safely: if recording, stop and wait (max 5 s) for the MP4 to be finalised first.</summary>
        AudioRecorderWindow _audioWin;
        LiveCaptionWindow _liveWin;
        public void ShowAudioRecorder() => ShowAudioRecorder(false);
        public void ShowAudioRecorder(bool liveOnly)
        {
            if (_audioWin != null) { _audioWin.Activate(); if (liveOnly) _audioWin.SetLiveOnly(true); return; }
            _audioWin = new AudioRecorderWindow(_settings, _audio, liveOnly);
            _audioWin.Closed += (o, e) => _audioWin = null;
            _audioWin.Show();
        }

        public void RequestExit()
        {
            if (!_recorder.IsRecording && !_audio.IsRecording && !_live.IsRunning) { Application.Current.Shutdown(); return; }
            _exiting = true;
            Notify(L.T("正在结束录屏并保存，请稍候…"));
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            void Done() { timer.Stop(); Application.Current.Shutdown(); }
            _recorder.Completed += _ => Done();
            _recorder.Failed += _ => Done();
            _audio.Completed += _ => Done();
            _audio.Failed += _ => Done();
            _live.Stopped += Done;
            _live.Failed += _ => Done();
            timer.Tick += (o, e) => { Logger.Info("Exit: recorder did not finish within 5 s"); Done(); };
            timer.Start();
            if (_recorder.IsRecording) _recorder.Stop();
            if (_audio.IsRecording) _audio.Stop();
            if (_live.IsRunning) _live.Stop();
        }

        public void Dispose()
        {
            _timed.Stop();
            _hotkeys.Dispose();
            try { _audio.Dispose(); } catch { }
            try { _live.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        }
    }
}
