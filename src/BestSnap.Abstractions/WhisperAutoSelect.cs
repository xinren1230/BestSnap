using System;
using System.Globalization;

namespace BestSnap.Plugins
{
    public enum WhisperBackend
    {
        Cpu = 0,
        Cuda = 1,
        Vulkan = 2,
    }

    public enum WhisperGpuVendor
    {
        Other = 0,
        Nvidia = 1,
        Amd = 2,
        Intel = 3,
    }

    public sealed class WhisperGpuInfo
    {
        public string Name { get; set; }
        public WhisperGpuVendor Vendor { get; set; }
        public long DedicatedBytes { get; set; }
        public bool FromNvidiaSmi { get; set; }
    }

    public sealed class WhisperHardware
    {
        public WhisperGpuInfo[] Adapters { get; set; }
        public long RamBytes { get; set; }
        public int CpuCores { get; set; }
        public bool CudaRuntime { get; set; }
        public bool VulkanRuntime { get; set; }
        public static WhisperHardware Empty => new WhisperHardware { Adapters = Array.Empty<WhisperGpuInfo>(), CpuCores = 1 };
    }

    public sealed class WhisperAutoPick
    {
        public string ModelId { get; set; }
        public WhisperBackend Backend { get; set; }
        public string DeviceName { get; set; }
        public long VramBytes { get; set; }
        public long RamBytes { get; set; }
        public int CpuCores { get; set; }
        public bool Manual { get; set; }
        public string FallbackNote { get; set; }

        public string BackendLabel => Backend == WhisperBackend.Cuda ? "CUDA" : Backend == WhisperBackend.Vulkan ? "Vulkan" : "CPU";

        public string Reason
        {
            get
            {
                string model = ModelId ?? "base";
                string tail = FallbackNote;
                string core;
                if (Backend == WhisperBackend.Cpu)
                {
                    string ram = WhisperAutoSelect.FormatGb(RamBytes);
                    core = ram + " RAM, " + Math.Max(1, CpuCores) + " cores → " + model + ", CPU";
                }
                else
                {
                    string name = WhisperAutoSelect.ShortGpuName(DeviceName);
                    core = name + " " + WhisperAutoSelect.FormatGb(VramBytes) + " → " + model + ", " + BackendLabel;
                }
                if (string.IsNullOrEmpty(tail)) return core;
                return core + " (" + tail + ")";
            }
        }
    }

    /// <summary>
    /// Hardware → model/backend. Pure rules so tests do not need a GPU.
    /// </summary>
    public static class WhisperAutoSelect
    {
        public const long GiB = 1024L * 1024 * 1024;
        public const long FourGiB = 4 * GiB;
        public const long SixGiB = 6 * GiB;
        public const long EightGiB = 8 * GiB;
        public const long SixteenGiB = 16 * GiB;

        public static WhisperAutoPick Resolve(string choice, WhisperHardware hw, bool live)
        {
            hw ??= WhisperHardware.Empty;
            if (!WhisperModels.IsAuto(choice) && !string.IsNullOrWhiteSpace(choice))
            {
                string id = WhisperModels.Normalize(choice);
                if (id != WhisperModels.AutoId)
                {
                    var backend = PreferBackend(hw);
                    var gpu = backend == WhisperBackend.Cuda ? BestNvidia(hw) : backend == WhisperBackend.Vulkan ? BestDiscrete(hw) : null;
                    return new WhisperAutoPick
                    {
                        ModelId = id,
                        Backend = backend,
                        DeviceName = gpu?.Name,
                        VramBytes = gpu?.DedicatedBytes ?? 0,
                        RamBytes = hw.RamBytes,
                        CpuCores = hw.CpuCores,
                        Manual = true,
                    };
                }
            }
            return Pick(hw, live);
        }

        public static WhisperAutoPick Pick(WhisperHardware hw, bool live)
        {
            hw ??= WhisperHardware.Empty;
            var nvidia = BestNvidia(hw);
            bool cuda = hw.CudaRuntime && nvidia != null && nvidia.DedicatedBytes >= FourGiB;
            if (cuda && nvidia.DedicatedBytes >= SixGiB)
            {
                string model = "large-v3-turbo";
                if (live && nvidia.DedicatedBytes < EightGiB) model = Smaller(model);
                return GpuPick(model, WhisperBackend.Cuda, nvidia, hw);
            }
            if (cuda)
            {
                string model = "large-v3-turbo-q5_0";
                if (live) model = Smaller(model);
                return GpuPick(model, WhisperBackend.Cuda, nvidia, hw);
            }

            var discrete = BestDiscrete(hw);
            bool vulkan = hw.VulkanRuntime && discrete != null && discrete.DedicatedBytes >= FourGiB;
            if (vulkan)
            {
                string model = "large-v3-turbo-q5_0";
                if (live && discrete.DedicatedBytes < SixGiB) model = Smaller(model);
                return GpuPick(model, WhisperBackend.Vulkan, discrete, hw);
            }

            int cores = hw.CpuCores > 0 ? hw.CpuCores : 1;
            bool strong = hw.RamBytes >= SixteenGiB && cores >= 8;
            string cpuModel = strong ? (live ? "small" : "medium-q5_0") : "base";
            if (!strong && live) cpuModel = Smaller("base");
            return new WhisperAutoPick
            {
                ModelId = cpuModel,
                Backend = WhisperBackend.Cpu,
                RamBytes = hw.RamBytes,
                CpuCores = cores,
            };
        }

        public static WhisperBackend PreferBackend(WhisperHardware hw)
        {
            hw ??= WhisperHardware.Empty;
            var nvidia = BestNvidia(hw);
            if (hw.CudaRuntime && nvidia != null && nvidia.DedicatedBytes >= FourGiB) return WhisperBackend.Cuda;
            var d = BestDiscrete(hw);
            if (hw.VulkanRuntime && d != null && d.DedicatedBytes >= FourGiB) return WhisperBackend.Vulkan;
            return WhisperBackend.Cpu;
        }

        public static string Smaller(string id)
        {
            id = WhisperModels.Normalize(id);
            if (id == WhisperModels.AutoId) id = "base";
            switch (id)
            {
                case "large-v3-turbo": return "large-v3-turbo-q5_0";
                case "large-v3-turbo-q5_0": return "medium-q5_0";
                case "medium": return "medium-q5_0";
                case "medium-q5_0": return "small";
                case "small": return "base";
                default: return "tiny";
            }
        }

        public static WhisperAutoPick CpuFallback(WhisperAutoPick previous, string smaller)
        {
            previous ??= new WhisperAutoPick { ModelId = "base" };
            smaller = WhisperModels.NormalizeWeights(smaller);
            return new WhisperAutoPick
            {
                ModelId = smaller,
                Backend = WhisperBackend.Cpu,
                DeviceName = previous.DeviceName,
                VramBytes = previous.VramBytes,
                RamBytes = previous.RamBytes,
                CpuCores = previous.CpuCores,
                Manual = previous.Manual,
                FallbackNote = "GPU load failed, CPU " + smaller,
            };
        }

        /// <summary>GPU loaded but produced no segments; retry the same weights on CPU.</summary>
        public static WhisperAutoPick CpuDecodeFallback(WhisperAutoPick previous)
        {
            previous ??= new WhisperAutoPick { ModelId = "base" };
            return new WhisperAutoPick
            {
                ModelId = WhisperModels.NormalizeWeights(previous.ModelId),
                Backend = WhisperBackend.Cpu,
                DeviceName = previous.DeviceName,
                VramBytes = previous.VramBytes,
                RamBytes = previous.RamBytes,
                CpuCores = previous.CpuCores,
                Manual = previous.Manual,
                FallbackNote = "GPU decode empty, CPU retry",
            };
        }

        public static WhisperGpuInfo BestNvidia(WhisperHardware hw)
        {
            WhisperGpuInfo best = null;
            if (hw?.Adapters == null) return null;
            foreach (var a in hw.Adapters)
            {
                if (a == null || a.Vendor != WhisperGpuVendor.Nvidia) continue;
                if (best == null || a.DedicatedBytes > best.DedicatedBytes) best = a;
            }
            return best;
        }

        public static WhisperGpuInfo BestDiscrete(WhisperHardware hw)
        {
            WhisperGpuInfo best = null;
            if (hw?.Adapters == null) return null;
            foreach (var a in hw.Adapters)
            {
                if (a == null || a.DedicatedBytes < FourGiB) continue;
                if (IsBasicRender(a.Name)) continue;
                if (best == null || a.DedicatedBytes > best.DedicatedBytes) best = a;
            }
            return best;
        }

        public static WhisperGpuVendor VendorFromId(uint vendorId)
        {
            if (vendorId == 0x10DE) return WhisperGpuVendor.Nvidia;
            if (vendorId == 0x1002 || vendorId == 0x1022) return WhisperGpuVendor.Amd;
            if (vendorId == 0x8086) return WhisperGpuVendor.Intel;
            return WhisperGpuVendor.Other;
        }

        public static WhisperGpuVendor VendorFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return WhisperGpuVendor.Other;
            string n = name.ToLowerInvariant();
            if (n.Contains("nvidia") || n.Contains("geforce") || n.Contains("rtx") || n.Contains("gtx") || n.Contains("quadro"))
                return WhisperGpuVendor.Nvidia;
            if (n.Contains("amd") || n.Contains("radeon") || n.Contains("rx ")) return WhisperGpuVendor.Amd;
            if (n.Contains("intel") || n.Contains("arc ") || n.Contains("iris") || n.Contains("uhd")) return WhisperGpuVendor.Intel;
            return WhisperGpuVendor.Other;
        }

        public static bool IsBasicRender(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string n = name.ToLowerInvariant();
            return n.Contains("basic render") || n.Contains("microsoft") && n.Contains("render");
        }

        public static string ShortGpuName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "GPU";
            name = name.Trim();
            foreach (var p in new[] { "NVIDIA GeForce ", "NVIDIA RTX ", "NVIDIA ", "AMD Radeon ", "AMD ", "Intel(R) ", "Intel " })
            {
                if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(p.Length).Trim();
            }
            return name;
        }

        public static string FormatGb(long bytes)
        {
            if (bytes <= 0) return "0 GB";
            double gb = bytes / (double)GiB;
            if (gb >= 9.95) return gb.ToString("0", CultureInfo.InvariantCulture) + " GB";
            string s = gb.ToString("0.#", CultureInfo.InvariantCulture);
            return s + " GB";
        }

        public static string BackendLabel(WhisperBackend b) =>
            b == WhisperBackend.Cuda ? "CUDA" : b == WhisperBackend.Vulkan ? "Vulkan" : "CPU";

        /// <summary>
        /// Parse <c>nvidia-smi --query-gpu=name,memory.total --format=csv,noheader,nounits</c>.
        /// Memory is MiB.
        /// </summary>
        public static WhisperGpuInfo[] ParseNvidiaSmi(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<WhisperGpuInfo>();
            var list = new System.Collections.Generic.List<WhisperGpuInfo>();
            foreach (var raw in csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("name", StringComparison.OrdinalIgnoreCase)) continue;
                int comma = line.LastIndexOf(',');
                if (comma <= 0) continue;
                string name = line.Substring(0, comma).Trim().Trim('"');
                string mem = line.Substring(comma + 1).Trim().Trim('"');
                mem = mem.Replace("MiB", "", StringComparison.OrdinalIgnoreCase).Replace("MB", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (!double.TryParse(mem, NumberStyles.Float, CultureInfo.InvariantCulture, out double mib) || mib <= 0)
                    continue;
                list.Add(new WhisperGpuInfo
                {
                    Name = name,
                    Vendor = WhisperGpuVendor.Nvidia,
                    DedicatedBytes = (long)Math.Round(mib * 1024L * 1024L),
                    FromNvidiaSmi = true,
                });
            }
            return list.ToArray();
        }

        static WhisperAutoPick GpuPick(string model, WhisperBackend backend, WhisperGpuInfo gpu, WhisperHardware hw) =>
            new WhisperAutoPick
            {
                ModelId = model,
                Backend = backend,
                DeviceName = gpu?.Name,
                VramBytes = gpu?.DedicatedBytes ?? 0,
                RamBytes = hw.RamBytes,
                CpuCores = hw.CpuCores,
            };
    }
}
