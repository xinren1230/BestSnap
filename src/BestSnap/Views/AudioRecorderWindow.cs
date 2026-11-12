using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Audio-only recorder: microphone, WASAPI loopback, or both mixed. WAV / MP3.</summary>
    public class AudioRecorderWindow : Window
    {
        readonly SettingsService _svc;
        readonly AudioRecorderService _rec;
        readonly RadioButton _rMic = new RadioButton { Content = L.T("麦克风"), GroupName = "asrc" };
        readonly RadioButton _rSys = new RadioButton { Content = L.T("系统声音（环回）"), GroupName = "asrc" };
        readonly RadioButton _rMix = new RadioButton { Content = L.T("麦克风 + 系统声音"), GroupName = "asrc" };
        readonly ComboBox _mics = new ComboBox { DisplayMemberPath = "Item2", MinWidth = 240 };
        readonly ComboBox _sys = new ComboBox { DisplayMemberPath = "Item2", MinWidth = 240 };
        readonly ProgressBar _micBar = new ProgressBar { Height = 12, Minimum = 0, Maximum = 1 };
        readonly ProgressBar _sysBar = new ProgressBar { Height = 12, Minimum = 0, Maximum = 1 };
        readonly RadioButton _fWav = new RadioButton { Content = L.T("WAV 无损"), GroupName = "afmt" };
        readonly RadioButton _fMp3 = new RadioButton { Content = L.T("MP3"), GroupName = "afmt" };
        readonly CheckBox _liveOnly = new CheckBox { Content = L.T("仅转写（不保存音频）"), Margin = new Thickness(0, 12, 0, 0) };
        readonly TextBlock _liveHint = new TextBlock
        {
            Text = L.T("仅转写：音频不会保存，只保留在内存中的短片段并在转写后立即丢弃。"),
            FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
        };
        readonly TextBox _folder = new TextBox { MinWidth = 220 };
        readonly Button _start = new Button { Content = L.T("开始"), Width = 72, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        readonly Button _pause = new Button { Content = L.T("暂停"), Width = 72, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        readonly Button _resume = new Button { Content = L.T("继续"), Width = 72, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        readonly Button _stop = new Button { Content = L.T("停止"), Width = 72, Height = 32 };
        readonly TextBlock _time = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        readonly DispatcherTimer _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        readonly Stopwatch _sw = new Stopwatch();

        public AudioRecorderWindow(SettingsService svc, AudioRecorderService rec, bool liveOnly = false)
        {
            _svc = svc; _rec = rec;
            var o = AudioRecordingSettings.Normalize(svc.Current.AudioRecording, svc.Current.SaveFolder);
            Title = L.T("音频录制") + " - " + AppIdentity.Name;
            Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
            Background = SmartUi.LightBarBackground;

            try { _mics.ItemsSource = AudioRecorderService.GetMicrophones().Select(m => Tuple.Create(m.Id, m.Name)).ToList(); } catch { }
            try { _sys.ItemsSource = AudioRecorderService.GetLoopbackDevices().Select(m => Tuple.Create(m.Id, m.Name)).ToList(); } catch { }
            _rMic.IsChecked = o.Source == AudioRecordSourceKind.Microphone;
            _rSys.IsChecked = o.Source == AudioRecordSourceKind.System;
            _rMix.IsChecked = o.Source == AudioRecordSourceKind.Mix;
            Pick(_mics, o.MicrophoneDevice); Pick(_sys, o.SystemDevice);
            _fWav.IsChecked = o.Format != AudioRecordFileFormat.Mp3;
            _fMp3.IsChecked = o.Format == AudioRecordFileFormat.Mp3;
            _folder.Text = string.IsNullOrWhiteSpace(o.SaveFolder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), AppIdentity.Name) : o.SaveFolder;

            var browse = new Button { Content = L.T("浏览…"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 0, 12, 0), Height = 28 };
            browse.Click += (s, e) =>
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = _folder.Text, Description = L.T("选择录音保存文件夹") };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _folder.Text = dlg.SelectedPath;
            };
            var fp = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); fp.Children.Add(browse); fp.Children.Add(_folder);

            var g = new StackPanel { Margin = new Thickness(16) };
            g.Children.Add(new TextBlock { Text = L.T("来源"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            g.Children.Add(_rMic); g.Children.Add(_mics);
            g.Children.Add(new TextBlock { Text = L.T("电平"), FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 2) });
            g.Children.Add(_micBar);
            g.Children.Add(_rSys); g.Children.Add(_sys);
            g.Children.Add(new TextBlock { Text = L.T("电平"), FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 2) });
            g.Children.Add(_sysBar);
            g.Children.Add(_rMix);
            g.Children.Add(_liveOnly);
            g.Children.Add(_liveHint);
            g.Children.Add(new TextBlock { Text = L.T("格式"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
            var fmt = new StackPanel { Orientation = Orientation.Horizontal }; fmt.Children.Add(_fWav); _fMp3.Margin = new Thickness(16, 0, 0, 0); fmt.Children.Add(_fMp3);
            g.Children.Add(fmt);
            g.Children.Add(new TextBlock { Text = L.T("保存到（点保存时使用）"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
            g.Children.Add(fp);
            _liveOnly.IsChecked = liveOnly;
            _liveOnly.Checked += (s, e) => ApplyLiveUi();
            _liveOnly.Unchecked += (s, e) => ApplyLiveUi();
            ApplyLiveUi();

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            foreach (var b in new[] { _start, _pause, _resume, _stop })
            {
                b.Cursor = System.Windows.Input.Cursors.Hand; b.BorderThickness = new Thickness(0);
                b.Foreground = SmartUi.LightFg; b.Background = SmartUi.QuietAccentLight; b.Style = SmartUi.ButtonStyle(false);
            }
            btns.Children.Add(_start); btns.Children.Add(_pause); btns.Children.Add(_resume); btns.Children.Add(_stop); btns.Children.Add(_time);
            g.Children.Add(btns);
            Content = g;

            _start.Click += (s, e) => Begin();
            _pause.Click += (s, e) => { _rec.Pause(); _sw.Stop(); Sync(); };
            _resume.Click += (s, e) => { _rec.Resume(); _sw.Start(); Sync(); };
            _stop.Click += (s, e) => { _rec.Stop(); };
            _rec.Completed += OnDone;
            _rec.Failed += OnFail;
            _rec.Started += () => { if (!_rec.IsPaused) _sw.Start(); Sync(); };
            _tick.Tick += (s, e) =>
            {
                _micBar.Value = _rec.MicLevel; _sysBar.Value = _rec.SystemLevel;
                var t = _sw.Elapsed;
                _time.Text = _rec.HasStarted ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}" : "00:00:00";
                Sync();
            };
            Loaded += (s, e) => { _tick.Start(); Sync(); };
            Closed += (s, e) =>
            {
                _tick.Stop();
                _rec.Completed -= OnDone; _rec.Failed -= OnFail;
                if (_rec.IsRecording) _rec.Stop();
                Persist();
            };
            void SyncSrc()
            {
                bool mic = _rMic.IsChecked == true || _rMix.IsChecked == true;
                bool sys = _rSys.IsChecked == true || _rMix.IsChecked == true;
                _mics.IsEnabled = mic && !_rec.IsRecording;
                _sys.IsEnabled = sys && !_rec.IsRecording;
            }
            _rMic.Checked += (s, e) => SyncSrc(); _rSys.Checked += (s, e) => SyncSrc(); _rMix.Checked += (s, e) => SyncSrc();
            SyncSrc();
        }

        static void Pick(ComboBox box, string id)
        {
            box.SelectedItem = (box.ItemsSource as System.Collections.Generic.List<Tuple<string, string>>)?.FirstOrDefault(m => m.Item1 == id);
            if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
        }

        AudioRecordingSettings ReadOpts()
        {
            var src = _rMix.IsChecked == true ? AudioRecordSourceKind.Mix : _rSys.IsChecked == true ? AudioRecordSourceKind.System : AudioRecordSourceKind.Microphone;
            return AudioRecordingSettings.Normalize(new AudioRecordingSettings
            {
                Source = src,
                MicrophoneDevice = (_mics.SelectedItem as Tuple<string, string>)?.Item1,
                SystemDevice = (_sys.SelectedItem as Tuple<string, string>)?.Item1,
                Format = _fMp3.IsChecked == true ? AudioRecordFileFormat.Mp3 : AudioRecordFileFormat.Wav,
                SaveFolder = _folder.Text?.Trim(),
            }, _svc.Current.SaveFolder);
        }

        void Persist()
        {
            _svc.Current.AudioRecording = ReadOpts();
            _svc.Save();
        }

        public void SetLiveOnly(bool on)
        {
            _liveOnly.IsChecked = on;
            ApplyLiveUi();
        }

        void ApplyLiveUi()
        {
            bool live = _liveOnly.IsChecked == true;
            _liveHint.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            _fWav.IsEnabled = _fMp3.IsEnabled = _folder.IsEnabled = !live && !_rec.IsRecording;
            Title = (live ? L.T("仅转写（不保存音频）") : L.T("音频录制")) + " - " + AppIdentity.Name;
        }

        void Begin()
        {
            if (_rec.IsRecording) return;
            try
            {
                var o = ReadOpts();
                Persist();
                if (_liveOnly.IsChecked == true)
                {
                    App.Controller.StartLiveTranscribe(o);
                    Close();
                    return;
                }
                _sw.Reset();
                _rec.Start(o, o.SaveFolder);
                Sync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("无法开始录音：\n") + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void Sync()
        {
            bool rec = _rec.IsRecording, paused = _rec.IsPaused;
            _start.IsEnabled = !rec;
            _pause.IsEnabled = rec && !paused;
            _resume.IsEnabled = rec && paused;
            _stop.IsEnabled = rec;
            _rMic.IsEnabled = _rSys.IsEnabled = _rMix.IsEnabled = _liveOnly.IsEnabled = !rec;
            ApplyLiveUi();
        }

        void OnDone(string file)
        {
            _sw.Stop(); Sync();
            new RecordingDoneWindow(file, _svc).Show();
        }

        void OnFail(string err)
        {
            _sw.Stop(); Sync();
            MessageBox.Show(this, L.T("录音失败：\n") + err, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
