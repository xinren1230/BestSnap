using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using SnapForge.Plugins;

namespace SnapForge.Services
{
    public class HistoryItem
    {
        public string File { get; set; }
        public string Thumb { get; set; }
        public DateTime Time { get; set; }
        public string Label => Time.ToString("MM-dd HH:mm:ss");
    }

    /// <summary>Keeps the last N captures as PNG (+ 240px thumbnail) in %LocalAppData%\BestSnap\History.</summary>
    public static class HistoryService
    {
        public static string Folder => AppPaths.History;
        public static event Action Changed;

        public static void Add(BitmapSource img, int max)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var name = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                var file = Path.Combine(Folder, name + ".png");
                ImageExporter.Save(img, file, 90);
                double s = Math.Min(1.0, 240.0 / Math.Max(img.PixelWidth, img.PixelHeight));
                BitmapSource th = s < 1 ? new TransformedBitmap(img, new System.Windows.Media.ScaleTransform(s, s)) : img;
                ImageExporter.Save(th, Path.Combine(Folder, name + ".thumb.jpg"), 80);
                Trim(max);
                Changed?.Invoke();
            }
            catch (Exception ex) { Logger.Error("History.Add", ex); }
        }

        public static List<HistoryItem> List() =>
            !Directory.Exists(Folder) ? new List<HistoryItem>() :
            Directory.GetFiles(Folder, "*.png").Select(f => new HistoryItem
            {
                File = f, Thumb = Path.ChangeExtension(f, ".thumb.jpg"), Time = File.GetLastWriteTime(f)
            }).OrderByDescending(h => h.Time).ToList();

        public static void Delete(HistoryItem h)
        {
            try { File.Delete(h.File); if (File.Exists(h.Thumb)) File.Delete(h.Thumb); } catch (Exception ex) { Logger.Error("History.Delete", ex); }
            Changed?.Invoke();
        }

        public static void Clear() { foreach (var h in List()) Delete(h); }

        private static void Trim(int max)
        {
            foreach (var h in List().Skip(Math.Max(1, max)))
            { try { File.Delete(h.File); if (File.Exists(h.Thumb)) File.Delete(h.Thumb); } catch { } }
        }

        /// <summary>Load fully into memory (no file lock) and freeze.</summary>
        public static BitmapSource Load(string file)
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; bi.UriSource = new Uri(file); bi.EndInit(); bi.Freeze();
            return CaptureService.Normalize(bi);
        }
    }
}
