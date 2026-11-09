using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Native;
using SnapForge.Plugins;
using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Small floating bar shown right below (or above) the captured area: copy/save/save as/pin/OCR/upload/edit/close.
    /// Positioned in physical pixels; always excluded from capture.</summary>
    public class QuickActionBar : Window
    {
        private static QuickActionBar _current;
        private readonly BitmapSource _img;
        private readonly Int32Rect? _rect;
        private readonly TextBlock _status = new TextBlock { Foreground = Brushes.Gainsboro, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly System.Windows.Threading.DispatcherTimer _autoClose = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };

        public static void ShowFor(BitmapSource img, Int32Rect? absPhysicalRect)
        {
            _current?.Close();
            _current = new QuickActionBar(img, absPhysicalRect);
            _current.Closed += (o, e) => { if (_current == o) _current = null; };
            _current.Show();
        }

        public QuickActionBar(BitmapSource img, Int32Rect? rect)
        {
            _img = img; _rect = rect;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            AllowsTransparency = true; Background = Brushes.Transparent; SizeToContent = SizeToContent.WidthAndHeight;
            ShowActivated = true; Title = AppIdentity.Name;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            Content = SmartUi.Bar(sp);
            var thumb = new Image { Source = img, Height = 32, MaxWidth = 64, Stretch = Stretch.Uniform, Margin = new Thickness(4, 0, 8, 0) };
            sp.Children.Add(thumb);
            Action Guard(Action a) => () => { _autoClose.Stop(); try { a(); } catch (Exception ex) { Logger.Error("QuickBar", ex); _status.Text = ex.Message; } };
            void Btn(string glyph, string text, string tip, Action a, bool accent = false) =>
                sp.Children.Add(SmartUi.Fluent(glyph, text, tip, Guard(a), dark: true, accent: accent));
            var S = App.Settings.Current;
            BitmapSource Out() => Watermark.ApplyIfNeeded(_img, S);
            Btn(SmartUi.GCopy, L.T("复制"), L.T("复制到剪贴板") + "  (Ctrl+C)", () => { ImageExporter.CopyToClipboard(Out()); Close(); });
            Btn(SmartUi.GSave, L.T("保存"), L.T("按文件名模板保存"), () => { var f = ImageExporter.AutoSave(Out(), S); App.Settings.Save(); App.Controller?.Notify(L.T("已保存: ") + f); Close(); });
            Btn(SmartUi.GSaveAs, null, L.T("另存为…"), () =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = ImageExporter.SaveFilter, FileName = ImageExporter.ExpandTemplate(S), InitialDirectory = Directory.Exists(S.SaveFolder) ? S.SaveFolder : null };
                Hide();
                if (dlg.ShowDialog() == true) { ImageExporter.Save(Out(), dlg.FileName, S.JpegQuality); Close(); } else Show();
            });
            Btn(SmartUi.GPin, null, L.T("钉到桌面"), () => { new PinWindow(_img, _rect).Show(); Close(); });
            var up = SmartUi.Fluent(SmartUi.GUpload, null, L.T("上传/分享"), () => { }, dark: true);
            up.Click += (s, e) =>
            {
                _autoClose.Stop();
                var m = UploadTargetsWindow.BuildUploadMenu(() =>
                {
                    var f = Path.Combine(Path.GetTempPath(), ImageExporter.ExpandTemplate(S) + ".png");
                    ImageExporter.Save(Out(), f); return f;
                }, t => _status.Text = t);
                m.PlacementTarget = up; m.IsOpen = true;
            };
            sp.Children.Add(up);
            Btn(SmartUi.GQr, null, L.T("识别二维码/条码"), () => { CodeReader.Run(_img); Close(); });
            Btn(SmartUi.GEdit, L.T("编辑"), L.T("在编辑器中打开"), () => { new EditorWindow(_img, App.Settings).Show(); Close(); });
            sp.Children.Add(SmartUi.Divider());
            Btn(SmartUi.GOcr, "OCR", L.T("识别文字（内置中英文 OCR）") + SmartUi.Key("OcrRegion"), () => { OcrRunner.Run(_img); Close(); }, accent: true);
            Btn(SmartUi.GFormula, L.T("公式LaTeX"), L.T("公式转LaTeX") + SmartUi.Key("FormulaRegion"), () => { FormulaWindow.Run(_img); Close(); }, accent: true);
            Btn(SmartUi.GTranslate, L.T("翻译"), L.T("识别文字并翻译（中↔英）"), () => { OcrRunner.Run(_img, null, "auto"); Close(); }, accent: true);
            Btn(SmartUi.GBlur, L.T("模糊/打码"), L.T("在编辑器中拖动打马赛克"), () => { var ed = new EditorWindow(_img, App.Settings); ed.Show(); ed.UseTool(EditorTool.Pixelate); Close(); }, accent: true);
            Btn(SmartUi.GRedact, L.T("自动打码"), L.T("在编辑器中打开并自动打码（邮箱/电话/证件号/网址/人脸）"), () => { var ed = new EditorWindow(_img, App.Settings); ed.Show(); ed.AutoRedactNow(); Close(); }, accent: true);
            sp.Children.Add(SmartUi.Divider());
            Btn(SmartUi.GClose, null, L.T("关闭") + " (Esc)", Close);
            sp.Children.Add(_status);
            KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); else if (e.Key == System.Windows.Input.Key.C && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control) { ImageExporter.CopyToClipboard(Out()); Close(); } };
            _autoClose.Tick += (s, e) => { _autoClose.Stop(); if (!IsMouseOver) Close(); else _autoClose.Start(); };
            Loaded += (s, e) => { PlacePhysical(); _autoClose.Start(); Activate(); };
        }

        /// <summary>Place below the capture rect (physical px); above it if no room; else centred on the cursor's screen.</summary>
        private void PlacePhysical()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.GetWindowRect(hwnd, out var me);
            int w = me.Width, h = me.Height;
            NativeMethods.GetCursorPos(out var cp);
            var scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cp.X, cp.Y));
            int x, y;
            if (_rect.HasValue)
            {
                var r = _rect.Value;
                scr = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)));
                var wa = scr.WorkingArea;
                x = r.X + r.Width - w;
                y = r.Y + r.Height + 6;
                if (y + h > wa.Bottom) y = r.Y - h - 6;
                if (y < wa.Top) y = r.Y + r.Height - h - 6; // inside, bottom edge
                x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
                y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - h));
            }
            else
            {
                var wa = scr.WorkingArea;
                x = wa.Left + (wa.Width - w) / 2; y = wa.Bottom - h - 40;
            }
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // NOSIZE|NOZORDER|NOACTIVATE
        }
    }
}
