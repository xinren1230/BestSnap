using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    /// Offline image → LaTeX with the public pix2text-mfr model (breezedeus/pix2text-mfr, MIT; DeiT encoder + TrOCR decoder, ONNX).
    /// Greedy decoding, byte-level BPE detokenizer. Model files are downloaded by the host into modelDir.
    /// </summary>
    public sealed class Pix2TextFormulaRecognizer : IFormulaRecognizer, IDisposable
    {
        public string Id => "pix2text-mfr";
        public string DisplayName => "pix2text-mfr (offline)";
        public bool IsAvailable => true;

        private const int Size = 384, Start = 2, Eos = 2, MaxTokens = 512;
        private InferenceSession _enc, _dec; private string _dir; private Dictionary<int, string> _vocab; private Dictionary<char, byte> _u2b;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private void Load(string dir)
        {
            if (_dir == dir && _enc != null) return;
            Dispose();
            var pluginMain = typeof(Pix2TextFormulaRecognizer).Assembly.Location;
            if (!PluginFileResolver.PublishedDepsArePresent(pluginMain))
                throw new FileNotFoundException(
                    "Could not load file or assembly 'Microsoft.ML.OnnxRuntime, Version=1.20.1.0, Culture=neutral, PublicKeyToken=f27f157f0a5b7bb6'. Plugin folder: " + (Path.GetDirectoryName(pluginMain) ?? pluginMain),
                    "Microsoft.ML.OnnxRuntime");
            var o = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL, IntraOpNumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)) };
            _enc = new InferenceSession(Path.Combine(dir, "encoder_model.onnx"), o);
            _dec = new InferenceSession(Path.Combine(dir, "decoder_model.onnx"), o);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "tokenizer.json")));
            _vocab = new Dictionary<int, string>();
            foreach (var p in doc.RootElement.GetProperty("model").GetProperty("vocab").EnumerateObject()) _vocab[p.Value.GetInt32()] = p.Name;
            // GPT-2 byte<->unicode table
            var bs = new List<int>(); for (int b = 33; b < 127; b++) bs.Add(b); for (int b = 161; b < 173; b++) bs.Add(b); for (int b = 174; b < 256; b++) bs.Add(b);
            var cs = new List<int>(bs); int n = 0;
            for (int b = 0; b < 256; b++) if (!bs.Contains(b)) { bs.Add(b); cs.Add(256 + n); n++; }
            _u2b = new Dictionary<char, byte>(); for (int i = 0; i < bs.Count; i++) _u2b[(char)cs[i]] = (byte)bs[i];
            _dir = dir;
        }

        private static async Task<float[]> Pixels(byte[] png)
        {
            using var ms = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(ms.GetOutputStreamAt(0))) { w.WriteBytes(png); await w.StoreAsync(); await w.FlushAsync(); w.DetachStream(); }
            var dec = await BitmapDecoder.CreateAsync(ms);
            var t = new BitmapTransform { ScaledWidth = Size, ScaledHeight = Size, InterpolationMode = BitmapInterpolationMode.Cubic };
            var pd = await dec.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, t, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var px = pd.DetachPixelData();
            var f = new float[3 * Size * Size]; int plane = Size * Size;
            for (int i = 0; i < plane; i++)
            {
                float a = px[i * 4 + 3] / 255f; // transparent → white
                for (int c = 0; c < 3; c++) f[c * plane + i] = ((px[i * 4 + c] * a + 255 * (1 - a)) / 255f - 0.5f) / 0.5f;
            }
            return f;
        }

        public async Task<string> RecognizeLatexAsync(byte[] png, string modelDir, CancellationToken ct)
        {
            var pixels = await Pixels(png);
            await _lock.WaitAsync(ct);
            try
            {
                return await Task.Run(() =>
                {
                    Load(modelDir);
                    var input = new DenseTensor<float>(pixels, new[] { 1, 3, Size, Size });
                    using var encOut = _enc.Run(new[] { NamedOnnxValue.CreateFromTensor("pixel_values", input) });
                    var hidden = encOut.First().AsTensor<float>().ToDenseTensor();
                    var ids = new List<long> { Start };
                    for (int step = 0; step < MaxTokens; step++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var idT = new DenseTensor<long>(ids.ToArray(), new[] { 1, ids.Count });
                        using var o = _dec.Run(new[] { NamedOnnxValue.CreateFromTensor("input_ids", idT), NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden) });
                        var lg = o.First().AsTensor<float>();
                        int V = lg.Dimensions[2], last = ids.Count - 1, arg = 0; float best = float.MinValue;
                        for (int v = 0; v < V; v++) { var x = lg[0, last, v]; if (x > best) { best = x; arg = v; } }
                        if (arg == Eos) break;
                        ids.Add(arg);
                    }
                    var sb = new StringBuilder();
                    foreach (var id in ids.Skip(1)) if (_vocab.TryGetValue((int)id, out var tok) && !(tok.StartsWith("<") && tok.EndsWith(">"))) sb.Append(tok);
                    var bytes = sb.ToString().Select(c => _u2b.TryGetValue(c, out var b) ? b : (byte)'?').ToArray();
                    return Tidy(Encoding.UTF8.GetString(bytes));
                }, ct);
            }
            finally { _lock.Release(); }
        }

        /// <summary>The model emits space-separated tokens; keep a space only where LaTeX needs it (after a command word before a letter).</summary>
        public static string Tidy(string s) => FormulaLatex.Tidy(s);

        public void Dispose() { _enc?.Dispose(); _dec?.Dispose(); _enc = _dec = null; _dir = null; }
    }
}
