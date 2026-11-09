using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Small always-on-top panel (excluded from capture) with live preview and manual stop/cancel for scrolling capture.</summary>
    public class ScrollCaptureWindow : Window
    {
        private readonly Image _preview = new Image { Width = 200, Height = 150, Stretch = Stretch.Uniform };
        private readonly TextBlock _text = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Width = 200, Margin = new Thickness(0, 6, 0, 6) };
        public bool StopRequested { get; private set; }
        public bool Cancelled { get; private set; }

        public ScrollCaptureWindow()
        {
            Title = L.T("滚动截图"); WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.NoResize; Topmost = true;
            ShowActivated = false; ShowInTaskbar = false; SizeToContent = SizeToContent.WidthAndHeight;
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 44));
            var stop = new Button { Content = L.T("完成"), Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = L.T("取消"), Width = 90 };
            stop.Click += (s, e) => StopRequested = true;
            cancel.Click += (s, e) => { Cancelled = true; StopRequested = true; };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(stop); buttons.Children.Add(cancel);
            var sp = new StackPanel { Margin = new Thickness(10) };
            sp.Children.Add(new Border { Child = _preview, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) });
            sp.Children.Add(_text); sp.Children.Add(buttons);
            Content = sp;
            Closed += (s, e) => StopRequested = true;
            _text.Text = L.T("保持鼠标在要滚动的内容上…");
            Loaded += (s, e) =>
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - ActualWidth - 12; Top = wa.Bottom - ActualHeight - 12;
            };
        }

        public void Update(BitmapSource lastFrame, int frames, int height, string hint = null)
        {
            if (lastFrame != null) _preview.Source = lastFrame;
            _text.Text = string.Format(L.T("已拼接 {0} 屏，高度 {1} 像素"), frames, height) + (hint != null ? "\n" + hint : "") + L.T("\n到底部会自动结束，也可随时点“完成”。");
        }
    }
}
