using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SnapForge.Models;
using SnapForge.Plugins;
using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Options dialog for timed capture.</summary>
    public class TimedCaptureWindow : Window
    {
        public TimedCaptureSettings Result { get; private set; }

        public TimedCaptureWindow(TimedCaptureSettings cur, string defaultFolder)
        {
            Title = L.T("定时截图") + " - " + AppIdentity.Name; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var g = new Grid { Margin = new Thickness(14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            int row = 0;
            void Row(string label, UIElement c)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 4) };
                Grid.SetRow(t, row); Grid.SetRow(c, row); Grid.SetColumn(c, 1); g.Children.Add(t); g.Children.Add(c); row++;
            }
            var mode = new ComboBox { Margin = new Thickness(0, 4, 0, 4) };
            mode.Items.Add(L.T("全屏")); mode.Items.Add(L.T("选择区域（开始后拖动框选）")); mode.Items.Add(L.T("窗口（开始后单击选择）"));
            mode.SelectedIndex = (int)cur.Mode;
            var interval = new TextBox { Text = cur.IntervalSeconds.ToString(), Margin = new Thickness(0, 4, 0, 4) };
            var count = new TextBox { Text = cur.Count.ToString(), Margin = new Thickness(0, 4, 0, 4) };
            var folder = new TextBox { Text = cur.Folder ?? Path.Combine(defaultFolder, "Timed") };
            var browse = new Button { Content = L.T("浏览…"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0) };
            browse.Click += (s, e) => { using var d = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = folder.Text }; if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) folder.Text = d.SelectedPath; };
            var fdock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) }; DockPanel.SetDock(browse, Dock.Right); fdock.Children.Add(browse); fdock.Children.Add(folder);
            var tpl = new TextBox { Text = cur.Template, Margin = new Thickness(0, 4, 0, 0) };
            var help = new TextBlock { Text = ImageExporter.PlaceholderHelp, Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
            var tplPanel = new StackPanel(); tplPanel.Children.Add(tpl); tplPanel.Children.Add(help);
            var fmt = new ComboBox { Margin = new Thickness(0, 4, 0, 4) }; foreach (var f in new[] { "png", "jpg", "bmp" }) fmt.Items.Add(f); fmt.SelectedItem = cur.Format ?? "png"; if (fmt.SelectedIndex < 0) fmt.SelectedIndex = 0;
            var cursor = new CheckBox { Content = L.T("截图包含鼠标指针"), IsChecked = cur.IncludeCursor, Margin = new Thickness(0, 4, 0, 4) };
            Row(L.T("截图方式"), mode); Row(L.T("间隔(秒)"), interval); Row(L.T("次数（0 = 直到停止）"), count);
            Row(L.T("保存文件夹"), fdock); Row(L.T("文件名模板"), tplPanel); Row(L.T("格式"), fmt); Row("", cursor);
            var ok = new Button { Content = L.T("开始"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            bar.Children.Add(ok); bar.Children.Add(cancel);
            g.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(bar, row); Grid.SetColumnSpan(bar, 2); g.Children.Add(bar);
            Content = g;
            ok.Click += (s, e) =>
            {
                if (!int.TryParse(interval.Text, out int iv) || iv < 1 || iv > 86400) { MessageBox.Show(this, L.T("间隔必须是 1–86400 秒。")); return; }
                if (!int.TryParse(count.Text, out int n) || n < 0) { MessageBox.Show(this, L.T("次数必须 ≥ 0。")); return; }
                Result = new TimedCaptureSettings
                {
                    Mode = (TimedCaptureMode)mode.SelectedIndex, IntervalSeconds = iv, Count = n,
                    Folder = string.IsNullOrWhiteSpace(folder.Text) ? null : folder.Text.Trim(), Template = tpl.Text, Format = (string)fmt.SelectedItem, IncludeCursor = cursor.IsChecked == true,
                };
                DialogResult = true;
            };
        }
    }
}
