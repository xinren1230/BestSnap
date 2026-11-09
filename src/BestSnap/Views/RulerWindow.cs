using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>On-screen ruler (pixels). Drag to move, R rotates, mouse wheel resizes, Esc closes.</summary>
    public class RulerWindow : Window
    {
        private bool _vertical;
        private double? _mouse;

        public RulerWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResizeWithGrip; Width = 600; Height = 50; Cursor = Cursors.SizeAll;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MouseLeftButtonDown += (s, e) => DragMove();
            MouseMove += (s, e) => { var p = e.GetPosition(this); _mouse = _vertical ? p.Y : p.X; InvalidateVisual(); };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.R) { _vertical = !_vertical; (Width, Height) = (Height, Width); InvalidateVisual(); }
            };
            MouseWheel += (s, e) => { if (_vertical) Height = Math.Max(100, Height + e.Delta / 2.0); else Width = Math.Max(100, Width + e.Delta / 2.0); };
            MouseRightButtonUp += (s, e) => Close();
            ToolTip = L.T("拖动移动 · R 旋转 · 滚轮改变长度 · Esc/右键关闭");
        }

        protected override void OnRender(DrawingContext dc)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = dpi.DpiScaleX; // ticks are drawn in physical pixels
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, 255, 236, 140)), new Pen(Brushes.Gray, 1), new Rect(0, 0, ActualWidth, ActualHeight));
            var pen = new Pen(Brushes.Black, 1 / scale);
            double len = _vertical ? ActualHeight : ActualWidth;
            double depth = _vertical ? ActualWidth : ActualHeight;
            for (int px = 0; px / scale < len; px += 2)
            {
                double pos = px / scale;
                double t = px % 100 == 0 ? depth * 0.5 : px % 10 == 0 ? depth * 0.25 : depth * 0.1;
                if (px % 2 != 0 || (px % 10 != 0 && scale < 1.5 && px % 4 != 0)) continue;
                if (_vertical) dc.DrawLine(pen, new Point(0, pos), new Point(t, pos)); else dc.DrawLine(pen, new Point(pos, 0), new Point(pos, t));
                if (px % 100 == 0 && px > 0)
                {
                    var ft = new FormattedText(px.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.Black, dpi.PixelsPerDip);
                    dc.DrawText(ft, _vertical ? new Point(t + 2, pos - 6) : new Point(pos + 2, t - 2));
                }
            }
            if (_mouse.HasValue)
            {
                var red = new Pen(Brushes.Red, 1);
                double m = _mouse.Value;
                if (_vertical) dc.DrawLine(red, new Point(0, m), new Point(depth, m)); else dc.DrawLine(red, new Point(m, 0), new Point(m, depth));
                var ft = new FormattedText(((int)(m * scale)) + " px", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Red, dpi.PixelsPerDip);
                dc.DrawText(ft, _vertical ? new Point(depth * 0.5, m + 2) : new Point(m + 3, depth * 0.55));
            }
        }
    }
}
