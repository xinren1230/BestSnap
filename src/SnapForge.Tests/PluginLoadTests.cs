using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SnapForge.Plugins;
using Xunit;

namespace SnapForge.Tests
{
    /// <summary>
    /// Regression for formula recognition FileNotFound on Microsoft.ML.OnnxRuntime after single-file publish.
    /// Covers published plugin layout + ALC dependency probing; does not run WPF/ONNX inference (Linux CI).
    /// </summary>
    public class PluginLoadTests
    {
        static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "SnapForge.sln")) && Directory.Exists(Path.Combine(dir.FullName, "src", "SnapForge.Ocr")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            }
        }

        [Fact]
        public void FormulaLatex_Tidy_KeepsSpaceOnlyAfterCommandBeforeLetter()
        {
            Assert.Equal(@"\Delta=\Delta p\times\cos(\gamma)+\Delta L\times\sin(\gamma).",
                FormulaLatex.Tidy(@"\Delta = \Delta p \times \cos (\gamma ) + \Delta L \times \sin (\gamma )."));
            Assert.Equal(@"\frac{a}{b}", FormulaLatex.Tidy(@"\frac {a}{b}"));
            Assert.Equal("", FormulaLatex.Tidy(""));
        }

        static string WriteStubPlugin(string dir)
        {
            var main = Path.Combine(dir, "SnapForge.Ocr.dll");
            File.Copy(typeof(PluginFileResolver).Assembly.Location, main);
            return main;
        }

        [Fact]
        public void TryResolveManaged_FindsFlattenedOnnxRuntime_WhenDepsJsonKeepsNuGetPath()
        {
            var dir = NewTempDir();
            var main = WriteStubPlugin(dir);
            File.WriteAllBytes(Path.Combine(dir, "Microsoft.ML.OnnxRuntime.dll"), new byte[] { 1, 2, 3 });
            File.WriteAllText(Path.Combine(dir, "SnapForge.Ocr.deps.json"), @"{
  ""targets"": {
    "".NETCoreApp,Version=v8.0/win-x64"": {
      ""Microsoft.ML.OnnxRuntime.Managed/1.20.1"": {
        ""runtime"": { ""lib/net8.0/Microsoft.ML.OnnxRuntime.dll"": {} }
      }
    }
  }
}");
            // deps.json relative path does not exist on disk (publish flattened the DLL).
            Assert.False(File.Exists(Path.Combine(dir, "lib", "net8.0", "Microsoft.ML.OnnxRuntime.dll")));
            var full = new AssemblyName("Microsoft.ML.OnnxRuntime, Version=1.20.1.0, Culture=neutral, PublicKeyToken=f27f157f0a5b7bb6");
            var resolved = PluginFileResolver.TryResolveManaged(main, full);
            Assert.Equal(Path.Combine(dir, "Microsoft.ML.OnnxRuntime.dll"), resolved);
        }

        [Fact]
        public void TryResolveNative_FindsOnnxRuntime_WithoutDllSuffix()
        {
            var dir = NewTempDir();
            var main = WriteStubPlugin(dir);
            File.WriteAllBytes(Path.Combine(dir, "onnxruntime.dll"), new byte[] { 4 });
            File.WriteAllText(Path.Combine(dir, "SnapForge.Ocr.deps.json"), @"{
  ""targets"": {
    "".NETCoreApp,Version=v8.0/win-x64"": {
      ""Microsoft.ML.OnnxRuntime/1.20.1"": {
        ""native"": { ""runtimes/win-x64/native/onnxruntime.dll"": {} }
      }
    }
  }
}");
            Assert.False(File.Exists(Path.Combine(dir, "runtimes", "win-x64", "native", "onnxruntime.dll")));
            Assert.Equal(Path.Combine(dir, "onnxruntime.dll"), PluginFileResolver.TryResolveNative(main, "onnxruntime"));
            Assert.Equal(Path.Combine(dir, "onnxruntime.dll"), PluginFileResolver.TryResolveNative(main, "onnxruntime.dll"));
        }

        [Fact]
        public void PublishedDepsArePresent_FalseWhenOnnxListedButFilesMissing()
        {
            var dir = NewTempDir();
            var main = WriteStubPlugin(dir);
            File.WriteAllText(Path.ChangeExtension(main, ".deps.json"), @"{ ""libraries"": { ""Microsoft.ML.OnnxRuntime/1.20.1"": {} } }");
            Assert.False(PluginFileResolver.PublishedDepsArePresent(main));
        }

        [Fact]
        public void PublishedDepsArePresent_TrueWhenFlattenedOnnxFilesExist()
        {
            var dir = NewTempDir();
            var main = WriteStubPlugin(dir);
            File.WriteAllBytes(Path.Combine(dir, "Microsoft.ML.OnnxRuntime.dll"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(dir, "onnxruntime.dll"), new byte[] { 2 });
            File.WriteAllText(Path.ChangeExtension(main, ".deps.json"), @"{
  ""targets"": {
    "".NETCoreApp,Version=v8.0/win-x64"": {
      ""Microsoft.ML.OnnxRuntime/1.20.1"": { ""native"": { ""runtimes/win-x64/native/onnxruntime.dll"": {} } },
      ""Microsoft.ML.OnnxRuntime.Managed/1.20.1"": { ""runtime"": { ""lib/net8.0/Microsoft.ML.OnnxRuntime.dll"": {} } }
    }
  }
}");
            Assert.True(PluginFileResolver.PublishedDepsArePresent(main));
        }

        [Fact]
        public void PublishedOcrPlugin_ContainsOnnxRuntimeAndFormulaType()
        {
            var outDir = PublishOcrPlugin();
            var main = Path.Combine(outDir, "SnapForge.Ocr.dll");
            foreach (var n in new[]
            {
                "SnapForge.Ocr.dll",
                "SnapForge.Ocr.deps.json",
                "Microsoft.ML.OnnxRuntime.dll",
                "onnxruntime.dll",
                Path.Combine("models", "ch_PP-OCRv4_det_infer.onnx"),
                Path.Combine("models", "ch_PP-OCRv4_rec_infer.onnx"),
            })
                Assert.True(File.Exists(Path.Combine(outDir, n)), "missing published plugin file " + n);

            var deps = File.ReadAllText(Path.Combine(outDir, "SnapForge.Ocr.deps.json"));
            Assert.Contains("Microsoft.ML.OnnxRuntime", deps);
            Assert.Contains("lib/net8.0/Microsoft.ML.OnnxRuntime.dll", deps);
            Assert.Contains("runtimes/win-x64/native/onnxruntime.dll", deps);
            Assert.False(File.Exists(Path.Combine(outDir, "lib", "net8.0", "Microsoft.ML.OnnxRuntime.dll")),
                "publish should flatten OnnxRuntime; if the SDK starts emitting the NuGet-relative path, update PluginFileResolver");

            Assert.True(PluginFileResolver.PublishedDepsArePresent(main));
            var onnx = PluginFileResolver.TryResolveManaged(main,
                new AssemblyName("Microsoft.ML.OnnxRuntime, Version=1.20.1.0, Culture=neutral, PublicKeyToken=f27f157f0a5b7bb6"));
            Assert.Equal(Path.Combine(outDir, "Microsoft.ML.OnnxRuntime.dll"), onnx);
            Assert.Equal(Path.Combine(outDir, "onnxruntime.dll"), PluginFileResolver.TryResolveNative(main, "onnxruntime"));

            using var fs = File.OpenRead(main);
            using var pe = new PEReader(fs);
            var md = pe.GetMetadataReader();
            var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
            Assert.Contains("Microsoft.ML.OnnxRuntime", refs);
            Assert.Contains("SnapForge.Abstractions", refs);
            var types = md.TypeDefinitions.Select(h =>
            {
                var t = md.GetTypeDefinition(h);
                var ns = md.GetString(t.Namespace);
                var name = md.GetString(t.Name);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }).ToArray();
            Assert.Contains("SnapForge.Ocr.Pix2TextFormulaRecognizer", types);
            Assert.Contains("SnapForge.Ocr.WindowsOcrEngine", types);
        }

        static string NewTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "sf-plugin-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string PublishOcrPlugin()
        {
            var outDir = Path.Combine(Path.GetTempPath(), "sf-ocr-pub-" + Guid.NewGuid().ToString("N"));
            var csproj = Path.Combine(RepoRoot, "src", "SnapForge.Ocr", "SnapForge.Ocr.csproj");
            Assert.True(File.Exists(csproj), "OCR project not found at " + csproj);
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "publish \"" + csproj + "\" -c Release -r win-x64 --self-contained false -o \"" + outDir + "\" --nologo",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, "dotnet publish SnapForge.Ocr failed: " + stdout + stderr);
            return outDir;
        }
    }
}
