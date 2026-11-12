using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>
    /// Preview + timeline trimmer. Mark one or more ranges to remove (including from the middle),
    /// then export a new file via ffmpeg (or native PCM cut for WAV). Never overwrites the original without confirmation.
    /// </summary>
    public class TrimWindow : Window
    {
        readonly string _file;
        readonly bool _audio;
        readonly MediaElement _media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true };
        readonly Slider _pos = new Slider { Minimum = 0, Maximum = 1 };
        readonly Canvas _wave = new Canvas { Height = 72, Background = new SolidColorBrush(Color.FromRgb(30, 30, 36)), ClipToBounds = true };
        readonly Canvas _ranges = new Canvas { Height = 28, Background = new SolidColorBrush(Color.FromRgb(40, 40, 48)), ClipToBounds = true };
        readonly ListBox _list = new ListBox { Height = 90, Margin = new Thickness(0, 6, 0, 0) };
        readonly TextBlock _info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        readonly DispatcherTimer _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        readonly List<TimeRange> _remove = new List<TimeRange>();
        double _duration, _markIn = double.NaN;
        bool _seeking, _playing, _dragging;
        double _dragA;

        public TrimWindow(string file)
        {
            _file = file;
            _audio = TrimRangeMath.IsAudioPath(file);
            Title = L.T("剪辑") + " - " + IOPath.GetFileName(file);
            Width = 960; Height = _audio ? 520 : 680; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = SmartUi.LightBarBackground;
            _media.Source = new Uri(file);

            var play = Mk(L.T("播放/暂停"));
            var markIn = Mk(L.T("标记删除起点"));
            var markOut = Mk(L.T("标记删除终点"));
            var addCut = Mk(L.T("添加删除区间"));
            var delCut = Mk(L.T("删除所选区间"));
            var export = Mk(L.T("导出剪辑…"));
            var replace = Mk(L.T("覆盖原文件"));
            var folder = Mk(L.T("打开文件夹"));

            var grid = new Grid { Margin = new Thickness(12) };
            for (int i = 0; i < 8; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

            UIElement preview;
            if (_audio)
                preview = new Border { Background = Brushes.Black, Height = 80, Child = new TextBlock { Text = "♪  " + IOPath.GetFileName(file), Foreground = Brushes.White, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            else
                preview = new Border { Background = Brushes.Black, Child = _media };
            if (_audio) { _media.Volume = 1; _media.Width = 0; _media.Height = 0; } // still used for playback clock
            grid.Children.Add(preview);
            if (_audio) grid.Children.Add(_media);

            Grid.SetRow(_wave, 1); grid.Children.Add(_wave);
            Grid.SetRow(_ranges, 2); grid.Children.Add(_ranges);
            var posRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            posRow.Children.Add(new TextBlock { Text = L.T("位置"), Width = 56, VerticalAlignment = VerticalAlignment.Center });
            posRow.Children.Add(_pos);
            Grid.SetRow(posRow, 3); grid.Children.Add(posRow);
            Grid.SetRow(_list, 4); grid.Children.Add(_list);

            var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var b in new[] { play, markIn, markOut, addCut, delCut }) { b.Margin = new Thickness(0, 0, 8, 6); tools.Children.Add(b); }
            Grid.SetRow(tools, 5); grid.Children.Add(tools);

            var btns = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            export.Margin = replace.Margin = folder.Margin = new Thickness(0, 0, 8, 0);
            left.Children.Add(export); left.Children.Add(replace); left.Children.Add(folder); left.Children.Add(_info);
            btns.Children.Add(left);
            Grid.SetRow(btns, 6); grid.Children.Add(btns);

            var hint = new TextBlock
            {
                Text = L.T("在时间轴上拖动添加要删掉的区间（可从中间剪切）。导出默认保存为 原名_trimmed，不会覆盖原文件。"),
                Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(hint, 7); grid.Children.Add(hint);
            Content = grid;

            _media.MediaOpened += (s, e) =>
            {
                _duration = _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan.TotalSeconds : 0;
                _pos.Maximum = Math.Max(0.001, _duration);
                _pos.SmallChange = 0.1; _pos.LargeChange = 1;
                try { _media.Play(); _media.Pause(); } catch { }
                DrawWave(); DrawRanges(); UpdateInfo();
            };
            _pos.ValueChanged += (s, e) => { if (!_seeking) Seek(_pos.Value); };
            play.Click += (s, e) =>
            {
                if (_playing) { _media.Pause(); _playing = false; return; }
                _media.Play(); _playing = true;
            };
            markIn.Click += (s, e) => { _markIn = _pos.Value; UpdateInfo(); };
            markOut.Click += (s, e) =>
            {
                if (double.IsNaN(_markIn)) _markIn = 0;
                AddRemove(_markIn, _pos.Value);
                _markIn = double.NaN;
            };
            addCut.Click += (s, e) =>
            {
                if (double.IsNaN(_markIn)) { _markIn = _pos.Value; UpdateInfo(); return; }
                AddRemove(_markIn, _pos.Value); _markIn = double.NaN;
            };
            delCut.Click += (s, e) =>
            {
                int i = _list.SelectedIndex;
                if (i >= 0 && i < _remove.Count) { _remove.RemoveAt(i); RefreshCuts(); }
            };
            _list.KeyDown += (s, e) => { if (e.Key == Key.Delete) delCut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };

            void OnDrag(MouseEventArgs e, bool end)
            {
                double x = e.GetPosition(_ranges).X;
                double t = _duration <= 0 ? 0 : Math.Clamp(x / Math.Max(1, _ranges.ActualWidth), 0, 1) * _duration;
                if (!end) { _dragA = t; Seek(t); return; }
                AddRemove(_dragA, t);
                _dragging = false;
            }
            _ranges.MouseLeftButtonDown += (s, e) => { _dragging = true; _ranges.CaptureMouse(); OnDrag(e, false); };
            _ranges.MouseMove += (s, e) => { if (_dragging) OnDrag(e, false); };
            _ranges.MouseLeftButtonUp += (s, e) => { if (_dragging) { OnDrag(e, true); _ranges.ReleaseMouseCapture(); } };
            _wave.MouseLeftButtonDown += (s, e) =>
            {
                double x = e.GetPosition(_wave).X;
                double t = _duration <= 0 ? 0 : Math.Clamp(x / Math.Max(1, _wave.ActualWidth), 0, 1) * _duration;
                Seek(t);
            };
            SizeChanged += (s, e) => { DrawWave(); DrawRanges(); };

            _tick.Tick += (s, e) =>
            {
                _seeking = true; _pos.Value = _media.Position.TotalSeconds; _seeking = false;
                if (_playing && _duration > 0 && _media.Position.TotalSeconds >= _duration - 0.05) { _media.Pause(); _playing = false; }
                DrawPlayhead();
            };
            _tick.Start();

            folder.Click += (s, e) => ShellUtil.RevealInExplorer(_file);
            export.Click += async (s, e) =>
            {
                string def = TrimRangeMath.DefaultOutputPath(_file);
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = IOPath.GetFileName(def),
                    InitialDirectory = IOPath.GetDirectoryName(_file),
                    Filter = _audio ? "WAV|*.wav|MP3|*.mp3|All|*.*" : "MP4|*.mp4|All|*.*"
                };
                if (dlg.ShowDialog(this) == true) await Export(dlg.FileName, overwrite: false);
            };
            replace.Click += async (s, e) =>
            {
                if (MessageBox.Show(this, L.T("确定用剪辑后的文件覆盖原文件吗？"), AppIdentity.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                await Export(_file, overwrite: true);
            };
            Closed += (s, e) => { _tick.Stop(); _media.Close(); };
            UpdateInfo();
        }

        static Button Mk(string t) => new Button { Content = t, Padding = new Thickness(10, 4, 10, 4), Height = 30 };

        void Seek(double sec)
        {
            if (_duration <= 0) return;
            sec = Math.Clamp(sec, 0, _duration);
            _media.Position = TimeSpan.FromSeconds(sec);
            _seeking = true; _pos.Value = sec; _seeking = false;
        }

        void AddRemove(double a, double b)
        {
            var r = TrimRangeMath.TryCreate(a, b, Math.Max(_duration, Math.Max(a, b)));
            if (!r.HasValue) return;
            _remove.Add(r.Value);
            RefreshCuts();
        }

        void RefreshCuts()
        {
            var merged = TrimRangeMath.Merge(_remove, Math.Max(_duration, 0.001));
            _remove.Clear(); _remove.AddRange(merged);
            _list.Items.Clear();
            foreach (var r in _remove)
                _list.Items.Add($"{r.Start:0.00}s – {r.End:0.00}s  (−{r.Duration:0.00}s)");
            DrawRanges(); UpdateInfo();
        }

        List<TimeRange> Keep() => TrimRangeMath.KeepFromRemove(_duration, _remove);

        void UpdateInfo()
        {
            var keep = Keep();
            string extra = double.IsNaN(_markIn) ? "" : "  " + L.T("起点") + $" {_markIn:0.00}s";
            _info.Text = string.Format(L.T("保留 {0:0.0}s / 原长 {1:0.0}s，删除 {2} 段"), TrimRangeMath.TotalDuration(keep), _duration, _remove.Count) + extra;
        }

        void DrawWave()
        {
            _wave.Children.Clear();
            double w = Math.Max(1, _wave.ActualWidth), h = Math.Max(1, _wave.Height);
            if (w < 4) return;
            float[] peaks = null;
            try
            {
                if (string.Equals(IOPath.GetExtension(_file), ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    using var fs = File.OpenRead(_file);
                    if (AudioPcm.TryReadPcmWav(fs, out int rate, out int ch, out int bits, out long dataPos, out int dataBytes) && bits == 16)
                    {
                        int n = Math.Min(dataBytes, 8_000_000);
                        var buf = new byte[n];
                        fs.Position = dataPos;
                        int got = fs.Read(buf, 0, n);
                        var samples = new short[got / 2];
                        Buffer.BlockCopy(buf, 0, samples, 0, samples.Length * 2);
                        peaks = AudioPcm.Peaks(samples, (int)w, ch);
                    }
                }
            }
            catch { }
            if (peaks == null || peaks.Length == 0)
            {
                _wave.Children.Add(new Line { X1 = 0, Y1 = h / 2, X2 = w, Y2 = h / 2, Stroke = Brushes.Gray, StrokeThickness = 1 });
                return;
            }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(0, h / 2), true, true);
                for (int i = 0; i < peaks.Length; i++)
                    ctx.LineTo(new Point(i, h / 2 - peaks[i] * (h / 2 - 2)), true, false);
                for (int i = peaks.Length - 1; i >= 0; i--)
                    ctx.LineTo(new Point(i, h / 2 + peaks[i] * (h / 2 - 2)), true, false);
            }
            g.Freeze();
            _wave.Children.Add(new System.Windows.Shapes.Path { Data = g, Fill = new SolidColorBrush(Color.FromRgb(96, 165, 250)) });
        }

        void DrawRanges()
        {
            _ranges.Children.Clear();
            double w = Math.Max(1, _ranges.ActualWidth), h = Math.Max(1, _ranges.Height);
            if (_duration <= 0) return;
            var keepBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            var cutBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            foreach (var k in Keep())
            {
                double x = k.Start / _duration * w, ww = Math.Max(1, k.Duration / _duration * w);
                _ranges.Children.Add(new Rectangle { Fill = keepBrush, Width = ww, Height = h, Opacity = 0.85 });
                Canvas.SetLeft(_ranges.Children[_ranges.Children.Count - 1], x);
            }
            foreach (var r in _remove)
            {
                double x = r.Start / _duration * w, ww = Math.Max(1, r.Duration / _duration * w);
                _ranges.Children.Add(new Rectangle { Fill = cutBrush, Width = ww, Height = h * 0.7, Opacity = 0.9 });
                Canvas.SetLeft(_ranges.Children[_ranges.Children.Count - 1], x);
                Canvas.SetTop(_ranges.Children[_ranges.Children.Count - 1], h * 0.15);
            }
            DrawPlayhead();
        }

        Line _head;
        void DrawPlayhead()
        {
            if (_head != null) _ranges.Children.Remove(_head);
            double w = Math.Max(1, _ranges.ActualWidth), h = Math.Max(1, _ranges.Height);
            if (_duration <= 0) return;
            double x = _pos.Value / _duration * w;
            _head = new Line { X1 = x, X2 = x, Y1 = 0, Y2 = h, Stroke = Brushes.White, StrokeThickness = 1 };
            _ranges.Children.Add(_head);
        }

        async System.Threading.Tasks.Task Export(string output, bool overwrite)
        {
            var keep = Keep();
            if (keep.Count == 0) { MessageBox.Show(this, L.T("没有可保留的片段。"), AppIdentity.Name); return; }
            if (!overwrite && string.Equals(IOPath.GetFullPath(output), IOPath.GetFullPath(_file), StringComparison.OrdinalIgnoreCase))
            {
                if (MessageBox.Show(this, L.T("确定用剪辑后的文件覆盖原文件吗？"), AppIdentity.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                overwrite = true;
            }
            string target = overwrite
                ? IOPath.Combine(IOPath.GetDirectoryName(_file), IOPath.GetFileNameWithoutExtension(_file) + ".trim.tmp" + IOPath.GetExtension(_file))
                : output;
            _info.Text = L.T("正在导出…");
            try
            {
                IProgress<double> prog = new Progress<double>(p => _info.Text = L.T("正在导出…") + $" {p * 100:0}%");
                bool wav = string.Equals(IOPath.GetExtension(_file), ".wav", StringComparison.OrdinalIgnoreCase)
                           && string.Equals(IOPath.GetExtension(target), ".wav", StringComparison.OrdinalIgnoreCase);
                if (wav)
                {
                    await System.Threading.Tasks.Task.Run(() => AudioPcm.CutWavFile(_file, target, keep));
                    prog.Report(1);
                }
                else if (FfmpegCutter.Available)
                {
                    bool video = !_audio;
                    await FfmpegCutter.CutAsync(_file, target, keep, video, audio: true, prog, CancellationToken.None);
                }
                else if (keep.Count == 1 && !_audio && PluginHost.Trimmer != null)
                {
                    await PluginHost.Trimmer.TrimAsync(_file, target, TimeSpan.FromSeconds(keep[0].Start), TimeSpan.FromSeconds(keep[0].End), prog, CancellationToken.None);
                }
                else
                    throw new InvalidOperationException(L.T("未找到 ffmpeg。请安装 ffmpeg 并将其加入 PATH 后再剪辑 MP4/MP3。"));

                if (overwrite)
                {
                    _media.Close(); _media.Source = null;
                    await System.Threading.Tasks.Task.Delay(200);
                    await FileRetry.CopyAsync(target, _file, true);
                    try { File.Delete(target); } catch { }
                    _media.Source = new Uri(_file);
                    _info.Text = L.T("已保存") + ": " + _file;
                }
                else _info.Text = L.T("已保存") + ": " + output;
            }
            catch (Exception ex)
            {
                Logger.Error("Trim", ex);
                MessageBox.Show(this, L.T("剪辑失败") + ":\n" + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
