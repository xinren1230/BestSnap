using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Models;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Formula → LaTeX result: editable source, copy as raw / $…$ / $$…$$, live rendered preview.</summary>
    public class FormulaWindow : Window
    {
        private readonly TextBox _src = new TextBox { FontFamily = new FontFamily("Consolas"), FontSize = 14, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly ContentControl _preview = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _status = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        private readonly ProgressBar _bar = new ProgressBar { Height = 6, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        private readonly BitmapSource _img;
        private readonly Button _retry;
        private CancellationTokenSource _cts;

        public static void Run(BitmapSource img, Window owner = null) => new FormulaWindow(img) { Owner = owner?.IsVisible == true ? owner : null }.Show();

        public FormulaWindow(BitmapSource img)
        {
            _img = img;
            Title = L.T("公式转LaTeX"); Width = 680; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var dock = new DockPanel { Margin = new Thickness(10) };
            var srcImg = new Image { Source = img, MaxHeight = 90, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(srcImg, Dock.Top); dock.Children.Add(srcImg);
            var bp = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Button Mk(string t, Action a) { var b = new Button { Content = t, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) }; b.Click += (s, e) => a(); bp.Children.Add(b); return b; }
            Mk(L.T("复制 LaTeX"), () => Copy(_src.Text.Trim()));
            Mk(L.T("复制 $…$"), () => Copy("$" + _src.Text.Trim() + "$"));
            Mk(L.T("复制 $$…$$"), () => Copy("$$" + _src.Text.Trim() + "$$"));
            _retry = Mk(L.T("重新识别"), Start);
            Mk(L.T("公式识别设置…"), () => { if (new FormulaSettingsWindow { Owner = this }.ShowDialog() == true) Start(); });
            var bottom = new StackPanel(); bottom.Children.Add(bp); bottom.Children.Add(_bar); bottom.Children.Add(_status);
            DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
            DockPanel.SetDock(_src, Dock.Top); dock.Children.Add(_src);
            dock.Children.Add(new Border { Child = new ScrollViewer { Content = _preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 8, 0, 0), MinHeight = 80 });
            Content = dock;
            _src.TextChanged += (s, e) => Render();
            Closed += (s, e) => _cts?.Cancel();
            Loaded += (s, e) => Start();
        }

        private void Copy(string t) { try { Clipboard.SetText(t); _status.Text = L.T("已复制到剪贴板"); } catch { } }

        private void Render()
        {
            var tex = _src.Text.Trim();
            if (tex.Length == 0) { _preview.Content = null; return; }
            try
            {
                var fc = new WpfMath.Controls.FormulaControl { Formula = tex, Scale = 22, SystemTextFontName = "Cambria Math" };
                _preview.Content = fc.HasError ? Err(L.T("预览无法渲染此公式（可直接复制使用）")) : fc;
            }
            catch (Exception) { _preview.Content = Err(L.T("预览无法渲染此公式（可直接复制使用）")); }
        }
        private static TextBlock Err(string t) => new TextBlock { Text = t, Foreground = Brushes.Gray, Margin = new Thickness(8) };

        private async void Start()
        {
            _cts?.Cancel(); _cts = new CancellationTokenSource(); var ct = _cts.Token;
            _retry.IsEnabled = false; _status.Text = L.T("识别中…"); _bar.Visibility = Visibility.Collapsed;
            var engine = FormulaService.Cfg.Engine;
            try
            {
                byte[] png;
                using (var ms = new MemoryStream()) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(_img)); enc.Save(ms); png = ms.ToArray(); }
                var prog = new Progress<double>(p =>
                {
                    _bar.Visibility = Visibility.Visible; _bar.Value = p;
                    _status.Text = string.Format(L.T("首次使用：正在下载公式识别模型（约 112 MB）… {0:P0}"), p);
                });
                if (engine == FormulaEngine.Local && FormulaService.Local == null)
                    throw new InvalidOperationException(L.T("离线公式识别需要 OCR 插件（plugins\\BestSnap.Ocr）。可在“公式识别设置”中改用在线服务。"));
                var t0 = DateTime.Now;
                var tex = await FormulaService.RecognizeAsync(png, prog, ct);
                if (ct.IsCancellationRequested) return;
                _bar.Visibility = Visibility.Collapsed;
                _src.Text = tex ?? "";
                _status.Text = (engine == FormulaEngine.Local ? "pix2text-mfr (" + L.T("离线") + ")" : engine.ToString()) + $" · {(DateTime.Now - t0).TotalSeconds:0.0}s";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.Error("Formula", ex); _bar.Visibility = Visibility.Collapsed; _status.Text = L.T("识别失败") + ": " + ex.Message; }
            finally { _retry.IsEnabled = true; }
        }
    }

    /// <summary>Formula engine settings. Keys are stored DPAPI-encrypted (current Windows user).</summary>
    public class FormulaSettingsWindow : Window
    {
        public FormulaSettingsWindow()
        {
            Title = L.T("公式识别设置"); Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var c = FormulaService.Cfg;
            var eng = new ComboBox { ItemsSource = new[] { L.T("离线（pix2text-mfr；安装包已含模型，缺失时才下载约 112 MB）"), L.T("OpenAI 兼容的视觉模型 API"), "Mathpix API" }, SelectedIndex = (int)c.Engine };
            var apiBase = new TextBox { Text = c.ApiBase ?? "" };
            var apiModel = new TextBox { Text = c.ApiModel ?? "" };
            var apiKey = new PasswordBox();
            var mpId = new TextBox { Text = c.MathpixAppId ?? "" };
            var mpKey = new PasswordBox();
            TextBlock Hint(bool has) => new TextBlock { Text = has ? L.T("已保存密钥（留空 = 保持不变）") : L.T("未保存密钥"), Foreground = Brushes.Gray, FontSize = 11 };
            var models = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11,
                Text = L.T("离线模型：") + FormulaService.ModelRepo + " (MIT)\n" + FormulaService.ModelDir + "  —  " + (FormulaService.ModelsReady ? L.T("已下载") : L.T("未下载")) };
            var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11,
                Text = L.T("在线服务会把截图发送到所选服务，使用你自己的密钥。OpenAI 兼容：填写地址（如 https://api.openai.com/v1）和支持图片的模型名。") };
            var g = new Grid { Margin = new Thickness(14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
            void Row(string label, UIElement el)
            {
                int r = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(l, r); Grid.SetRow(el, r); Grid.SetColumn(el, 1); ((FrameworkElement)el).Margin = new Thickness(0, 4, 0, 4);
                g.Children.Add(l); g.Children.Add(el);
            }
            Row(L.T("识别引擎"), eng); Row("", models);
            Row(L.T("API 地址"), apiBase); Row(L.T("模型"), apiModel); Row(L.T("API 密钥"), apiKey); Row("", Hint(!string.IsNullOrEmpty(c.ApiKeyProtected)));
            Row("Mathpix app_id", mpId); Row("Mathpix app_key", mpKey); Row("", Hint(!string.IsNullOrEmpty(c.MathpixKeyProtected)));
            Row("", help);
            var ok = new Button { Content = L.T("确定"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            ok.Click += (s, e) =>
            {
                c.Engine = (FormulaEngine)Math.Max(0, eng.SelectedIndex);
                c.ApiBase = string.IsNullOrWhiteSpace(apiBase.Text) ? null : apiBase.Text.Trim();
                c.ApiModel = string.IsNullOrWhiteSpace(apiModel.Text) ? null : apiModel.Text.Trim();
                c.MathpixAppId = string.IsNullOrWhiteSpace(mpId.Text) ? null : mpId.Text.Trim();
                if (!string.IsNullOrEmpty(apiKey.Password)) c.ApiKeyProtected = Secrets.Protect(apiKey.Password.Trim());
                if (!string.IsNullOrEmpty(mpKey.Password)) c.MathpixKeyProtected = Secrets.Protect(mpKey.Password.Trim());
                App.Settings.Save(); DialogResult = true;
            };
            var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 14) };
            bp.Children.Add(ok); bp.Children.Add(cancel);
            var sp = new StackPanel(); sp.Children.Add(g); sp.Children.Add(bp); Content = sp;
        }
    }
}
