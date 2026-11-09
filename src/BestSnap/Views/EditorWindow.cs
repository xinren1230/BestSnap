using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using BestSnap.Models;
using BestSnap.Plugins;
using BestSnap.Services;
using BestSnap.ViewModels;

namespace BestSnap.Views
{
    public enum EditorTool { Select, Arrow, Line, Rectangle, Ellipse, Text, Highlight, Step, Pen, Spotlight, Pixelate, Blur, Crop, Eyedropper, Eraser }

    /// <summary>Draws the editable annotations, the in-progress draft and the selection frame above the bitmap.</summary>
    internal class AnnotationLayer : FrameworkElement
    {
        public EditorDocument Doc;
        public Annotation Draft, Selected, WatermarkPreview;
        public Rect? DraftRect;
        public Point? BrushCursor;
        public double BrushRadius;
        public double Zoom = 1;
        public AnnotationLayer() { IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext dc)
        {
            if (Doc == null) return;
            try
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, Doc.Image.PixelWidth, Doc.Image.PixelHeight)));
                var items = WatermarkPreview != null ? Doc.Annotations.Where(a => a.Kind != AnnotationKind.Watermark) : Doc.Annotations;
                Annotation.RenderAll(dc, items, new Size(Doc.Image.PixelWidth, Doc.Image.PixelHeight), Draft, Doc.Image, drawBase: true);
                WatermarkPreview?.Render(dc);
                dc.Pop();
                double t = 1 / Math.Max(0.1, Zoom);
                if (DraftRect.HasValue) dc.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, t) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, DraftRect.Value);
                if (BrushCursor is Point c && BrushRadius > 0.5)
                {
                    dc.DrawEllipse(null, new Pen(Brushes.White, t * 2), c, BrushRadius, BrushRadius);
                    dc.DrawEllipse(null, new Pen(Brushes.Black, t), c, BrushRadius, BrushRadius);
                }
                if (Selected != null)
                {
                    var r = Selected.Bounds; r.Inflate(3 * t, 3 * t);
                    dc.DrawRectangle(null, new Pen(Brushes.White, 3 * t), r);
                    dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, t) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, r);
                }
            }
            catch (Exception ex) { Logger.Error("Editor.Render", ex); }
        }
    }

    /// <summary>
    /// Image editor. Arrows/lines/shapes/text/highlight are editable objects (select, move, recolor, delete) until export;
    /// crop/resize/pixelate/blur change the base bitmap. Every edit is a command so undo/redo covers both.
    /// </summary>
    public partial class EditorWindow : Window
    {
        private readonly EditorDocument _doc;
        private readonly SettingsService _settings;
        private readonly Canvas _canvas = new Canvas();
        private readonly Image _image = new Image { Stretch = Stretch.None };
        private readonly AnnotationLayer _layer = new AnnotationLayer();
        private readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
        private readonly TextBlock _status = new TextBlock { Margin = new Thickness(6, 2, 6, 2) };
        private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = new Dictionary<EditorTool, ToggleButton>();
        private EditorTool _tool = EditorTool.Arrow;
        private Color _color = Colors.Red;
        private double _thickness = 3;
        private double _eraserSize = 16;
        private Button _colorBtn, _delBtn;
        private ComboBox _thickBox, _eraseFillBox;
        private Slider _eraserSlider;
        private TextBlock _styleLabel, _eraserSizeText;
        private Button _eraseCustomBtn;
        private ToggleButton _eraseObjectBtn, _eraseRectBtn;
        private enum EraseFillMode { Auto, White, Black, Custom, Transparent }
        private EraseFillMode _eraseFill = EraseFillMode.Auto;
        private Color _eraseCustom = Colors.White;
        private bool _eraseObject, _eraseRect;
        private bool _syncingStyle;
        private ComboBox _fontBox, _fontSizeBox;
        private ToggleButton _textBg;
        private Border _styleHost, _wmHost;
        private ToggleButton _wmBtn;
        private bool _wmOpen;
        private bool _erasing;
        private readonly HashSet<Annotation> _erasedStroke = new HashSet<Annotation>();
        private readonly Dictionary<EditorTool, MenuItem> _toolMenuItems = new Dictionary<EditorTool, MenuItem>();
        private string _fontFamily = "Microsoft YaHei UI";
        private double _fontSize; // 0 = auto (from thickness)
        private bool _textBackground;
        private EditorTool _toolBeforeEyedropper = EditorTool.Arrow;
        private EditorTool _toolBeforeWm = EditorTool.Arrow;
        private bool _wmDragging, _wmDragIsPreview;
        private Annotation _wmDragTarget;
        private Point _wmDragOrigin;
        private Vector _wmGrab;
        private Point _start;
        private bool _dragging, _moving, _processing, _releasing;
        private Vector _moveDelta;

        public EditorWindow(BitmapSource img, SettingsService settings)
        {
            _doc = new EditorDocument(img); _settings = settings;
            Title = L.T("BestSnap 编辑器");
            double workW = SystemParameters.WorkArea.Width, workH = SystemParameters.WorkArea.Height;
            MinWidth = 760; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = Math.Min(workW * 0.92, Math.Max(760, Math.Min(1200, img.PixelWidth + 80)));
            Height = Math.Min(workH * 0.9, Math.Min(900, img.PixelHeight + 160));
            Background = new SolidColorBrush(Color.FromRgb(249, 249, 251)); FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, Microsoft YaHei UI");

            _image.SetBinding(Image.SourceProperty, new Binding(nameof(EditorDocument.Image)) { Source = _doc });
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
            _layer.Doc = _doc;
            _canvas.Children.Add(_image); _canvas.Children.Add(_layer);
            foreach (var fe in new FrameworkElement[] { _canvas, _layer })
            {
                fe.SetBinding(WidthProperty, new Binding("Image.PixelWidth") { Source = _doc });
                fe.SetBinding(HeightProperty, new Binding("Image.PixelHeight") { Source = _doc });
            }
            _image.Opacity = 0; _image.IsHitTestVisible = false;
            _canvas.Background = CheckerboardBrush(); _canvas.ClipToBounds = true;
            _canvas.LayoutTransform = _zoom;
            _canvas.MouseLeftButtonDown += OnDown; _canvas.MouseMove += OnMove; _canvas.MouseLeftButtonUp += OnUp;
            _canvas.MouseLeave += (s, e) => { if (_layer.BrushCursor != null) { _layer.BrushCursor = null; _layer.InvalidateVisual(); } };
            _canvas.LostMouseCapture += (s, e) => ReleasePointer();

            var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(60, 60, 64)),
                Content = new Border { Child = _canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) } };
            scroll.PreviewMouseWheel += (s, e) => { if (Keyboard.Modifiers == ModifierKeys.Control) { SetZoom(_zoom.ScaleX * (e.Delta > 0 ? 1.25 : 0.8)); e.Handled = true; } };

            var dock = new DockPanel();
            var bar = BuildToolbar();
            DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
            var sb = new StatusBar(); sb.Items.Add(_status); DockPanel.SetDock(sb, Dock.Bottom); dock.Children.Add(sb);
            dock.Children.Add(scroll);
            Content = dock;

            _doc.PropertyChanged += (s, e) => { UpdateStatus(); _layer.InvalidateVisual(); };
            _doc.AnnotationsChanged += () => { if (_layer.Selected != null && !_doc.Annotations.Contains(_layer.Selected)) { _layer.Selected = null; UpdateStyleBar(); } _layer.InvalidateVisual(); };
            UpdateStatus(); SelectTool(EditorTool.Arrow);

            bool NotTyping() => !IsTypingFocus();
            InputBindings.Add(new KeyBinding(new Relay(Undo, NotTyping), Key.Z, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Relay(Redo, NotTyping), Key.Y, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Relay(Copy, NotTyping), Key.C, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Relay(SaveAs, NotTyping), Key.S, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Relay(() => SetZoom(_zoom.ScaleX * 1.25), NotTyping), Key.OemPlus, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Relay(() => SetZoom(_zoom.ScaleX * 0.8), NotTyping), Key.OemMinus, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(new Relay(() => SetZoom(1), NotTyping), Key.D0, ModifierKeys.Control));
            PreviewKeyDown += OnPreviewKey;
            AddHandler(AccessKeyManager.AccessKeyPressedEvent, new AccessKeyPressedEventHandler((s, e) =>
            {
                if (!IsTypingFocus()) return;
                e.Handled = true;
                e.Target = null;
            }), true);
            Deactivated += (s, e) => ReleasePointer();
            Closed += (s, e) => ReleasePointer();
        }

        private UIElement BuildToolbar()
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemHeight = 36 };
            var seps = new List<FrameworkElement>();

            Button B(string glyph, string text, Action a, string tip = null, bool accent = false)
            {
                var b = SmartUi.Fluent(glyph, text, tip ?? text, a, dark: false, accent: accent);
                wrap.Children.Add(b);
                return b;
            }
            void Sep()
            {
                var d = (FrameworkElement)SmartUi.Divider(dark: false);
                wrap.Children.Add(d);
                seps.Add(d);
            }
            void Tool(EditorTool t, string glyph, string label, string tip = null)
            {
                var tbn = SmartUi.FluentToggle(glyph, label, tip ?? label, dark: false);
                tbn.Click += (s, e) => SelectTool(t);
                _toolButtons[t] = tbn; wrap.Children.Add(tbn);
            }

            B(SmartUi.GCopy, L.T("复制"), Copy, L.T("复制到剪贴板 (Ctrl+C)"));
            B(SmartUi.GSave, L.T("保存"), SaveAs, L.T("另存为 (Ctrl+S)"));
            B(SmartUi.GSaveAs, L.T("自动保存"), QuickSave, L.T("按文件名模板保存"));
            Sep();
            B(SmartUi.GUndo, L.T("撤销"), Undo, "Ctrl+Z");
            B(SmartUi.GRedo, L.T("重做"), Redo, "Ctrl+Y");
            Sep();
            Tool(EditorTool.Arrow, SmartUi.GArrow, L.T("箭头"));
            Tool(EditorTool.Rectangle, SmartUi.GRect, L.T("矩形"));
            Tool(EditorTool.Text, SmartUi.GText, L.T("文字"));
            Tool(EditorTool.Pixelate, SmartUi.GBlur, L.T("马赛克"), L.T("模糊/打码"));
            Tool(EditorTool.Eraser, SmartUi.GEraser, L.T("擦除"), L.T("拖动画笔覆盖截图像素 (E)"));
            Tool(EditorTool.Crop, SmartUi.GCrop, L.T("裁剪"));
            Sep();
            B(SmartUi.GOcr, "OCR", () => OcrRunner.Run(_doc.Render(), this), L.T("识别图片中的文字（内置中英文 OCR）"), accent: true);
            B(SmartUi.GFormula, L.T("公式LaTeX"), () => FormulaWindow.Run(_doc.Render(), this), L.T("公式转LaTeX"), accent: true);
            _wmBtn = SmartUi.FluentToggle(SmartUi.GWatermark, L.T("水印"), L.T("加水印"), dark: false);
            _wmBtn.Click += (s, e) => ToggleWatermarkPanel();
            wrap.Children.Add(_wmBtn);
            Sep();

            var more = SmartUi.Fluent(SmartUi.GMore, L.T("更多"), L.T("更多工具"), () => { }, dark: false);
            var menu = new ContextMenu();
            void MenuTool(EditorTool t, string label, string tip = null)
            {
                var mi = new MenuItem { Header = label, ToolTip = tip ?? (object)label, IsCheckable = true };
                mi.Click += (s, e) => SelectTool(t);
                _toolMenuItems[t] = mi; menu.Items.Add(mi);
            }
            void MenuAct(string label, Action a, string tip = null)
            {
                var mi = new MenuItem { Header = label, ToolTip = tip ?? (object)label };
                mi.Click += (s, e) => a();
                menu.Items.Add(mi);
            }
            MenuTool(EditorTool.Select, L.T("选择"), L.T("选择/移动标注；Delete 删除；双击文字/序号可修改；选中后改颜色/线宽/字体即生效"));
            MenuTool(EditorTool.Line, L.T("直线"));
            MenuTool(EditorTool.Ellipse, L.T("椭圆"));
            MenuTool(EditorTool.Highlight, L.T("高亮"));
            MenuTool(EditorTool.Step, L.T("序号"), L.T("单击放置自动递增的步骤序号"));
            MenuTool(EditorTool.Pen, L.T("画笔"), L.T("自由手绘"));
            MenuTool(EditorTool.Spotlight, L.T("聚光灯"), L.T("框选要突出的区域，其余部分变暗"));
            MenuTool(EditorTool.Blur, L.T("模糊"));
            MenuTool(EditorTool.Eyedropper, L.T("取色"), L.T("从图片取颜色作为当前颜色（并复制色值）"));
            menu.Items.Add(new Separator());
            MenuAct(L.T("翻译"), () => OcrRunner.Run(_doc.Render(), this, "auto"), L.T("识别文字并翻译（中↔英）"));
            MenuAct(L.T("自动打码"), AutoRedactNow, L.T("自动识别邮箱、电话、证件号、网址和人脸并打马赛克（可编辑/撤销，需要 OCR 插件）"));
            MenuAct(L.T("二维码"), () => CodeReader.Run(_doc.Render(), this), L.T("识别图片中的二维码/条码"));
            menu.Items.Add(new Separator());
            MenuAct(L.T("钉到桌面"), () => new PinWindow(_doc.Render()).Show(), L.T("把当前图片钉在桌面最前面"));
            var up = new MenuItem { Header = L.T("上传/分享"), ToolTip = L.T("上传/分享到已配置的目标，完成后复制链接") };
            up.Click += (s, e) => { var m = UploadTargetsWindow.BuildUploadMenu(MakeUploadFile, t => _status.Text = t); m.PlacementTarget = more; m.IsOpen = true; };
            menu.Items.Add(up);
            MenuAct(L.T("调整大小"), ResizeImage);
            MenuAct(L.T("放大"), () => SetZoom(_zoom.ScaleX * 1.25));
            MenuAct(L.T("缩小"), () => SetZoom(_zoom.ScaleX * 0.8));
            MenuAct("100%", () => SetZoom(1));
            more.Click += (s, e) => { menu.PlacementTarget = more; menu.IsOpen = true; };
            wrap.Children.Add(more);

            bool fixingSeps = false;
            wrap.SizeChanged += (s, e) =>
            {
                if (fixingSeps || wrap.ActualWidth <= 1) return;
                fixingSeps = true;
                try
                {
                    foreach (var d in seps) d.Visibility = Visibility.Visible;
                    wrap.UpdateLayout();
                    foreach (var d in seps)
                    {
                        int i = wrap.Children.IndexOf(d);
                        if (i <= 0) continue;
                        var prev = (UIElement)wrap.Children[i - 1];
                        double y0 = prev.TranslatePoint(new Point(0, 0), wrap).Y;
                        double y1 = d.TranslatePoint(new Point(0, 0), wrap).Y;
                        if (y1 - y0 > 8) d.Visibility = Visibility.Collapsed;
                    }
                }
                finally { fixingSeps = false; }
            };

            var style = new WrapPanel { Orientation = Orientation.Horizontal };
            _styleLabel = new TextBlock { Text = L.T("颜色和线宽"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0), Foreground = Brushes.Gray, FontSize = 11 };
            style.Children.Add(_styleLabel);
            _colorBtn = new Button { Width = 28, Height = 28, Background = new SolidColorBrush(_color), ToolTip = L.T("颜色（单击切换）"), Margin = new Thickness(2), BorderThickness = new Thickness(0), Style = SmartUi.ButtonStyle(false) };
            var palette = new[] { Colors.Red, Colors.Orange, Colors.Gold, Colors.LimeGreen, Colors.DodgerBlue, Colors.MediumPurple, Colors.Black, Colors.White };
            int ci = 0;
            _colorBtn.Click += (s, e) => { ci = (ci + 1) % palette.Length; _color = palette[ci]; _colorBtn.Background = new SolidColorBrush(_color); ApplyStyleToSelection(); };
            style.Children.Add(_colorBtn);
            _thickBox = new ComboBox { Width = 50, ItemsSource = new double[] { 1, 2, 3, 5, 8, 12 }, SelectedItem = 3.0, ToolTip = L.T("线宽 / 字号"), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            _thickBox.SelectionChanged += (s, e) => { if (_thickBox.SelectedItem is double d) { _thickness = d; ApplyStyleToSelection(); } };
            style.Children.Add(_thickBox);
            _eraserSlider = new Slider { Width = 120, Minimum = 4, Maximum = 64, Value = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), ToolTip = L.T("擦除大小"), Visibility = Visibility.Collapsed };
            _eraserSlider.ValueChanged += (s, e) => { _eraserSize = _eraserSlider.Value; if (_eraserSizeText != null) _eraserSizeText.Text = ((int)_eraserSize).ToString(); _layer.BrushRadius = _eraserSize / 2; _layer.InvalidateVisual(); };
            style.Children.Add(_eraserSlider);
            _eraserSizeText = new TextBlock { Text = "16", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = Brushes.Gray, FontSize = 11, Visibility = Visibility.Collapsed, MinWidth = 18 };
            style.Children.Add(_eraserSizeText);
            _eraseFillBox = new ComboBox
            {
                Width = 110, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("擦除填充"),
                ItemsSource = new[] { L.T("背景色自动"), L.T("白"), L.T("黑"), L.T("自定义"), L.T("透明") }, SelectedIndex = 0, Visibility = Visibility.Collapsed
            };
            _eraseFillBox.SelectionChanged += (s, e) =>
            {
                _eraseFill = (EraseFillMode)Math.Clamp(_eraseFillBox.SelectedIndex, 0, 4);
                if (_eraseCustomBtn != null) _eraseCustomBtn.Visibility = (_tool == EditorTool.Eraser && !_eraseObject && _eraseFill == EraseFillMode.Custom) ? Visibility.Visible : Visibility.Collapsed;
            };
            style.Children.Add(_eraseFillBox);
            _eraseCustomBtn = new Button { Width = 28, Height = 28, Background = new SolidColorBrush(_eraseCustom), ToolTip = L.T("自定义"), Margin = new Thickness(2), BorderThickness = new Thickness(0), Style = SmartUi.ButtonStyle(false), Visibility = Visibility.Collapsed };
            var erasePalette = new[] { Colors.White, Color.FromRgb(240, 240, 240), Colors.LightGray, Color.FromRgb(32, 32, 32), Colors.Black };
            int ei = 0;
            _eraseCustomBtn.Click += (s, e) => { ei = (ei + 1) % erasePalette.Length; _eraseCustom = erasePalette[ei]; _eraseCustomBtn.Background = new SolidColorBrush(_eraseCustom); };
            style.Children.Add(_eraseCustomBtn);
            _eraseRectBtn = SmartUi.FluentToggle(SmartUi.GRect, L.T("矩形擦除"), L.T("拖出矩形覆盖截图像素"), dark: false);
            _eraseRectBtn.Visibility = Visibility.Collapsed;
            _eraseRectBtn.Click += (s, e) => { _eraseRect = _eraseRectBtn.IsChecked == true; if (_eraseRect) { _eraseObject = false; _eraseObjectBtn.IsChecked = false; } UpdateStyleBar(); UpdateEraserCursor(); };
            style.Children.Add(_eraseRectBtn);
            _eraseObjectBtn = SmartUi.FluentToggle(SmartUi.GEraser, L.T("擦除标注"), L.T("单击或拖过标注即可删除 (E)"), dark: false);
            _eraseObjectBtn.Visibility = Visibility.Collapsed;
            _eraseObjectBtn.Click += (s, e) => { _eraseObject = _eraseObjectBtn.IsChecked == true; if (_eraseObject) { _eraseRect = false; _eraseRectBtn.IsChecked = false; } UpdateStyleBar(); UpdateEraserCursor(); };
            style.Children.Add(_eraseObjectBtn);
            _delBtn = SmartUi.Fluent("\uE74D", L.T("删除"), L.T("删除选中的标注 (Delete)"), DeleteSelected, dark: false);
            style.Children.Add(_delBtn);
            _fontBox = new ComboBox { Width = 120, ToolTip = L.T("字体"), ItemsSource = new[] { "Microsoft YaHei UI", "SimSun", "SimHei", "KaiTi", "Segoe UI", "Arial", "Consolas", "Times New Roman" }, SelectedItem = _fontFamily, IsEditable = true, Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            _fontBox.SelectionChanged += (s, e) => { if (_fontBox.SelectedItem is string f) { _fontFamily = f; ApplyTextStyleToSelection(); } };
            _fontBox.LostFocus += (s, e) => { if (!string.IsNullOrWhiteSpace(_fontBox.Text) && _fontBox.Text != _fontFamily) { _fontFamily = _fontBox.Text; ApplyTextStyleToSelection(); } };
            style.Children.Add(_fontBox);
            _fontSizeBox = new ComboBox { Width = 56, ToolTip = L.T("字号（自动 = 跟随线宽）"), ItemsSource = new object[] { L.T("自动"), 10.0, 12.0, 14.0, 16.0, 20.0, 24.0, 32.0, 48.0, 72.0 }, SelectedIndex = 0, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            _fontSizeBox.SelectionChanged += (s, e) => { _fontSize = _fontSizeBox.SelectedItem is double d ? d : 0; ApplyTextStyleToSelection(); };
            style.Children.Add(_fontSizeBox);
            _textBg = SmartUi.FluentToggle("\uE8B1", L.T("底色"), L.T("文字加填充底色（颜色 = 当前颜色）"), dark: false);
            _textBg.Click += (s, e) => { _textBackground = _textBg.IsChecked == true; ApplyTextStyleToSelection(); };
            style.Children.Add(_textBg);

            _styleHost = new Border
            {
                Child = style, Visibility = Visibility.Collapsed,
                Background = SmartUi.LightBarBackground,
                BorderBrush = SmartUi.LightBarBorder,
                BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 4, 8, 4)
            };

            var chrome = new Border
            {
                Child = wrap, Background = SmartUi.LightBarBackground, BorderBrush = SmartUi.LightBarBorder,
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 4, 6, 4)
            };
            _wmHost = BuildWatermarkBar();
            var box = new DockPanel();
            DockPanel.SetDock(_wmHost, Dock.Bottom);
            DockPanel.SetDock(_styleHost, Dock.Bottom);
            box.Children.Add(_wmHost);
            box.Children.Add(_styleHost);
            box.Children.Add(chrome);
            return box;
        }

        private static bool IsDrawingTool(EditorTool t) =>
            t == EditorTool.Arrow || t == EditorTool.Line || t == EditorTool.Rectangle || t == EditorTool.Ellipse
            || t == EditorTool.Text || t == EditorTool.Highlight || t == EditorTool.Step || t == EditorTool.Pen
            || t == EditorTool.Spotlight || t == EditorTool.Pixelate || t == EditorTool.Blur;

        private void UpdateStyleBar()
        {
            if (_styleHost == null) return;
            if (_wmOpen) { _styleHost.Visibility = Visibility.Collapsed; return; }
            bool eraser = _tool == EditorTool.Eraser;
            bool show = eraser || IsDrawingTool(_tool) || (_tool == EditorTool.Select && _layer.Selected != null);
            _styleHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (_styleLabel != null) _styleLabel.Text = eraser ? L.T("擦除大小") : L.T("颜色和线宽");
            var drawVis = eraser ? Visibility.Collapsed : Visibility.Visible;
            if (_colorBtn != null) _colorBtn.Visibility = drawVis;
            if (_thickBox != null) _thickBox.Visibility = drawVis;
            if (_delBtn != null) _delBtn.Visibility = drawVis;
            bool pixel = eraser && !_eraseObject;
            var ev = (bool on) => on ? Visibility.Visible : Visibility.Collapsed;
            if (_eraserSlider != null) _eraserSlider.Visibility = ev(eraser && (_eraseObject || !_eraseRect));
            if (_eraserSizeText != null) _eraserSizeText.Visibility = ev(eraser && (_eraseObject || !_eraseRect));
            if (_eraseFillBox != null) _eraseFillBox.Visibility = ev(pixel);
            if (_eraseCustomBtn != null) _eraseCustomBtn.Visibility = ev(pixel && _eraseFill == EraseFillMode.Custom);
            if (_eraseRectBtn != null) _eraseRectBtn.Visibility = ev(eraser && !_eraseObject);
            if (_eraseObjectBtn != null) _eraseObjectBtn.Visibility = ev(eraser);
            bool text = !eraser && (_tool == EditorTool.Text || _layer.Selected?.Kind == AnnotationKind.Text);
            var vis = text ? Visibility.Visible : Visibility.Collapsed;
            if (_fontBox != null) _fontBox.Visibility = vis;
            if (_fontSizeBox != null) _fontSizeBox.Visibility = vis;
            if (_textBg != null) _textBg.Visibility = vis;
        }

        /// <summary>Detects sensitive text/faces and adds pixelate (Redact) annotations as one undoable step.</summary>
        public async void AutoRedactNow()
        {
            if (!AutoRedact.Available)
            {
                MessageBox.Show(this, L.T("自动打码需要 OCR 插件（plugins\\BestSnap.Ocr）。"), AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _status.Text = L.T("正在检测敏感信息…");
            try
            {
                var hits = await AutoRedact.DetectAsync(_doc.Image);
                var cmds = hits.Select(h => (IEditCommand)new AddAnnotationCommand(new Annotation
                {
                    Kind = AnnotationKind.Redact, A = h.Rect.TopLeft, B = h.Rect.BottomRight, Thickness = Math.Max(3, _thickness), Color = Colors.Gray
                })).ToList();
                if (cmds.Count > 0) _doc.Execute(new CompositeCommand(cmds));
                _status.Text = cmds.Count == 0 ? L.T("未发现需要打码的内容") : string.Format(L.T("已自动打码 {0} 处（可选中移动/删除，Ctrl+Z 撤销）"), cmds.Count);
            }
            catch (Exception ex) { Logger.Error("AutoRedact", ex); _status.Text = L.T("自动打码失败") + ": " + ex.Message; }
        }

        public void UseTool(EditorTool t) => SelectTool(t);

        private void SelectTool(EditorTool t)
        {
            if (t != EditorTool.Select) CloseWatermarkPanel(true, restoreTool: false);
            if (t == EditorTool.Eyedropper && _tool != EditorTool.Eyedropper) _toolBeforeEyedropper = _tool;
            _tool = t;
            foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == t;
            foreach (var kv in _toolMenuItems) kv.Value.IsChecked = kv.Key == t;
            if (t != EditorTool.Select) Select(null);
            UpdateStyleBar();
            UpdateEraserCursor();
            UpdateStatus();
        }

        private void UpdateEraserCursor()
        {
            if (_tool != EditorTool.Eraser)
            {
                _layer.BrushCursor = null; _layer.BrushRadius = 0;
                _canvas.Cursor = _tool == EditorTool.Select ? Cursors.Arrow : _tool == EditorTool.Text ? Cursors.IBeam : Cursors.Cross;
                _layer.InvalidateVisual();
                return;
            }
            bool circle = !_eraseRect || _eraseObject;
            _canvas.Cursor = circle ? Cursors.None : Cursors.Cross;
            _layer.BrushRadius = circle ? _eraserSize / 2 : 0;
            if (!circle) _layer.BrushCursor = null;
            _layer.InvalidateVisual();
        }

        private Color ResolveEraseFill(Point p)
        {
            switch (_eraseFill)
            {
                case EraseFillMode.White: return Colors.White;
                case EraseFillMode.Black: return Colors.Black;
                case EraseFillMode.Custom: return _eraseCustom;
                case EraseFillMode.Transparent: return Color.FromArgb(0, 0, 0, 0);
                default: return SampleSurrounding(p, _eraserSize / 2);
            }
        }

        private Color SampleSurrounding(Point p, double radius)
        {
            var img = _doc.Image;
            if (img == null) return Colors.White;
            int w = img.PixelWidth, h = img.PixelHeight;
            int cx = (int)Math.Clamp(p.X, 0, w - 1), cy = (int)Math.Clamp(p.Y, 0, h - 1);
            int inner = Math.Max(1, (int)(radius + 1)), outer = inner + 8;
            var pts = EraserMath.RingSamples(cx, cy, inner, outer, w, h, 2);
            if (pts.Count == 0)
            {
                pts = new List<(int, int)> { (0, 0), (Math.Max(0, w - 1), 0), (0, Math.Max(0, h - 1)), (Math.Max(0, w - 1), Math.Max(0, h - 1)) };
            }
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0;
            foreach (var (x, y) in pts) { if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
            int rw = x1 - x0 + 1, rh = y1 - y0 + 1;
            if (rw < 1 || rh < 1) return Colors.White;
            var buf = new byte[rw * rh * 4];
            try { img.CopyPixels(new Int32Rect(x0, y0, rw, rh), buf, rw * 4, 0); }
            catch { return Colors.White; }
            var samples = new List<(byte, byte, byte)>(pts.Count);
            foreach (var (x, y) in pts)
            {
                int i = ((y - y0) * rw + (x - x0)) * 4;
                if (i >= 0 && i + 2 < buf.Length) samples.Add((buf[i + 2], buf[i + 1], buf[i]));
            }
            var m = EraserMath.MedianRgb(samples);
            return Color.FromRgb(m.R, m.G, m.B);
        }

        private static Brush CheckerboardBrush()
        {
            var g = new GeometryGroup();
            g.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
            g.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
            g.Freeze();
            var d = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(200, 200, 204)), null, g); d.Freeze();
            var bg = new GeometryDrawing(new SolidColorBrush(Color.FromRgb(232, 232, 236)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))); bg.Freeze();
            var dg = new DrawingGroup(); dg.Children.Add(bg); dg.Children.Add(d); dg.Freeze();
            var b = new DrawingBrush(dg) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
            b.Freeze();
            return b;
        }

        private void Select(Annotation a)
        {
            _layer.Selected = a; _layer.InvalidateVisual();
            if (a == null) { UpdateStyleBar(); return; }
            _syncingStyle = true;
            _color = a.Color; _colorBtn.Background = new SolidColorBrush(_color);
            _thickness = a.Thickness; _thickBox.SelectedItem = (_thickBox.ItemsSource as double[]).Contains(a.Thickness) ? (object)a.Thickness : null;
            if (a.Kind == AnnotationKind.Text)
            {
                _fontFamily = a.FontFamilyName; _fontBox.Text = a.FontFamilyName;
                _fontSize = a.FontSizeOverride; _fontSizeBox.SelectedItem = a.FontSizeOverride > 0 ? (object)a.FontSizeOverride : L.T("自动");
                _textBackground = a.TextBackground; _textBg.IsChecked = a.TextBackground;
            }
            _syncingStyle = false;
            UpdateStyleBar();
        }

        private void ApplyStyleToSelection()
        {
            var a = _layer.Selected;
            if (_syncingStyle || a == null || (a.Color == _color && a.Thickness == _thickness)) return;
            _doc.Execute(new StyleAnnotationCommand(a, _color, _thickness, a.Text));
        }

        private void ApplyTextStyleToSelection()
        {
            var a = _layer.Selected;
            if (_syncingStyle || a == null || a.Kind != AnnotationKind.Text) return;
            if (a.FontFamilyName == _fontFamily && a.FontSizeOverride == _fontSize && a.TextBackground == _textBackground) return;
            string f = _fontFamily; double sz = _fontSize; bool bg = _textBackground;
            _doc.Execute(new StyleAnnotationCommand(a, x => { x.FontFamilyName = f; x.FontSizeOverride = sz; x.TextBackground = bg; }));
        }

        private int NextStepNumber() => _doc.Annotations.Where(x => x.Kind == AnnotationKind.Step).Select(x => x.Number).DefaultIfEmpty(0).Max() + 1;

        private string MakeUploadFile()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BestSnapUpload"); System.IO.Directory.CreateDirectory(dir);
            var s = _settings.Current;
            var name = ImageExporter.ExpandTemplate(s, commit: true) + "." + (string.IsNullOrEmpty(s.DefaultFormat) || s.DefaultFormat == "pdf" ? "png" : s.DefaultFormat);
            var f = System.IO.Path.Combine(dir, name);
            ImageExporter.Save(ExportImage(), f, s.JpegQuality); _settings.Save();
            return f;
        }

        private void PickColor(Point p)
        {
            var img = _doc.Render();
            int x = (int)p.X, y = (int)p.Y;
            if (x < 0 || y < 0 || x >= img.PixelWidth || y >= img.PixelHeight) return;
            var px = new byte[4]; img.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
            _color = Color.FromRgb(px[2], px[1], px[0]); _colorBtn.Background = new SolidColorBrush(_color);
            var hex = $"#{px[2]:X2}{px[1]:X2}{px[0]:X2}";
            try { Clipboard.SetText(hex); } catch { }
            SelectTool(_toolBeforeEyedropper);
            _status.Text = L.T("已取色并复制") + ": " + hex + $"  RGB({px[2]}, {px[1]}, {px[0]})";
        }

        private void DeleteSelected() { if (_layer.Selected != null) _doc.Execute(new RemoveAnnotationCommand(_layer.Selected)); }
        private void Undo() { if (!_processing) { CancelDraft(); _doc.Undo(); } }
        private void Redo() { if (!_processing) { CancelDraft(); _doc.Redo(); } }

        /// <summary>True when keyboard focus is in a control that needs the keystrokes (text, combo, watermark/style bars, dialogs).</summary>
        bool IsTypingFocus(object original = null)
        {
            if (WalkTyping(Keyboard.FocusedElement as DependencyObject)) return true;
            if (WalkTyping(original as DependencyObject)) return true;
            if (WalkTyping(FocusManager.GetFocusedElement(this) as DependencyObject)) return true;
            return false;
        }

        bool WalkTyping(DependencyObject start)
        {
            for (var d = start; d != null; d = InputParent(d))
            {
                if (ReferenceEquals(d, _wmHost) || ReferenceEquals(d, _styleHost)) return true;
                if (EditorKeys.IsTypingTypeName(d.GetType().Name)) return true;
                if (d is ComboBox) return true;
            }
            return false;
        }

        static DependencyObject InputParent(DependencyObject d)
        {
            if (d is Visual)
            {
                var v = VisualTreeHelper.GetParent(d);
                if (v != null) return v;
            }
            return LogicalTreeHelper.GetParent(d);
        }

        void OnPreviewKey(object s, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool typing = IsTypingFocus(e.OriginalSource);
            bool mods = Keyboard.Modifiers != ModifierKeys.None;
            if (EditorKeys.AllowLetterShortcut(typing || _wmOpen, mods, key.ToString()))
            {
                SelectTool(EditorTool.Eraser);
                e.Handled = true;
                return;
            }
            if (typing) return;
            if (key == Key.Escape) { StopErase(); CancelDraft(); Select(null); ReleasePointer(); }
            else if (key == Key.Delete) DeleteSelected();
            else if (_layer.Selected != null && (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down))
            {
                double st = Keyboard.Modifiers == ModifierKeys.Shift ? 10 : 1;
                var v = key == Key.Left ? new Vector(-st, 0) : key == Key.Right ? new Vector(st, 0) : key == Key.Up ? new Vector(0, -st) : new Vector(0, st);
                _doc.Execute(new MoveAnnotationCommand(_layer.Selected, v)); e.Handled = true;
            }
        }

        void ReleasePointer()
        {
            if (_releasing) return;
            _releasing = true;
            try
            {
                if (_wmDragging) HandleWatermarkUp();
                if (_moving)
                {
                    _moving = false;
                    var a = _layer.Selected;
                    if (a != null && _moveDelta.Length > 0.5) { a.Offset(-_moveDelta); _doc.Execute(new MoveAnnotationCommand(a, _moveDelta)); }
                    _moveDelta = new Vector();
                }
                StopErase();
                if (_dragging) { _dragging = false; CancelDraft(); }
                if (_canvas.IsMouseCaptured) _canvas.ReleaseMouseCapture();
                Mouse.Capture(null);
            }
            catch (Exception ex) { Logger.Error("Editor.ReleasePointer", ex); }
            finally { _releasing = false; }
        }

        private void SetZoom(double z) { z = Math.Clamp(z, 0.1, 16); _zoom.ScaleX = _zoom.ScaleY = z; _layer.Zoom = z; _layer.InvalidateVisual(); UpdateStatus(); }
        private void UpdateStatus() => _status.Text = string.Format(L.T("{0} 像素    缩放 {1:P0}    工具: {2}    标注: {3}"), _doc.SizeText, _zoom.ScaleX, _tool, _doc.Annotations.Count);

        private AnnotationKind? KindFor(EditorTool t) => t switch
        {
            EditorTool.Arrow => AnnotationKind.Arrow, EditorTool.Line => AnnotationKind.Line, EditorTool.Rectangle => AnnotationKind.Rectangle,
            EditorTool.Ellipse => AnnotationKind.Ellipse, EditorTool.Highlight => AnnotationKind.Highlight,
            EditorTool.Pen => AnnotationKind.Pen, EditorTool.Spotlight => AnnotationKind.Spotlight, _ => (AnnotationKind?)null
        };

        private Annotation HitAnnotation(Point p)
        {
            double tol = 4 / _zoom.ScaleX;
            for (int i = _doc.Annotations.Count - 1; i >= 0; i--) if (_doc.Annotations[i].HitTest(p, tol)) return _doc.Annotations[i];
            return null;
        }

        private void StopErase()
        {
            if (!_erasing) return;
            _erasing = false;
            _erasedStroke.Clear();
            if (_canvas.IsMouseCaptured) _canvas.ReleaseMouseCapture();
        }

        private void EraseAt(Point p)
        {
            var items = _doc.Annotations;
            if (items.Count == 0) return;
            double radius = Math.Max(1, _eraserSize * 0.5);
            var idx = EraserMath.SweepHits(new[] { (p.X, p.Y) }, items.Count, (i, x, y) => !items[i].IsErase && items[i].HitTest(new Point(x, y), radius));
            var hit = new List<Annotation>(idx.Count);
            for (int k = 0; k < idx.Count; k++) hit.Add(items[idx[k]]);
            foreach (var a in hit)
            {
                if (!_erasedStroke.Add(a)) continue;
                if (_layer.Selected == a) Select(null);
                _doc.Execute(new RemoveAnnotationCommand(a));
            }
        }

        // ---------- mouse ----------
        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_processing) return;
            _start = e.GetPosition(_canvas);
            if (HandleWatermarkDown(_start)) { e.Handled = true; return; }
            if (_tool == EditorTool.Select)
            {
                var hit = HitAnnotation(_start);
                Select(hit);
                if (hit == null) return;
                if (e.ClickCount == 2 && hit.Kind == AnnotationKind.Text)
                {
                    var t = InputDialog.Show(this, L.T("修改文字"), L.T("文字内容:"), hit.Text);
                    if (!string.IsNullOrEmpty(t) && t != hit.Text) _doc.Execute(new StyleAnnotationCommand(hit, hit.Color, hit.Thickness, t));
                    return;
                }
                if (e.ClickCount == 2 && hit.Kind == AnnotationKind.Step)
                {
                    var t = InputDialog.Show(this, L.T("修改序号"), L.T("序号:"), hit.Number.ToString());
                    if (int.TryParse(t, out var n) && n != hit.Number) _doc.Execute(new StyleAnnotationCommand(hit, x => x.Number = n));
                    return;
                }
                _moving = true; _moveDelta = new Vector(); _canvas.CaptureMouse();
                return;
            }
            if (_tool == EditorTool.Text) { AddText(_start); return; }
            if (_tool == EditorTool.Eyedropper) { PickColor(_start); return; }
            if (_tool == EditorTool.Eraser)
            {
                if (_eraseObject)
                {
                    _erasing = true; _erasedStroke.Clear(); _canvas.CaptureMouse();
                    EraseAt(_start);
                    return;
                }
                _dragging = true; _canvas.CaptureMouse();
                var fill = ResolveEraseFill(_start);
                if (_eraseRect)
                    _layer.Draft = new Annotation { Kind = AnnotationKind.EraseRect, A = _start, B = _start, Color = fill, Thickness = 1 };
                else
                {
                    _layer.Draft = new Annotation { Kind = AnnotationKind.EraseStroke, A = _start, B = _start, Color = fill, Thickness = Math.Max(1, _eraserSize) };
                    _layer.Draft.Points.Add(_start);
                }
                _layer.InvalidateVisual();
                return;
            }
            if (_tool == EditorTool.Step)
            {
                _doc.Execute(new AddAnnotationCommand(new Annotation { Kind = AnnotationKind.Step, A = _start, B = _start, Color = _color, Thickness = _thickness, Number = NextStepNumber() }));
                return;
            }
            _dragging = true; _canvas.CaptureMouse();
            var k = KindFor(_tool);
            if (k.HasValue) { _layer.Draft = new Annotation { Kind = k.Value, A = _start, B = _start, Color = _color, Thickness = _thickness }; if (k == AnnotationKind.Pen) _layer.Draft.Points.Add(_start); }
            else _layer.DraftRect = new Rect(_start, _start);
            _layer.InvalidateVisual();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (HandleWatermarkMove(p)) return;
            _status.Text = string.Format(L.T("{0} 像素    缩放 {1:P0}    ({2}, {3})"), _doc.SizeText, _zoom.ScaleX, (int)p.X, (int)p.Y);
            if (_tool == EditorTool.Eraser && (_eraseObject || !_eraseRect))
            {
                _layer.BrushCursor = p; _layer.BrushRadius = _eraserSize / 2; _layer.InvalidateVisual();
            }
            if (_moving && _layer.Selected != null)
            {
                var d = (p - _start) - _moveDelta; _layer.Selected.Offset(d); _moveDelta += d; _layer.InvalidateVisual();
            }
            else if (_erasing) EraseAt(p);
            else if (_dragging)
            {
                if (_layer.Draft != null)
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift && (_tool == EditorTool.Line || _tool == EditorTool.Arrow)) p = Snap45(_start, p);
                    if (_layer.Draft.Kind == AnnotationKind.Pen || _layer.Draft.Kind == AnnotationKind.EraseStroke)
                    { if ((p - _layer.Draft.Points[_layer.Draft.Points.Count - 1]).Length >= 1.5) _layer.Draft.Points.Add(p); }
                    _layer.Draft.B = p;
                }
                else _layer.DraftRect = new Rect(_start, p);
                _layer.InvalidateVisual();
            }
        }

        private static Point Snap45(Point a, Point b)
        {
            var v = b - a; double ang = Math.Round(Math.Atan2(v.Y, v.X) / (Math.PI / 4)) * (Math.PI / 4);
            return a + new Vector(Math.Cos(ang), Math.Sin(ang)) * v.Length;
        }

        private async void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (HandleWatermarkUp()) return;
            if (_moving)
            {
                _moving = false; _canvas.ReleaseMouseCapture();
                var a = _layer.Selected;
                if (a != null && _moveDelta.Length > 0.5) { a.Offset(-_moveDelta); _doc.Execute(new MoveAnnotationCommand(a, _moveDelta)); }
                return;
            }
            if (_erasing) { StopErase(); return; }
            if (!_dragging) return;
            _dragging = false; _canvas.ReleaseMouseCapture();
            var end = e.GetPosition(_canvas);
            var draft = _layer.Draft; CancelDraft();
            bool stamp = draft != null && (draft.Kind == AnnotationKind.Pen || draft.Kind == AnnotationKind.EraseStroke) && draft.Points.Count >= 1;
            if ((end - _start).Length < 2 && !stamp) return;
            if (draft != null)
            {
                if (draft.Kind == AnnotationKind.Spotlight || draft.Kind == AnnotationKind.Rectangle || draft.Kind == AnnotationKind.Ellipse || draft.Kind == AnnotationKind.Highlight || draft.Kind == AnnotationKind.EraseRect)
                { var rr = new Rect(draft.A, draft.B); draft.A = rr.TopLeft; draft.B = rr.BottomRight; } // normalise
                if (draft.Kind == AnnotationKind.EraseRect && (Math.Abs(draft.B.X - draft.A.X) < 1 || Math.Abs(draft.B.Y - draft.A.Y) < 1)) return;
                _doc.Execute(new AddAnnotationCommand(draft)); return;
            }

            var rect = new Rect(_start, end);
            var ir = CaptureService.Clip(new Int32Rect((int)rect.X, (int)rect.Y, (int)Math.Round(rect.Width), (int)Math.Round(rect.Height)), _doc.Image.PixelWidth, _doc.Image.PixelHeight);
            if (ir.Width <= 1 || ir.Height <= 1) return;
            var src = _doc.Image;
            switch (_tool)
            {
                case EditorTool.Crop: _doc.Execute(new RasterCommand(src, CaptureService.Crop(src, ir), new Vector(ir.X, ir.Y), 1)); break;
                case EditorTool.Pixelate: int block = Math.Max(6, (int)_thickness * 3); await RunEffect(() => Effects.Pixelate(src, ir, block)); break;
                case EditorTool.Blur: await RunEffect(() => Effects.Blur(src, ir, 8)); break;
            }
        }

        /// <summary>Pixel effects run on a worker thread (source bitmaps are frozen) so the UI stays responsive.</summary>
        private async System.Threading.Tasks.Task RunEffect(Func<BitmapSource> f)
        {
            _processing = true; Cursor = Cursors.Wait; _status.Text = L.T("处理中…");
            try { var r = await System.Threading.Tasks.Task.Run(f); _doc.Apply(r); }
            finally { _processing = false; Cursor = null; UpdateStatus(); }
        }

        private void CancelDraft() { _layer.Draft = null; _layer.DraftRect = null; _dragging = false; _layer.InvalidateVisual(); }

        private void AddText(Point p)
        {
            var text = InputDialog.Show(this, L.T("输入文字"), L.T("文字内容:"), "");
            if (string.IsNullOrEmpty(text)) return;
            var a = new Annotation { Kind = AnnotationKind.Text, A = p, B = p, Color = _color, Thickness = _thickness, Text = text, FontFamilyName = _fontFamily, FontSizeOverride = _fontSize, TextBackground = _textBackground };
            _doc.Execute(new AddAnnotationCommand(a));
        }

        private void ResizeImage()
        {
            var s = InputDialog.Show(this, L.T("调整大小"), L.T("新宽度(像素) 或 百分比(如 50%):"), _doc.Image.PixelWidth.ToString());
            if (string.IsNullOrWhiteSpace(s)) return;
            double scale;
            if (s.Trim().EndsWith("%") && double.TryParse(s.Trim().TrimEnd('%'), out var pct)) scale = pct / 100.0;
            else if (int.TryParse(s, out var nw) && nw > 0) scale = (double)nw / _doc.Image.PixelWidth;
            else return;
            if (scale <= 0 || scale > 10) return;
            var tb = new TransformedBitmap(_doc.Image, new ScaleTransform(scale, scale));
            _doc.Execute(new RasterCommand(_doc.Image, CaptureService.Normalize(tb), new Vector(0, 0), scale));
        }

        private BitmapSource ExportImage()
        {
            try
            {
                var img = _doc.Render();
                if (_doc.Annotations.Exists(a => a.Kind == AnnotationKind.Watermark)) return img;
                return Watermark.ApplyIfNeeded(img, _settings.Current);
            }
            catch (Exception ex) { Logger.Error("Editor.Export", ex); return _doc.Image; }
        }

        private void Copy()
        {
            try { ImageExporter.CopyToClipboard(ExportImage()); _status.Text = L.T("已复制到剪贴板"); }
            catch (Exception ex) { Logger.Error("Editor.Copy", ex); _status.Text = ex.Message; }
        }

        private void QuickSave()
        {
            var path = ImageExporter.AutoSave(ExportImage(), _settings.Current); _settings.Save();
            _doc.IsDirty = false; _status.Text = L.T("已保存: ") + path;
        }

        private void SaveAs()
        {
            var s = _settings.Current;
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = ImageExporter.SaveFilter, FileName = ImageExporter.ExpandTemplate(s, commit: false), InitialDirectory = System.IO.Directory.Exists(s.SaveFolder) ? s.SaveFolder : null };
            if (dlg.ShowDialog(this) != true) return;
            ImageExporter.Save(ExportImage(), dlg.FileName, s.JpegQuality);
            ImageExporter.CommitCounter(s);
            _settings.Save(); _doc.IsDirty = false; _status.Text = L.T("已保存: ") + dlg.FileName;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            ReleasePointer();
            PersistWatermarkSettings();
            if (_doc.IsDirty && MessageBox.Show(this, L.T("图片已修改，确定关闭不保存吗？"), AppIdentity.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
            base.OnClosing(e);
        }
    }

    public class Relay : ICommand
    {
        private readonly Action _a;
        private readonly Func<bool> _can;
        public Relay(Action a, Func<bool> can = null) { _a = a; _can = can; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object p) => _can == null || _can();
        public void Execute(object p) => _a();
    }
}
