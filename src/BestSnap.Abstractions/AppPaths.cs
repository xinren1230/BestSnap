using System;
using System.IO;
using System.Threading;

namespace BestSnap.Plugins
{
    /// <summary>
    /// %AppData%\%LocalAppData%\BestSnap, with a one-time copy/move from the SnapForge folders
    /// used by earlier builds (see <see cref="AppIdentity.LegacyName"/>).
    /// Call <see cref="EnsureMigrated"/> before the first read or write.
    /// </summary>
    public static class AppPaths
    {
        static int _migrated;

        public static string AppData =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.Name);

        public static string LegacyAppData =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppIdentity.LegacyName);

        public static string LocalData =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.Name);

        public static string LegacyLocalData =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.LegacyName);

        public static string History => Path.Combine(LocalData, "History");
        public static string RuntimeRoot => Path.Combine(LocalData, "runtime");
        public static string JitProfile => Path.Combine(LocalData, "Profile");

        public static void EnsureMigrated()
        {
            if (Interlocked.Exchange(ref _migrated, 1) != 0) return;
            TryMigrateTree(LegacyAppData, AppData);
            TryMigrateTree(LegacyLocalData, LocalData);
        }

        /// <summary>Move when dest is missing; otherwise copy files that dest does not already have.</summary>
        public static void TryMigrateTree(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return;
            if (string.Equals(Path.GetFullPath(from), Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(from)) return;
            try
            {
                if (!Directory.Exists(to))
                {
                    var parent = Path.GetDirectoryName(to);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    Directory.Move(from, to);
                    return;
                }
                foreach (var src in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(to, Path.GetRelativePath(from, src));
                    if (File.Exists(dest)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(src, dest, false);
                }
            }
            catch { }
        }
    }
}
