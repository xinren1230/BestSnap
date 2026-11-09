namespace SnapForge.Plugins
{
    /// <summary>
    /// Public product name is BestSnap. Source projects, plugin folder SnapForge.Ocr, and IPC names
    /// stay on the old identifiers so upgrades, the OCR plugin, and a running legacy exe keep working.
    /// </summary>
    public static class AppIdentity
    {
        public const string Name = "BestSnap";
        public const string LegacyName = "SnapForge";
        public const string MutexName = "SnapForge_SingleInstance_Mutex";
        public const string ShowEventName = "SnapForge_ShowToolbar_Event";
        public const string CliPipeName = "SnapForge_Cli_Pipe_v1";
        public const string PluginOcrFolder = "SnapForge.Ocr";
        /// <summary>Explicit EmbeddedResource LogicalName — do not derive from AssemblyName (MSB4096).</summary>
        public const string TrayIconResource = "SnapForge.snapforge.ico";
        public const string ExeFileName = Name + ".exe";
        public const string SetupPrefix = Name + "-Setup-";
    }
}
