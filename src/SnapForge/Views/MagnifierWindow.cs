using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnapForge.Native;
using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Floating magnifier that follows the cursor. Mouse wheel changes zoom, Esc / double-click closes.</summary>
    public class MagnifierWindow : Window
    {
        private readonly Image _img = new Image { Stretch = Stretch.Fill };
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        private int _zoom;
        private const int Size = 240;

        public MagnifierWindow(int zoom)
        {
            _zoom = Math.Clamp(zoom, 2, 16);
            Width = Size; Height = Size; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.NearestNeighbor);
            var grid = new Grid();
            grid.Children.Add(_img);
            grid.Children.Add(new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Stroke = Brushes.Red, StrokeThickness = 1 });
            Content = new Border { Child = grid, BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(2) };
            _timer.Tick += (s, e) => Tick();
            Loaded += (s, e) => _timer.Start();
            Closed += (s, e) => _timer.Stop();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            MouseDoubleClick += (s, e) => Close();
            MouseWheel += (s, e) => _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? 1 : -1), 2, 16);
        }

        private void Tick()
        {
            NativeMethods.GetCursorPos(out var p);
            var dpi = VisualTreeHelper.GetDpi(this);
            int phys = (int)(Size * dpi.DpiScaleX);
            int src = Math.Max(4, phys / _zoom);
            _img.Source = CaptureService.CaptureRect(new Int32Rect(p.X - src / 2, p.Y - src / 2, src, src));
            // keep window offset from cursor so it does not capture itself
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int ox = p.X + 30, oy = p.Y + 30;
            var vs = NativeMethods.VirtualScreen;
            if (ox + phys > vs.Right) ox = p.X - 30 - phys;
            if (oy + phys > vs.Bottom) oy = p.Y - 30 - phys;
            NativeMethods.SetWindowPos(h, IntPtr.Zero, ox, oy, 0, 0, 0x0001 /*NOSIZE*/ | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }
}
