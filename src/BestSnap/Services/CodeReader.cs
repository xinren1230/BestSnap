using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>QR / barcode recognition (ZXing.Net core, fed raw BGRA pixels; no imaging bindings).</summary>
    public static class CodeReader
    {
        public static List<Result> Decode(BitmapSource img)
        {
            var src = img.Format == PixelFormats.Bgra32 || img.Format == PixelFormats.Pbgra32 ? img : new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var px = new byte[stride * h]; src.CopyPixels(px, stride, 0);
            var reader = new BarcodeReaderGeneric { AutoRotate = true, Options = new DecodingOptions { TryHarder = true, TryInverted = true } };
            var lum = new RGBLuminanceSource(px, w, h, RGBLuminanceSource.BitmapFormat.BGRA32);
            var many = reader.DecodeMultiple(lum);
            if (many != null && many.Length > 0) return many.GroupBy(r => r.Text).Select(g => g.First()).ToList();
            var one = reader.Decode(lum);
            return one != null ? new List<Result> { one } : new List<Result>();
        }

        public static void Run(BitmapSource img, Window owner = null)
        {
            List<Result> res;
            try { res = Decode(img); }
            catch (Exception ex) { Logger.Error("CodeReader", ex); MessageBox.Show(L.T("识别失败") + ": " + ex.Message, AppIdentity.Name); return; }
            var w = new Window { Title = L.T("二维码/条码识别结果"), Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 600, WindowStartupLocation = WindowStartupLocation.CenterScreen, Owner = owner?.IsVisible == true ? owner : null };
            var sp = new StackPanel { Margin = new Thickness(12) };
            if (res.Count == 0) sp.Children.Add(new TextBlock { Text = L.T("未识别到二维码或条码。"), Margin = new Thickness(0, 0, 0, 8) });
            foreach (var r in res)
            {
                var box = new TextBox { Text = r.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
                row.Children.Add(new TextBlock { Text = r.BarcodeFormat.ToString(), Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                var copy = new Button { Content = L.T("复制"), Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
                var text = r.Text;
                copy.Click += (s, e) => { try { Clipboard.SetText(text); } catch { } };
                row.Children.Add(copy);
                if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                {
                    var open = new Button { Content = L.T("打开链接"), Padding = new Thickness(10, 2, 10, 2), ToolTip = u.AbsoluteUri };
                    open.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { Logger.Error("OpenLink", ex); } };
                    row.Children.Add(open);
                }
                sp.Children.Add(box); sp.Children.Add(row);
            }
            var close = new Button { Content = L.T("关闭"), Width = 90, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
            close.Click += (s, e) => w.Close(); sp.Children.Add(close);
            w.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            w.Show();
        }
    }
}
