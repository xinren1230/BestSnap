using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BestSnap.Models;
using BestSnap.Plugins;
using BestSnap.Services;
using BestSnap.ViewModels;

namespace BestSnap.Views
{
    public partial class EditorWindow
    {
        ToggleButton _wmTextBtn, _wmImageBtn, _wmTiledBtn;
        ToggleButton[] _wmAnchors;
        TextBox _wmTextBox;
        ComboBox _wmFontBox, _wmSizeBox, _wmImgHBox, _wmColorBox;
        Button _wmBrowse;
        Slider _wmOpacity, _wmRot, _wmMargin, _wmSpacing;
        TextBlock _wmPathLabel, _wmOpVal, _wmRotVal, _wmMarginVal, _wmSpacingVal;
        StackPanel _wmTextRow, _wmImageRow, _wmGridRow, _wmTileRow;
        bool _wmSync, _wmAuto = true;
        Color _wmColor = Color.FromRgb(0x80, 0x80, 0x80);
        readonly string[] _wmAnchorTips = { "左上", "上中", "右上", "左中", "居中", "右中", "左下", "下中", "右下" };

        sealed class WmColorChoice
        {
            public string Key, Hex; public bool Auto;
            public override string ToString() => L.T(Key);
        }

        readonly WmColorChoice[] _wmColors =
        {
            new WmColorChoice { Key = "自动", Auto = true },
            new WmColorChoice { Key = "灰色", Hex = "#808080" },
            new WmColorChoice { Key = "白色", Hex = "#FFFFFF" },
            new WmColorChoice { Key = "黑色", Hex = "#222222" },
            new WmColorChoice { Key = "红色", Hex = "#E53935" },
            new WmColorChoice { Key = "蓝色", Hex = "#1E88E5" },
            new WmColorChoice { Key = "金色", Hex = "#F9A825" },
        };

        Border BuildWatermarkBar()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            FrameworkElement Gap() => new Border { Width = 8 };
            void Add(UIElement e) => wrap.Children.Add(e);

            _wmTextBtn = SmartUi.FluentToggle(SmartUi.GText, L.T("文字"), L.T("文字水印"), dark: false);
            _wmImageBtn = SmartUi.FluentToggle(SmartUi.GWatermark, L.T("图片"), L.T("图片水印"), dark: false);
            _wmTextBtn.Click += (s, e) => { if (_wmSync) return; SetWmImage(false); };
            _wmImageBtn.Click += (s, e) => { if (_wmSync) return; SetWmImage(true); };
            Add(_wmTextBtn); Add(_wmImageBtn); Add(Gap());

            _wmTextRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _wmTextBox = new TextBox { Width = 140, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("文字内容") };
            _wmTextBox.TextChanged += (s, e) => RefreshWatermarkPreview();
            _wmFontBox = new ComboBox { Width = 110, ItemsSource = new[] { "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "Segoe UI", "Arial", "Consolas" }, IsEditable = true, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("字体") };
            _wmFontBox.SelectionChanged += (s, e) => RefreshWatermarkPreview();
            _wmFontBox.LostFocus += (s, e) => RefreshWatermarkPreview();
            _wmSizeBox = new ComboBox { Width = 56, ItemsSource = new double[] { 16, 20, 24, 28, 32, 40, 48, 64 }, SelectedItem = 28.0, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("字号") };
            _wmSizeBox.SelectionChanged += (s, e) => RefreshWatermarkPreview();
            _wmColorBox = new ComboBox { Width = 72, ItemsSource = _wmColors, SelectedIndex = 0, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("自动") };
            _wmColorBox.SelectionChanged += (s, e) =>
            {
                if (_wmSync || _wmColorBox.SelectedItem is not WmColorChoice c) return;
                _wmAuto = c.Auto;
                if (!c.Auto) _wmColor = Watermark.ParseColor(c.Hex, 100);
                RefreshWatermarkPreview();
            };
            _wmTextRow.Children.Add(_wmTextBox); _wmTextRow.Children.Add(_wmFontBox); _wmTextRow.Children.Add(_wmSizeBox); _wmTextRow.Children.Add(_wmColorBox);
            Add(_wmTextRow);

            _wmImageRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _wmBrowse = SmartUi.Fluent("\uE8E5", L.T("浏览…"), L.T("选择水印图片"), PickWatermarkImage, dark: false);
            _wmPathLabel = new TextBlock { MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Foreground = Brushes.Gray, FontSize = 11 };
            _wmImgHBox = new ComboBox { Width = 56, ItemsSource = new double[] { 24, 32, 48, 64, 96, 128 }, SelectedItem = 48.0, Margin = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("大小") };
            _wmImgHBox.SelectionChanged += (s, e) => RefreshWatermarkPreview();
            _wmImageRow.Children.Add(_wmBrowse); _wmImageRow.Children.Add(_wmPathLabel); _wmImageRow.Children.Add(_wmImgHBox);
            Add(_wmImageRow);

            Add(new TextBlock { Text = L.T("透明度"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), Foreground = Brushes.Gray, FontSize = 11 });
            _wmOpacity = SmartUi.Lock(new Slider { Width = 90, Minimum = 0, Maximum = 100, Value = 35, VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("透明度") });
            _wmOpVal = new TextBlock { Width = 28, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.Gray, Text = "35%" };
            _wmOpacity.ValueChanged += (s, e) => { _wmOpVal.Text = ((int)_wmOpacity.Value).ToString() + "%"; RefreshWatermarkPreview(); };
            Add(_wmOpacity); Add(_wmOpVal);

            Add(new TextBlock { Text = L.T("旋转"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), Foreground = Brushes.Gray, FontSize = 11 });
            _wmRot = SmartUi.Lock(new Slider { Width = 90, Minimum = -90, Maximum = 90, Value = -30, VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("旋转") });
            _wmRotVal = new TextBlock { Width = 40, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.Gray, Text = "-30°" };
            _wmRot.ValueChanged += (s, e) => { _wmRotVal.Text = ((int)_wmRot.Value).ToString() + "°"; RefreshWatermarkPreview(); };
            Add(_wmRot); Add(_wmRotVal);

            _wmGridRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 66, Height = 66, Margin = new Thickness(2), ToolTip = L.T("九宫格位置") };
            _wmAnchors = new ToggleButton[9];
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var b = new ToggleButton { Width = 20, Height = 20, Margin = new Thickness(1), ToolTip = L.T(_wmAnchorTips[i]), Focusable = false };
                b.Click += (s, e) => { if (_wmSync) return; SetWmTiled(false); SetWmAnchor(idx); };
                _wmAnchors[i] = b; grid.Children.Add(b);
            }
            _wmGridRow.Children.Add(grid);
            _wmGridRow.Children.Add(new TextBlock { Text = L.T("边距"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), Foreground = Brushes.Gray, FontSize = 11 });
            _wmMargin = SmartUi.Lock(new Slider { Width = 70, Minimum = 0, Maximum = 80, Value = 16, VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("边距") });
            _wmMarginVal = new TextBlock { Width = 28, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.Gray, Text = "16" };
            _wmMargin.ValueChanged += (s, e) => { _wmMarginVal.Text = ((int)_wmMargin.Value).ToString(); RefreshWatermarkPreview(); };
            _wmGridRow.Children.Add(_wmMargin); _wmGridRow.Children.Add(_wmMarginVal);
            Add(_wmGridRow);

            _wmTiledBtn = SmartUi.FluentToggle("\uE8A9", L.T("平铺"), L.T("平铺"), dark: false);
            _wmTiledBtn.Click += (s, e) => { if (_wmSync) return; SetWmTiled(_wmTiledBtn.IsChecked == true); };
            Add(_wmTiledBtn);
            _wmTileRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _wmTileRow.Children.Add(new TextBlock { Text = L.T("间距"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Foreground = Brushes.Gray, FontSize = 11 });
            _wmSpacing = SmartUi.Lock(new Slider { Width = 80, Minimum = 8, Maximum = 200, Value = 64, VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("间距") });
            _wmSpacingVal = new TextBlock { Width = 32, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.Gray, Text = "64" };
            _wmSpacing.ValueChanged += (s, e) => { _wmSpacingVal.Text = ((int)_wmSpacing.Value).ToString(); RefreshWatermarkPreview(); };
            _wmTileRow.Children.Add(_wmSpacing); _wmTileRow.Children.Add(_wmSpacingVal);
            Add(_wmTileRow);

            Add(SmartUi.Fluent(SmartUi.GEdit, L.T("应用水印"), L.T("应用水印"), ApplyWatermark, dark: false, accent: true));

            return new Border
            {
                Child = wrap, Visibility = Visibility.Collapsed,
                Background = SmartUi.LightBarBackground, BorderBrush = SmartUi.LightBarBorder,
                BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 4, 8, 4)
            };
        }

        void ToggleWatermarkPanel()
        {
            if (_wmBtn?.IsChecked == true) OpenWatermarkPanel();
            else CloseWatermarkPanel(true);
        }

        void OpenWatermarkPanel()
        {
            _wmOpen = true;
            if (_wmBtn != null) _wmBtn.IsChecked = true;
            _toolBeforeWm = _tool;
            if (_tool != EditorTool.Select) SelectTool(EditorTool.Select);
            LoadWatermarkPanel();
            if (_wmHost != null) _wmHost.Visibility = Visibility.Visible;
            UpdateStyleBar();
            RefreshWatermarkPreview();
            _status.Text = Wm().Tiled ? L.T("平铺水印不可拖动") : L.T("拖动以移动水印");
        }

        void CloseWatermarkPanel(bool persist, bool restoreTool = true)
        {
            if (!_wmOpen) return;
            WriteWatermarkFromPanel();
            if (persist) _settings.Save();
            _wmOpen = false;
            if (_wmBtn != null) _wmBtn.IsChecked = false;
            if (_wmHost != null) _wmHost.Visibility = Visibility.Collapsed;
            _layer.WatermarkPreview = null;
            _layer.InvalidateVisual();
            UpdateStyleBar();
            if (restoreTool && _toolBeforeWm != EditorTool.Select && _tool == EditorTool.Select)
                SelectTool(_toolBeforeWm);
            else UpdateEraserCursor();
        }

        void SetWmImage(bool image)
        {
            var w = Wm(); w.IsImage = image;
            _wmSync = true;
            _wmTextBtn.IsChecked = !image; _wmImageBtn.IsChecked = image;
            _wmSync = false;
            _wmTextRow.Visibility = image ? Visibility.Collapsed : Visibility.Visible;
            _wmImageRow.Visibility = image ? Visibility.Visible : Visibility.Collapsed;
            RefreshWatermarkPreview();
        }

        void SetWmTiled(bool tiled)
        {
            var w = Wm();
            w.Tiled = tiled;
            if (tiled && Math.Abs(w.Rotation) < 0.5) { w.Rotation = -30; _wmSync = true; _wmRot.Value = -30; _wmSync = false; }
            _wmSync = true;
            _wmTiledBtn.IsChecked = tiled;
            _wmSync = false;
            _wmTileRow.Visibility = tiled ? Visibility.Visible : Visibility.Collapsed;
            _wmGridRow.Opacity = tiled ? 0.45 : 1;
            _canvas.Cursor = tiled ? Cursors.Arrow : Cursors.SizeAll;
            if (!tiled) HighlightAnchor(w.Anchor);
            RefreshWatermarkPreview();
            if (_wmOpen) _status.Text = tiled ? L.T("平铺水印不可拖动") : L.T("拖动以移动水印");
        }

        void HighlightAnchor(int i)
        {
            if (_wmAnchors == null) return;
            _wmSync = true;
            for (int k = 0; k < 9; k++) _wmAnchors[k].IsChecked = k == i;
            _wmSync = false;
        }

        void SetWmAnchor(int i)
        {
            var w = Wm();
            w.Anchor = Math.Clamp(i, 0, 8);
            w.PosX = w.PosY = null;
            HighlightAnchor(w.Anchor);
            RefreshWatermarkPreview();
        }

        WatermarkSettings Wm()
        {
            var s = _settings.Current;
            return s.Watermark ?? (s.Watermark = new WatermarkSettings());
        }

        void LoadWatermarkPanel()
        {
            var w = Wm();
            _wmSync = true;
            _wmTextBox.Text = string.IsNullOrEmpty(w.Text) ? AppIdentity.Name : w.Text;
            _wmFontBox.Text = string.IsNullOrEmpty(w.FontFamily) ? "Microsoft YaHei UI" : w.FontFamily;
            _wmSizeBox.SelectedItem = Nearest(new[] { 16.0, 20, 24, 28, 32, 40, 48, 64 }, w.FontSize, 28);
            _wmAuto = Watermark.IsAuto(w.Color);
            if (_wmAuto) _wmColorBox.SelectedIndex = 0;
            else
            {
                int idx = 1;
                for (int i = 1; i < _wmColors.Length; i++)
                    if (string.Equals(_wmColors[i].Hex, w.Color, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                _wmColorBox.SelectedIndex = idx;
                _wmColor = Watermark.ParseColor(w.Color, 100);
            }
            _wmOpacity.Value = Math.Clamp(w.Opacity, 0, 100);
            _wmRot.Value = Math.Clamp(w.Rotation, -90, 90);
            _wmMargin.Value = Math.Clamp(w.Margin, 0, 80);
            _wmSpacing.Value = Math.Clamp(w.Spacing, 8, 200);
            _wmOpVal.Text = ((int)_wmOpacity.Value).ToString() + "%";
            _wmRotVal.Text = ((int)_wmRot.Value).ToString() + "°";
            _wmMarginVal.Text = ((int)_wmMargin.Value).ToString();
            _wmSpacingVal.Text = ((int)_wmSpacing.Value).ToString();
            _wmImgHBox.SelectedItem = Nearest(new[] { 24.0, 32, 48, 64, 96, 128 }, w.ImageHeight, 48);
            _wmPathLabel.Text = string.IsNullOrEmpty(w.ImagePath) ? "" : System.IO.Path.GetFileName(w.ImagePath);
            _wmPathLabel.ToolTip = w.ImagePath;
            _wmSync = false;
            SetWmImage(w.IsImage);
            SetWmTiled(w.Tiled);
            HighlightAnchor(w.Anchor);
        }

        static double Nearest(double[] choices, double v, double fallback)
        {
            double best = fallback, d = double.MaxValue;
            foreach (var c in choices) { double x = Math.Abs(c - v); if (x < d) { d = x; best = c; } }
            return best;
        }

        void PersistWatermarkSettings()
        {
            if (_wmTextBox == null) return;
            WriteWatermarkFromPanel();
            _settings.Save();
        }

        void WriteWatermarkFromPanel()
        {
            if (_wmTextBox == null) return;
            var w = Wm();
            w.Text = _wmTextBox.Text;
            w.FontFamily = string.IsNullOrWhiteSpace(_wmFontBox.Text) ? "Microsoft YaHei UI" : _wmFontBox.Text;
            w.FontSize = _wmSizeBox.SelectedItem is double fs ? fs : 28;
            w.Color = _wmAuto ? "auto" : Watermark.ToHex(_wmColor);
            w.Opacity = (int)_wmOpacity.Value;
            w.Rotation = _wmRot.Value;
            w.Margin = _wmMargin.Value;
            w.Spacing = _wmSpacing.Value;
            w.ImageHeight = _wmImgHBox.SelectedItem is double ih ? ih : 48;
            w.IsImage = _wmImageBtn.IsChecked == true;
            w.Tiled = _wmTiledBtn.IsChecked == true;
        }

        void RefreshWatermarkPreview()
        {
            if (_wmSync || !_wmOpen || _wmDragging) return;
            try
            {
                WriteWatermarkFromPanel();
                _layer.WatermarkPreview = Watermark.Build(Wm(), _doc.Image.PixelWidth, _doc.Image.PixelHeight, _doc.Image);
                _layer.InvalidateVisual();
            }
            catch (Exception ex) { Logger.Error("Watermark.Preview", ex); }
        }

        void PickWatermarkImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("PNG 图片") + "|*.png", Title = L.T("选择水印图片") };
            if (dlg.ShowDialog(this) != true) return;
            Wm().ImagePath = dlg.FileName;
            _wmPathLabel.Text = System.IO.Path.GetFileName(dlg.FileName);
            _wmPathLabel.ToolTip = dlg.FileName;
            SetWmImage(true);
        }

        void ApplyWatermark()
        {
            try
            {
                PersistWatermarkSettings();
                var a = Watermark.Build(Wm(), _doc.Image.PixelWidth, _doc.Image.PixelHeight, _doc.Image);
                if (a == null)
                {
                    _status.Text = L.T("请选择水印图片");
                    return;
                }
                var cmds = new System.Collections.Generic.List<IEditCommand>();
                foreach (var old in _doc.Annotations) if (old.Kind == AnnotationKind.Watermark) cmds.Add(new RemoveAnnotationCommand(old));
                cmds.Add(new AddAnnotationCommand(a));
                _doc.Execute(new CompositeCommand(cmds));
                _layer.WatermarkPreview = null;
                _layer.InvalidateVisual();
                _status.Text = L.T("已加水印");
            }
            catch (Exception ex)
            {
                Logger.Error("Watermark.Apply", ex);
                _status.Text = ex.Message;
            }
        }

        bool HandleWatermarkDown(Point p)
        {
            bool tiled = Wm().Tiled;
            if (_wmOpen && tiled)
            {
                _status.Text = L.T("平铺水印不可拖动");
                _canvas.Cursor = Cursors.No;
                return true;
            }
            Annotation target = null;
            bool preview = false;
            if (_wmOpen && _layer.WatermarkPreview != null && _layer.WatermarkPreview.Points.Count <= 1)
            {
                target = _layer.WatermarkPreview;
                preview = true;
            }
            if (target == null)
            {
                var hit = HitAnnotation(p);
                if (hit != null && hit.Kind == AnnotationKind.Watermark)
                {
                    if (hit.Points.Count > 1)
                    {
                        if (_wmOpen) { _status.Text = L.T("平铺水印不可拖动"); return true; }
                        return false;
                    }
                    target = hit;
                }
            }
            if (target == null) return _wmOpen;
            if (target.Points.Count == 0) return _wmOpen;
            _wmDragging = true;
            _wmDragIsPreview = preview;
            _wmDragTarget = target;
            _wmDragOrigin = target.Points[0];
            _wmGrab = p - target.Points[0];
            _canvas.CaptureMouse();
            _canvas.Cursor = Cursors.SizeAll;
            return true;
        }

        bool HandleWatermarkMove(Point p)
        {
            if (_wmDragging && _wmDragTarget != null && _wmDragTarget.Points.Count > 0)
            {
                double iw = Math.Max(1, _wmDragTarget.A.X), ih = Math.Max(1, _wmDragTarget.A.Y);
                var raw = p - _wmGrab;
                var clamped = WatermarkMath.ClampPos(_doc.Image.PixelWidth, _doc.Image.PixelHeight, iw, ih, raw.X, raw.Y, _wmDragTarget.Rotation);
                _wmDragTarget.Points[0] = new Point(clamped.X, clamped.Y);
                var w = Wm();
                w.PosX = clamped.X; w.PosY = clamped.Y;
                HighlightAnchor(-1);
                _layer.InvalidateVisual();
                _canvas.Cursor = Cursors.SizeAll;
                return true;
            }
            if (_wmOpen && Wm().Tiled)
            {
                _canvas.Cursor = Cursors.No;
                return false;
            }
            if (_wmOpen && !Wm().Tiled)
            {
                _canvas.Cursor = Cursors.SizeAll;
                return false;
            }
            var hit = HitAnnotation(p);
            if (hit != null && hit.Kind == AnnotationKind.Watermark && hit.Points.Count <= 1)
                _canvas.Cursor = Cursors.SizeAll;
            return false;
        }

        bool HandleWatermarkUp()
        {
            if (!_wmDragging) return false;
            _wmDragging = false;
            if (_canvas.IsMouseCaptured) _canvas.ReleaseMouseCapture();
            var t = _wmDragTarget;
            _wmDragTarget = null;
            if (t == null || t.Points.Count == 0) return true;
            var end = t.Points[0];
            var d = end - _wmDragOrigin;
            var w = Wm();
            w.PosX = end.X; w.PosY = end.Y;
            if (!_wmDragIsPreview && d.Length > 0.5)
            {
                t.Points[0] = _wmDragOrigin;
                _doc.Execute(new MoveAnnotationCommand(t, d));
            }
            if (_wmDragIsPreview && _wmAuto) RefreshWatermarkPreview();
            if (d.Length > 0.5) _settings.Save();
            _canvas.Cursor = (_wmOpen && w.Tiled) ? Cursors.No : Cursors.SizeAll;
            return true;
        }
    }
}
