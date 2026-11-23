using System;
using System.Globalization;

namespace BestSnap.Plugins
{
    /// <summary>Headless <c>--transcribe-test</c> flags (no GUI).</summary>
    public sealed class TranscribeTestArgs
    {
        public const string Flag = "--transcribe-test";

        public string Input { get; set; }
        public string Model { get; set; } = "auto";
        public string Backend { get; set; } = "auto";
        public bool LiveSim { get; set; }
        public bool DebugChunks { get; set; }
        public string Out { get; set; }
        public double StretchMinutes { get; set; }

        public static bool LooksLike(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public WhisperBackend? ParseBackend()
        {
            string b = string.IsNullOrWhiteSpace(Backend) ? "auto" : Backend.Trim().ToLowerInvariant();
            if (b == "auto") return null;
            if (b == "cpu") return WhisperBackend.Cpu;
            if (b == "cuda" || b == "gpu" || b == "nvidia") return WhisperBackend.Cuda;
            if (b == "vulkan") return WhisperBackend.Vulkan;
            throw new ArgumentException("--backend must be auto, cpu, cuda, or vulkan");
        }

        public static TranscribeTestArgs Parse(string[] args)
        {
            if (!LooksLike(args)) throw new ArgumentException("missing " + Flag);
            var o = new TranscribeTestArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string key = a.ToLowerInvariant();
                string Next()
                {
                    if (i + 1 >= args.Length) throw new ArgumentException(a + " needs a value");
                    return args[++i];
                }
                switch (key)
                {
                    case Flag:
                        o.Input = Next();
                        break;
                    case "--model":
                        o.Model = Next();
                        break;
                    case "--backend":
                        o.Backend = Next();
                        o.ParseBackend();
                        break;
                    case "--live-sim":
                        o.LiveSim = true;
                        break;
                    case "--debug-chunks":
                        o.DebugChunks = true;
                        break;
                    case "--out":
                        o.Out = Next();
                        break;
                    case "--stretch-minutes":
                        if (!double.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes)
                            || minutes < 0)
                            throw new ArgumentException("--stretch-minutes needs a non-negative number");
                        o.StretchMinutes = minutes;
                        break;
                    case "--tray":
                        break;
                    default:
                        throw new ArgumentException("unknown argument: " + args[i]);
                }
            }
            if (string.IsNullOrWhiteSpace(o.Input))
                throw new ArgumentException(Flag + " needs a .wav or .mp4 file");
            if (string.IsNullOrWhiteSpace(o.Model)) o.Model = "auto";
            if (string.IsNullOrWhiteSpace(o.Backend)) o.Backend = "auto";
            return o;
        }
    }
}
