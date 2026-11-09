using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Compare two images: side-by-side (synced scroll), overlay with opacity slider, or difference highlight.</summary>
    public class CompareWindow : Window
    {
        private BitmapSource _a, _b;
        private readonly TextBlock _nameA = new TextBlock { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _nameB = new TextBlock { Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ComboBox _mode = new ComboBox { Width = 130, Margin = new Thickness(0, 0, 10, 0) };
        private readonly Slider _opacity = new Slider { Minimum = 0, Maximum = 1, Value = 0.5, Width = 160, VerticalAlignment = VerticalAlignment.Center };
        private readonly Slider _tol = new Slider { Minimum = 0, Maximum = 64, Value = 8, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _fit = new CheckBox { Content = L.T("适应窗口"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        private readonly TextBlock _status = new TextBlock { Margin = new Thickness(8, 4, 8, 4) };
        private readonly ContentControl _host = new ContentControl();
        private readonly StackPanel _opPanel = new StackPanel { Orientation = Orientation.Horizontal }, _tolPanel = new StackPanel { Orientation = Orientation.Horizontal };

        public CompareWindow(BitmapSource a = null, BitmapSource b = null)
        {
            Title = L.T("图片对比") + " - " + AppIdentity.Name; Width = 1100; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _a = a; _b = b; if (a != null) _nameA.Text = L.T("当前图片");
            var bar = new WrapPanel { Margin = new Thickness(8) };
            Button Pick(string t, bool first)
            {
                var btn = new Button { Content = t, Padding = new Thickness(8, 3, 8, 3) };
                btn.Click += (s, e) =>
                {
                    var m = new ContextMenu();
                    var f = new MenuItem { Header = L.T("从文件…") }; f.Click += (o, ee) => Set(first, ImagePicker.FromFiles(false));
                    var h = new MenuItem { Header = L.T("从截图历史…") }; h.Click += (o, ee) => Set(first, ImagePicker.FromHistory(this, false));
                    var c = new MenuItem { Header = L.T("从剪贴板") }; c.Click += (o, ee) => { if (Clipboard.ContainsImage()) Set(first, new System.Collections.Generic.List<ImagePicker.Entry> { ImagePicker.Make(CaptureService.Normalize(Clipboard.GetImage()), L.T("剪贴板")) }); };
                    m.Items.Add(f); m.Items.Add(h); m.Items.Add(c); m.PlacementTarget = btn; m.IsOpen = true;
                };
                return btn;
            }
            bar.Children.Add(Pick(L.T("图片 A ▾"), true)); bar.Children.Add(_nameA);
            bar.Children.Add(Pick(L.T("图片 B ▾"), false)); bar.Children.Add(_nameB);
            var swap = new Button { Content = "A ⇄ B", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 12, 0) };
            swap.Click += (s, e) => { (_a, _b) = (_b, _a); var t = _nameA.Text; _nameA.Text = _nameB.Text; _nameB.Text = t; Refresh(); };
            bar.Children.Add(swap);
            _mode.Items.Add(L.T("并排")); _mode.Items.Add(L.T("叠加")); _mode.Items.Add(L.T("差异高亮")); _mode.SelectedIndex = 0;
            _mode.SelectionChanged += (s, e) => Refresh();
            bar.Children.Add(_mode);
            _opPanel.Children.Add(new TextBlock { Text = L.T("B 的不透明度") + " ", VerticalAlignment = VerticalAlignment.Center }); _opPanel.Children.Add(_opacity);
            _tolPanel.Children.Add(new TextBlock { Text = L.T("容差") + " ", VerticalAlignment = VerticalAlignment.Center }); _tolPanel.Children.Add(_tol);
            bar.Children.Add(_opPanel); bar.Children.Add(_tolPanel); bar.Children.Add(_fit);
            _tol.ValueChanged += (s, e) => { if (_mode.SelectedIndex == 2) Refresh(); };
            _fit.Click += (s, e) => Refresh();
            var dock = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
            DockPanel.SetDock(_status, Dock.Bottom); dock.Children.Add(_status);
            dock.Children.Add(_host);
            Content = dock;
            Loaded += (s, e) => Refresh();
        }

        private void Set(bool first, System.Collections.Generic.List<ImagePicker.Entry> l)
        {
            if (l.Count == 0) return;
            if (first) { _a = l[0].Image; _nameA.Text = l[0].Name; } else { _b = l[0].Image; _nameB.Text = l[0].Name; }
            Refresh();
        }

        private FrameworkElement View(UIElement content, ScrollViewer sync = null)
        {
            if (_fit.IsChecked == true) return new Viewbox { Child = content, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            var sv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
            if (sync != null)
            {
                sv.ScrollChanged += (s, e) => { sync.ScrollToVerticalOffset(sv.VerticalOffset); sync.ScrollToHorizontalOffset(sv.HorizontalOffset); };
                sync.ScrollChanged += (s, e) => { sv.ScrollToVerticalOffset(sync.VerticalOffset); sv.ScrollToHorizontalOffset(sync.HorizontalOffset); };
            }
            return sv;
        }

        private static Image Img(BitmapSource b) => new Image { Source = b, Width = b.PixelWidth, Height = b.PixelHeight, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };

        private void Refresh()
        {
            int mode = _mode.SelectedIndex;
            _opPanel.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
            _tolPanel.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (_a == null || _b == null)
            {
                _host.Content = new TextBlock { Text = L.T("请选择图片 A 和图片 B。"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, FontSize = 16 };
                _status.Text = ""; return;
            }
            _status.Text = $"A: {_a.PixelWidth}×{_a.PixelHeight}    B: {_b.PixelWidth}×{_b.PixelHeight}";
            switch (mode)
            {
                case 0:
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                    var left = View(Img(_a));
                    var right = View(Img(_b), left as ScrollViewer);
                    g.Children.Add(left); Grid.SetColumn(right, 2); g.Children.Add(right);
                    var split = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.LightGray }; Grid.SetColumn(split, 1); g.Children.Add(split);
                    _host.Content = g; break;
                case 1:
                    var canvas = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                    canvas.Children.Add(Img(_a));
                    var top = Img(_b); top.SetBinding(OpacityProperty, new System.Windows.Data.Binding("Value") { Source = _opacity });
                    canvas.Children.Add(top);
                    _host.Content = View(canvas); break;
                default:
                    var d = ImageOps.Difference(_a, _b, (int)_tol.Value, out long n, out var bounds);
                    var cg = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                    cg.Children.Add(Img(d));
                    if (!bounds.IsEmpty)
                        cg.Children.Add(new System.Windows.Shapes.Rectangle { Stroke = Brushes.Red, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 2 }, Width = bounds.Width + 4, Height = bounds.Height + 4,
                            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(bounds.X - 2, bounds.Y - 2, 0, 0) });
                    _host.Content = View(cg);
                    long total = (long)Math.Max(_a.PixelWidth, _b.PixelWidth) * Math.Max(_a.PixelHeight, _b.PixelHeight);
                    _status.Text += "    " + (n == 0 ? L.T("完全相同") : string.Format(L.T("不同像素 {0} ({1:P2})，范围 {2},{3} {4}×{5}"), n, (double)n / total, bounds.X, bounds.Y, bounds.Width, bounds.Height));
                    break;
            }
        }
    }
}
