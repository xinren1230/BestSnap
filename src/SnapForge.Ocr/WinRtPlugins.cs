using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnapForge.Plugins;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SnapForge.Ocr
{
    public sealed class WindowsOcrEngine : IOcrEngine
    {
        public string Id => "windows-ocr";
        public string DisplayName => PaddleOcr.Available ? "PP-OCR + Windows OCR" : "Windows OCR";
        internal static bool WinAvailable { get { try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; } catch { return false; } } }
        public bool IsAvailable => PaddleOcr.Available || WinAvailable;
        public IReadOnlyList<string> Languages => OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

        /// <summary>Auto: Chinese/Japanese/Korean recognizers if installed + the user-profile recognizer.</summary>
        internal static List<OcrEngine> AutoEngines()
        {
            var list = new List<OcrEngine>();
            void Add(OcrEngine e) { if (e != null && !list.Any(x => x.RecognizerLanguage.LanguageTag == e.RecognizerLanguage.LanguageTag)) list.Add(e); }
            var avail = OcrEngine.AvailableRecognizerLanguages.ToList();
            foreach (var pre in new[] { "zh-Hans", "zh-CN", "zh-Hant", "zh-TW", "zh-HK", "ja", "ko" })
                foreach (var l in avail.Where(l => l.LanguageTag.StartsWith(pre, StringComparison.OrdinalIgnoreCase))) Add(OcrEngine.TryCreateFromLanguage(l));
            Add(OcrEngine.TryCreateFromUserProfileLanguages());
            if (list.Count == 0 && avail.Count > 0) Add(OcrEngine.TryCreateFromLanguage(avail[0]));
            return list;
        }

        internal static List<OcrEngine> EnginesFor(string language)
        {
            if (string.IsNullOrEmpty(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)) return AutoEngines();
            var e = OcrEngine.TryCreateFromLanguage(new Language(language));
            if (e == null) throw new InvalidOperationException("未安装该语言的 OCR 识别包 / OCR language pack not installed: " + language);
            return new List<OcrEngine> { e };
        }

        /// <summary>Decode, upscale small captures 2x (Windows OCR needs ~20px+ glyphs), grayscale.</summary>
        internal static async Task<SoftwareBitmap> PrepareAsync(byte[] png, CancellationToken ct)
        {
            using var ms = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(ms.GetOutputStreamAt(0))) { w.WriteBytes(png); await w.StoreAsync(); await w.FlushAsync(); w.DetachStream(); }
            var dec = await BitmapDecoder.CreateAsync(ms);
            ct.ThrowIfCancellationRequested();
            uint max = OcrEngine.MaxImageDimension, W = dec.PixelWidth, H = dec.PixelHeight;
            double s = 1;
            if (Math.Max(W, H) > max) s = (double)max / Math.Max(W, H);
            else if (Math.Min(W, H) < 600 || Math.Max(W, H) < 1200) s = Math.Min(2.0, (double)max / Math.Max(W, H));
            var t = new BitmapTransform { ScaledWidth = Math.Max(1u, (uint)(W * s)), ScaledHeight = Math.Max(1u, (uint)(H * s)), InterpolationMode = BitmapInterpolationMode.Fant };
            using var color = await dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, t, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return SoftwareBitmap.Convert(color, BitmapPixelFormat.Gray8);
        }

        internal static string Format(OcrResult res, string tag)
        {
            var spans = new List<OcrSpan>();
            foreach (var line in res.Lines)
            {
                foreach (var w in line.Words)
                {
                    var r = w.BoundingRect;
                    spans.Add(new OcrSpan(w.Text, r.X, r.Y, r.X + r.Width, r.Y + r.Height));
                }
            }
            if (spans.Count == 0)
            {
                // Fallback when a recognizer yields line text without word boxes.
                bool cjk = tag != null && (tag.StartsWith("zh") || tag.StartsWith("ja") || tag.StartsWith("ko"));
                var sbOut = new StringBuilder();
                foreach (var line in res.Lines)
                    sbOut.AppendLine(cjk ? JoinWords(line.Words.Select(x => x.Text)) : line.Text);
                return OcrText.PostProcess(sbOut.ToString().TrimEnd());
            }
            return OcrText.PostProcess(OcrLayout.Format(spans));
        }

        // CJK recognizers: keep a space only between two Latin/digit words.
        private static string JoinWords(IEnumerable<string> words)
        {
            var sb = new StringBuilder();
            foreach (var w in words)
            {
                if (sb.Length > 0 && w.Length > 0 && IsLatin(sb[sb.Length - 1]) && IsLatin(w[0])) sb.Append(' ');
                sb.Append(w);
            }
            return sb.ToString();
        }
        private static bool IsLatin(char c) => c < 0x2E80 && char.IsLetterOrDigit(c);

        /// <summary>Score without confidences: plausible characters, CJK glyphs weigh more, odd symbols count against.</summary>
        internal static double Score(string text)
        {
            double sc = 0;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (OcrText.CountCjk(c.ToString()) > 0) sc += 2;
                else if (char.IsLetterOrDigit(c)) sc += 1;
                else if (".,;:!?()-'\"/%，。；：！？（）、“”".IndexOf(c) >= 0) sc += 0.3;
                else sc -= 0.5;
            }
            return sc;
        }

        /// <summary>"" / "auto" / "paddle" → built-in PP-OCR (Chinese + English); "windows" → Windows OCR auto; a language tag → that Windows recognizer.</summary>
        public static bool UsesBuiltIn(string language) =>
            string.IsNullOrEmpty(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase) || language.Equals("paddle", StringComparison.OrdinalIgnoreCase);

        public async Task<string> RecognizeAsync(byte[] png, string language, CancellationToken ct)
        {
            if (UsesBuiltIn(language) && PaddleOcr.Available)
            {
                try { return await PaddleOcr.RecognizeAsync(png, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) when (!language.Equals("paddle", StringComparison.OrdinalIgnoreCase) && WinAvailable) { } // fall back to Windows OCR
            }
            if (language != null && language.Equals("windows", StringComparison.OrdinalIgnoreCase)) language = "auto";
            var engines = EnginesFor(language);
            if (engines.Count == 0) throw new InvalidOperationException("没有可用的 OCR 识别语言 / no OCR recognizer installed");
            using var bmp = await PrepareAsync(png, ct);
            string best = ""; double bestScore = double.MinValue;
            foreach (var e in engines)
            {
                ct.ThrowIfCancellationRequested();
                var text = Format(await e.RecognizeAsync(bmp), e.RecognizerLanguage.LanguageTag);
                var sc = Score(text);
                if (sc > bestScore) { bestScore = sc; best = text; }
            }
            return best;
        }
    }

    public sealed class MediaCompositionTrimmer : IVideoTrimmer
    {
        public string Id => "winrt-trim";
        public string DisplayName => "Windows Media Editing";

        public async Task TrimAsync(string input, string output, TimeSpan start, TimeSpan end, IProgress<double> progress, CancellationToken ct)
        {
            var file = await StorageFile.GetFileFromPathAsync(input);
            var clip = await MediaClip.CreateFromFileAsync(file);
            var total = clip.OriginalDuration;
            clip.TrimTimeFromStart = start < TimeSpan.Zero ? TimeSpan.Zero : start;
            clip.TrimTimeFromEnd = end >= total ? TimeSpan.Zero : total - end;
            var comp = new MediaComposition();
            comp.Clips.Add(clip);
            var dir = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(output));
            var outFile = await dir.CreateFileAsync(Path.GetFileName(output), CreationCollisionOption.ReplaceExisting);
            var op = comp.RenderToFileAsync(outFile, MediaTrimmingPreference.Precise);
            op.Progress = (o, p) => progress?.Report(p / 100.0);
            using (ct.Register(() => op.Cancel()))
            {
                var r = await op;
                if (r != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException("Trim failed: " + r);
            }
        }
    }
}
