using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SnapForge.Plugins;
using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Post-recording preview with start/end trim (MediaComposition via the optional WinRT plugin).</summary>
    public class TrimWindow : Window
    {
        private readonly MediaElement _media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true };
        private readonly Slider _start = new Slider(), _end = new Slider(), _pos = new Slider();
        private readonly TextBlock _info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        private readonly DispatcherTimer _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly string _file;
        private bool _seeking;

        public TrimWindow(string file)
        {
            _file = file;
            Title = L.T("剪辑录屏") + " - " + Path.GetFileName(file); Width = 900; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _media.Source = new Uri(file);
            var play = new Button { Content = L.T("播放/暂停"), Width = 90, Margin = new Thickness(0, 0, 6, 0) };
            var save = new Button { Content = L.T("另存剪辑后的视频…"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
            var replace = new Button { Content = L.T("覆盖原文件"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
            var folder = new Button { Content = L.T("打开文件夹"), Padding = new Thickness(8, 2, 8, 2) };
            bool canTrim = PluginHost.Trimmer != null;
            if (!canTrim) { save.IsEnabled = replace.IsEnabled = false; save.ToolTip = L.T("需要 plugins\\SnapForge.Ocr 插件"); }

            var grid = new Grid { Margin = new Thickness(10) };
            for (int i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var vb = new Border { Background = Brushes.Black, Child = _media }; grid.Children.Add(vb);
            UIElement Lab(string t, Slider s, int row) { var d = new DockPanel { Margin = new Thickness(0, 4, 0, 0) }; var l = new TextBlock { Text = t, Width = 60 }; DockPanel.SetDock(l, Dock.Left); d.Children.Add(l); d.Children.Add(s); Grid.SetRow(d, row); grid.Children.Add(d); return d; }
            Lab(L.T("位置"), _pos, 1); Lab(L.T("开始"), _start, 2); Lab(L.T("结束"), _end, 3);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            btns.Children.Add(play); btns.Children.Add(save); btns.Children.Add(replace); btns.Children.Add(folder); btns.Children.Add(_info);
            Grid.SetRow(btns, 4); grid.Children.Add(btns);
            Content = grid;

            bool playing = false;
            _media.MediaOpened += (s, e) =>
            {
                double d = _media.NaturalDuration.HasTimeSpan ? _media.NaturalDuration.TimeSpan.TotalSeconds : 0;
                foreach (var sl in new[] { _start, _end, _pos }) { sl.Maximum = d; sl.SmallChange = 0.1; sl.LargeChange = 1; }
                _end.Value = d; UpdateInfo();
                _media.Play(); _media.Pause(); // show first frame
            };
            _start.ValueChanged += (s, e) => { if (_start.Value > _end.Value - 0.2) _start.Value = Math.Max(0, _end.Value - 0.2); Seek(_start.Value); UpdateInfo(); };
            _end.ValueChanged += (s, e) => { if (_end.Value < _start.Value + 0.2) _end.Value = Math.Min(_end.Maximum, _start.Value + 0.2); Seek(_end.Value); UpdateInfo(); };
            _pos.ValueChanged += (s, e) => { if (!_seeking) Seek(_pos.Value); };
            play.Click += (s, e) =>
            {
                if (playing) { _media.Pause(); playing = false; return; }
                if (_media.Position.TotalSeconds >= _end.Value - 0.05 || _media.Position.TotalSeconds < _start.Value) Seek(_start.Value);
                _media.Play(); playing = true;
            };
            _tick.Tick += (s, e) =>
            {
                _seeking = true; _pos.Value = _media.Position.TotalSeconds; _seeking = false;
                if (playing && _media.Position.TotalSeconds >= _end.Value) { _media.Pause(); playing = false; }
            };
            _tick.Start();
            folder.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + _file + "\""); } catch { } };
            save.Click += async (s, e) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(_file) + "_trim.mp4", InitialDirectory = Path.GetDirectoryName(_file), Filter = "MP4|*.mp4" };
                if (dlg.ShowDialog(this) == true) await DoTrim(dlg.FileName, false, save, replace);
            };
            replace.Click += async (s, e) =>
            {
                if (MessageBox.Show(this, L.T("确定用剪辑后的视频覆盖原文件吗？"), AppIdentity.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    await DoTrim(_file, true, save, replace);
            };
            Closed += (s, e) => { _tick.Stop(); _media.Close(); };
        }

        private void Seek(double sec) { _media.Position = TimeSpan.FromSeconds(sec); }
        private void UpdateInfo() => _info.Text = $"{L.T("保留")} {TimeSpan.FromSeconds(_start.Value):mm\\:ss\\.f} – {TimeSpan.FromSeconds(_end.Value):mm\\:ss\\.f}  ({_end.Value - _start.Value:0.0}s)";

        private async System.Threading.Tasks.Task DoTrim(string output, bool replace, Button a, Button b)
        {
            a.IsEnabled = b.IsEnabled = false;
            string target = replace ? Path.Combine(Path.GetDirectoryName(_file), Path.GetFileNameWithoutExtension(_file) + ".trim.tmp.mp4") : output;
            try
            {
                var prog = new Progress<double>(p => _info.Text = L.T("正在导出…") + $" {p * 100:0}%");
                await PluginHost.Trimmer.TrimAsync(_file, target, TimeSpan.FromSeconds(_start.Value), TimeSpan.FromSeconds(_end.Value), prog, CancellationToken.None);
                if (replace)
                {
                    _media.Close(); _media.Source = null;
                    await System.Threading.Tasks.Task.Delay(300);
                    await SnapForge.Services.FileRetry.CopyAsync(target, _file, true); try { File.Delete(target); } catch { }
                    _media.Source = new Uri(_file);
                }
                _info.Text = L.T("已保存") + ": " + (replace ? _file : output);
            }
            catch (Exception ex) { Logger.Error("Trim", ex); MessageBox.Show(this, L.T("剪辑失败") + ":\n" + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { a.IsEnabled = b.IsEnabled = true; }
        }
    }
}
