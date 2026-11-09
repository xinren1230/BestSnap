using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.Plugins;
using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Manual long-image stitching: add from history/files, reorder by drag (or ↑/↓), vertical/horizontal, optional overlap detection, export.</summary>
    public class StitchWindow : Window
    {
        private readonly ObservableCollection<ImagePicker.Entry> _items = new ObservableCollection<ImagePicker.Entry>();
        private readonly ListBox _list = new ListBox { AllowDrop = true };
        private readonly RadioButton _vert = new RadioButton { Content = L.T("纵向"), IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
        private readonly RadioButton _horz = new RadioButton { Content = L.T("横向") };
        private readonly CheckBox _overlap = new CheckBox { Content = L.T("自动检测重叠并去除"), IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        private readonly Image _preview = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        private readonly TextBlock _info = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        private BitmapSource _result;
        private Point _dragStart; private ImagePicker.Entry _dragItem;

        public StitchWindow()
        {
            Title = L.T("拼接长图") + " - " + AppIdentity.Name; Width = 980; Height = 680; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _list.ItemsSource = _items;
            var tpl = new DataTemplate();
            var sp = new FrameworkElementFactory(typeof(StackPanel)); sp.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var im = new FrameworkElementFactory(typeof(Image)); im.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Thumb")); im.SetValue(WidthProperty, 64.0); im.SetValue(HeightProperty, 48.0); im.SetValue(MarginProperty, new Thickness(2, 2, 6, 2));
            var tx = new FrameworkElementFactory(typeof(StackPanel));
            var t1 = new FrameworkElementFactory(typeof(TextBlock)); t1.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            var t2 = new FrameworkElementFactory(typeof(TextBlock)); t2.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Size")); t2.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            tx.AppendChild(t1); tx.AppendChild(t2); sp.AppendChild(im); sp.AppendChild(tx); tpl.VisualTree = sp;
            _list.ItemTemplate = tpl;
            _list.PreviewMouseLeftButtonDown += (s, e) => { _dragStart = e.GetPosition(null); _dragItem = ItemAt(e.OriginalSource); };
            _list.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;
                var d = e.GetPosition(null) - _dragStart;
                if (Math.Abs(d.X) < 5 && Math.Abs(d.Y) < 5) return;
                DragDrop.DoDragDrop(_list, _dragItem, DragDropEffects.Move); _dragItem = null;
            };
            _list.Drop += (s, e) =>
            {
                if (e.Data.GetData(typeof(ImagePicker.Entry)) is ImagePicker.Entry src)
                {
                    var dst = ItemAt(e.OriginalSource); int from = _items.IndexOf(src), to = dst == null ? _items.Count - 1 : _items.IndexOf(dst);
                    if (from >= 0 && to >= 0 && from != to) { _items.Move(from, to); Rebuild(); }
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
                        try { _items.Add(ImagePicker.Make(ImagePicker.LoadFile(f), Path.GetFileName(f))); } catch { }
                    Rebuild();
                }
            };
            _list.KeyDown += (s, e) => { if (e.Key == Key.Delete) Remove(); };

            Button B(string t, RoutedEventHandler h) { var b = new Button { Content = t, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(6, 3, 6, 3) }; b.Click += h; return b; }
            var left = new StackPanel { Width = 200, Margin = new Thickness(0, 0, 10, 0) };
            left.Children.Add(B(L.T("从截图历史添加…"), (s, e) => { foreach (var x in ImagePicker.FromHistory(this)) _items.Add(x); Rebuild(); }));
            left.Children.Add(B(L.T("从文件添加…"), (s, e) => { foreach (var x in ImagePicker.FromFiles()) _items.Add(x); Rebuild(); }));
            left.Children.Add(B(L.T("从剪贴板添加"), (s, e) => { if (Clipboard.ContainsImage()) { _items.Add(ImagePicker.Make(CaptureService.Normalize(Clipboard.GetImage()), L.T("剪贴板"))); Rebuild(); } }));
            left.Children.Add(B("↑ " + L.T("上移"), (s, e) => Move(-1)));
            left.Children.Add(B("↓ " + L.T("下移"), (s, e) => Move(1)));
            left.Children.Add(B(L.T("删除"), (s, e) => Remove()));
            var dir = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) }; dir.Children.Add(_vert); dir.Children.Add(_horz);
            left.Children.Add(dir); left.Children.Add(_overlap);
            _vert.Checked += (s, e) => Rebuild(); _horz.Checked += (s, e) => Rebuild(); _overlap.Click += (s, e) => Rebuild();
            left.Children.Add(new TextBlock { Text = L.T("拖动列表项可调整顺序；也可直接把图片文件拖进来。"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0) });
            left.Children.Add(_info);
            var exp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            exp.Children.Add(B(L.T("复制到剪贴板"), (s, e) => { if (_result != null) ImageExporter.CopyToClipboard(_result); }));
            exp.Children.Add(B(L.T("另存为…"), (s, e) => Export()));
            exp.Children.Add(B(L.T("在编辑器中打开"), (s, e) => { if (_result != null) new EditorWindow(_result, App.Settings).Show(); }));
            left.Children.Add(exp);

            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(left);
            Grid.SetColumn(_list, 1); grid.Children.Add(_list);
            var pv = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _preview, Margin = new Thickness(10, 0, 0, 0), Background = Brushes.WhiteSmoke };
            Grid.SetColumn(pv, 2); grid.Children.Add(pv);
            Content = grid;
        }


        public void Add(BitmapSource img, string name) { _items.Add(ImagePicker.Make(img, name)); Rebuild(); }

        private ImagePicker.Entry ItemAt(object src)
        {
            var d = src as DependencyObject;
            while (d != null && !(d is ListBoxItem)) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return (d as ListBoxItem)?.DataContext as ImagePicker.Entry;
        }
        private void Move(int delta)
        {
            int i = _list.SelectedIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= _items.Count) return;
            _items.Move(i, j); _list.SelectedIndex = j; Rebuild();
        }
        private void Remove() { if (_list.SelectedItem is ImagePicker.Entry e) { _items.Remove(e); Rebuild(); } }

        private int _ver;
        private async void Rebuild()
        {
            int ver = ++_ver;
            if (_items.Count == 0) { _result = null; _preview.Source = null; _info.Text = ""; return; }
            var imgs = _items.Select(x => x.Image).ToList(); bool v = _vert.IsChecked == true, ov = _overlap.IsChecked == true;
            _info.Text = L.T("处理中…");
            try
            {
                var r = await System.Threading.Tasks.Task.Run(() => ImageOps.Stitch(imgs, v, ov, Colors.White));
                if (ver != _ver) return;
                _result = r; _preview.Source = r; _info.Text = r.PixelWidth + " × " + r.PixelHeight + " px";
            }
            catch (Exception ex) { Logger.Error("Stitch", ex); _info.Text = ex.Message; }
        }

        private void Export()
        {
            if (_result == null) return;
            var S = App.Settings.Current;
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = ImageExporter.SaveFilter, FileName = "Stitched_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") };
            if (dlg.ShowDialog(this) == true) { ImageExporter.Save(_result, dlg.FileName, S.JpegQuality); _info.Text = L.T("已保存: ") + dlg.FileName; }
        }
    }
}
