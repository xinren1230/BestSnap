using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class WhisperAutoSelectTests
    {
        static WhisperHardware Hw(
            long vram = 0, WhisperGpuVendor vendor = WhisperGpuVendor.Nvidia, string name = "NVIDIA GeForce RTX 4060",
            bool cuda = true, bool vulkan = true, long ram = 16L * WhisperAutoSelect.GiB, int cores = 8)
        {
            var adapters = vram <= 0
                ? System.Array.Empty<WhisperGpuInfo>()
                : new[] { new WhisperGpuInfo { Name = name, Vendor = vendor, DedicatedBytes = vram } };
            return new WhisperHardware
            {
                Adapters = adapters,
                CudaRuntime = cuda,
                VulkanRuntime = vulkan,
                RamBytes = ram,
                CpuCores = cores,
            };
        }

        [Fact]
        public void Cuda_8GB_PicksFullTurbo()
        {
            var p = WhisperAutoSelect.Pick(Hw(8 * WhisperAutoSelect.GiB), live: false);
            Assert.Equal("large-v3-turbo", p.ModelId);
            Assert.Equal(WhisperBackend.Cuda, p.Backend);
            Assert.Contains("RTX 4060", p.Reason);
            Assert.Contains("8 GB", p.Reason);
            Assert.Contains("CUDA", p.Reason);
            var live = WhisperAutoSelect.Pick(Hw(8 * WhisperAutoSelect.GiB), live: true);
            Assert.Equal("large-v3-turbo", live.ModelId);
        }

        [Fact]
        public void Cuda_6GB_LivePicksOneSizeSmaller()
        {
            var hw = Hw(6 * WhisperAutoSelect.GiB, name: "NVIDIA GeForce RTX 3060");
            var file = WhisperAutoSelect.Pick(hw, live: false);
            Assert.Equal("large-v3-turbo", file.ModelId);
            Assert.Equal(WhisperBackend.Cuda, file.Backend);
            var live = WhisperAutoSelect.Pick(hw, live: true);
            Assert.Equal("large-v3-turbo-q5_0", live.ModelId);
            Assert.Equal(WhisperBackend.Cuda, live.Backend);
        }

        [Fact]
        public void Cuda_4to6GB_PicksQuantizedTurbo()
        {
            var hw = Hw(4 * WhisperAutoSelect.GiB, name: "NVIDIA GeForce GTX 1650");
            var file = WhisperAutoSelect.Pick(hw, live: false);
            Assert.Equal("large-v3-turbo-q5_0", file.ModelId);
            Assert.Equal(WhisperBackend.Cuda, file.Backend);
            var live = WhisperAutoSelect.Pick(hw, live: true);
            Assert.Equal("medium-q5_0", live.ModelId);
        }

        [Fact]
        public void Vulkan_Amd8GB_PicksQuantizedTurbo()
        {
            var hw = Hw(8 * WhisperAutoSelect.GiB, WhisperGpuVendor.Amd, "AMD Radeon RX 6800", cuda: false, vulkan: true);
            var p = WhisperAutoSelect.Pick(hw, live: false);
            Assert.Equal("large-v3-turbo-q5_0", p.ModelId);
            Assert.Equal(WhisperBackend.Vulkan, p.Backend);
            Assert.Contains("RX 6800", p.Reason);
            Assert.Contains("Vulkan", p.Reason);
        }

        [Fact]
        public void Vulkan_IntelArc_4GB_LiveShrinks()
        {
            var hw = Hw(4 * WhisperAutoSelect.GiB, WhisperGpuVendor.Intel, "Intel Arc A380", cuda: false);
            var file = WhisperAutoSelect.Pick(hw, live: false);
            Assert.Equal("large-v3-turbo-q5_0", file.ModelId);
            Assert.Equal(WhisperBackend.Vulkan, file.Backend);
            var live = WhisperAutoSelect.Pick(hw, live: true);
            Assert.Equal("medium-q5_0", live.ModelId);
        }

        [Fact]
        public void NvidiaWithoutCudaRuntime_UsesVulkanIfShipped()
        {
            var hw = Hw(8 * WhisperAutoSelect.GiB, cuda: false, vulkan: true);
            var p = WhisperAutoSelect.Pick(hw, live: false);
            Assert.Equal(WhisperBackend.Vulkan, p.Backend);
            Assert.Equal("large-v3-turbo-q5_0", p.ModelId);
        }

        [Fact]
        public void IgpuLowVram_UsesCpu()
        {
            var hw = Hw(128L * 1024 * 1024, WhisperGpuVendor.Intel, "Intel UHD Graphics", cuda: true, vulkan: true);
            var p = WhisperAutoSelect.Pick(hw, live: false);
            Assert.Equal(WhisperBackend.Cpu, p.Backend);
            Assert.Equal("medium-q5_0", p.ModelId);
        }

        [Fact]
        public void CpuStrong_FileMediumQ5_LiveSmall()
        {
            var hw = Hw(0, ram: 16 * WhisperAutoSelect.GiB, cores: 8, cuda: false, vulkan: false);
            Assert.Equal("medium-q5_0", WhisperAutoSelect.Pick(hw, live: false).ModelId);
            Assert.Equal("small", WhisperAutoSelect.Pick(hw, live: true).ModelId);
            Assert.Contains("CPU", WhisperAutoSelect.Pick(hw, live: false).Reason);
        }

        [Fact]
        public void CpuWeak_FileBase_LiveTiny()
        {
            var hw = Hw(0, ram: 8 * WhisperAutoSelect.GiB, cores: 4, cuda: false, vulkan: false);
            Assert.Equal("base", WhisperAutoSelect.Pick(hw, live: false).ModelId);
            Assert.Equal("tiny", WhisperAutoSelect.Pick(hw, live: true).ModelId);
        }

        [Fact]
        public void ManualChoice_OverridesHardware()
        {
            var hw = Hw(8 * WhisperAutoSelect.GiB);
            var p = WhisperAutoSelect.Resolve("tiny", hw, live: false);
            Assert.Equal("tiny", p.ModelId);
            Assert.True(p.Manual);
            Assert.Equal(WhisperBackend.Cuda, p.Backend);
        }

        [Fact]
        public void Smaller_StepsDownTheLadder()
        {
            Assert.Equal("large-v3-turbo-q5_0", WhisperAutoSelect.Smaller("large-v3-turbo"));
            Assert.Equal("medium-q5_0", WhisperAutoSelect.Smaller("large-v3-turbo-q5_0"));
            Assert.Equal("small", WhisperAutoSelect.Smaller("medium-q5_0"));
            Assert.Equal("base", WhisperAutoSelect.Smaller("small"));
            Assert.Equal("tiny", WhisperAutoSelect.Smaller("base"));
            Assert.Equal("tiny", WhisperAutoSelect.Smaller("tiny"));
        }

        [Fact]
        public void CpuFallback_AnnotatesReason()
        {
            var prev = WhisperAutoSelect.Pick(Hw(8 * WhisperAutoSelect.GiB), live: false);
            var fb = WhisperAutoSelect.CpuFallback(prev, WhisperAutoSelect.Smaller(prev.ModelId));
            Assert.Equal(WhisperBackend.Cpu, fb.Backend);
            Assert.Equal("large-v3-turbo-q5_0", fb.ModelId);
            Assert.Contains("GPU load failed", fb.Reason);
            var decode = WhisperAutoSelect.CpuDecodeFallback(prev);
            Assert.Equal(WhisperBackend.Cpu, decode.Backend);
            Assert.Equal(prev.ModelId, decode.ModelId);
            Assert.Contains("GPU decode empty", decode.Reason);
        }

        [Fact]
        public void ParseNvidiaSmi_ReadsNameAndMib()
        {
            var gpus = WhisperAutoSelect.ParseNvidiaSmi("NVIDIA GeForce RTX 4060, 8188\nNVIDIA GeForce RTX 3060, 6144\n");
            Assert.Equal(2, gpus.Length);
            Assert.Equal(WhisperGpuVendor.Nvidia, gpus[0].Vendor);
            Assert.True(gpus[0].DedicatedBytes > 7 * WhisperAutoSelect.GiB);
            Assert.True(gpus[0].FromNvidiaSmi);
            var hw = new WhisperHardware { Adapters = gpus, CudaRuntime = true, RamBytes = 32 * WhisperAutoSelect.GiB, CpuCores = 12 };
            var p = WhisperAutoSelect.Pick(hw, live: false);
            Assert.Equal("large-v3-turbo", p.ModelId);
            Assert.Contains("RTX 4060", p.Reason);
        }

        [Fact]
        public void VendorFromId_NvidiaAmdIntel()
        {
            Assert.Equal(WhisperGpuVendor.Nvidia, WhisperAutoSelect.VendorFromId(0x10DE));
            Assert.Equal(WhisperGpuVendor.Amd, WhisperAutoSelect.VendorFromId(0x1002));
            Assert.Equal(WhisperGpuVendor.Intel, WhisperAutoSelect.VendorFromId(0x8086));
            Assert.Equal(WhisperGpuVendor.Nvidia, WhisperAutoSelect.VendorFromName("GeForce RTX 4070"));
        }
    }
}
