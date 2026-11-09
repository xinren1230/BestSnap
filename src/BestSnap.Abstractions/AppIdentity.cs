using System;

namespace BestSnap.Plugins
{
    /// <summary>
    /// Public product name is BestSnap. Strings marked LEGACY MIGRATION are on-disk or IPC
    /// names from the previous SnapForge product; they must stay so existing installs upgrade
    /// and a still-running old process can be detected.
    /// </summary>
    public static class AppIdentity
    {
        public const string Name = "BestSnap";

        /// <summary>LEGACY MIGRATION: old %AppData%/%LocalAppData% folder name and HKCU\...\Run value.</summary>
        public const string LegacyName = "SnapForge";

        /// <summary>
        /// LEGACY MIGRATION: single-instance mutex used by SnapForge.exe and BestSnap 1.1.x.
        /// Installer AppMutex must match; renaming would let an old instance and a new one run together.
        /// </summary>
        public const string MutexName = "SnapForge_SingleInstance_Mutex";

        /// <summary>LEGACY MIGRATION: event used to show the toolbar of an already-running instance.</summary>
        public const string ShowEventName = "SnapForge_ShowToolbar_Event";

        /// <summary>LEGACY MIGRATION: named pipe for CLI forwarding to a running instance.</summary>
        public const string CliPipeName = "SnapForge_Cli_Pipe_v1";

        /// <summary>Preferred environment-variable prefix (BESTSNAP_SHOW_IN_CAPTURE, BESTSNAP_UI_DEMO, …).</summary>
        public const string EnvPrefix = "BESTSNAP_";

        /// <summary>LEGACY MIGRATION: previous environment-variable prefix; still read as a fallback.</summary>
        public const string LegacyEnvPrefix = "SNAPFORGE_";

        public const string PluginOcrFolder = "BestSnap.Ocr";

        /// <summary>
        /// LEGACY MIGRATION: previous plugin folder next to the exe.
        /// PluginHost still scans every plugins\ subdirectory, so a leftover copy can load if present.
        /// </summary>
        public const string LegacyPluginOcrFolder = "SnapForge.Ocr";

        /// <summary>Explicit EmbeddedResource LogicalName — do not derive from AssemblyName (MSB4096).</summary>
        public const string TrayIconResource = "BestSnap.bestsnap.ico";
        public const string ExeFileName = Name + ".exe";
        public const string SetupPrefix = Name + "-Setup-";

        /// <summary>Read BESTSNAP_{suffix}, then the legacy SNAPFORGE_{suffix} alias.</summary>
        public static string GetEnv(string suffix)
        {
            var v = Environment.GetEnvironmentVariable(EnvPrefix + suffix);
            if (!string.IsNullOrEmpty(v)) return v;
            return Environment.GetEnvironmentVariable(LegacyEnvPrefix + suffix);
        }
    }
}
