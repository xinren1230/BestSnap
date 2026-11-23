using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BestSnap.Plugins;
using Whisper.net.LibraryLoader;

namespace BestSnap.Services
{
    /// <summary>
    /// NVIDIA via nvidia-smi (name + VRAM) and DXGI adapters (NVIDIA/AMD/Intel dedicated VRAM).
    /// </summary>
    public static class WhisperHardwareProbe
    {
        static readonly object Gate = new object();
        static WhisperHardware _cached;

        public static WhisperHardware Current
        {
            get
            {
                lock (Gate)
                {
                    if (_cached == null) _cached = Detect();
                    return _cached;
                }
            }
        }

        public static void Invalidate() { lock (Gate) _cached = null; }

        public static WhisperHardware Detect()
        {
            string root = SearchRoot();
            var hw = new WhisperHardware
            {
                CpuCores = Math.Max(1, Environment.ProcessorCount),
                RamBytes = TotalRamBytes(),
                CudaRuntime = WhisperNativeLayout.HasCudaRuntime(root),
                VulkanRuntime = WhisperNativeLayout.HasVulkanRuntime(root),
            };
            var adapters = new List<WhisperGpuInfo>();
            foreach (var g in WhisperAutoSelect.ParseNvidiaSmi(RunNvidiaSmi()))
                adapters.Add(g);
            foreach (var g in EnumDxgiAdapters())
            {
                if (g.Vendor == WhisperGpuVendor.Nvidia && adapters.Exists(a => a.Vendor == WhisperGpuVendor.Nvidia))
                    continue;
                adapters.Add(g);
            }
            hw.Adapters = adapters.ToArray();
            return hw;
        }

        public static string SearchRoot()
        {
            var candidates = new List<string>();
            void Add(string p) { if (!string.IsNullOrWhiteSpace(p)) candidates.Add(p); }
            Add(AppContext.BaseDirectory);
            try { Add(Path.GetDirectoryName(Environment.ProcessPath)); } catch { }
            try { Add(Path.GetDirectoryName(RuntimeOptions.LibraryPath)); } catch { }
            return WhisperNativeLayout.ResolveSearchRoot(candidates);
        }

        public static void ApplyBackend(WhisperBackend backend)
        {
            try
            {
                if (backend == WhisperBackend.Cuda)
                {
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.Cuda, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx
                    };
                }
                else if (backend == WhisperBackend.Vulkan)
                {
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx
                    };
                }
                else
                {
                    RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx
                    };
                }
            }
            catch (Exception ex) { Logger.Error("Whisper.RuntimeOrder", ex); }
        }

        static string RunNvidiaSmi()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                if (!p.WaitForExit(2500))
                {
                    try { p.Kill(true); } catch { }
                    return null;
                }
                if (p.ExitCode != 0) return null;
                return p.StandardOutput.ReadToEnd();
            }
            catch { return null; }
        }

        static long TotalRamBytes()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                    if (GlobalMemoryStatusEx(ref m) && m.ullTotalPhys > 0)
                        return (long)m.ullTotalPhys;
                }
                catch { }
            }
            try
            {
                string meminfo = "/proc/meminfo";
                if (File.Exists(meminfo))
                {
                    foreach (var line in File.ReadLines(meminfo))
                    {
                        if (!line.StartsWith("MemTotal:", StringComparison.Ordinal)) continue;
                        string n = line.Substring(9).Trim();
                        int sp = n.IndexOf(' ');
                        if (sp > 0) n = n.Substring(0, sp);
                        if (long.TryParse(n, out long kb)) return kb * 1024;
                    }
                }
            }
            catch { }
            return 0;
        }

        static WhisperGpuInfo[] EnumDxgiAdapters()
        {
            if (!OperatingSystem.IsWindows()) return Array.Empty<WhisperGpuInfo>();
            var list = new List<WhisperGpuInfo>();
            IntPtr unk = IntPtr.Zero;
            try
            {
                var iid = IID_IDXGIFactory;
                int hr = CreateDXGIFactory(ref iid, out unk);
                if (hr != 0 || unk == IntPtr.Zero) return Array.Empty<WhisperGpuInfo>();
                var factory = (IDXGIFactory)Marshal.GetObjectForIUnknown(unk);
                for (uint i = 0; i < 16; i++)
                {
                    int ehr = factory.EnumAdapters(i, out var adapter);
                    if (ehr != 0 || adapter == null) break;
                    try
                    {
                        int dhr = adapter.GetDesc(out var desc);
                        if (dhr != 0) continue;
                        string name = desc.Description;
                        if (WhisperAutoSelect.IsBasicRender(name)) continue;
                        long vram = (long)desc.DedicatedVideoMemory.ToUInt64();
                        var vendor = WhisperAutoSelect.VendorFromId(desc.VendorId);
                        if (vendor == WhisperGpuVendor.Other)
                            vendor = WhisperAutoSelect.VendorFromName(name);
                        list.Add(new WhisperGpuInfo
                        {
                            Name = name,
                            Vendor = vendor,
                            DedicatedBytes = vram,
                        });
                    }
                    catch (Exception ex) { Logger.Error("Whisper.DXGI.Adapter", ex); }
                    finally { try { Marshal.ReleaseComObject(adapter); } catch { } }
                }
                try { Marshal.ReleaseComObject(factory); } catch { }
            }
            catch (Exception ex) { Logger.Error("Whisper.DXGI", ex); }
            finally
            {
                if (unk != IntPtr.Zero) try { Marshal.Release(unk); } catch { }
            }
            return list.ToArray();
        }

        static readonly Guid IID_IDXGIFactory = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369");

        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory(ref Guid riid, out IntPtr ppFactory);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DXGI_ADAPTER_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid;
        }

        [ComImport, Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIFactory
        {
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig] int EnumAdapters(uint Adapter, out IDXGIAdapter ppAdapter);
        }

        [ComImport, Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIAdapter
        {
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
            [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        }
    }
}
