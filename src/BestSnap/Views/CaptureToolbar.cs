using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Native;
using BestSnap.Plugins;
using BestSnap.Services;
using CaptureMode = BestSnap.Services.CaptureMode;

namespace BestSnap.Views
{
    /// <summary>Small floating, borderless, draggable, topmost capture toolbar — core capture buttons plus a More menu.</summary>
    public class CaptureToolbar : Window
    {
        private readonly AppController _ctl;
        private readonly SettingsService _svc;
        private readonly TextBlock _delayBadge = new TextBlock { FontSize = 9, Foreground = Brushes.Gold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        private static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public CaptureToolbar(AppController ctl, SettingsService svc)
        {
            _ctl = ctl; _svc = svc;
            Title = AppIdentity.Name; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            var s = svc.Current;
            bool physical = s.ToolbarX.HasValue && s.ToolbarY.HasValue && OnScreenPhysical(s.ToolbarX.Value, s.ToolbarY.Value);
            if (physical)
            {
                WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000;
                SourceInitialized += (o, e) => NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, IntPtr.Zero, s.ToolbarX.Value, s.ToolbarY.Value, 0, 0, 0x0001 | 0x0004 | 0x0010);
            }
            else if (s.ToolbarLeft.HasValue && s.ToolbarTop.HasValue && OnScreen(s.ToolbarLeft.Value, s.ToolbarTop.Value))
            { WindowStartupLocation = WindowStartupLocation.Manual; Left = s.ToolbarLeft.Value; Top = s.ToolbarTop.Value; }
            else { WindowStartupLocation = WindowStartupLocation.Manual; Left = SystemParameters.WorkArea.Width / 2 - 140; Top = 12; }

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo_24.png")), Width = 22, Height = 22, Margin = new Thickness(4, 0, 6, 0), Cursor = Cursors.SizeAll, ToolTip = L.T("BestSnap（拖动移动工具栏）") };
            panel.Children.Add(logo);

            Button Btn(string glyph, string tip, Action a, UIElement badge = null)
            {
                var g = new Grid();
                g.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                if (badge != null) g.Children.Add(badge);
                var b = new Button { Content = g, ToolTip = tip, Width = 30, Height = 28, Margin = new Thickness(1), Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Style = FlatStyle };
                b.Click += (o, e) => a();
                panel.Children.Add(b);
                return b;
            }

            Btn("\uE7A8", L.T("矩形区域截图") + SmartUi.Key("Region"), () => ctl.Capture(CaptureMode.Region));
            Btn("\uE8B3", L.T("截取窗口/对象") + SmartUi.Key("WindowObject"), () => ctl.Capture(CaptureMode.WindowObject));
            Btn("\uE740", L.T("全屏截图") + SmartUi.Key("FullScreen"), () => ctl.Capture(CaptureMode.FullScreen));
            Button recBtn = null;
            recBtn = Btn("\uE7C8", L.T("开始/停止录屏") + SmartUi.Key("Recorder"), () =>
            {
                if (ctl.IsAnyRecording) { ctl.StopAnyRecording(); return; }
                var recMenu = new ContextMenu();
                void RecItem(string header, Action a)
                {
                    var mi = new MenuItem { Header = L.T(header) };
                    mi.Click += (s, e) => a();
                    recMenu.Items.Add(mi);
                }
                RecItem("屏幕录制…", ctl.ToggleRecording);
                RecItem("录制音频…", ctl.ShowAudioRecorder);
                RecItem("仅转写（不保存音频）…", ctl.ShowLiveTranscribe);
                recMenu.PlacementTarget = recBtn;
                recMenu.IsOpen = true;
            });

            var more = BuildMoreButton();
            panel.Children.Add(more);
            var minimize = SmartUi.Fluent(SmartUi.GMin, null, L.T("最小化到托盘"), Hide, dark: true);
            minimize.Height = 28;
            minimize.MinWidth = 30;
            minimize.Margin = new Thickness(1);
            panel.Children.Add(minimize);

            Content = SmartUi.Bar(panel);
            MouseLeftButtonDown += (o, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
            LocationChanged += (o, e) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero || !NativeMethods.GetWindowRect(h, out var r) || r.Left <= -30000) return;
                _svc.Current.ToolbarX = r.Left; _svc.Current.ToolbarY = r.Top;
                _svc.Current.ToolbarLeft = Left; _svc.Current.ToolbarTop = Top;
            };
            Deactivated += (o, e) => _svc.Save();
            UpdateBadge();
        }

        private Button BuildMoreButton()
        {
            var menu = new ContextMenu();
            void Item(string header, Action a, string action = null)
            {
                var mi = new MenuItem { Header = L.T(header) + (action != null ? SmartUi.Key(action) : "") };
                mi.Click += (s, e) => a();
                menu.Items.Add(mi);
            }
            void Sep() => menu.Items.Add(new Separator());

            Item("截取活动窗口", () => _ctl.Capture(CaptureMode.ActiveWindow), "ActiveWindow");
            Item("手绘区域截图", () => _ctl.Capture(CaptureMode.Freehand), "Freehand");
            Item("固定大小区域截图", () => _ctl.Capture(CaptureMode.FixedRegion), "FixedRegion");
            Item("滚动窗口截图", () => _ctl.Capture(CaptureMode.Scrolling), "Scrolling");
            Item("重复上次截图", _ctl.RepeatLast, "RepeatLast");

            var delayRoot = new MenuItem { Header = L.T("截图延时(秒)") };
            var delayChoices = new[] { 0, 3, 5, 10 };
            foreach (var d in delayChoices)
            {
                int secs = d;
                var mi = new MenuItem
                {
                    Header = secs == 0 ? L.T("截图延时：关闭") : string.Format(L.T("截图延时：{0} 秒"), secs),
                    IsCheckable = true
                };
                mi.Click += (s, e) =>
                {
                    _svc.Current.CaptureDelaySeconds = secs; _svc.Save(); UpdateBadge();
                    _ctl.Notify(secs == 0 ? L.T("截图延时：关闭") : string.Format(L.T("截图延时：{0} 秒"), secs));
                };
                delayRoot.Items.Add(mi);
            }
            delayRoot.SubmenuOpened += (s, e) =>
            {
                int cur = _svc.Current.CaptureDelaySeconds;
                foreach (MenuItem mi in delayRoot.Items)
                    mi.IsChecked = string.Equals(mi.Header as string,
                        cur == 0 ? L.T("截图延时：关闭") : string.Format(L.T("截图延时：{0} 秒"), cur), StringComparison.Ordinal);
            };
            menu.Items.Add(delayRoot);
            Sep();
            Item("识别文字 (OCR)", _ctl.OcrRegion, "OcrRegion");
            Item("公式转LaTeX", _ctl.FormulaRegion, "FormulaRegion");
            Item("翻译", _ctl.TranslateRegion);
            Item("模糊/打码", _ctl.BlurRegion);
            Item("自动打码", _ctl.RedactRegion);
            Item("识别二维码/条码", _ctl.QrRegion, "QrRegion");
            Sep();
            Item("屏幕取色器", _ctl.ShowColorPicker);
            Item("屏幕放大镜", _ctl.ToggleMagnifier);
            Item("屏幕标尺", _ctl.ShowRuler);
            Item("打开图片到编辑器", _ctl.OpenImage);
            Item("截图历史…", _ctl.ShowHistory, "History");
            Item("定时截图…", _ctl.ToggleTimedCapture, "TimedCapture");
            Item("录制音频…", _ctl.ShowAudioRecorder, "AudioRecorder");
            Item("拼接长图…", () => new StitchWindow().Show());
            Item("图片对比…", () => new CompareWindow().Show());
            Sep();
            Item("设置", _ctl.ShowSettings);
            Item("隐藏到托盘（托盘菜单可再次显示）", Hide);

            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = "\uE712", FontFamily = IconFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            label.Children.Add(new TextBlock { Text = L.T("更多"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var g = new Grid();
            g.Children.Add(label);
            g.Children.Add(_delayBadge);
            var more = new Button
            {
                Content = g, ToolTip = L.T("更多"), Height = 28, Margin = new Thickness(2, 1, 2, 1), Padding = new Thickness(8, 0, 8, 0),
                Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Style = FlatStyle
            };
            more.Click += (s, e) => { menu.PlacementTarget = more; menu.IsOpen = true; };
            return more;
        }

        private void UpdateBadge() => _delayBadge.Text = _svc.Current.CaptureDelaySeconds > 0 ? _svc.Current.CaptureDelaySeconds + "s" : "";

        private static bool OnScreenPhysical(int x, int y)
        {
            foreach (var sc in System.Windows.Forms.Screen.AllScreens)
                if (sc.WorkingArea.Contains(x + 30, y + 10)) return true;
            return false;
        }

        private static bool OnScreen(double l, double t) =>
            l >= SystemParameters.VirtualScreenLeft - 50 && t >= SystemParameters.VirtualScreenTop - 10 &&
            l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 20;

        private static Style _flat;
        private static Style FlatStyle
        {
            get
            {
                if (_flat != null) return _flat;
                var tpl = new ControlTemplate(typeof(Button));
                var bd = new FrameworkElementFactory(typeof(Border), "bd");
                bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
                cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
                bd.AppendChild(cp);
                tpl.VisualTree = bd;
                var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), "bd"));
                tpl.Triggers.Add(hover);
                var press = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
                press.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(120, 124, 58, 237)), "bd"));
                tpl.Triggers.Add(press);
                _flat = new Style(typeof(Button));
                _flat.Setters.Add(new Setter(TemplateProperty, tpl));
                return _flat;
            }
        }
    }
}
