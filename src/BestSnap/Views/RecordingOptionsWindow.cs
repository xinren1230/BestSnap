using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BestSnap.Models;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Shown before every recording. Fluent tabs: video / audio / mouse &amp; keys / output. Start/Cancel stay at the bottom.</summary>
    public class RecordingOptionsWindow : Window
    {
        private readonly RadioButton _rMon = new RadioButton { Content = L.T("全屏（选择显示器）"), GroupName = "src" };
        private readonly RadioButton _rVirt = new RadioButton { Content = L.T("全屏（所有显示器）"), GroupName = "src" };
        private readonly RadioButton _rReg = new RadioButton { Content = L.T("选择区域（开始后拖动框选）"), GroupName = "src" };
        private readonly RadioButton _rWin = new RadioButton { Content = L.T("指定窗口"), GroupName = "src" };
        private readonly ComboBox _monitors = new ComboBox { DisplayMemberPath = "Item2" };
        private readonly ComboBox _windows = new ComboBox { DisplayMemberPath = "Item2" };
        private readonly CheckBox _sys = new CheckBox { Content = L.T("录制系统声音") };
        private readonly CheckBox _mic = new CheckBox { Content = L.T("录制麦克风") };
        private readonly ComboBox _mics = new ComboBox { DisplayMemberPath = "Item2" };
        private readonly ComboBox _sysDev = new ComboBox { DisplayMemberPath = "Item2" };
        private readonly CheckBox _cursor = new CheckBox { Content = L.T("显示鼠标指针") };
        private readonly CheckBox _clicks = new CheckBox { Content = L.T("高亮鼠标点击") };
        private readonly ComboBox _fps = new ComboBox { ItemsSource = RecordRectMath.VideoFpsChoices, Width = 72 };
        private readonly ComboBox _quality = new ComboBox { Width = 120, ItemsSource = new[] { L.T("低（文件小）"), L.T("中"), L.T("高"), L.T("极高") } };
        private readonly ComboBox _countdown = new ComboBox { ItemsSource = new[] { 0, 3, 5 }, Width = 72 };
        private readonly TextBox _folder = new TextBox { MinWidth = 220 };
        private readonly CheckBox _remember = new CheckBox { Content = L.T("记住这些设置") };
        private readonly RadioButton _fMp4 = new RadioButton { Content = L.T("MP4 视频 (H.264 + 声音)"), GroupName = "fmt" };
        private readonly RadioButton _fGif = new RadioButton { Content = L.T("GIF 动图（无声音）"), GroupName = "fmt" };
        private readonly RadioButton _fMp4Out = new RadioButton { Content = L.T("MP4 视频 (H.264 + 声音)"), GroupName = "fmtOut" };
        private readonly RadioButton _fGifOut = new RadioButton { Content = L.T("GIF 动图（无声音）"), GroupName = "fmtOut" };
        private readonly ComboBox _gifFps = new ComboBox { ItemsSource = new[] { 5, 8, 10, 12, 15, 20 }, Width = 72 };
        private readonly ComboBox _gifWidth = new ComboBox { ToolTip = L.T("0 = 保持原始尺寸"), ItemsSource = new[] { 480, 640, 800, 960, 1280, 0 }, Width = 88 };
        private readonly Slider _sysVol = new Slider { Minimum = 0, Maximum = 100, Width = 200, TickFrequency = 10, IsSnapToTickEnabled = false };
        private readonly Slider _micVol = new Slider { Minimum = 0, Maximum = 100, Width = 200, TickFrequency = 10 };
        private readonly CheckBox _border = new CheckBox { Content = L.T("录制区域时显示边框（不会被录进去）") };
        private readonly CheckBox _trim = new CheckBox { Content = L.T("录制完成后自动打开剪辑编辑器") };
        private readonly CheckBox _cam = new CheckBox { Content = L.T("画中画：叠加摄像头") };
        private readonly ComboBox _cams = new ComboBox { DisplayMemberPath = "Item2", MinWidth = 200 };
        private readonly ComboBox _camCorner = new ComboBox { Width = 100 };
        private readonly ComboBox _camSize = new ComboBox { ItemsSource = new[] { 120, 160, 200, 240, 320, 400 }, Width = 72 };
        private readonly RadioButton _camCircle = new RadioButton { Content = L.T("圆形"), GroupName = "camshape" };
        private readonly RadioButton _camRect = new RadioButton { Content = L.T("矩形"), GroupName = "camshape", Margin = new Thickness(12, 0, 0, 0) };
        private readonly CheckBox _keys = new CheckBox { Content = L.T("显示按键/快捷键（屏幕底部居中，会被录进视频）") };
        private readonly CheckBox _keyClicks = new CheckBox { Content = L.T("同时显示鼠标点击") };
        private readonly TextBlock _fileHint = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private static readonly int[] QualityValues = { 40, 60, 80, 95 };

        public RecordingSettings Result { get; private set; }
        public IntPtr SelectedWindow { get; private set; }
        public bool Remember => _remember.IsChecked == true;

        public RecordingOptionsWindow(RecordingSettings o, string defaultFolder)
        {
            Title = L.T("录屏选项 - BestSnap"); Icon = App.AppIcon;
            Width = 540; Height = 500; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
            Background = SmartUi.LightBarBackground;

            try { _monitors.ItemsSource = RecorderService.GetMonitors().Select(m => Tuple.Create(m.Device, m.Name)).ToList(); } catch { }
            try { _windows.ItemsSource = RecorderService.GetWindows().Select(w => Tuple.Create(w.Handle, w.Title)).ToList(); } catch { }
            try { _mics.ItemsSource = RecorderService.GetMicrophones().Select(m => Tuple.Create(m.Device, m.Name)).ToList(); } catch { }
            try { _sysDev.ItemsSource = RecorderService.GetSystemOutputs().Select(m => Tuple.Create(m.Device, m.Name)).ToList(); } catch { }

            _rMon.IsChecked = o.Source == RecordSourceKind.Monitor;
            _rVirt.IsChecked = o.Source == RecordSourceKind.VirtualScreen;
            _rReg.IsChecked = o.Source == RecordSourceKind.Region;
            _rWin.IsChecked = o.Source == RecordSourceKind.Window;
            _monitors.SelectedItem = (_monitors.ItemsSource as System.Collections.Generic.List<Tuple<string, string>>)?.FirstOrDefault(m => m.Item1 == o.MonitorDevice);
            if (_monitors.SelectedIndex < 0 && _monitors.Items.Count > 0) _monitors.SelectedIndex = 0;
            if (_windows.Items.Count > 0) _windows.SelectedIndex = 0;
            _mics.SelectedItem = (_mics.ItemsSource as System.Collections.Generic.List<Tuple<string, string>>)?.FirstOrDefault(m => m.Item1 == o.MicrophoneDevice);
            if (_mics.SelectedIndex < 0 && _mics.Items.Count > 0) _mics.SelectedIndex = 0;
            _sysDev.SelectedItem = (_sysDev.ItemsSource as System.Collections.Generic.List<Tuple<string, string>>)?.FirstOrDefault(m => m.Item1 == o.SystemAudioDevice);
            if (_sysDev.SelectedIndex < 0 && _sysDev.Items.Count > 0) _sysDev.SelectedIndex = 0;
            _sys.IsChecked = o.SystemAudio; _mic.IsChecked = o.Microphone; _cursor.IsChecked = o.ShowCursor; _clicks.IsChecked = o.HighlightClicks;
            _fps.SelectedItem = RecordRectMath.NormalizeFps(o.Fps);
            int qi = Array.FindIndex(QualityValues, v => v >= o.Quality); _quality.SelectedIndex = qi < 0 ? 3 : qi;
            _countdown.SelectedItem = new[] { 0, 3, 5 }.Contains(o.Countdown) ? o.Countdown : 3;
            _folder.Text = string.IsNullOrWhiteSpace(o.SaveFolder) ? defaultFolder : o.SaveFolder;
            _remember.IsChecked = true;
            _fMp4.IsChecked = o.Format != RecordFormat.Gif; _fGif.IsChecked = o.Format == RecordFormat.Gif;
            _fMp4Out.IsChecked = _fMp4.IsChecked; _fGifOut.IsChecked = _fGif.IsChecked;
            _gifFps.SelectedItem = new[] { 5, 8, 10, 12, 15, 20 }.Contains(o.GifFps) ? o.GifFps : 10;
            _gifWidth.SelectedItem = new[] { 480, 640, 800, 960, 1280, 0 }.Contains(o.GifMaxWidth) ? o.GifMaxWidth : 960;
            _sysVol.Value = Math.Round(o.SystemVolume * 100); _micVol.Value = Math.Round(o.MicVolume * 100);
            _border.IsChecked = o.ShowRegionBorder; _trim.IsChecked = o.TrimAfterRecording;
            if (PluginHost.Trimmer == null) { _trim.IsEnabled = false; _trim.ToolTip = L.T("未安装 plugins\\BestSnap.Ocr 插件，剪辑不可用"); }

            _camCorner.ItemsSource = new[] { L.T("右下"), L.T("左下"), L.T("右上"), L.T("左上") };
            _camCorner.SelectedIndex = (int)o.WebcamCorner;
            _camSize.SelectedItem = new[] { 120, 160, 200, 240, 320, 400 }.Contains(o.WebcamSize) ? o.WebcamSize : 200;
            try { foreach (var c in RecorderService.GetCameras()) _cams.Items.Add(Tuple.Create(c.Device, c.Name)); } catch { }
            foreach (Tuple<string, string> c in _cams.Items) if (c.Item1 == o.WebcamDevice) _cams.SelectedItem = c;
            if (_cams.SelectedIndex < 0 && _cams.Items.Count > 0) _cams.SelectedIndex = 0;
            if (_cams.Items.Count == 0) { _cam.IsEnabled = false; _cam.ToolTip = L.T("未找到摄像头"); }
            _cam.IsChecked = o.WebcamEnabled && _cam.IsEnabled; _camCircle.IsChecked = o.WebcamCircle; _camRect.IsChecked = !o.WebcamCircle;
            _keys.IsChecked = o.KeystrokeOverlay; _keyClicks.IsChecked = o.KeystrokeShowClicks;
            _cam.ToolTip = L.T("圆形/GIF 需要 OCR 插件（摄像头气泡窗口，可拖动）；否则使用矩形叠加。录制指定窗口时始终为矩形。");

            var video = Page(
                Section(L.T("录制范围"),
                    Pair(_rMon, _monitors),
                    _rVirt,
                    _rReg,
                    Pair(_rWin, _windows)),
                Inline(
                    Field(L.T("帧率 (FPS)"), _fps),
                    Field(L.T("画质"), _quality),
                    Field(L.T("倒计时 (秒)"), _countdown)),
                Section(L.T("编码"), Inline(_fMp4, _fGif)),
                Inline(
                    Field(L.T("GIF 帧率"), _gifFps),
                    Field(L.T("GIF 最大宽度"), _gifWidth)),
                _border,
                Pair(_cam, _cams),
                Inline(
                    Field(L.T("位置"), _camCorner),
                    Field(L.T("大小"), _camSize),
                    Horz(_camCircle, _camRect)));

            var audio = Page(
                _sys, _sysDev, VolRow(_sysVol),
                Gap(8),
                _mic, _mics, VolRow(_micVol),
                Hint(L.T("GIF 动图（无声音）")));

            var mouse = Page(
                _cursor, _clicks, Gap(8),
                _keys, Indent(_keyClicks));

            var browse = new Button
            {
                Content = L.T("浏览…"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0),
                Height = 32, Cursor = Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                Foreground = SmartUi.LightFg, Background = Brushes.Transparent, Style = SmartUi.ButtonStyle(false)
            };
            browse.Click += (s, e) =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = _folder.Text, Description = L.T("选择录屏保存文件夹") };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _folder.Text = dlg.SelectedPath;
            };
            var fp = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); fp.Children.Add(browse); fp.Children.Add(_folder);

            var output = Page(
                Field(L.T("保存到"), fp, stretch: true),
                Field(L.T("文件名"), _fileHint, stretch: true),
                Section(L.T("格式"), Inline(_fMp4Out, _fGifOut)),
                _trim, _remember);

            var tabs = SmartUi.SegmentTabs(
                (L.T("视频"), video),
                (L.T("音频"), audio),
                (L.T("鼠标与按键"), mouse),
                (L.T("输出"), output));

            var ok = new Button
            {
                Content = L.T("开始录制"), IsDefault = true, MinWidth = 108, Height = 32, Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                Foreground = SmartUi.LightFg, Background = SmartUi.QuietAccentLight, Style = SmartUi.ButtonStyle(false)
            };
            var cancel = new Button
            {
                Content = L.T("取消"), IsCancel = true, MinWidth = 90, Height = 32,
                Cursor = Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                Foreground = SmartUi.LightFg, Background = Brushes.Transparent, Style = SmartUi.ButtonStyle(false)
            };
            ok.Click += (s, e) => OnOk();
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btns.Children.Add(ok); btns.Children.Add(cancel);

            var root = new Grid { Margin = new Thickness(16, 12, 16, 12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(tabs, 0); Grid.SetRow(btns, 1);
            btns.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(tabs); root.Children.Add(btns);
            Content = root;

            void Sync()
            {
                bool gif = _fGif.IsChecked == true;
                _monitors.IsEnabled = _rMon.IsChecked == true; _windows.IsEnabled = _rWin.IsChecked == true;
                _sys.IsEnabled = _mic.IsEnabled = !gif; _mics.IsEnabled = !gif && _mic.IsChecked == true;
                _sysDev.IsEnabled = !gif && _sys.IsChecked == true;
                _sysVol.IsEnabled = !gif && _sys.IsChecked == true; _micVol.IsEnabled = !gif && _mic.IsChecked == true;
                _fps.IsEnabled = _quality.IsEnabled = !gif; _gifFps.IsEnabled = _gifWidth.IsEnabled = gif;
                _clicks.IsEnabled = !gif;
                string ext = gif ? ".gif" : ".mp4";
                _fileHint.Text = "Rec_yyyyMMdd_HHmmss" + ext + "  " + L.T("先写入临时文件，完成后再另存");
            }
            bool syncingFmt = false;
            void BindFmt(RadioButton srcMp4, RadioButton srcGif, RadioButton dstMp4, RadioButton dstGif)
            {
                srcMp4.Checked += (s, e) => { if (syncingFmt) return; syncingFmt = true; dstMp4.IsChecked = true; syncingFmt = false; Sync(); };
                srcGif.Checked += (s, e) => { if (syncingFmt) return; syncingFmt = true; dstGif.IsChecked = true; syncingFmt = false; Sync(); };
            }
            BindFmt(_fMp4, _fGif, _fMp4Out, _fGifOut);
            BindFmt(_fMp4Out, _fGifOut, _fMp4, _fGif);
            _sys.Checked += (s, e) => Sync(); _sys.Unchecked += (s, e) => Sync();
            _rMon.Checked += (s, e) => Sync(); _rVirt.Checked += (s, e) => Sync(); _rReg.Checked += (s, e) => Sync(); _rWin.Checked += (s, e) => Sync();
            _mic.Checked += (s, e) => Sync(); _mic.Unchecked += (s, e) => Sync();
            Sync();
        }

        private static UIElement Page(params UIElement[] items)
        {
            var p = new StackPanel();
            foreach (var i in items)
            {
                if (i is FrameworkElement fe && fe.Margin == new Thickness(0)) fe.Margin = new Thickness(0, 4, 0, 4);
                p.Children.Add(i);
            }
            return p;
        }

        private static UIElement Section(string header, params UIElement[] items)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 6), Foreground = SmartUi.LightFg });
            foreach (var i in items)
            {
                if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 3, 0, 3);
                p.Children.Add(i);
            }
            return p;
        }

        private static UIElement Horz(params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items) { if (i is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center; p.Children.Add(i); }
            return p;
        }

        private static UIElement Inline(params UIElement[] items)
        {
            var p = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var i in items)
            {
                if (i is FrameworkElement fe) fe.Margin = new Thickness(0, 2, 16, 2);
                p.Children.Add(i);
            }
            return p;
        }

        private static UIElement Field(string label, UIElement el, bool stretch = false)
        {
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = SmartUi.LightFg };
            if (!stretch) return Horz(l, el);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            l.MinWidth = 56; l.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(el, 1); ((FrameworkElement)el).VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l); g.Children.Add(el);
            return g;
        }

        private static UIElement Pair(UIElement left, UIElement right)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (left is FrameworkElement lf) { lf.Margin = new Thickness(0, 0, 10, 0); lf.VerticalAlignment = VerticalAlignment.Center; }
            if (right is FrameworkElement rf) rf.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            g.Children.Add(left); g.Children.Add(right);
            return g;
        }

        private static UIElement Indent(UIElement e) { if (e is FrameworkElement fe) fe.Margin = new Thickness(22, 3, 0, 3); return e; }
        private static UIElement Gap(double h) => new FrameworkElement { Height = h };
        private static UIElement Hint(string t) => new TextBlock { Text = t, FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };

        private static UIElement VolRow(Slider sl)
        {
            var t = new TextBlock { Width = 72, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = SmartUi.LightFg };
            void U() => t.Text = L.T("音量") + " " + (int)sl.Value + "%";
            sl.ValueChanged += (s, e) => U(); U();
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 2, 0, 8) }; p.Children.Add(sl); p.Children.Add(t); return p;
        }

        private void OnOk()
        {
            if (_rWin.IsChecked == true && !(_windows.SelectedItem is Tuple<IntPtr, string>)) { MessageBox.Show(this, L.T("请选择要录制的窗口。"), AppIdentity.Name); return; }
            if (string.IsNullOrWhiteSpace(_folder.Text)) { MessageBox.Show(this, L.T("请选择保存文件夹。"), AppIdentity.Name); return; }
            Result = new RecordingSettings
            {
                Source = _rReg.IsChecked == true ? RecordSourceKind.Region : _rWin.IsChecked == true ? RecordSourceKind.Window : _rVirt.IsChecked == true ? RecordSourceKind.VirtualScreen : RecordSourceKind.Monitor,
                MonitorDevice = (_monitors.SelectedItem as Tuple<string, string>)?.Item1,
                SystemAudio = _sys.IsChecked == true, Microphone = _mic.IsChecked == true,
                MicrophoneDevice = (_mics.SelectedItem as Tuple<string, string>)?.Item1,
                SystemAudioDevice = (_sysDev.SelectedItem as Tuple<string, string>)?.Item1,
                ShowCursor = _cursor.IsChecked == true, HighlightClicks = _clicks.IsChecked == true,
                Fps = (int)(_fps.SelectedItem ?? 30), Quality = QualityValues[Math.Max(0, _quality.SelectedIndex)],
                Countdown = (int)(_countdown.SelectedItem ?? 0), SaveFolder = _folder.Text.Trim(),
                Format = _fGif.IsChecked == true ? RecordFormat.Gif : RecordFormat.Mp4,
                GifFps = (int)(_gifFps.SelectedItem ?? 10), GifMaxWidth = (int)(_gifWidth.SelectedItem ?? 960),
                SystemVolume = _sysVol.Value / 100, MicVolume = _micVol.Value / 100,
                ShowRegionBorder = _border.IsChecked == true, TrimAfterRecording = _trim.IsChecked == true,
                WebcamEnabled = _cam.IsChecked == true, WebcamDevice = (_cams.SelectedItem as Tuple<string, string>)?.Item1,
                WebcamName = (_cams.SelectedItem as Tuple<string, string>)?.Item2,
                WebcamCorner = (WebcamCorner)Math.Max(0, _camCorner.SelectedIndex), WebcamSize = (int)(_camSize.SelectedItem ?? 200),
                WebcamCircle = _camCircle.IsChecked == true,
                KeystrokeOverlay = _keys.IsChecked == true, KeystrokeShowClicks = _keyClicks.IsChecked == true,
            };
            SelectedWindow = (_windows.SelectedItem as Tuple<IntPtr, string>)?.Item1 ?? IntPtr.Zero;
            if (_rWin.IsChecked == true && SelectedWindow != IntPtr.Zero && BestSnap.Native.NativeMethods.IsIconic(SelectedWindow))
            {
                BestSnap.Native.NativeMethods.ShowWindow(SelectedWindow, BestSnap.Native.NativeMethods.SW_RESTORE);
                BestSnap.Native.NativeMethods.SetForegroundWindow(SelectedWindow);
            }
            DialogResult = true;
        }
    }
}
