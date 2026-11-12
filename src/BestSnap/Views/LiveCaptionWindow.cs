using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BestSnap.Native;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Always-on-top, resizable live captions. Capture-excluded. Audio stays in memory only.</summary>
    public class LiveCaptionWindow : Window
    {
        readonly LiveTranscribeService _live;
        readonly SettingsService _svc;
        readonly TextBlock _caption = new TextBlock
        {
            Text = "…", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White, Margin = new Thickness(12, 8, 12, 8)
        };
        readonly TextBox _run = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent,
            Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 13
        };
        readonly TextBlock _banner = new TextBlock
        {
            Text = L.T("仅转写：音频不会保存，只保留在内存中的短片段并在转写后立即丢弃。"),
            FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 12, 4)
        };
        readonly TextBlock _status = new TextBlock { Foreground = Brushes.LightGray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        readonly TextBlock _time = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        readonly Button _pause, _resume, _stop;
        readonly DispatcherTimer _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        readonly Stopwatch _sw = new Stopwatch();
        TranscribePanel _panel;
        bool _finished;

        public LiveCaptionWindow(LiveTranscribeService live, SettingsService svc)
        {
            _live = live; _svc = svc;
            Title = L.T("实时字幕") + " - " + AppIdentity.Name;
            Width = 640; Height = 360; MinWidth = 360; MinHeight = 180;
            Topmost = true; ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Right - 680; Top = SystemParameters.WorkArea.Bottom - 400;
            Background = new SolidColorBrush(Color.FromArgb(235, 24, 24, 32));
            ResizeMode = ResizeMode.CanResizeWithGrip;

            _pause = Mk(L.T("暂停"));
            _resume = Mk(L.T("继续"));
            _stop = Mk(L.T("停止"));
            _pause.Click += (s, e) => { _live.Pause(); _sw.Stop(); Sync(); };
            _resume.Click += (s, e) => { _live.Resume(); _sw.Start(); Sync(); };
            _stop.Click += (s, e) => _live.Stop();

            var top = new DockPanel { Margin = new Thickness(8, 6, 8, 0) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            btns.Children.Add(_pause); btns.Children.Add(_resume); btns.Children.Add(_stop);
            DockPanel.SetDock(btns, Dock.Right);
            top.Children.Add(btns);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(_time); left.Children.Add(_status);
            top.Children.Add(left);

            var root = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(_banner, Dock.Top);
            root.Children.Add(top);
            root.Children.Add(_banner);
            var body = new DockPanel();
            var capBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(80, 124, 58, 237)), Child = _caption, Margin = new Thickness(8, 4, 8, 4) };
            DockPanel.SetDock(capBorder, Dock.Top);
            body.Children.Add(capBorder);
            body.Children.Add(_run);
            root.Children.Add(body);
            Content = root;

            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
            SourceInitialized += (s, e) => CaptureExclusion.Apply(this);

            _live.Caption += OnCaption;
            _live.StatusChanged += () => { _status.Text = _live.Status; };
            _live.Stopped += OnStopped;
            _live.Failed += err =>
            {
                MessageBox.Show(this, L.T("转写失败：\n") + err, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            };
            _live.Started += () => { if (!_live.IsPaused) _sw.Start(); Sync(); };
            _status.Text = _live.Status ?? L.T("仅转写中（不保存音频）");
            _tick.Tick += (s, e) =>
            {
                var t = _sw.Elapsed;
                _time.Text = $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
                Sync();
            };
            Loaded += (s, e) => { _tick.Start(); if (_live.HasStarted && !_live.IsPaused) _sw.Start(); Sync(); };
            Closing += (s, e) =>
            {
                if (_live.IsRunning)
                {
                    e.Cancel = true;
                    _live.Stop();
                }
            };
            Closed += (s, e) =>
            {
                _tick.Stop();
                _live.Caption -= OnCaption;
                _live.Stopped -= OnStopped;
            };
        }

        static Button Mk(string t) => new Button
        {
            Content = t, Width = 64, Height = 28, Margin = new Thickness(4, 0, 0, 0),
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand
        };

        void OnCaption(string line)
        {
            if (!string.IsNullOrWhiteSpace(line)) _caption.Text = line;
            _run.Text = TranscriptFormat.ToTxt(_live.Cues);
            _run.CaretIndex = _run.Text.Length;
            _run.ScrollToEnd();
        }

        void OnStopped()
        {
            if (_finished) return;
            _finished = true;
            _sw.Stop(); _tick.Stop();
            _pause.IsEnabled = _resume.IsEnabled = _stop.IsEnabled = false;
            _banner.Text = L.T("音频未保存。可复制或导出文本。");
            _caption.Text = L.T("转写已停止（未保存音频）");
            var cues = _live.Cues;
            _panel = new TranscribePanel(_svc, () => null, cues);
            var host = Content as DockPanel;
            host?.Children.Add(_panel);
            Height = Math.Max(Height, 520);
        }

        void Sync()
        {
            bool rec = _live.IsRunning, paused = _live.IsPaused;
            _pause.IsEnabled = rec && !paused;
            _resume.IsEnabled = rec && paused;
            _stop.IsEnabled = rec;
        }
    }
}
