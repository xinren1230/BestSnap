using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BestSnap.Models;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    public class TranscriptionSettingsWindow : Window
    {
        public TranscriptionSettingsWindow()
        {
            Title = L.T("转写与摘要设置"); Width = 560; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = SmartUi.LightBarBackground;
            var s = App.Settings.Current;
            s.Chat ??= new ChatSettings();
            var model = new ComboBox { ItemsSource = WhisperModels.Ids.Select(id => id + "  (" + WhisperModels.SizeLabel(id) + ")").ToList() };
            int mi = Array.IndexOf(WhisperModels.Ids, WhisperModels.Normalize(s.WhisperModel));
            model.SelectedIndex = mi < 0 ? 1 : mi;
            var dir = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Text = L.T("模型目录：") + AppPaths.WhisperModels };
            var apiBase = new TextBox { Text = s.Chat.ApiBase ?? "" };
            var apiModel = new TextBox { Text = s.Chat.ApiModel ?? "gpt-4o-mini" };
            var apiKey = new PasswordBox();
            var hint = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11,
                Text = L.T("转写始终在本机用 Whisper 完成，不会上传音频。摘要默认用本机抽取；若填写 OpenAI 兼容地址（也可复用公式识别里的 OpenAI 设置），才会把转写文本发到该接口。密钥用 DPAPI 保存。")
            };
            var g = new Grid { Margin = new Thickness(14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            void Row(string label, UIElement el)
            {
                int r = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(l, r); Grid.SetRow(el, r); Grid.SetColumn(el, 1); ((FrameworkElement)el).Margin = new Thickness(0, 4, 0, 4);
                g.Children.Add(l); g.Children.Add(el);
            }
            Row(L.T("Whisper 模型"), model);
            Row("", dir);
            Row(L.T("摘要 API 地址"), apiBase);
            Row(L.T("摘要模型"), apiModel);
            Row(L.T("API 密钥"), apiKey);
            Row("", new TextBlock { Text = string.IsNullOrEmpty(s.Chat.ApiKeyProtected) ? L.T("未保存密钥") : L.T("已保存密钥（留空 = 保持不变）"), Foreground = Brushes.Gray, FontSize = 11 });
            Row("", hint);
            var ok = new Button { Content = L.T("确定"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            ok.Click += (o, e) =>
            {
                int i = model.SelectedIndex;
                s.WhisperModel = i >= 0 && i < WhisperModels.Ids.Length ? WhisperModels.Ids[i] : WhisperModels.DefaultId;
                s.Chat.ApiBase = string.IsNullOrWhiteSpace(apiBase.Text) ? null : apiBase.Text.Trim();
                s.Chat.ApiModel = string.IsNullOrWhiteSpace(apiModel.Text) ? "gpt-4o-mini" : apiModel.Text.Trim();
                if (!string.IsNullOrEmpty(apiKey.Password)) s.Chat.ApiKeyProtected = Secrets.Protect(apiKey.Password.Trim());
                App.Settings.Save();
                DialogResult = true;
            };
            var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 14) };
            bp.Children.Add(ok); bp.Children.Add(cancel);
            var sp = new StackPanel(); sp.Children.Add(g); sp.Children.Add(bp); Content = sp;
        }
    }
}
