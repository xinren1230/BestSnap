using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnapForge.Models;
using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Translation provider settings. The API key is stored DPAPI-encrypted (current Windows user).</summary>
    public class TranslateSettingsWindow : Window
    {
        public TranslateSettingsWindow()
        {
            Title = L.T("翻译服务设置"); Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var t = App.Settings.Current.Translate ?? (App.Settings.Current.Translate = new TranslateSettings());
            var prov = new ComboBox { ItemsSource = new[] { L.T("无（离线，不翻译）"), "DeepL API", "Azure Translator", "LibreTranslate" }, SelectedIndex = (int)t.Provider };
            var ep = new TextBox { Text = t.Endpoint ?? "" };
            var region = new TextBox { Text = t.Region ?? "" };
            var key = new PasswordBox();
            bool hasKey = !string.IsNullOrEmpty(t.KeyProtected);
            var keyHint = new TextBlock { Text = hasKey ? L.T("已保存密钥（留空 = 保持不变）") : L.T("未保存密钥"), Foreground = Brushes.Gray, FontSize = 11 };
            var clear = new CheckBox { Content = L.T("清除已保存的密钥"), IsEnabled = hasKey };
            var help = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 0),
                Text = L.T("DeepL：填 API 密钥（:fx 结尾自动用免费端点）；Azure：填密钥和区域，地址可留空；LibreTranslate：填服务地址（如 https://libretranslate.example.com），密钥可选。识别的文字会发送到所选服务。") };

            var g = new Grid { Margin = new Thickness(14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
            void Row(string label, UIElement el)
            {
                int r = g.RowDefinitions.Count; g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(l, r); Grid.SetRow(el, r); Grid.SetColumn(el, 1); ((FrameworkElement)el).Margin = new Thickness(0, 4, 0, 4);
                g.Children.Add(l); g.Children.Add(el);
            }
            Row(L.T("服务"), prov); Row(L.T("地址 (可选)"), ep); Row(L.T("区域 (Azure)"), region); Row(L.T("API 密钥"), key); Row("", keyHint); Row("", clear); Row("", help);
            var ok = new Button { Content = L.T("确定"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            ok.Click += (s, e) =>
            {
                t.Provider = (TranslateProvider)Math.Max(0, prov.SelectedIndex);
                t.Endpoint = string.IsNullOrWhiteSpace(ep.Text) ? null : ep.Text.Trim();
                t.Region = string.IsNullOrWhiteSpace(region.Text) ? null : region.Text.Trim();
                if (clear.IsChecked == true) t.KeyProtected = null;
                if (!string.IsNullOrEmpty(key.Password)) t.KeyProtected = Secrets.Protect(key.Password.Trim());
                App.Settings.Save();
                DialogResult = true;
            };
            var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 0, 14, 14) };
            bp.Children.Add(ok); bp.Children.Add(cancel);
            var sp = new StackPanel(); sp.Children.Add(g); sp.Children.Add(bp); Content = sp;
        }
    }
}
