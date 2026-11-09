using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Resolves plugin dependencies for an isolated AssemblyLoadContext.
    /// <c>dotnet publish -r win-x64</c> flattens NuGet files into the plugin folder, but the plugin's
    /// .deps.json often keeps package-relative paths (lib/net8.0/Microsoft.ML.OnnxRuntime.dll,
    /// runtimes/win-x64/native/onnxruntime.dll). AssemblyDependencyResolver then misses the files
    /// (or only matches "onnxruntime.dll", not DllImport("onnxruntime")). Probe the plugin directory
    /// and those NuGet-relative paths instead of trusting deps.json.
    /// </summary>
    public static class PluginFileResolver
    {
        public static string TryResolveManaged(string pluginMainDll, AssemblyName name)
        {
            if (string.IsNullOrEmpty(pluginMainDll) || name == null || string.IsNullOrEmpty(name.Name)) return null;
            var dir = Path.GetDirectoryName(pluginMainDll) ?? "";
            foreach (var candidate in new[]
            {
                Path.Combine(dir, name.Name + ".dll"),
                Path.Combine(dir, "lib", "net8.0", name.Name + ".dll"),
            })
                if (File.Exists(candidate)) return candidate;
            return null;
        }

        public static string TryResolveNative(string pluginMainDll, string unmanagedName)
        {
            if (string.IsNullOrEmpty(pluginMainDll) || string.IsNullOrEmpty(unmanagedName)) return null;
            var dir = Path.GetDirectoryName(pluginMainDll) ?? "";
            foreach (var n in NativeNames(unmanagedName))
                foreach (var candidate in new[]
                {
                    Path.Combine(dir, n),
                    Path.Combine(dir, "runtimes", "win-x64", "native", n),
                })
                    if (File.Exists(candidate)) return candidate;
            return null;
        }

        /// <summary>
        /// True when every ONNX Runtime file listed in the plugin deps.json is actually on disk
        /// (flattened next to the plugin or at the NuGet-relative path). Incomplete side-by-side
        /// plugins\ folders must not hide a complete embedded copy.
        /// </summary>
        public static bool PublishedDepsArePresent(string pluginMainDll)
        {
            if (string.IsNullOrEmpty(pluginMainDll) || !File.Exists(pluginMainDll)) return false;
            var depsPath = Path.ChangeExtension(pluginMainDll, ".deps.json");
            if (!File.Exists(depsPath)) return true;
            string text;
            try { text = File.ReadAllText(depsPath); }
            catch { return true; }
            if (text.IndexOf("Microsoft.ML.OnnxRuntime", StringComparison.OrdinalIgnoreCase) < 0) return true;
            if (TryResolveManaged(pluginMainDll, new AssemblyName("Microsoft.ML.OnnxRuntime, Version=1.20.1.0, Culture=neutral, PublicKeyToken=f27f157f0a5b7bb6")) == null
                && TryResolveManaged(pluginMainDll, new AssemblyName("Microsoft.ML.OnnxRuntime")) == null)
                return false;
            if (TryResolveNative(pluginMainDll, "onnxruntime") == null) return false;
            return true;
        }

        static IEnumerable<string> NativeNames(string name)
        {
            yield return name;
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            {
                yield return name + ".dll";
                yield return "lib" + name + ".so";
            }
        }
    }
}
