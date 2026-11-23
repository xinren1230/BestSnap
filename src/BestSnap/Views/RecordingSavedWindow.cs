using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>
    /// After a recording is saved: show the full path, open Explorer, and (for video) open the editor.
    /// Audio-only also auto-transcribes into a sibling .txt.
    /// </summary>
    public class RecordingSavedWindow : Window
    {
        readonly SettingsService _svc;
        string _file;
        readonly TextBox _pathBox = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
        TranscribePanel _panel;

        public RecordingSavedWindow(string file, SettingsService svc = null)
        {
            _file = file ?? "";
            _svc = svc ?? App.Settings;
            Title = L.T("录制已保存") + " - " + AppIdentity.Name;
            Width = 640;
            MinWidth = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Background = SmartUi.LightBarBackground;
            SizeToContent = SizeToContent.Height;

            bool exists = File.Exists(_file);
            bool audio = TrimRangeMath.IsAudioPath(_file);
            Height = audio ? 720 : 280;
            if (audio) SizeToContent = SizeToContent.Manual;
            MinHeight = audio ? 420 : 200;

            var root = new DockPanel { Margin = new Thickness(16) };
            var title = new TextBlock
            {
                Text = L.T("已保存到"),
                FontWeight = FontWeights.SemiBold,
                Foreground = SmartUi.LightFg,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _pathBox.Text = _file;

            Button Btn(string label, bool enabled = true)
            {
                return new Button
                {
                    Content = L.T(label), MinWidth = 108, Height = 32, Margin = new Thickness(0, 0, 8, 0),
                    Cursor = Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                    Foreground = SmartUi.LightFg, Background = SmartUi.QuietAccentLight, Style = SmartUi.ButtonStyle(false),
                    IsEnabled = enabled
                };
            }

            var folder = Btn("打开文件位置", exists);
            folder.Click += (s, e) => ShellUtil.RevealInExplorer(_file);
            var edit = Btn("编辑视频", exists && !audio);
            edit.Background = SmartUi.Accent;
            edit.Foreground = Brushes.White;
            edit.Click += (s, e) => RecordingCommit.OpenEditor(_file);
            var open = Btn("打开文件", exists);
            open.Click += (s, e) => ShellUtil.OpenFile(_file);
            var close = Btn("关闭");
            close.IsCancel = true;
            close.Background = Brushes.Transparent;
            close.Click += (s, e) => Close();

            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(folder);
            if (!audio) row.Children.Add(edit);
            row.Children.Add(open);
            row.Children.Add(close);

            var head = new StackPanel();
            head.Children.Add(title);
            head.Children.Add(_pathBox);
            head.Children.Add(row);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            if (audio)
            {
                _panel = new TranscribePanel(_svc, () => _file);
                var scroller = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                root.Children.Add(scroller);
                Loaded += async (s, e) =>
                {
                    try { await _panel.MaybeAutoTranscribeAsync().ConfigureAwait(true); }
                    catch (Exception ex) { Logger.Error("AutoTranscribe", ex); }
                };
                Closed += (s, e) =>
                {
                    try { _panel?.Cancel(); } catch { }
                };
            }

            Content = root;
        }
    }
}
