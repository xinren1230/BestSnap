using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using SnapForge.Plugins;

namespace SnapForge.Services
{
    /// <summary>
    /// Loads optional plugins from &lt;exe folder&gt;\plugins\&lt;PluginName&gt;\*.dll, each in its own AssemblyLoadContext
    /// (dependencies via .deps.json plus a flattened-folder probe — see PluginFileResolver). The contract assembly is
    /// always shared with the host. Incomplete side-by-side plugin folders (missing ONNX Runtime) are skipped so the
    /// embedded copy can load. Missing/broken plugins are logged and ignored, so the main app works without them.
    /// </summary>
    public static class PluginHost
    {
        private static readonly List<ISnapForgePlugin> _plugins = new List<ISnapForgePlugin>();
        private static bool _loaded;
        public static string Folder => Path.Combine(AppContext.BaseDirectory, "plugins");

        private sealed class PluginContext : AssemblyLoadContext
        {
            private readonly string _main;
            public PluginContext(string main) : base(Path.GetFileNameWithoutExtension(main), isCollectible: false) { _main = main; }
            protected override Assembly Load(AssemblyName name)
            {
                if (name.Name == typeof(ISnapForgePlugin).Assembly.GetName().Name) return null; // share contract with host
                var p = PluginFileResolver.TryResolveManaged(_main, name);
                return p != null ? LoadFromAssemblyPath(p) : null;
            }
            protected override IntPtr LoadUnmanagedDll(string name)
            {
                var p = PluginFileResolver.TryResolveNative(_main, name);
                return p != null ? LoadUnmanagedDllFromPath(p) : IntPtr.Zero;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            var roots = new List<string> { Folder };
            if (EmbeddedRuntime.Dir != null) roots.Add(Path.Combine(EmbeddedRuntime.Dir, "plugins"));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in roots.Where(Directory.Exists).SelectMany(Directory.GetDirectories))
            {
                var main = Path.Combine(dir, Path.GetFileName(dir) + ".dll");
                var id = Path.GetFileName(dir);
                if (!File.Exists(main) || seen.Contains(id)) continue; // complete exe-dir plugins still win over embedded ones
                if (!PluginFileResolver.PublishedDepsArePresent(main))
                {
                    Logger.Info("Skipping incomplete plugin folder (missing ONNX Runtime next to the plugin DLL): " + dir);
                    continue;
                }
                try
                {
                    var asm = new PluginContext(main).LoadFromAssemblyPath(main);
                    var added = 0;
                    foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && typeof(ISnapForgePlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null))
                    {
                        _plugins.Add((ISnapForgePlugin)Activator.CreateInstance(t));
                        added++;
                        Logger.Info("Plugin loaded: " + t.FullName + " from " + main);
                    }
                    if (added > 0) seen.Add(id);
                }
                catch (Exception ex) { Logger.Error("Plugin " + main, ex); }
            }
        }

        public static IEnumerable<T> All<T>() where T : ISnapForgePlugin { EnsureLoaded(); return _plugins.OfType<T>(); }
        public static IOcrEngine Ocr => All<IOcrEngine>().FirstOrDefault(o => { try { return o.IsAvailable; } catch { return false; } });
        public static IOcrLayoutEngine OcrLayout => All<IOcrLayoutEngine>().FirstOrDefault(o => { try { return o.IsAvailable; } catch { return false; } });
        public static IFaceDetector Faces => All<IFaceDetector>().FirstOrDefault(o => { try { return o.IsAvailable; } catch { return false; } });
        public static IWebcamSource Webcam => All<IWebcamSource>().FirstOrDefault();
        public static IVideoTrimmer Trimmer => All<IVideoTrimmer>().FirstOrDefault();
    }
}
