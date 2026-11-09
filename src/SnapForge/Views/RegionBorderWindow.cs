using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using SnapForge.Native;

namespace SnapForge.Views
{
    /// <summary>Dashed frame drawn just OUTSIDE the recorded region. Click-through and excluded from capture.</summary>
    public class RegionBorderWindow : Window
    {
        private const int Pad = 4;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

        public RegionBorderWindow(Int32Rect physical)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
            ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false; IsHitTestVisible = false;
            Left = -32000; Top = -32000; Width = 10; Height = 10;
            Content = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(239, 68, 68)), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } };
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20 | 0x80 | 0x08000000); // TRANSPARENT | TOOLWINDOW | NOACTIVATE
                CaptureExclusion.Apply(this);
                NativeMethods.SetWindowPos(h, new IntPtr(-1), physical.X - Pad, physical.Y - Pad, physical.Width + 2 * Pad, physical.Height + 2 * Pad, 0x0010 | 0x0040);
            };
        }
    }
}
