using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BestSnap.Plugins;
using BestSnap.Services;
using IOPath = System.IO.Path;

namespace BestSnap.Views
{
    /// <summary>
    /// After screen/audio recording: preview the temp file, Trim / Transcribe / Summarize, then Save As.
    /// Closing without save confirms discard of the temp recording.
    /// </summary>
    public class RecordingDoneWindow : Window
    {
        readonly SettingsService _svc;
        string _file;
        bool _saved, _discarded;
        readonly MediaElement _media = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true };
        readonly TextBox _pathBox = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
        readonly TextBlock _hint = new TextBlock { FontSize = 12, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        TranscribePanel _panel;

        public RecordingDoneWindow(string file, SettingsService svc = null)
        {
            _file = file ?? "";
            _svc = svc ?? App.Settings;
            Title = L.T("录制完成") + " - " + AppIdentity.Name;
            Width = 720; Height = 720; MinWidth = 520; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
            Background = SmartUi.LightBarBackground;

            var root = new DockPanel { Margin = new Thickness(16) };
            var title = new TextBlock { Text = L.T("录音尚未保存到你的文件夹。关闭前请点“保存”。"), FontWeight = FontWeights.SemiBold, Foreground = SmartUi.LightFg, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            _pathBox.Text = _file;
            bool exists = File.Exists(_file);
            bool canTrim = exists && TrimRangeMath.CanTrimPath(_file);
            bool audio = TrimRangeMath.IsAudioPath(_file);
            _hint.Text = L.T("临时文件保存在 %LocalAppData%\\BestSnap\\temp\\recordings。超过 7 天未保存的会在启动时清理。崩溃后仍可从此处恢复。");

            Button Btn(string label, bool enabled = true)
            {
                return new Button
                {
                    Content = L.T(label), MinWidth = 96, Height = 32, Margin = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                    Foreground = SmartUi.LightFg, Background = SmartUi.QuietAccentLight, Style = SmartUi.ButtonStyle(false),
                    IsEnabled = enabled
                };
            }

            var preview = new Border { Background = Brushes.Black, Height = audio ? 72 : 220, Margin = new Thickness(0, 0, 0, 10) };
            if (exists)
            {
                try { _media.Source = new Uri(_file); } catch { }
                if (audio)
                {
                    preview.Child = new TextBlock { Text = "♪  " + IOPath.GetFileName(_file), Foreground = Brushes.White, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    _media.Width = 0; _media.Height = 0; _media.Volume = 1;
                }
                else preview.Child = _media;
            }
            else preview.Child = new TextBlock { Text = L.T("找不到文件。"), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            var play = Btn("播放/暂停", exists);
            play.Click += (s, e) =>
            {
                try
                {
                    if (_media.Position.TotalSeconds > 0 && _media.CanPause) { _media.Pause(); _media.Position = TimeSpan.Zero; _media.Play(); }
                    else _media.Play();
                }
                catch { }
            };

            var folder = Btn("打开文件夹", exists);
            var open = Btn("打开文件", exists);
            var trim = Btn("剪辑", canTrim);
            var save = Btn("保存", exists);
            save.Background = SmartUi.Accent;
            save.Foreground = Brushes.White;
            var close = Btn("关闭");
            close.IsCancel = true; close.Background = Brushes.Transparent;
            folder.Click += (s, e) => ShellUtil.RevealInExplorer(_file);
            open.Click += (s, e) => ShellUtil.OpenFile(_file);
            trim.Click += (s, e) =>
            {
                var tw = new TrimWindow(_file, applied =>
                {
                    if (string.IsNullOrWhiteSpace(applied) || !File.Exists(applied)) return;
                    _file = applied;
                    _pathBox.Text = _file;
                    try { _media.Source = new Uri(_file); } catch { }
                });
                tw.Show();
            };
            save.Click += (s, e) => DoSave();
            close.Click += (s, e) => Close();
            if (!canTrim) trim.ToolTip = L.T("仅支持剪辑 MP4 / WAV / MP3");

            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var b in new[] { play, folder, open, trim, save, close }) row.Children.Add(b);

            _panel = new TranscribePanel(_svc, () => _file);
            var head = new StackPanel();
            head.Children.Add(title); head.Children.Add(_pathBox); head.Children.Add(_hint); head.Children.Add(preview); head.Children.Add(row);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            var scroller = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(scroller);
            Content = root;

            Closing += (s, e) =>
            {
                if (_saved || _discarded) return;
                if (!File.Exists(_file)) return;
                if (!IsTemp(_file)) return;
                var r = MessageBox.Show(this, L.T("尚未保存到你的文件夹。关闭将丢弃这段临时录音。要保存吗？"),
                    AppIdentity.Name, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (r == MessageBoxResult.Yes)
                {
                    if (!DoSave()) e.Cancel = true;
                }
                else
                {
                    Discard();
                }
            };
            Closed += (s, e) => { try { _media.Close(); } catch { } };
        }

        static bool IsTemp(string path) =>
            TempRecordingStore.IsUnder(path, TempRecordingStore.Folder(AppPaths.LocalData));

        bool DoSave()
        {
            if (!File.Exists(_file)) return false;
            string ext = IOPath.GetExtension(_file);
            string dir = LastFolder();
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = IOPath.GetFileName(_file),
                InitialDirectory = Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Filter = FilterFor(ext)
            };
            if (dlg.ShowDialog(this) != true) return false;
            try
            {
                Directory.CreateDirectory(IOPath.GetDirectoryName(dlg.FileName) ?? ".");
                File.Copy(_file, dlg.FileName, overwrite: true);
                RememberFolder(IOPath.GetDirectoryName(dlg.FileName));
                if (IsTemp(_file) && !string.Equals(IOPath.GetFullPath(_file), IOPath.GetFullPath(dlg.FileName), StringComparison.OrdinalIgnoreCase))
                    TempRecordingStore.DeletePair(_file);
                _file = dlg.FileName;
                _pathBox.Text = _file;
                _saved = true;
                _hint.Text = L.T("已保存") + ": " + _file;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("保存失败：\n") + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        void Discard()
        {
            _discarded = true;
            try { _media.Close(); _media.Source = null; } catch { }
            if (IsTemp(_file)) TempRecordingStore.DeletePair(_file);
        }

        string LastFolder()
        {
            if (TrimRangeMath.IsAudioPath(_file))
            {
                var a = _svc.Current.AudioRecording?.SaveFolder;
                if (!string.IsNullOrWhiteSpace(a)) return a;
            }
            var r = _svc.Current.Recording?.SaveFolder;
            if (!string.IsNullOrWhiteSpace(r)) return r;
            return _svc.Current.SaveFolder;
        }

        void RememberFolder(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            if (TrimRangeMath.IsAudioPath(_file))
            {
                var a = AudioRecordingSettings.Normalize(_svc.Current.AudioRecording, dir);
                a.SaveFolder = dir;
                _svc.Current.AudioRecording = a;
            }
            else
            {
                _svc.Current.Recording ??= new BestSnap.Models.RecordingSettings();
                _svc.Current.Recording.SaveFolder = dir;
            }
            _svc.Save();
        }

        static string FilterFor(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            if (ext == ".wav") return "WAV|*.wav|All|*.*";
            if (ext == ".mp3") return "MP3|*.mp3|All|*.*";
            if (ext == ".gif") return "GIF|*.gif|All|*.*";
            return "MP4|*.mp4|All|*.*";
        }
    }
}
