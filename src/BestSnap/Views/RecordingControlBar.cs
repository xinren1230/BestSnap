using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BestSnap.Native;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Small topmost bar shown while recording; excluded from capture when the OS supports it.</summary>
    public class RecordingControlBar : Window
    {
        private readonly RecorderService _rec;
        private readonly TextBlock _time = new TextBlock { Foreground = Brushes.White, FontSize = 14, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) };
        private readonly Button _start = new Button { Content = L.T("开始"), Width = 56, Margin = new Thickness(2) };
        private readonly Button _pause = new Button { Content = L.T("暂停"), Width = 56, Margin = new Thickness(2) };
        private readonly Button _resume = new Button { Content = L.T("继续"), Width = 56, Margin = new Thickness(2) };
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly Stopwatch _sw = new Stopwatch();
        private readonly Action _onStart;

        public RecordingControlBar(RecorderService rec, Action onStart = null)
        {
            _rec = rec; _onStart = onStart;
            Title = L.T("录屏中"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight; AllowsTransparency = true; Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - 340; Top = SystemParameters.WorkArea.Bottom - 70;
            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = Brushes.Red, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var stop = new Button { Content = L.T("停止"), Width = 56, Margin = new Thickness(2, 2, 6, 2) };
            _start.Click += (s, e) => { _onStart?.Invoke(); SyncButtons(); };
            _pause.Click += (s, e) => { if (!_rec.HasStarted) return; _rec.Pause(); _sw.Stop(); SyncButtons(); };
            _resume.Click += (s, e) => { if (!_rec.HasStarted) return; _rec.Resume(); _sw.Start(); SyncButtons(); };
            stop.Click += (s, e) => { stop.IsEnabled = false; if (_rec.IsRecording) _rec.Stop(); else Close(); };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            sp.Children.Add(dot); sp.Children.Add(_time); sp.Children.Add(_start); sp.Children.Add(_pause); sp.Children.Add(_resume);
            if (_rec.SupportsLiveVolume)
            {
                var vol = new Button { Content = L.T("音量"), Width = 48, Margin = new Thickness(2) };
                var o = App.Settings.Current.Recording ?? new BestSnap.Models.RecordingSettings();
                var mic = new Slider { Minimum = 0, Maximum = 1, Value = o.MicVolume, Width = 140 };
                var sys = new Slider { Minimum = 0, Maximum = 1, Value = o.SystemVolume, Width = 140 };
                void Apply() => _rec.SetVolumes(mic.Value, sys.Value);
                mic.ValueChanged += (s2, e2) => Apply(); sys.ValueChanged += (s2, e2) => Apply();
                var pp = new StackPanel { Margin = new Thickness(8) };
                pp.Children.Add(new TextBlock { Text = L.T("系统声音") }); pp.Children.Add(sys);
                pp.Children.Add(new TextBlock { Text = L.T("麦克风"), Margin = new Thickness(0, 6, 0, 0) }); pp.Children.Add(mic);
                var popup = new System.Windows.Controls.Primitives.Popup { Child = new Border { Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = pp }, PlacementTarget = vol, Placement = System.Windows.Controls.Primitives.PlacementMode.Top, StaysOpen = false };
                vol.Click += (s2, e2) => popup.IsOpen = !popup.IsOpen;
                sp.Children.Add(vol);
            }
            sp.Children.Add(stop);
            Content = new Border { Child = sp, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 40)), Padding = new Thickness(2) };
            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            SourceInitialized += (s, e) => CaptureExclusion.Apply(this);
            _timer.Tick += (s, e) =>
            {
                var t = _sw.Elapsed;
                _time.Text = _rec.HasStarted ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}" : L.T("正在启动…");
                dot.Opacity = _rec.IsPaused ? 0.3 : (t.Milliseconds < 500 ? 1 : 0.4);
                SyncButtons();
            };
            Loaded += (s, e) => { if (_rec.HasStarted && !_rec.IsPaused) _sw.Start(); _timer.Start(); SyncButtons(); };
            Action onStarted = () => { if (!_rec.IsPaused) _sw.Start(); SyncButtons(); };
            _rec.Started += onStarted; Closed += (s, e) => _rec.Started -= onStarted;
            Closed += (s, e) => _timer.Stop();
            _time.Text = "00:00:00";
            SyncButtons();
        }

        void SyncButtons()
        {
            bool rec = _rec.IsRecording, started = _rec.HasStarted, paused = _rec.IsPaused;
            _start.IsEnabled = !rec && _onStart != null;
            _start.Visibility = rec ? Visibility.Collapsed : Visibility.Visible;
            _pause.IsEnabled = rec && started && !paused;
            _resume.IsEnabled = rec && started && paused;
        }
    }

    /// <summary>Full-screen-ish countdown overlay (excluded from capture).</summary>
    public class CountdownWindow : Window
    {
        public CountdownWindow(int seconds, Action done)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
            Width = 220; Height = 220; WindowStartupLocation = WindowStartupLocation.CenterScreen; IsHitTestVisible = false;
            var tb = new TextBlock { FontSize = 110, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Content = new Border { CornerRadius = new CornerRadius(110), Background = new SolidColorBrush(Color.FromArgb(180, 124, 58, 237)), Child = tb };
            SourceInitialized += (s, e) => CaptureExclusion.Apply(this);
            int left = seconds; tb.Text = left.ToString();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (s, e) => { left--; if (left <= 0) { t.Stop(); Close(); done(); } else tb.Text = left.ToString(); };
            Loaded += (s, e) => t.Start();
        }
    }
}
