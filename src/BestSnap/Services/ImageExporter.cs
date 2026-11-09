using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using BestSnap.Models;

namespace BestSnap.Services
{
    public static class ImageExporter
    {
        public const string SaveFilter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp|GIF|*.gif|TIFF|*.tif|PDF|*.pdf";

        /// <summary>Supported placeholders. Anything else in braces is kept literally.</summary>
        public static readonly string[] Placeholders = { "n", "yyyy", "yy", "MM", "dd", "HH", "mm", "ss", "fff", "yyyyMMdd", "HHmmss", "yyyy-MM-dd", "HH-mm-ss", "date", "time" };
        public const string PlaceholderHelp = "{n}=序号  {yyyy}{MM}{dd}{HH}{mm}{ss}{fff}  {date}=yyyy-MM-dd  {time}=HH-mm-ss";

        /// <summary>Expands the file-name template. commit=false only previews (counter unchanged); commit=true consumes the counter.</summary>
        public static string ExpandTemplate(AppSettings s, bool commit = false)
        {
            var now = DateTime.Now;
            int n = s.Counter;
            string name = Regex.Replace(string.IsNullOrWhiteSpace(s.FileNameTemplate) ? "Snap_{n}" : s.FileNameTemplate, @"\{([\w\-]+)\}", m =>
            {
                var t = m.Groups[1].Value;
                if (Array.IndexOf(Placeholders, t) < 0) return m.Value;
                switch (t)
                {
                    case "n": return n.ToString("D4");
                    case "date": return now.ToString("yyyy-MM-dd");
                    case "time": return now.ToString("HH-mm-ss");
                    default: return now.ToString(t);
                }
            });
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (commit) CommitCounter(s);
            return name.Length == 0 ? "Snap" : name;
        }

        /// <summary>Advance the counter after a file was actually written (only if the template uses {n}).</summary>
        public static void CommitCounter(AppSettings s)
        {
            if ((s.FileNameTemplate ?? "Snap_{n}").Contains("{n}") || string.IsNullOrWhiteSpace(s.FileNameTemplate)) s.Counter++;
        }

        public static string AutoSave(BitmapSource img, AppSettings s)
        {
            Directory.CreateDirectory(s.SaveFolder);
            string ext = "." + s.DefaultFormat.TrimStart('.');
            string baseName = ExpandTemplate(s, commit: false);
            string path = Path.Combine(s.SaveFolder, baseName + ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(s.SaveFolder, $"{baseName} ({i}){ext}");
            Save(img, path, s.JpegQuality);
            CommitCounter(s);
            return path;
        }

        /// <summary>Puts both a DIB (for classic apps) and a PNG stream (keeps alpha; Office/browsers prefer it) on the clipboard.</summary>
        public static void CopyToClipboard(BitmapSource img)
        {
            var data = new DataObject();
            data.SetImage(img);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img));
            var ms = new MemoryStream(); enc.Save(ms); ms.Position = 0;
            data.SetData("PNG", ms, false);
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch (Exception ex) { if (i == 4) Logger.Error("Clipboard", ex); System.Threading.Thread.Sleep(50); }
            }
        }

        public static void Save(BitmapSource img, string path, int jpegQuality = 90)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf") { SavePdf(img, path, jpegQuality); return; }
            BitmapEncoder enc;
            switch (ext)
            {
                case ".jpg": case ".jpeg": enc = new JpegBitmapEncoder { QualityLevel = jpegQuality }; break;
                case ".bmp": enc = new BmpBitmapEncoder(); break;
                case ".gif": enc = new GifBitmapEncoder(); break;
                case ".tif": case ".tiff": enc = new TiffBitmapEncoder(); break;
                default: enc = new PngBitmapEncoder(); break;
            }
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        /// <summary>Minimal single-page PDF with the image embedded as JPEG (DCTDecode). No dependencies.</summary>
        public static void SavePdf(BitmapSource img, string path, int q)
        {
            byte[] jpg;
            var enc = new JpegBitmapEncoder { QualityLevel = q };
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var ms = new MemoryStream()) { enc.Save(ms); jpg = ms.ToArray(); }
            int w = img.PixelWidth, h = img.PixelHeight;
            using (var fs = File.Create(path))
            {
                var offsets = new long[6];
                void W(string str) { var b = Encoding.ASCII.GetBytes(str); fs.Write(b, 0, b.Length); }
                W("%PDF-1.4\n");
                offsets[1] = fs.Position; W("1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n");
                offsets[2] = fs.Position; W("2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n");
                offsets[3] = fs.Position; W($"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 {w} {h}]/Resources<</XObject<</Im0 4 0 R>>>>/Contents 5 0 R>>endobj\n");
                offsets[4] = fs.Position; W($"4 0 obj<</Type/XObject/Subtype/Image/Width {w}/Height {h}/ColorSpace/DeviceRGB/BitsPerComponent 8/Filter/DCTDecode/Length {jpg.Length}>>stream\n");
                fs.Write(jpg, 0, jpg.Length); W("\nendstream endobj\n");
                string content = $"q {w} 0 0 {h} 0 0 cm /Im0 Do Q";
                offsets[5] = fs.Position; W($"5 0 obj<</Length {content.Length}>>stream\n{content}\nendstream endobj\n");
                long xref = fs.Position;
                W("xref\n0 6\n0000000000 65535 f \n");
                for (int i = 1; i <= 5; i++) W(offsets[i].ToString("D10") + " 00000 n \n");
                W($"trailer<</Size 6/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
            }
        }
    }
}
