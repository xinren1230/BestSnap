using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BestSnap.Plugins;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Thumbnail grid of recent captures: open in editor, copy, pin, save, delete.</summary>
    public class HistoryWindow : Window
    {
        private readonly ListBox _list = new ListBox();
        private static HistoryWindow _instance;

        public static void ShowSingle()
        {
            if (_instance == null) { _instance = new HistoryWindow(); _instance.Closed += (s, e) => _instance = null; }
            _instance.Show(); _instance.Activate();
        }

        private HistoryWindow()
        {
            Title = L.T("截图历史") + " - " + AppIdentity.Name; Width = 820; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            var wrap = new FrameworkElementFactory(typeof(WrapPanel));
            _list.ItemsPanel = new ItemsPanelTemplate(wrap);
            _list.SelectionMode = SelectionMode.Extended;
            var tpl = new DataTemplate();
            var sp = new FrameworkElementFactory(typeof(StackPanel)); sp.SetValue(MarginProperty, new Thickness(4)); sp.SetValue(WidthProperty, 180.0);
            var img = new FrameworkElementFactory(typeof(Image)); img.SetValue(HeightProperty, 120.0);
            img.SetBinding(Image.SourceProperty, new Binding("Thumb") { Converter = new ThumbConverter() });
            var tb = new FrameworkElementFactory(typeof(TextBlock)); tb.SetBinding(TextBlock.TextProperty, new Binding("Label")); tb.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            sp.AppendChild(img); sp.AppendChild(tb); tpl.VisualTree = sp;
            _list.ItemTemplate = tpl;
            _list.MouseDoubleClick += (s, e) => Open();
            _list.KeyDown += (s, e) => { if (e.Key == Key.Delete) Delete(); else if (e.Key == Key.Enter) Open(); };

            var bar = new ToolBar();
            void B(string t, Action a) { var b = new Button { Content = t, Margin = new Thickness(2) }; b.Click += (s, e) => a(); bar.Items.Add(b); }
            B(L.T("编辑"), Open); B(L.T("复制"), () => { if (Sel() is HistoryItem h) ImageExporter.CopyToClipboard(HistoryService.Load(h.File)); });
            B(L.T("钉到桌面"), () => { foreach (var h in _list.SelectedItems.Cast<HistoryItem>()) new PinWindow(HistoryService.Load(h.File)).Show(); });
            B(L.T("另存为…"), SaveAs); B(L.T("删除"), Delete);
            bar.Items.Add(new Separator());
            B(L.T("打开文件夹"), () => { try { System.IO.Directory.CreateDirectory(HistoryService.Folder); System.Diagnostics.Process.Start("explorer.exe", HistoryService.Folder); } catch { } });
            B(L.T("清空历史"), () => { if (MessageBox.Show(this, L.T("确定删除全部截图历史吗？"), AppIdentity.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) HistoryService.Clear(); });

            var dock = new DockPanel(); DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar); dock.Children.Add(_list);
            Content = dock;
            Action refresh = () => Dispatcher.BeginInvoke(new Action(Refresh));
            HistoryService.Changed += refresh;
            Closed += (s, e) => HistoryService.Changed -= refresh;
            Refresh();
        }

        private void Refresh() => _list.ItemsSource = HistoryService.List();
        private HistoryItem Sel() => _list.SelectedItem as HistoryItem;
        private void Open() { if (Sel() is HistoryItem h) new EditorWindow(HistoryService.Load(h.File), App.Settings).Show(); }
        private void Delete() { foreach (var h in _list.SelectedItems.Cast<HistoryItem>().ToList()) HistoryService.Delete(h); }
        private void SaveAs()
        {
            if (!(Sel() is HistoryItem h)) return;
            var s = App.Settings.Current;
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = ImageExporter.SaveFilter, FileName = ImageExporter.ExpandTemplate(s, commit: false) };
            if (dlg.ShowDialog(this) == true) { ImageExporter.Save(HistoryService.Load(h.File), dlg.FileName, s.JpegQuality); ImageExporter.CommitCounter(s); App.Settings.Save(); }
        }

        private class ThumbConverter : IValueConverter
        {
            public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
            {
                var f = v as string;
                if (f == null || !System.IO.File.Exists(f)) return null;
                try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(f); bi.EndInit(); bi.Freeze(); return bi; }
                catch { return null; }
            }
            public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
        }
    }
}
