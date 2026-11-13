using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BestSnap.Models;
using BestSnap.Plugins;
using BestSnap.Services;
using BestSnap.ViewModels;

namespace BestSnap.Views
{
    public class SettingsWindow : Window
    {
        public static readonly Dictionary<string, string> ActionNames = new Dictionary<string, string>
        {
            ["FullScreen"] = "全屏截图", ["ActiveWindow"] = "活动窗口", ["WindowObject"] = "窗口/对象", ["Region"] = "矩形区域",
            ["Freehand"] = "手绘区域", ["FixedRegion"] = "固定大小区域", ["Scrolling"] = "滚动窗口", ["RepeatLast"] = "重复上次截图",
            ["Recorder"] = "开始/停止录屏", ["AudioRecorder"] = "开始/停止录音", ["OcrRegion"] = "识别文字 (OCR)", ["PinRegion"] = "截图并钉到桌面", ["History"] = "截图历史", ["TimedCapture"] = "开始/停止定时截图", ["QrRegion"] = "识别二维码/条码", ["FormulaRegion"] = "公式转LaTeX",
        };

        public SettingsWindow(SettingsService svc, AppController ctl)
        {
            var vm = new SettingsViewModel(svc.Current);
            DataContext = vm;
            Title = L.T("BestSnap 设置"); Width = 620; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var tabs = new TabControl { Margin = new Thickness(10, 10, 10, 0) };

            Grid NewGrid() { var g = new Grid { Margin = new Thickness(12) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); return g; }
            void Row(Grid g, string label, FrameworkElement el)
            {
                int row = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = string.IsNullOrEmpty(label) ? "" : L.T(label), Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(l, row); Grid.SetRow(el, row); Grid.SetColumn(el, 1); el.Margin = new Thickness(0, 4, 0, 4);
                g.Children.Add(l); g.Children.Add(el);
            }
            void Tab(string header, UIElement body) => tabs.Items.Add(new TabItem { Header = L.T(header), Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            UIElement Page(Grid main, Grid adv)
            {
                var sp = new StackPanel();
                sp.Children.Add(main);
                if (adv != null)
                    sp.Children.Add(new Expander { Header = L.T("高级"), Margin = new Thickness(12, 0, 12, 12), IsExpanded = false, Content = adv });
                return sp;
            }
            TextBox T(string path) { var t = new TextBox(); t.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); return t; }
            CheckBox C(string text, string path) { var c = new CheckBox { Content = L.T(text) }; c.SetBinding(CheckBox.IsCheckedProperty, new Binding(path)); return c; }
            ComboBox Combo(object items, string path, string display = null) { var c = new ComboBox { ItemsSource = (System.Collections.IEnumerable)items }; if (display != null) { c.DisplayMemberPath = "Value"; c.SelectedValuePath = "Key"; c.SetBinding(ComboBox.SelectedValueProperty, new Binding(path)); } else c.SetBinding(ComboBox.SelectedItemProperty, new Binding(path)); return c; }

            // ---- General ----
            var g1 = NewGrid();
            Row(g1, L.T("界面语言"), Combo(SettingsViewModel.LanguageNames, nameof(vm.Language), "Value"));
            Row(g1, "", new TextBlock { Text = L.T("切换后立即生效；可在 lang 文件夹中添加自定义语言 (*.json)。"), Foreground = Brushes.Gray, FontSize = 11 });
            Row(g1, L.T("截图后动作"), Combo(SettingsViewModel.AfterCaptureNames, nameof(vm.AfterCapture), "Value"));
            Row(g1, L.T("保存文件夹"), T(nameof(vm.SaveFolder)));
            var tmplBox = T(nameof(vm.FileNameTemplate));
            Row(g1, L.T("文件名模板"), tmplBox);
            var preview = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            void Prev()
            {
                var tmp = new AppSettings { FileNameTemplate = tmplBox.Text, Counter = svc.Current.Counter };
                preview.Text = L.T("预览") + ": " + ImageExporter.ExpandTemplate(tmp, commit: false) + "\n" + ImageExporter.PlaceholderHelp;
            }
            tmplBox.TextChanged += (o, e) => Prev(); Prev();
            Row(g1, "", preview);
            Row(g1, L.T("默认格式"), Combo(SettingsViewModel.Formats, nameof(vm.DefaultFormat)));
            Row(g1, L.T("输出"), C(L.T("保存/复制时自动加水印"), nameof(vm.WatermarkAutoApply)));
            Row(g1, "", C(L.T("开机自动启动（当前用户）"), nameof(vm.AutoStart)));
            Row(g1, "", C(L.T("启动时显示截图工具栏"), nameof(vm.ShowToolbarOnStartup)));

            var g1a = NewGrid();
            Row(g1a, L.T("JPEG 质量"), T(nameof(vm.JpegQuality)));
            Row(g1a, L.T("截图延时(秒)"), T(nameof(vm.CaptureDelaySeconds)));
            Row(g1a, "", C(L.T("截图包含鼠标指针"), nameof(vm.IncludeCursor)));
            Row(g1a, L.T("固定区域宽"), T(nameof(vm.FixedRegionWidth)));
            Row(g1a, L.T("固定区域高"), T(nameof(vm.FixedRegionHeight)));
            Row(g1a, "", C(L.T("保存截图历史"), nameof(vm.HistoryEnabled)));
            Row(g1a, L.T("历史保留张数"), T(nameof(vm.HistoryMax)));
            Row(g1a, "", C(L.T("编辑器/设置窗口也从截图录屏中排除"), nameof(vm.ExcludeAppWindows)));
            Row(g1a, "", new TextBlock { Text = L.T("单击托盘图标显示截图工具栏；右键打开菜单。"), Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            var upl = new Button { Content = L.T("管理上传/分享目标…"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
            upl.Click += (s, e) => new UploadTargetsWindow { Owner = this }.ShowDialog();
            Row(g1a, L.T("上传/分享"), upl);
            Tab(L.T("常规"), Page(g1, g1a));

            // ---- Hotkeys ----
            var g3 = NewGrid();
            var boxes = new List<(HotkeyBinding b, HotkeyBox box, TextBlock st)>();
            ctl.SuspendHotkeys(); // so pressing a combination here doesn't trigger it, and the system-conflict probe is accurate
            Closed += (s, e) => ctl.RegisterHotkeys();
            void Validate()
            {
                foreach (var (b, box, st) in boxes)
                {
                    var gs = box.Gesture;
                    string msg = null; Brush col = Brushes.Green;
                    if (string.IsNullOrWhiteSpace(gs)) { msg = L.T("未设置"); col = Brushes.Gray; }
                    else if (!HotkeyService.TryParse(gs, out _, out _)) { msg = L.T("无效"); col = Brushes.Red; }
                    else if (boxes.Any(o => o.box != box && string.Equals(o.box.Gesture, gs, StringComparison.OrdinalIgnoreCase))) { msg = L.T("与其他动作重复"); col = Brushes.Red; }
                    else if (!HotkeyService.IsSystemFree(gs)) { msg = L.T("已被其他程序占用"); col = Brushes.DarkOrange; }
                    else msg = "✓";
                    st.Text = msg; st.Foreground = col;
                }
            }
            foreach (var b in vm.Hotkeys)
            {
                var box = new HotkeyBox { Gesture = b.Gesture };
                var st = new TextBlock { Width = 120, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
                var bb = b; box.Changed += () => { bb.Gesture = box.Gesture; Validate(); };
                var dp = new DockPanel(); DockPanel.SetDock(st, Dock.Right); dp.Children.Add(st); dp.Children.Add(box);
                Row(g3, ActionNames.TryGetValue(b.Action, out var n) ? n : b.Action, dp);
                boxes.Add((b, box, st));
            }
            var reset = new Button { Content = L.T("恢复默认热键"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
            reset.Click += (s, e) =>
            {
                var d = new AppSettings().Hotkeys;
                foreach (var (b, box, _) in boxes) { var def = d.FirstOrDefault(x => x.Action == b.Action); if (def != null) { b.Gesture = def.Gesture; box.Gesture = def.Gesture; } }
                Validate();
            };
            Row(g3, "", reset);
            Row(g3, "", new TextBlock { Text = L.T("点击输入框后直接按下组合键。橙色 = 被其他程序占用（保存后该热键不会生效）。"), Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            Validate();
            Tab(L.T("热键"), g3);

            // ---- Recording ----
            var gRec = NewGrid();
            Row(gRec, L.T("帧率 (FPS)"), Combo(SettingsViewModel.FpsChoices, nameof(vm.RecorderFps)));
            Row(gRec, "", C(L.T("录制系统声音"), nameof(vm.RecordAudio)));
            Row(gRec, L.T("格式"), Combo(SettingsViewModel.RecordFormatNames, nameof(vm.RecordFormat), "Value"));
            Row(gRec, "", C(L.T("显示鼠标指针"), nameof(vm.RecordShowCursor)));
            Row(gRec, L.T("倒计时 (秒)"), Combo(SettingsViewModel.CountdownChoices, nameof(vm.RecordCountdown)));
            Row(gRec, L.T("保存到"), T(nameof(vm.RecordSaveFolder)));

            var gRecA = NewGrid();
            Row(gRecA, L.T("录制范围"), Combo(SettingsViewModel.RecordSourceNames, nameof(vm.RecordSource), "Value"));
            var quality = new ComboBox { ItemsSource = SettingsViewModel.QualityNames };
            quality.SetBinding(ComboBox.SelectedIndexProperty, new Binding(nameof(vm.RecordQualityIndex)));
            Row(gRecA, L.T("画质"), quality);
            Row(gRecA, "", C(L.T("录制麦克风"), nameof(vm.RecordMicrophone)));
            Row(gRecA, "", C(L.T("高亮鼠标点击"), nameof(vm.RecordHighlightClicks)));
            Row(gRecA, L.T("GIF 帧率"), Combo(SettingsViewModel.GifFpsChoices, nameof(vm.RecordGifFps)));
            Row(gRecA, L.T("GIF 最大宽度"), Combo(SettingsViewModel.GifWidthChoices, nameof(vm.RecordGifMaxWidth)));
            var sysVol = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.1 };
            sysVol.SetBinding(Slider.ValueProperty, new Binding(nameof(vm.RecordSystemVolume)));
            Row(gRecA, L.T("音量"), sysVol);
            var micVol = new Slider { Minimum = 0, Maximum = 1, TickFrequency = 0.1 };
            micVol.SetBinding(Slider.ValueProperty, new Binding(nameof(vm.RecordMicVolume)));
            Row(gRecA, L.T("麦克风"), micVol);
            Row(gRecA, "", C(L.T("录制区域时显示边框（不会被录进去）"), nameof(vm.RecordShowRegionBorder)));
            Row(gRecA, "", C(L.T("录制完成后打开剪辑预览（需要 OCR/媒体插件）"), nameof(vm.RecordTrimAfter)));
            Row(gRecA, "", C(L.T("画中画：叠加摄像头"), nameof(vm.RecordWebcamEnabled)));
            Row(gRecA, L.T("位置"), Combo(SettingsViewModel.WebcamCornerNames, nameof(vm.RecordWebcamCorner), "Value"));
            Row(gRecA, L.T("大小"), Combo(SettingsViewModel.WebcamSizeChoices, nameof(vm.RecordWebcamSize)));
            Row(gRecA, "", C(L.T("圆形"), nameof(vm.RecordWebcamCircle)));
            Row(gRecA, "", C(L.T("显示按键/快捷键（屏幕底部居中，会被录进视频）"), nameof(vm.RecordKeystrokeOverlay)));
            Row(gRecA, "", C(L.T("同时显示鼠标点击"), nameof(vm.RecordKeystrokeShowClicks)));
            Tab(L.T("录屏"), Page(gRec, gRecA));

            // ---- Recognition (OCR + formula + translate) ----
            var gOcr = NewGrid();
            var ocrLangs = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", L.T("自动（内置中英文 OCR，失败时用 Windows OCR）")), new KeyValuePair<string, string>("paddle", L.T("内置中英文 OCR (PP-OCRv4)")), new KeyValuePair<string, string>("windows", L.T("Windows OCR（自动语言）")) };
            try { var ocr = PluginHost.Ocr; if (ocr != null) foreach (var l in ocr.Languages) ocrLangs.Add(new KeyValuePair<string, string>(l, l)); } catch { }
            var ocrBox = Combo(ocrLangs, nameof(vm.OcrLanguage), "Value");
            if (PluginHost.Ocr == null) { ocrBox.IsEnabled = false; ocrBox.ToolTip = L.T("未安装 OCR 插件"); }
            Row(gOcr, L.T("OCR 语言"), ocrBox);
            var fmb = new Button { Content = L.T("公式识别设置…"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
            fmb.Click += (s, e) => new FormulaSettingsWindow { Owner = this }.ShowDialog();
            Row(gOcr, L.T("公式转LaTeX"), fmb);
            var trb = new Button { Content = L.T("翻译服务设置…"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
            trb.Click += (s, e) => new TranslateSettingsWindow { Owner = this }.ShowDialog();
            Row(gOcr, L.T("OCR 翻译"), trb);
            var stt = new Button { Content = L.T("转写与摘要设置…"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
            stt.Click += (s, e) => new TranscriptionSettingsWindow { Owner = this }.ShowDialog();
            Row(gOcr, L.T("转写 / 摘要"), stt);

            var gOcrA = NewGrid();
            Row(gOcrA, "", new TextBlock
            {
                Text = L.T("在线服务会把截图发送到所选服务，使用你自己的密钥。OpenAI 兼容：填写地址（如 https://api.openai.com/v1）和支持图片的模型名。")
                    + "\n" + L.T("DeepL：填 API 密钥（:fx 结尾自动用免费端点）；Azure：填密钥和区域，地址可留空；LibreTranslate：填服务地址（如 https://libretranslate.example.com），密钥可选。识别的文字会发送到所选服务。"),
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11
            });
            Tab(L.T("识别"), Page(gOcr, gOcrA));

            var ok = new Button { Content = L.T("保存"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            ok.Click += (s, e) =>
            {
                var dup = vm.Hotkeys.Where(h => !string.IsNullOrWhiteSpace(h.Gesture)).GroupBy(h => h.Gesture, StringComparer.OrdinalIgnoreCase).FirstOrDefault(gr => gr.Count() > 1);
                if (dup != null) { MessageBox.Show(this, L.T("热键重复") + ": " + dup.Key, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Warning); tabs.SelectedIndex = 1; return; }
                string langBefore = svc.Current.Language;
                vm.Apply();
                try { if (AutoStart.IsEnabled != vm.AutoStart) AutoStart.Set(vm.AutoStart); }
                catch (Exception ex) { MessageBox.Show(this, L.T("无法修改开机启动") + ": " + ex.Message, AppIdentity.Name); }
                svc.Save(); Close();
                if (!string.Equals(langBefore, svc.Current.Language, StringComparison.OrdinalIgnoreCase))
                    L.Reload();
            };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
            btns.Children.Add(ok); btns.Children.Add(cancel);
            var root = new DockPanel(); DockPanel.SetDock(btns, Dock.Bottom); root.Children.Add(btns); root.Children.Add(tabs);
            Content = root;
        }
    }
}
