using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BestSnap.Services;

namespace BestSnap.Views
{
    /// <summary>Shared "add images" helpers: from files or from the capture history (multi-select).</summary>
    public static class ImagePicker
    {
        public sealed class Entry { public string Name { get; set; } public BitmapSource Image { get; set; } public BitmapSource Thumb { get; set; } public string Size => Image.PixelWidth + "×" + Image.PixelHeight; }

        public static Entry Make(BitmapSource img, string name)
        {
            if (!img.IsFrozen && img.CanFreeze) img.Freeze();
            double s = Math.Min(1.0, 96.0 / Math.Max(img.PixelWidth, img.PixelHeight));
            BitmapSource t = s < 1 ? new TransformedBitmap(img, new System.Windows.Media.ScaleTransform(s, s)) : img;
            if (!t.IsFrozen) t.Freeze();
            return new Entry { Name = name, Image = img, Thumb = t };
        }

        public static BitmapSource LoadFile(string f)
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(f); bi.EndInit(); bi.Freeze();
            return CaptureService.Normalize(bi);
        }

        public static List<Entry> FromFiles(bool multi = true)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"), Multiselect = multi };
            var r = new List<Entry>();
            if (dlg.ShowDialog() != true) return r;
            foreach (var f in dlg.FileNames)
                try { r.Add(Make(LoadFile(f), Path.GetFileName(f))); } catch (Exception ex) { Logger.Error("Load " + f, ex); }
            return r;
        }

        public static List<Entry> FromHistory(Window owner, bool multi = true)
        {
            var items = HistoryService.List();
            var lb = new ListBox { SelectionMode = multi ? SelectionMode.Extended : SelectionMode.Single };
            ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
            lb.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
            foreach (var h in items)
            {
                var sp = new StackPanel { Margin = new Thickness(4), Tag = h };
                try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(File.Exists(h.Thumb) ? h.Thumb : h.File); bi.DecodePixelWidth = 140; bi.EndInit(); sp.Children.Add(new Image { Source = bi, Width = 140, Height = 90 }); } catch { }
                sp.Children.Add(new TextBlock { Text = h.Label, HorizontalAlignment = HorizontalAlignment.Center });
                lb.Items.Add(sp);
            }
            var ok = new Button { Content = L.T("确定"), Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = L.T("取消"), Width = 90, IsCancel = true };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            bar.Children.Add(ok); bar.Children.Add(cancel);
            var dock = new DockPanel { Margin = new Thickness(10) }; DockPanel.SetDock(bar, Dock.Bottom); dock.Children.Add(bar); dock.Children.Add(lb);
            var w = new Window { Title = L.T("从截图历史添加"), Width = 720, Height = 520, Content = dock, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            ok.Click += (s, e) => w.DialogResult = true;
            lb.MouseDoubleClick += (s, e) => { if (lb.SelectedItem != null) w.DialogResult = true; };
            var r = new List<Entry>();
            if (w.ShowDialog() != true) return r;
            // keep visual order, not click order
            foreach (var it in lb.Items.Cast<StackPanel>().Where(x => lb.SelectedItems.Contains(x)))
            {
                var h = (HistoryItem)it.Tag;
                try { r.Add(Make(HistoryService.Load(h.File), h.Label)); } catch (Exception ex) { Logger.Error("History load", ex); }
            }
            return r;
        }
    }
}
