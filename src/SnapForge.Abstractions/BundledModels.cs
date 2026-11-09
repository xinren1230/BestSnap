using System;
using System.IO;

namespace SnapForge.Plugins
{
    /// <summary>
    /// pix2text-mfr layout: prefer models next to the exe (installer payload), else
    /// %LocalAppData%\BestSnap\models\pix2text-mfr (download cache; SnapForge folder still accepted).
    /// </summary>
    public static class BundledModels
    {
        public const string FormulaFolder = "pix2text-mfr";
        public const string RelativeFormulaDir = "models/pix2text-mfr";

        public static readonly (string Name, string Sha256, long Size)[] FormulaFiles =
        {
            ("encoder_model.onnx", "bd8d5c322792e9ec45793af5569e9748f82a3d728a9e00213dbfc56c1486f37d", 87496990),
            ("decoder_model.onnx", "fd0f92d7a012f3dae41e1ac79421aea0ea888b5a66cb3f9a004e424f82f3daed", 30114937),
            ("tokenizer.json",     "3e2ab757277d22639bec28c9d7972e352d3d1dba223051fa674002dc5ab64df3", 39161),
        };

        public static string BundledFormulaDir(string exeDir) =>
            Path.Combine(exeDir ?? "", "models", FormulaFolder);

        public static string CacheFormulaDir(string localAppData) =>
            Path.Combine(localAppData ?? "", AppIdentity.Name, "models", FormulaFolder);

        public static string LegacyCacheFormulaDir(string localAppData) =>
            Path.Combine(localAppData ?? "", AppIdentity.LegacyName, "models", FormulaFolder);

        public static bool IsComplete(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            foreach (var f in FormulaFiles)
            {
                var path = Path.Combine(dir, f.Name);
                if (!File.Exists(path) || new FileInfo(path).Length != f.Size) return false;
            }
            return true;
        }

        /// <summary>Install-dir models win when complete; else BestSnap cache; else leftover SnapForge cache.</summary>
        public static string ResolveFormulaDir(string exeDir, string localAppData)
        {
            var bundled = BundledFormulaDir(exeDir);
            if (IsComplete(bundled)) return bundled;
            var cache = CacheFormulaDir(localAppData);
            if (IsComplete(cache)) return cache;
            var legacy = LegacyCacheFormulaDir(localAppData);
            if (IsComplete(legacy)) return legacy;
            return cache;
        }
    }
}
