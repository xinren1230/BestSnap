using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapForge.Native;
using SnapForge.Services;

namespace SnapForge.Views
{
    public enum SelectionKind { Rectangle, Freehand, Fixed, WindowObject, ColorPick }

    /// <summary>
    /// Full virtual-desktop overlay showing a frozen screenshot. The window is positioned in physical pixels
    /// (SetWindowPos) and mouse positions are scaled to image pixels, which keeps it correct on mixed-DPI setups.
    /// </summary>
    public class SelectionOverlay : Window
    {
        private readonly SelectionKind _kind;
        private readonly BitmapSource _screen;
        private readonly Int32Rect _vs;
        private readonly Canvas _canvas = new Canvas();
        private readonly Rectangle _sel = new Rectangle { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromArgb(30, 0, 160, 255)), Visibility = Visibility.Collapsed };
        private readonly Polyline _free = new Polyline { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2 };
        private readonly TextBlock _info = new TextBlock { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), Padding = new Thickness(6, 3, 6, 3), FontSize = 13 };
        private readonly Image _zoom = new Image { Width = 120, Height = 120 };
        private readonly Border _zoomBorder;
        private readonly PointCollection _freePx = new PointCollection();
        private List<WindowEnumerator.Win> _wins;
        private Point? _start;
        private Int32Rect _hoverRect;
        private readonly int _fw, _fh;

        public BitmapSource Result { get; private set; }
        public Int32Rect? ResultRect { get; private set; } // relative to virtual screen origin

        public SelectionOverlay(SelectionKind kind, int fixedW = 800, int fixedH = 600)
        {
            _kind = kind; _fw = fixedW; _fh = fixedH;
            _vs = CaptureService.VirtualScreen;
            if (kind == SelectionKind.WindowObject) _wins = WindowEnumerator.Snapshot();
            _screen = CaptureService.CaptureRect(_vs);

            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
            AllowsTransparency = false; Cursor = Cursors.Cross; WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0; Top = 0; Width = 100; Height = 100;

            var img = new Image { Source = _screen, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            var dim = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(kind == SelectionKind.ColorPick ? (byte)0 : (byte)70, 0, 0, 0)) };
            RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
            _zoomBorder = new Border { Child = _zoom, BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };

            var grid = new Grid();
            grid.Children.Add(img); grid.Children.Add(dim); grid.Children.Add(_canvas);
            _canvas.Children.Add(_sel); _canvas.Children.Add(_free); _canvas.Children.Add(_zoomBorder); _canvas.Children.Add(_info);
            Content = grid;

            SourceInitialized += (s, e) =>
            {
                var src = (HwndSource)PresentationSource.FromVisual(this);
                src.AddHook(WndProc);
                PlaceOnVirtualScreen();
            };
            Loaded += (s, e) => { Activate(); Focus(); };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter && _kind == SelectionKind.Fixed) Finish(_hoverRect); };
            MouseMove += OnMove; MouseLeftButtonDown += OnDown; MouseLeftButtonUp += OnUp;
            MouseRightButtonUp += (s, e) => Close();
            _info.Text = kind switch
            {
                SelectionKind.Rectangle => L.T("拖动选择区域，Esc 取消"),
                SelectionKind.Freehand => L.T("按住左键绘制任意形状，Esc 取消"),
                SelectionKind.Fixed => string.Format(L.T("固定大小 {0}×{1}：单击确认，Esc 取消"), _fw, _fh),
                SelectionKind.WindowObject => L.T("移动鼠标高亮窗口/控件，单击截取"),
                _ => L.T("单击复制颜色值，Esc 取消")
            };
        }

        // Mixed-DPI: the overlay spans all monitors, so Windows may send WM_DPICHANGED with a "suggested" rect when it
        // straddles monitors. We let WPF update its DPI scale, then force the window back onto exact physical bounds,
        // and map DIP<->pixel with the live device transform instead of assuming one global scale.
        private const int WM_DPICHANGED = 0x02E0;
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == WM_DPICHANGED) Dispatcher.BeginInvoke(new Action(PlaceOnVirtualScreen), System.Windows.Threading.DispatcherPriority.Send);
            return IntPtr.Zero;
        }

        private void PlaceOnVirtualScreen()
        {
            var h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            NativeMethods.SetWindowPos(h, IntPtr.Zero, _vs.X, _vs.Y, _vs.Width, _vs.Height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW);
        }

        private Matrix DeviceTransform => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        private double Sx => DeviceTransform.M11 > 0 ? DeviceTransform.M11 : _screen.PixelWidth / Math.Max(1, ActualWidth);
        private double Sy => DeviceTransform.M22 > 0 ? DeviceTransform.M22 : _screen.PixelHeight / Math.Max(1, ActualHeight);
        private Point ToPx(Point dip) => new Point(dip.X * Sx, dip.Y * Sy);
        private Point ToDip(double px, double py) => new Point(px / Sx, py / Sy);

        private void ShowRect(Int32Rect r)
        {
            var a = ToDip(r.X, r.Y); var b = ToDip(r.X + r.Width, r.Y + r.Height);
            Canvas.SetLeft(_sel, a.X); Canvas.SetTop(_sel, a.Y);
            _sel.Width = Math.Max(0, b.X - a.X); _sel.Height = Math.Max(0, b.Y - a.Y);
            _sel.Visibility = Visibility.Visible;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            var dip = e.GetPosition(this); var px = ToPx(dip);
            Canvas.SetLeft(_info, dip.X + 16); Canvas.SetTop(_info, dip.Y + 16);
            UpdateZoom(px, dip);
            switch (_kind)
            {
                case SelectionKind.Rectangle when _start.HasValue:
                    _hoverRect = RectFrom(ToPx(_start.Value), px); ShowRect(_hoverRect);
                    _info.Text = $"{_hoverRect.Width} × {_hoverRect.Height}"; break;
                case SelectionKind.Freehand when _start.HasValue:
                    _free.Points.Add(dip); _freePx.Add(px); break;
                case SelectionKind.Fixed:
                    _hoverRect = new Int32Rect((int)px.X - _fw / 2, (int)px.Y - _fh / 2, _fw, _fh); ShowRect(_hoverRect); break;
                case SelectionKind.WindowObject:
                    var hit = WindowEnumerator.HitTest(_wins, (int)px.X + _vs.X, (int)px.Y + _vs.Y);
                    if (hit.HasValue) { var r = hit.Value; _hoverRect = new Int32Rect(r.Left - _vs.X, r.Top - _vs.Y, r.Width, r.Height); ShowRect(_hoverRect); _info.Text = $"{r.Width} × {r.Height}"; }
                    break;
                case SelectionKind.ColorPick:
                    var c = PixelAt((int)px.X, (int)px.Y);
                    _info.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}  RGB({c.R},{c.G},{c.B})  ({(int)px.X + _vs.X},{(int)px.Y + _vs.Y})";
                    _info.Background = new SolidColorBrush(Color.FromArgb(220, c.R, c.G, c.B));
                    _info.Foreground = (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 140 ? Brushes.Black : Brushes.White;
                    break;
            }
        }

        private void UpdateZoom(Point px, Point dip)
        {
            int s = 15;
            var r = CaptureService.Clip(new Int32Rect((int)px.X - s, (int)px.Y - s, s * 2 + 1, s * 2 + 1), _screen.PixelWidth, _screen.PixelHeight);
            if (r.Width <= 0 || r.Height <= 0) return;
            _zoom.Source = new CroppedBitmap(_screen, r);
            Canvas.SetLeft(_zoomBorder, dip.X + 16); Canvas.SetTop(_zoomBorder, dip.Y + 44);
        }

        private Color PixelAt(int x, int y)
        {
            x = Math.Clamp(x, 0, _screen.PixelWidth - 1); y = Math.Clamp(y, 0, _screen.PixelHeight - 1);
            var b = new byte[4]; _screen.CopyPixels(new Int32Rect(x, y, 1, 1), b, 4, 0);
            return Color.FromRgb(b[2], b[1], b[0]);
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            var dip = e.GetPosition(this);
            switch (_kind)
            {
                case SelectionKind.Rectangle: case SelectionKind.Freehand:
                    _start = dip; CaptureMouse();
                    if (_kind == SelectionKind.Freehand) { _free.Points.Clear(); _freePx.Clear(); _free.Points.Add(dip); _freePx.Add(ToPx(dip)); }
                    break;
                case SelectionKind.Fixed: case SelectionKind.WindowObject: Finish(_hoverRect); break;
                case SelectionKind.ColorPick:
                    var px = ToPx(dip); var c = PixelAt((int)px.X, (int)px.Y);
                    var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                    try { Clipboard.SetText(hex); } catch { }
                    App.Controller?.Notify(string.Format(L.T("颜色 {0} / RGB({1},{2},{3}) 已复制"), hex, c.R, c.G, c.B));
                    Close(); break;
            }
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_start.HasValue) return;
            ReleaseMouseCapture();
            if (_kind == SelectionKind.Rectangle) Finish(_hoverRect);
            else if (_kind == SelectionKind.Freehand && _freePx.Count > 3)
            {
                Result = CaptureService.CropPolygon(_screen, _freePx);
                Close();
            }
            _start = null;
        }

        private void Finish(Int32Rect r)
        {
            if (r.Width < 2 || r.Height < 2) return;
            var clipped = CaptureService.Clip(r, _screen.PixelWidth, _screen.PixelHeight);
            Result = CaptureService.Crop(_screen, clipped);
            ResultRect = clipped;
            Close();
        }

        private static Int32Rect RectFrom(Point a, Point b) =>
            new Int32Rect((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Abs(a.X - b.X), (int)Math.Abs(a.Y - b.Y));
    }
}
