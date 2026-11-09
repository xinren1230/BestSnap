using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Runs the optional OCR plugin and shows an editable/copyable result window.</summary>
    public static class OcrRunner
    {
        public static bool Available => PluginHost.Ocr != null;

        public static bool HasChinese(BestSnap.Plugins.IOcrEngine ocr)
        {
            try { foreach (var l in ocr.Languages) if (l.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return true; } catch { }
            return false;
        }

        /// <summary>"" / auto / paddle use the built-in PP-OCR engine; "windows" or a language tag use Windows OCR.</summary>
        public static bool UsesBuiltIn(string lang) => string.IsNullOrEmpty(lang) || lang.Equals("auto", StringComparison.OrdinalIgnoreCase) || lang.Equals("paddle", StringComparison.OrdinalIgnoreCase);

        public const string ZhCapability = "Language.OCR~~~zh-CN~0.0.1.0";

        /// <summary>Installs the Windows Chinese OCR capability in an elevated PowerShell (UAC prompt); never runs silently.</summary>
        public static void InstallChinesePack(Window owner)
        {
            if (MessageBox.Show(owner, L.T("将以管理员身份运行 PowerShell 安装 Windows 中文 OCR 识别包（约 30 MB，需要联网）：") + "\n\nAdd-WindowsCapability -Online -Name " + ZhCapability + "\n\n" + L.T("安装完成后请重启 BestSnap。继续吗？"),
                    AppIdentity.Name + " OCR", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                var cmd = "Add-WindowsCapability -Online -Name '" + ZhCapability + "'; Write-Host ''; Write-Host 'Done. Please restart BestSnap.'; Read-Host 'Press Enter to close'";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + cmd + "\"") { UseShellExecute = true, Verb = "runas" });
            }
            catch (System.ComponentModel.Win32Exception) { } // UAC cancelled
            catch (Exception ex) { Logger.Error("Install zh OCR", ex); MessageBox.Show(owner, ex.Message, AppIdentity.Name + " OCR"); }
        }

        public static async void Run(BitmapSource img, Window owner = null, string translateTo = null)
        {
            var ocr = PluginHost.Ocr;
            if (ocr == null)
            {
                MessageBox.Show(L.T("未找到 OCR 插件。") + "\n\n" + L.T("请把 BestSnap.Ocr 插件放到：") + "\n" + Path.Combine(PluginHost.Folder, AppIdentity.PluginOcrFolder) + "\n\n" +
                    L.T("（需要 Windows 10 1809 及以上，并在“设置 → 时间和语言 → 语言”中安装对应语言的 OCR 功能）"), AppIdentity.Name + " OCR", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, Text = L.T("识别中…") };
            var w = new Window { Title = L.T("文字识别结果") + " - " + ocr.DisplayName, Width = 640, Height = 520, WindowStartupLocation = WindowStartupLocation.CenterScreen, Owner = owner?.IsVisible == true ? owner : null };
            var copy = new Button { Content = L.T("复制全部"), Width = 100, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            copy.Click += (s, e) => { try { Clipboard.SetText(box.Text); } catch { } };
            // translation (optional online provider)
            var trBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, IsReadOnly = true, Height = 150, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            Button Mk(string t) { var b = new Button { Content = t, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) }; bar.Children.Add(b); return b; }
            var toZh = Mk(L.T("译为中文")); var toEn = Mk(L.T("译为英文")); var trCopy = Mk(L.T("复制译文")); var trSet = Mk(L.T("翻译设置…"));
            trCopy.IsEnabled = false;
            async void Tr(string lang)
            {
                if (!Translator.Configured) { new BestSnap.Views.TranslateSettingsWindow { Owner = w }.ShowDialog(); if (!Translator.Configured) return; }
                trBox.Visibility = Visibility.Visible; trBox.Text = L.T("翻译中…"); toZh.IsEnabled = toEn.IsEnabled = false;
                try { trBox.Text = await Translator.TranslateAsync(box.SelectedText.Length > 0 ? box.SelectedText : box.Text, lang); trCopy.IsEnabled = true; }
                catch (Exception ex) { Logger.Error("Translate", ex); trBox.Text = L.T("翻译失败") + ": " + ex.Message; }
                finally { toZh.IsEnabled = toEn.IsEnabled = true; }
            }
            toZh.Click += (s, e) => Tr("zh"); toEn.Click += (s, e) => Tr("en");
            trCopy.Click += (s, e) => { try { Clipboard.SetText(trBox.Text); } catch { } };
            trSet.Click += (s, e) => new BestSnap.Views.TranslateSettingsWindow { Owner = w }.ShowDialog();
            var bottom = new DockPanel(); DockPanel.SetDock(copy, Dock.Right); bottom.Children.Add(copy); bottom.Children.Add(bar);
            var dock = new DockPanel { Margin = new Thickness(10) };
            var curLang = App.Settings.Current.OcrLanguage;
            if (!UsesBuiltIn(curLang) && !HasChinese(ocr))
            {
                var hint = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 244, 206)) };
                var inst = new Button { Content = L.T("安装中文识别包…"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6), ToolTip = L.T("以管理员身份运行：Add-WindowsCapability -Online -Name Language.OCR~~~zh-CN~0.0.1.0") };
                inst.Click += (s, e) => InstallChinesePack(w);
                DockPanel.SetDock(inst, Dock.Right); hint.Children.Add(inst);
                hint.Children.Add(new TextBlock { Text = L.T("未安装 Windows 中文 OCR 识别包，中文会识别成乱码或无法识别。安装后重启 BestSnap 即可。"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 4, 6), VerticalAlignment = VerticalAlignment.Center });
                DockPanel.SetDock(hint, Dock.Top); dock.Children.Add(hint);
            }
            DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
            DockPanel.SetDock(trBox, Dock.Bottom); dock.Children.Add(trBox);
            dock.Children.Add(box);
            w.Content = dock; w.Show();
            try
            {
                byte[] png;
                using (var ms = new MemoryStream()) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); enc.Save(ms); png = ms.ToArray(); }
                var lang = App.Settings.Current.OcrLanguage;
                var text = await Task.Run(() => ocr.RecognizeAsync(png, lang, CancellationToken.None));
                box.Text = string.IsNullOrWhiteSpace(text) ? L.T("（未识别到文字）") : text;
                box.Focus(); box.SelectAll();
                if (translateTo != null && !string.IsNullOrWhiteSpace(text))
                {
                    int cjk = 0; foreach (var ch in text) if (ch >= 0x4E00 && ch <= 0x9FFF) cjk++;
                    Tr(translateTo != "auto" ? translateTo : (cjk * 3 > text.Length ? "en" : "zh"));
                }
            }
            catch (Exception ex) { Logger.Error("OCR", ex); box.Text = L.T("识别失败") + ": " + ex.Message; }
        }
    }
}
