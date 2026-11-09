using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>
    /// Single-exe support. build.ps1 embeds files that cannot live inside the .NET single-file bundle
    /// (mixed-mode ScreenRecorderLib.dll + Ijwhost.dll, the OCR plugin and its WinRT projection) as
    /// resources named "rt/&lt;relative path&gt;". On first run they are extracted to
    /// %LocalAppData%\BestSnap\runtime\&lt;build id&gt;\ and resolved from there.
    /// Must run before any ScreenRecorderLib type is touched (called from App's static constructor).
    /// </summary>
    public static class EmbeddedRuntime
    {
        public static string Dir { get; private set; }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        public static void Init()
        {
            try
            {
                var asm = typeof(EmbeddedRuntime).Assembly;
                AppPaths.EnsureMigrated();
                var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("rt/") || n.StartsWith("rt\\")).ToArray();
                if (names.Length == 0) return;
                string id = asm.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 12);
                var dir = Path.Combine(AppPaths.RuntimeRoot, id);
                foreach (var n in names)
                {
                    var rel = n.Substring(3).Replace('\\', '/').TrimStart('/');
                    var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
                    using var src = asm.GetManifestResourceStream(n);
                    if (File.Exists(path) && new FileInfo(path).Length == src.Length) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    using (var f = File.Create(tmp)) src.CopyTo(f);
                    try { File.Move(tmp, path, true); } catch { try { File.Delete(tmp); } catch { } } // another instance won the race
                }
                Dir = dir;
                SetDllDirectory(dir); // lets ScreenRecorderLib find Ijwhost.dll
                AssemblyLoadContext.Default.Resolving += (ctx, name) =>
                {
                    if (string.IsNullOrEmpty(name?.Name)) return null;
                    var p = Path.Combine(dir, name.Name + ".dll");
                    if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
                    var plugins = Path.Combine(dir, "plugins");
                    if (!Directory.Exists(plugins)) return null;
                    foreach (var candidate in Directory.GetFiles(plugins, name.Name + ".dll", SearchOption.AllDirectories))
                        return ctx.LoadFromAssemblyPath(candidate);
                    return null;
                };
                CleanupOld(Path.GetDirectoryName(dir), id);
            }
            catch (Exception ex) { try { Logger.Error("EmbeddedRuntime", ex); } catch { } }
        }

        private static void CleanupOld(string root, string keep)
        {
            try { foreach (var d in Directory.GetDirectories(root)) if (!Path.GetFileName(d).Equals(keep, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(d, true); } catch { } }
            catch { }
        }
    }
}
