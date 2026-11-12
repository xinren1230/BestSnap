using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BestSnap.Models;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Transcribe / summarize / copy / save .txt .srt .md. Works with a media file or a live cue list.</summary>
    public class TranscribePanel : StackPanel
    {
        readonly SettingsService _svc;
        readonly Func<string> _media;
        readonly TextBox _box = new TextBox { MinHeight = 140, MaxHeight = 220, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsReadOnly = true };
        readonly TextBlock _status = new TextBlock { FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        readonly ProgressBar _bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        readonly List<TranscriptCue> _cues = new List<TranscriptCue>();
        readonly Button _transcribe, _summarize, _copy, _txt, _srt, _copyMd, _md, _cancel;
        readonly ComboBox _modelBox, _langBox;
        readonly TextBox _promptBox;
        CancellationTokenSource _cts;
        string _summary;

        public IReadOnlyList<TranscriptCue> Cues => _cues;

        void PersistWhisper()
        {
            _svc.Current.WhisperModel = WhisperUi.SelectedModel(_modelBox);
            _svc.Current.WhisperLanguage = WhisperUi.SelectedLanguage(_langBox);
            _svc.Current.WhisperPrompt = string.IsNullOrWhiteSpace(_promptBox.Text) ? null : _promptBox.Text.Trim();
            _svc.Save();
        }

        public TranscribePanel(SettingsService svc, Func<string> mediaPath, IEnumerable<TranscriptCue> seed = null)
        {
            _svc = svc;
            _media = mediaPath;
            Margin = new Thickness(0, 12, 0, 0);
            Children.Add(new TextBlock { Text = L.T("转写（本机 Whisper，音频不会上传）"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            _modelBox = WhisperUi.ModelCombo(_svc.Current.WhisperModel);
            var autoHint = WhisperUi.AutoHint(_svc.Current.WhisperModel);
            _langBox = WhisperUi.LanguageCombo(_svc.Current.WhisperLanguage);
            _promptBox = new TextBox
            {
                Text = _svc.Current.WhisperPrompt ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 36, MaxHeight = 72, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var opt = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opt.ColumnDefinitions.Add(new ColumnDefinition());
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opt.ColumnDefinitions.Add(new ColumnDefinition());
            var ml = new TextBlock { Text = L.T("Whisper 模型"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var ll = new TextBlock { Text = L.T("转写语言"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            Grid.SetColumn(ml, 0); Grid.SetColumn(_modelBox, 1); Grid.SetColumn(ll, 2); Grid.SetColumn(_langBox, 3);
            opt.Children.Add(ml); opt.Children.Add(_modelBox); opt.Children.Add(ll); opt.Children.Add(_langBox);
            Children.Add(opt);
            Children.Add(autoHint);
            Children.Add(new TextBlock { Text = L.T("提示词 / 词汇（可选）"), FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            Children.Add(_promptBox);
            _modelBox.SelectionChanged += (s, e) =>
            {
                PersistWhisper();
                WhisperUi.UpdateAutoHint(autoHint, WhisperUi.SelectedModel(_modelBox));
            };
            _langBox.SelectionChanged += (s, e) => PersistWhisper();
            _promptBox.LostFocus += (s, e) => PersistWhisper();

            Button Btn(string label)
            {
                return new Button
                {
                    Content = L.T(label), MinWidth = 88, Height = 30, Margin = new Thickness(0, 0, 8, 6),
                    Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(0),
                    Foreground = SmartUi.LightFg, Background = SmartUi.QuietAccentLight, Style = SmartUi.ButtonStyle(false)
                };
            }
            _transcribe = Btn("转写");
            _cancel = Btn("取消");
            _copy = Btn("复制");
            _txt = Btn("保存 .txt");
            _srt = Btn("保存 .srt");
            _summarize = Btn("摘要");
            _copyMd = Btn("复制 Markdown");
            _md = Btn("保存 .md");
            _cancel.IsEnabled = false;

            var row1 = new WrapPanel();
            row1.Children.Add(_transcribe); row1.Children.Add(_cancel); row1.Children.Add(_copy); row1.Children.Add(_txt); row1.Children.Add(_srt);
            var row2 = new WrapPanel();
            row2.Children.Add(_summarize); row2.Children.Add(_copyMd); row2.Children.Add(_md);
            Children.Add(row1); Children.Add(row2);
            Children.Add(_box); Children.Add(_bar); Children.Add(_status);

            if (seed != null) { _cues.AddRange(seed); RefreshText(); }
            bool hasMedia = !string.IsNullOrWhiteSpace(_media?.Invoke()) && File.Exists(_media());
            _transcribe.IsEnabled = hasMedia;
            if (!hasMedia && _cues.Count == 0)
                _status.Text = L.T("没有可转写的音频。");
            else if (!hasMedia)
                _status.Text = L.T("音频未保存。可复制或导出文本。");

            _transcribe.Click += async (s, e) => await RunTranscribe();
            _cancel.Click += (s, e) => { try { _cts?.Cancel(); } catch { } };
            _copy.Click += (s, e) => Copy(TranscriptFormat.ToTxt(_cues));
            _txt.Click += (s, e) => Save("TXT|*.txt", TranscriptFormat.ToTxt(_cues), ".txt");
            _srt.Click += (s, e) => Save("SRT|*.srt", TranscriptFormat.ToSrt(_cues), ".srt");
            _summarize.Click += async (s, e) => await RunSummarize();
            _copyMd.Click += (s, e) => Copy(_summary ?? "");
            _md.Click += (s, e) => Save("Markdown|*.md", _summary ?? "", ".md");
        }

        public void ReplaceCues(IEnumerable<TranscriptCue> cues)
        {
            _cues.Clear();
            if (cues != null) _cues.AddRange(cues);
            RefreshText();
        }

        void RefreshText()
        {
            _box.Text = _cues.Count == 0 ? "" : TranscriptFormat.ToTxt(_cues);
        }

        async Task RunTranscribe()
        {
            string file = _media?.Invoke();
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                MessageBox.Show(Window.GetWindow(this), L.T("没有可转写的音频。"), AppIdentity.Name);
                return;
            }
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _transcribe.IsEnabled = false; _cancel.IsEnabled = true;
            _bar.Visibility = Visibility.Visible; _bar.Value = 0;
            _status.Text = L.T("正在本机转写…首次使用会下载 Whisper 模型。");
            try
            {
                PersistWhisper();
                var pick = WhisperTranscriber.Resolve(_svc.Current.WhisperModel, live: false);
                var modelId = pick.ModelId;
                _status.Text = string.Format(L.T("正在本机转写（{0}，约 {1}）…"), modelId, WhisperModels.SizeLabel(modelId));
                if (!string.IsNullOrEmpty(pick.Reason))
                    _status.Text += "  " + pick.Reason;
                IProgress<double> prog = new Progress<double>(p => _bar.Value = p);
                var decode = new WhisperDecodeSettings
                {
                    Language = _svc.Current.WhisperLanguage,
                    Prompt = _svc.Current.WhisperPrompt,
                    ReuseProcessor = true
                };
                _cues.Clear();
                _summary = null;
                RefreshText();
                IProgress<IReadOnlyList<TranscriptCue>> stream = new Progress<IReadOnlyList<TranscriptCue>>(added =>
                {
                    if (added == null || added.Count == 0) return;
                    _cues.AddRange(added);
                    RefreshText();
                    _status.Text = string.Format(L.T("正在本机转写…已输出 {0} 段。"), _cues.Count);
                });
                var result = await WhisperTranscriber.TranscribeFileWindowedAsync(file, modelId, null, decode, prog, added => stream.Report(added), _cts.Token);
                if (_cues.Count == 0 && result?.Cues != null)
                    _cues.AddRange(result.Cues);
                RefreshText();
                _status.Text = string.Format(L.T("转写完成，共 {0} 段。音频仅在本机处理。"), _cues.Count);
            }
            catch (OperationCanceledException) { _status.Text = L.T("已取消转写。"); }
            catch (Exception ex)
            {
                Logger.Error("Transcribe", ex);
                string err = L.T("转写失败：\n") + ex.Message;
                _status.Text = err;
                MessageBox.Show(Window.GetWindow(this), err, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _transcribe.IsEnabled = File.Exists(_media?.Invoke() ?? "");
                _cancel.IsEnabled = false;
                _bar.Visibility = Visibility.Collapsed;
            }
        }

        async Task RunSummarize()
        {
            if (_cues.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(_media?.Invoke()) && File.Exists(_media()))
                    await RunTranscribe();
                if (_cues.Count == 0)
                {
                    MessageBox.Show(Window.GetWindow(this), L.T("请先转写。"), AppIdentity.Name);
                    return;
                }
            }
            var s = _svc.Current;
            bool net = ChatSummarizer.HasNetworkProvider(s);
            if (net && !s.SummarizeNetworkConsent)
            {
                var r = MessageBox.Show(Window.GetWindow(this),
                    L.T("将把转写文本发送到你配置的聊天接口以生成摘要。音频文件不会上传。是否继续？"),
                    AppIdentity.Name, MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (r != MessageBoxResult.Yes) return;
                s.SummarizeNetworkConsent = true;
                _svc.Save();
            }
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _summarize.IsEnabled = false; _cancel.IsEnabled = true;
            _status.Text = net ? L.T("正在生成摘要…") : L.T("正在生成本机摘要…");
            try
            {
                IProgress<string> st = new Progress<string>(t => _status.Text = t);
                _summary = await ChatSummarizer.SummarizeAsync(_cues, s, net, st, _cts.Token);
                _box.Text = _summary;
                _status.Text = net ? L.T("摘要完成。") : L.T("本机抽取式摘要完成。");
            }
            catch (OperationCanceledException) { _status.Text = L.T("已取消摘要。"); }
            catch (Exception ex)
            {
                Logger.Error("Summarize", ex);
                MessageBox.Show(Window.GetWindow(this), L.T("摘要失败：\n") + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                _status.Text = L.T("摘要失败，已回退到本机摘要。");
                try { _summary = SummarizeMath.ExtractiveMarkdown(_cues); _box.Text = _summary; } catch { }
            }
            finally
            {
                _summarize.IsEnabled = true; _cancel.IsEnabled = false;
            }
        }

        void Copy(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); _status.Text = L.T("已复制。"); }
            catch (Exception ex) { Logger.Error("Clipboard", ex); }
        }

        void Save(string filter, string text, string ext)
        {
            if (string.IsNullOrEmpty(text)) { MessageBox.Show(Window.GetWindow(this), L.T("没有可保存的文本。"), AppIdentity.Name); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = filter + "|All|*.*",
                FileName = "transcript" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext,
                InitialDirectory = SuggestDir()
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            {
                File.WriteAllText(dlg.FileName, text);
                _status.Text = L.T("已保存") + ": " + dlg.FileName;
            }
        }

        string SuggestDir()
        {
            string media = _media?.Invoke();
            if (!string.IsNullOrWhiteSpace(media))
            {
                var d = Path.GetDirectoryName(media);
                if (!string.IsNullOrEmpty(d) && !TempRecordingStore.IsUnder(media, TempRecordingStore.Folder(AppPaths.LocalData)))
                    return d;
            }
            var last = _svc.Current.AudioRecording?.SaveFolder ?? _svc.Current.SaveFolder;
            return Directory.Exists(last) ? last : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}
