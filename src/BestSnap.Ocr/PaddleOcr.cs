using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using BestSnap.Plugins;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace BestSnap.Ocr
{
    /// <summary>
    /// Built-in offline Chinese + English OCR: PaddleOCR PP-OCRv4 mobile det + rec (Apache-2.0, ONNX exports from RapidOCR),
    /// run with ONNX Runtime. Models ship inside the plugin folder (embedded in BestSnap.exe). DB text detection with
    /// axis-aligned boxes, CTC greedy decoding; boxes are grouped back into lines.
    /// </summary>
    public static class PaddleOcr
    {
        private static InferenceSession _det, _rec; private static string[] _chars;
        private static readonly object _gate = new object();
        public static string ModelDir => Path.Combine(Path.GetDirectoryName(typeof(PaddleOcr).Assembly.Location) ?? AppContext.BaseDirectory, "models");
        public static bool Available => File.Exists(Path.Combine(ModelDir, "ch_PP-OCRv4_det_infer.onnx")) && File.Exists(Path.Combine(ModelDir, "ch_PP-OCRv4_rec_infer.onnx"));

        // Det: slightly looser than stock RapidOCR so small / low-contrast / edge text is kept.
        const int MaxSide = 1280, MinLong = 640, PadPx = 16;
        const float BinThresh = 0.25f, BoxThresh = 0.42f;
        const double Unclip = 1.8;

        private static void Load()
        {
            if (_rec != null) return;
            var o = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)) };
            _det = new InferenceSession(Path.Combine(ModelDir, "ch_PP-OCRv4_det_infer.onnx"), o);
            _rec = new InferenceSession(Path.Combine(ModelDir, "ch_PP-OCRv4_rec_infer.onnx"), o);
            var dict = _rec.ModelMetadata.CustomMetadataMap["character"].Replace("\r", "").Split('\n');
            _chars = new[] { "" }.Concat(dict).Concat(new[] { " " }).ToArray();
        }

        private sealed class Img { public int W, H; public byte[] Bgra; }

        private static async Task<Img> Decode(byte[] png)
        {
            using var ms = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(ms.GetOutputStreamAt(0))) { w.WriteBytes(png); await w.StoreAsync(); await w.FlushAsync(); w.DetachStream(); }
            var dec = await BitmapDecoder.CreateAsync(ms);
            var pd = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var px = pd.DetachPixelData();
            for (int i = 0; i < px.Length; i += 4) // transparent → white
            {
                int a = px[i + 3]; if (a == 255) continue;
                for (int c = 0; c < 3; c++) px[i + c] = (byte)((px[i + c] * a + 255 * (255 - a)) / 255);
            }
            return new Img { W = (int)dec.PixelWidth, H = (int)dec.PixelHeight, Bgra = px };
        }

        static void LumaStats(Img im, out double mean, out byte min, out byte max)
        {
            long sum = 0; int lo = 255, hi = 0, n = im.W * im.H;
            var p = im.Bgra;
            for (int i = 0; i < p.Length; i += 4)
            {
                int y = (29 * p[i] + 150 * p[i + 1] + 77 * p[i + 2]) >> 8; // B,G,R
                sum += y; if (y < lo) lo = y; if (y > hi) hi = y;
            }
            mean = n == 0 ? 0 : (double)sum / n; min = (byte)lo; max = (byte)hi;
        }

        static Img Pad(Img im, int p)
        {
            if (p <= 0) return im;
            int nw = im.W + 2 * p, nh = im.H + 2 * p;
            var d = new byte[nw * nh * 4];
            long b = 0, g = 0, r = 0; int n = Math.Max(1, im.W * im.H);
            var s = im.Bgra;
            for (int i = 0; i < s.Length; i += 4) { b += s[i]; g += s[i + 1]; r += s[i + 2]; }
            byte mb = (byte)(b / n), mg = (byte)(g / n), mr = (byte)(r / n);
            for (int i = 0; i < d.Length; i += 4) { d[i] = mb; d[i + 1] = mg; d[i + 2] = mr; d[i + 3] = 255; }
            for (int y = 0; y < im.H; y++) System.Buffer.BlockCopy(s, y * im.W * 4, d, ((y + p) * nw + p) * 4, im.W * 4);
            return new Img { W = nw, H = nh, Bgra = d };
        }

        static void Bilinear(Img im, double sx, double sy, out float b, out float g, out float r)
        {
            int x1 = (int)Math.Floor(sx), y1 = (int)Math.Floor(sy);
            double tx = sx - x1, ty = sy - y1;
            int xa = Math.Clamp(x1, 0, im.W - 1), xb = Math.Clamp(x1 + 1, 0, im.W - 1);
            int ya = Math.Clamp(y1, 0, im.H - 1), yb = Math.Clamp(y1 + 1, 0, im.H - 1);
            float S(int c) => (float)((im.Bgra[(ya * im.W + xa) * 4 + c] * (1 - tx) + im.Bgra[(ya * im.W + xb) * 4 + c] * tx) * (1 - ty)
                                    + (im.Bgra[(yb * im.W + xa) * 4 + c] * (1 - tx) + im.Bgra[(yb * im.W + xb) * 4 + c] * tx) * ty);
            b = S(0); g = S(1); r = S(2);
        }

        /// <summary>Bilinear sample of a sub-rectangle resized to (w,h).</summary>
        private static void Resize(Img im, double x0, double y0, double sw, double sh, int w, int h, Action<int, int, float, float, float> put)
        {
            double fx = sw / w, fy = sh / h;
            for (int y = 0; y < h; y++)
            {
                double sy = y0 + (y + 0.5) * fy - 0.5;
                for (int x = 0; x < w; x++)
                {
                    double sx = x0 + (x + 0.5) * fx - 0.5;
                    Bilinear(im, sx, sy, out float b, out float g, out float r);
                    put(x, y, b, g, r);
                }
            }
        }

        private struct Box { public double X0, Y0, X1, Y1; public string Text; }

        static double DetScale(int w, int h)
        {
            double lon = Math.Max(w, h), shor = Math.Min(w, h);
            double s = 1;
            if (lon > MaxSide) s = (double)MaxSide / lon;
            else
            {
                // Small captures: ~2× so 12–16px glyphs reach the rec net (~48px). Cap at MaxSide.
                if (h < 400) s = Math.Min(2.0, (double)MaxSide / lon);
                else if (lon < MinLong) s = Math.Min(2.0, (double)MinLong / lon);
            }
            if (shor * s < 48) s = Math.Max(s, 48.0 / Math.Max(1, shor));
            if (lon * s > MaxSide) s = (double)MaxSide / lon;
            return s;
        }

        static List<Box> DetectOnce(Img im, bool invert, byte lumaMin, byte lumaMax)
        {
            double s = DetScale(im.W, im.H);
            int w = Math.Max(32, (int)Math.Round(im.W * s / 32) * 32), h = Math.Max(32, (int)Math.Round(im.H * s / 32) * 32);
            var t = new DenseTensor<float>(new[] { 1, 3, h, w });
            float[] mean = { 0.485f, 0.456f, 0.406f }, std = { 0.229f, 0.224f, 0.225f };
            bool stretch = lumaMax > lumaMin && (lumaMax - lumaMin) < 55;
            Resize(im, 0, 0, im.W, im.H, w, h, (x, y, b, g, r) =>
            {
                if (invert) { b = 255 - b; g = 255 - g; r = 255 - r; }
                if (stretch)
                {
                    float srcL = 0.114f * b + 0.587f * g + 0.299f * r;
                    float lo = invert ? 255 - lumaMax : lumaMin, hi = invert ? 255 - lumaMin : lumaMax;
                    float L = (srcL - lo) / Math.Max(1f, hi - lo) * 255f;
                    b = g = r = Math.Clamp(L, 0, 255);
                }
                t[0, 0, y, x] = (b / 255f - mean[0]) / std[0]; t[0, 1, y, x] = (g / 255f - mean[1]) / std[1]; t[0, 2, y, x] = (r / 255f - mean[2]) / std[2];
            });
            float[] p;
            using (var res = _det.Run(new[] { NamedOnnxValue.CreateFromTensor(_det.InputMetadata.Keys.First(), t) })) p = res.First().AsTensor<float>().ToArray();
            var lab = new int[w * h]; var boxes = new List<Box>(); var stack = new Stack<int>(); int id = 0;
            double sx = (double)im.W / w, sy = (double)im.H / h;
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i] <= BinThresh || lab[i] != 0) continue;
                id++; int x0 = w, y0 = h, x1 = 0, y1 = 0, n = 0; double sum = 0;
                stack.Push(i); lab[i] = id;
                while (stack.Count > 0)
                {
                    int k = stack.Pop(), kx = k % w, ky = k / w; n++; sum += p[k];
                    if (kx < x0) x0 = kx; if (kx > x1) x1 = kx; if (ky < y0) y0 = ky; if (ky > y1) y1 = ky;
                    void V(int q) { if (lab[q] == 0 && p[q] > BinThresh) { lab[q] = id; stack.Push(q); } }
                    if (kx > 0) V(k - 1); if (kx < w - 1) V(k + 1); if (ky > 0) V(k - w); if (ky < h - 1) V(k + w);
                }
                int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
                if (Math.Min(bw, bh) < 2 || Math.Max(bw, bh) < 4 || sum / n < BoxThresh) continue;
                double d = bw * bh * Unclip / (2.0 * (bw + bh));
                boxes.Add(new Box { X0 = Math.Max(0, (x0 - d) * sx), Y0 = Math.Max(0, (y0 - d) * sy), X1 = Math.Min(im.W, (x1 + 1 + d) * sx), Y1 = Math.Min(im.H, (y1 + 1 + d) * sy) });
            }
            return boxes;
        }

        static List<Box> Nms(List<Box> boxes)
        {
            var a = boxes.OrderByDescending(b => Math.Max(0, b.X1 - b.X0) * Math.Max(0, b.Y1 - b.Y0)).ToList();
            var keep = new List<Box>();
            foreach (var b in a)
            {
                bool hit = false;
                foreach (var k in keep)
                {
                    double x0 = Math.Max(b.X0, k.X0), y0 = Math.Max(b.Y0, k.Y0), x1 = Math.Min(b.X1, k.X1), y1 = Math.Min(b.Y1, k.Y1);
                    double inter = Math.Max(0, x1 - x0) * Math.Max(0, y1 - y0);
                    double area = Math.Min(Math.Max(1, (b.X1 - b.X0) * (b.Y1 - b.Y0)), Math.Max(1, (k.X1 - k.X0) * (k.Y1 - k.Y0)));
                    if (inter / area > 0.5) { hit = true; break; }
                }
                if (!hit) keep.Add(b);
            }
            return keep;
        }

        private static List<Box> Detect(Img im)
        {
            LumaStats(im, out double mean, out byte lo, out byte hi);
            var work = Pad(im, PadPx);
            var boxes = DetectOnce(work, invert: false, lo, hi);
            if (mean < 90 || boxes.Count == 0)
                boxes = Nms(Concat(boxes, DetectOnce(work, invert: true, lo, hi)));
            var mapped = new List<Box>(boxes.Count);
            foreach (var b in boxes)
            {
                var m = new Box
                {
                    X0 = Math.Clamp(b.X0 - PadPx, 0, im.W),
                    Y0 = Math.Clamp(b.Y0 - PadPx, 0, im.H),
                    X1 = Math.Clamp(b.X1 - PadPx, 0, im.W),
                    Y1 = Math.Clamp(b.Y1 - PadPx, 0, im.H)
                };
                if (m.X1 - m.X0 >= 4 && m.Y1 - m.Y0 >= 4) mapped.Add(m);
            }
            return mapped;
        }

        static List<Box> Concat(List<Box> a, List<Box> b)
        {
            var r = new List<Box>(a.Count + b.Count); r.AddRange(a); r.AddRange(b); return r;
        }

        enum CropRot { Upright, Cw90, Ccw90 }

        static int GlyphScore(string s)
        {
            int n = 0;
            if (s == null) return 0;
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (OcrText.IsCjk(c) || char.IsLetterOrDigit(c)) n += 2;
                else n += 1;
            }
            return n;
        }

        private static string Recognize(Img im, Box b) => RecognizeBest(im, b);

        static string RecognizeBest(Img im, Box b)
        {
            double bw = b.X1 - b.X0, bh = b.Y1 - b.Y0;
            string u = RecognizeRot(im, b, CropRot.Upright);
            if (bh <= bw * 1.35) return u;
            string cw = RecognizeRot(im, b, CropRot.Cw90);
            string ccw = RecognizeRot(im, b, CropRot.Ccw90);
            string best = u; int sc = GlyphScore(u);
            int s2 = GlyphScore(cw); if (s2 > sc) { sc = s2; best = cw; }
            int s3 = GlyphScore(ccw); if (s3 > sc) best = ccw;
            return best;
        }

        static string RecognizeRot(Img im, Box box, CropRot rot)
        {
            double bw = box.X1 - box.X0, bh = box.Y1 - box.Y0; if (bw < 2 || bh < 2) return "";
            const int H = 48;
            int w; bool upright = rot == CropRot.Upright;
            if (upright) w = Math.Clamp((int)Math.Ceiling(H * bw / bh), 8, 3200);
            else w = Math.Clamp((int)Math.Ceiling(H * bh / bw), 8, 3200);
            var t = new DenseTensor<float>(new[] { 1, 3, H, w });
            void Put(int x, int y, float bl, float g, float r)
            {
                t[0, 0, y, x] = (bl / 255f - 0.5f) / 0.5f; t[0, 1, y, x] = (g / 255f - 0.5f) / 0.5f; t[0, 2, y, x] = (r / 255f - 0.5f) / 0.5f;
            }
            if (upright) Resize(im, box.X0, box.Y0, bw, bh, w, H, Put);
            else
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < w; x++)
                    {
                        double sx, sy;
                        if (rot == CropRot.Cw90)
                        {
                            sx = box.X0 + bw - (y + 0.5) * bw / H;
                            sy = box.Y0 + (x + 0.5) * bh / w;
                        }
                        else
                        {
                            sx = box.X0 + (y + 0.5) * bw / H;
                            sy = box.Y0 + bh - (x + 0.5) * bh / w;
                        }
                        Bilinear(im, sx, sy, out float bl, out float g, out float r);
                        Put(x, y, bl, g, r);
                    }
            }
            using var res = _rec.Run(new[] { NamedOnnxValue.CreateFromTensor(_rec.InputMetadata.Keys.First(), t) });
            var o = res.First().AsTensor<float>(); int T = o.Dimensions[1], C = o.Dimensions[2];
            var arr = o.ToArray(); var glyphs = new List<(string, double)>(); int prev = -1;
            for (int j = 0; j < T; j++)
            {
                int best = 0; float bv = float.MinValue;
                for (int c = 0; c < C; c++) { float v = arr[j * C + c]; if (v > bv) { bv = v; best = c; } }
                if (best != 0 && best != prev && best < _chars.Length)
                {
                    var ch = _chars[best];
                    if (!string.IsNullOrEmpty(ch)) glyphs.Add((ch, j));
                }
                prev = best;
            }
            // em = rec-crop height (48px) in CTC timesteps, so word gaps can be judged vs char height.
            double em = w > 0 ? 48.0 * T / w : 0;
            return OcrText.RestoreLatinSpaces(glyphs, em);
        }

        /// <summary>Returns plain text in reading order (lines, paragraphs, simple columns).</summary>
        public static async Task<string> RecognizeAsync(byte[] png, CancellationToken ct)
        {
            var im = await Decode(png);
            ct.ThrowIfCancellationRequested();
            List<Box> boxes;
            lock (_gate)
            {
                Load();
                boxes = Detect(im);
                for (int i = 0; i < boxes.Count; i++) { ct.ThrowIfCancellationRequested(); var b = boxes[i]; b.Text = Recognize(im, b); boxes[i] = b; }
            }
            var spans = new List<OcrSpan>(boxes.Count);
            foreach (var b in boxes)
            {
                if (string.IsNullOrWhiteSpace(b.Text)) continue;
                spans.Add(new OcrSpan(b.Text.Trim(), b.X0, b.Y0, b.X1, b.Y1));
            }
            return OcrText.PostProcess(OcrLayout.Format(spans));
        }
    }
}
