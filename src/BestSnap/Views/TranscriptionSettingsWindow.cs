using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            Title = L.T("转写与摘要设置"); Width = 600; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = SmartUi.LightBarBackground;
            var s = App.Settings.Current;
            s.Chat ??= new ChatSettings();

            var model = WhisperUi.ModelCombo(s.WhisperModel);
            var autoHint = WhisperUi.AutoHint(s.WhisperModel);
            var lang = WhisperUi.LanguageCombo(s.WhisperLanguage);
            var prompt = new TextBox
            {
                Text = s.WhisperPrompt ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                Height = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var dir = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11, Text = L.T("模型目录：") + AppPaths.WhisperModels };
            var hint = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11,
                Text = L.T("转写始终在本机用 Whisper 完成，不会上传音频。默认「自动（推荐）」按显卡显存、内存和 CPU 选择模型与 CUDA/Vulkan/CPU。也可手动指定 tiny～large-v3-turbo（含 q5_0）。摘要默认用本机抽取；若填写 OpenAI 兼容地址（也可复用公式识别里的 OpenAI 设置），才会把转写文本发到该接口。密钥用 DPAPI 保存。")
            };
            var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
            var status = new TextBlock { FontSize = 11, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
            var download = new Button { Content = L.T("下载模型"), Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            var delOne = new Button { Content = L.T("删除此模型"), Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            var delAll = new Button { Content = L.T("删除已下载的模型"), MinWidth = 130 };
            void Refresh()
            {
                WhisperUi.RefreshModelCombo(model, WhisperUi.SelectedModel(model));
                WhisperUi.UpdateAutoHint(autoHint, WhisperUi.SelectedModel(model));
            }
            model.SelectionChanged += (o, e) => WhisperUi.UpdateAutoHint(autoHint, WhisperUi.SelectedModel(model));
            download.Click += async (o, e) =>
            {
                string id = WhisperUi.SelectedModel(model);
                if (WhisperModels.IsAuto(id))
                    id = WhisperTranscriber.Resolve(id, live: false).ModelId;
                download.IsEnabled = delOne.IsEnabled = delAll.IsEnabled = false;
                bar.Visibility = Visibility.Visible; bar.Value = 0;
                status.Text = string.Format(L.T("正在下载 {0}…"), id);
                try
                {
                    IProgress<double> prog = new Progress<double>(p =>
                    {
                        bar.Value = p;
                        status.Text = string.Format(L.T("正在下载 {0}… {1}%"), id, (int)Math.Round(p * 100));
                    });
                    await WhisperTranscriber.EnsureModelAsync(id, prog, CancellationToken.None);
                    status.Text = L.T("已下载") + " " + id;
                    Refresh();
                }
                catch (Exception ex)
                {
                    Logger.Error("Whisper.DownloadUi", ex);
                    status.Text = ex.Message;
                    MessageBox.Show(this, L.T("转写失败：\n") + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    download.IsEnabled = delOne.IsEnabled = delAll.IsEnabled = true;
                    bar.Visibility = Visibility.Collapsed;
                }
            };
            delOne.Click += (o, e) =>
            {
                string id = WhisperUi.SelectedModel(model);
                if (WhisperModels.IsAuto(id))
                    id = WhisperTranscriber.Resolve(id, live: false).ModelId;
                WhisperTranscriber.DeleteModel(id);
                status.Text = L.T("已删除模型。");
                Refresh();
            };
            delAll.Click += (o, e) =>
            {
                if (MessageBox.Show(this, L.T("确定删除已下载的 Whisper 模型吗？"), AppIdentity.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                WhisperTranscriber.DeleteAllModels();
                status.Text = L.T("已删除模型。");
                Refresh();
            };

            var apiBase = new TextBox { Text = s.Chat.ApiBase ?? "" };
            var apiModel = new TextBox { Text = s.Chat.ApiModel ?? "gpt-4o-mini" };
            var apiKey = new PasswordBox();
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
            Row("", autoHint);
            Row(L.T("转写语言"), lang);
            Row(L.T("提示词 / 词汇（可选）"), prompt);
            Row("", dir);
            var actions = new WrapPanel();
            actions.Children.Add(download); actions.Children.Add(delOne); actions.Children.Add(delAll);
            Row("", actions);
            Row("", bar);
            Row("", status);
            Row(L.T("摘要 API 地址"), apiBase);
            Row(L.T("摘要模型"), apiModel);
            Row(L.T("API 密钥"), apiKey);
            Row("", new TextBlock { Text = string.IsNullOrEmpty(s.Chat.ApiKeyProtected) ? L.T("未保存密钥") : L.T("已保存密钥（留空 = 保持不变）"), Foreground = Brushes.Gray, FontSize = 11 });
            Row("", hint);
            var ok = new Button { Content = L.T("确定"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            ok.Click += (o, e) =>
            {
                s.WhisperModel = WhisperUi.SelectedModel(model);
                s.WhisperLanguage = WhisperUi.SelectedLanguage(lang);
                s.WhisperPrompt = string.IsNullOrWhiteSpace(prompt.Text) ? null : prompt.Text.Trim();
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

    static class WhisperUi
    {
        public static ComboBox ModelCombo(string selected)
        {
            var box = new ComboBox();
            RefreshModelCombo(box, selected);
            return box;
        }

        public static void RefreshModelCombo(ComboBox box, string selected)
        {
            selected = WhisperModels.Normalize(selected);
            var ids = WhisperModels.ChoiceIds;
            box.ItemsSource = ids.Select(ModelLabel).ToList();
            int i = Array.IndexOf(ids, selected);
            if (i < 0) i = 0;
            box.SelectedIndex = i;
        }

        public static string ModelLabel(string id)
        {
            if (WhisperModels.IsAuto(id))
                return L.T("自动（推荐）");
            string s = WhisperModels.Get(id).Label + "  (" + WhisperModels.SizeLabel(id) + ")";
            if (WhisperTranscriber.ModelReady(id)) s += "  " + L.T("已下载");
            return s;
        }

        public static string SelectedModel(ComboBox box)
        {
            var ids = WhisperModels.ChoiceIds;
            int i = box?.SelectedIndex ?? -1;
            if (i >= 0 && i < ids.Length) return ids[i];
            return WhisperModels.DefaultId;
        }

        public static TextBlock AutoHint(string choice)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, FontSize = 11 };
            UpdateAutoHint(t, choice);
            return t;
        }

        public static void UpdateAutoHint(TextBlock t, string choice)
        {
            if (t == null) return;
            if (!WhisperModels.IsAuto(choice))
            {
                t.Visibility = Visibility.Collapsed;
                t.Text = "";
                return;
            }
            t.Visibility = Visibility.Visible;
            try
            {
                var file = WhisperAutoSelect.Resolve(choice, WhisperHardwareProbe.Current, live: false);
                var live = WhisperAutoSelect.Resolve(choice, WhisperHardwareProbe.Current, live: true);
                string s = file.Reason;
                if (!string.Equals(file.ModelId, live.ModelId, StringComparison.Ordinal))
                    s += "  " + L.T("实时字幕：") + live.ModelId;
                t.Text = s;
            }
            catch (Exception ex)
            {
                t.Text = ex.Message;
            }
        }

        public static ComboBox LanguageCombo(string selected)
        {
            selected = WhisperDecode.NormalizeLanguage(selected);
            var ids = WhisperDecode.Languages.Select(x => x.Id).ToArray();
            var box = new ComboBox { ItemsSource = WhisperDecode.Languages.Select(LanguageLabel).ToList() };
            int i = Array.IndexOf(ids, selected);
            box.SelectedIndex = i < 0 ? 0 : i;
            return box;
        }

        public static string LanguageLabel((string Id, string Label) item)
        {
            if (item.Id == "auto") return L.T("自动");
            if (item.Id == "zh") return L.T("中文");
            return item.Label;
        }

        public static string SelectedLanguage(ComboBox box)
        {
            int i = box?.SelectedIndex ?? -1;
            if (i >= 0 && i < WhisperDecode.Languages.Length) return WhisperDecode.Languages[i].Id;
            return "auto";
        }
    }
}
