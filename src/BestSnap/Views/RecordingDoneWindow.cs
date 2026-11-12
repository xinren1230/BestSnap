using System.IO;
using System.Windows;
using System.Windows.Controls;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Shown as soon as a screen or audio recording is saved: full path, open folder/file, trim.</summary>
    public class RecordingDoneWindow : Window
    {
        public RecordingDoneWindow(string file)
        {
            Title = L.T("录制完成") + " - " + AppIdentity.Name;
            Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
            Background = SmartUi.LightBarBackground;
            var path = file ?? "";
            bool exists = File.Exists(path);
            bool canTrim = exists && TrimRangeMath.CanTrimPath(path);

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock { Text = L.T("文件已保存到："), FontWeight = FontWeights.SemiBold, Foreground = SmartUi.LightFg, Margin = new Thickness(0, 0, 0, 8) });
            var box = new TextBox { Text = path, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 14) };
            root.Children.Add(box);

            Button Btn(string label, bool enabled = true)
            {
                var b = new Button
                {
                    Content = L.T(label), MinWidth = 100, Height = 32, Margin = new Thickness(0, 0, 8, 0),
                    Cursor = System.Windows.Input.Cursors.Hand, Focusable = false, BorderThickness = new Thickness(0),
                    Foreground = SmartUi.LightFg, Background = SmartUi.QuietAccentLight, Style = SmartUi.ButtonStyle(false),
                    IsEnabled = enabled
                };
                return b;
            }

            var folder = Btn("打开文件夹", exists);
            var open = Btn("打开文件", exists);
            var trim = Btn("剪辑", canTrim);
            var close = Btn("关闭");
            close.IsCancel = true; close.Background = System.Windows.Media.Brushes.Transparent;
            folder.Click += (s, e) => ShellUtil.RevealInExplorer(path);
            open.Click += (s, e) => ShellUtil.OpenFile(path);
            trim.Click += (s, e) => { new TrimWindow(path).Show(); Close(); };
            close.Click += (s, e) => Close();
            if (!canTrim) trim.ToolTip = L.T("仅支持剪辑 MP4 / WAV / MP3");

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            row.Children.Add(folder); row.Children.Add(open); row.Children.Add(trim); row.Children.Add(close);
            root.Children.Add(row);
            Content = root;
        }
    }
}
