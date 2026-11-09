using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>
    /// Image pinned on top of the desktop. Drag to move, wheel = zoom, Ctrl+wheel = opacity,
    /// double-click / Esc = close, right-click = menu. Shown at 1 image pixel = 1 physical pixel.
    /// </summary>
    public class PinWindow : Window
    {
        private readonly BitmapSource _img;
        private readonly Image _view = new Image { Stretch = Stretch.Fill };
        private double _zoom = 1;
        private Int32Rect? _placeAt; // physical virtual-screen pixels

        public PinWindow(BitmapSource img, Int32Rect? physicalRect = null)
        {
            _img = img; _placeAt = physicalRect;
            Title = L.T("钉图"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight; WindowStartupLocation = WindowStartupLocation.Manual;
            _view.Source = img; RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);
            Content = new Border { Child = _view, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(124, 58, 237)) };
            ToolTip = L.T("拖动移动 · 滚轮缩放 · Ctrl+滚轮透明度 · 双击/Esc 关闭 · 右键菜单");

            SourceInitialized += (s, e) =>
            {
                ApplyZoom();
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int x, y;
                if (_placeAt.HasValue) { x = _placeAt.Value.X - 1; y = _placeAt.Value.Y - 1; }
                else { Native.NativeMethods.GetCursorPos(out var p); x = p.X - 20; y = p.Y - 20; }
                Native.NativeMethods.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.NativeMethods.SWP_NOZORDER | 0x0001 /*NOSIZE*/);
            };
            DpiChanged += (s, e) => ApplyZoom();
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { Close(); return; }
                try { DragMove(); } catch { }
            };
            MouseWheel += (s, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control) Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.2, 1);
                else { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.1, 8); ApplyZoom(); }
            };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
                else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) ImageExporter.CopyToClipboard(_img);
            };
            ContextMenu = BuildMenu();
        }

        private void ApplyZoom()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            _view.Width = _img.PixelWidth * _zoom / dpi.DpiScaleX;
            _view.Height = _img.PixelHeight * _zoom / dpi.DpiScaleY;
        }

        private ContextMenu BuildMenu()
        {
            var m = new ContextMenu();
            void Item(string h, Action a) { var i = new MenuItem { Header = h }; i.Click += (s, e) => a(); m.Items.Add(i); }
            Item(L.T("复制"), () => ImageExporter.CopyToClipboard(_img));
            Item(L.T("另存为…"), () =>
            {
                var s = App.Settings.Current;
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = ImageExporter.SaveFilter, FileName = ImageExporter.ExpandTemplate(s, commit: false) };
                if (dlg.ShowDialog(this) == true) { ImageExporter.Save(_img, dlg.FileName, s.JpegQuality); ImageExporter.CommitCounter(s); App.Settings.Save(); }
            });
            Item(L.T("在编辑器中打开"), () => new EditorWindow(_img, App.Settings).Show());
            Item(L.T("文字识别 (OCR)"), () => OcrRunner.Run(_img, this));
            Item(L.T("公式转LaTeX"), () => FormulaWindow.Run(_img, this));
            m.Items.Add(new Separator());
            Item(L.T("原始大小 (100%)"), () => { _zoom = 1; ApplyZoom(); });
            var top = new MenuItem { Header = L.T("总在最前"), IsCheckable = true, IsChecked = true };
            top.Click += (s, e) => Topmost = top.IsChecked; m.Items.Add(top);
            var op = new MenuItem { Header = L.T("透明度") };
            foreach (var v in new[] { 100, 80, 60, 40 }) { var i = new MenuItem { Header = v + "%" }; i.Click += (s, e) => Opacity = v / 100.0; op.Items.Add(i); }
            m.Items.Add(op);
            m.Items.Add(new Separator());
            Item(L.T("关闭"), Close);
            return m;
        }
    }
}
