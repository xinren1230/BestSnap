using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SnapForge.Models;
using SnapForge.Plugins;

namespace SnapForge.Services
{
    /// <summary>settings.json with atomic save (temp file + replace, previous version kept as .bak).</summary>
    public class SettingsService
    {
        public static string Folder => AppPaths.AppData;
        public static string FilePath => Path.Combine(Folder, "settings.json");
        public static string BackupPath => FilePath + ".bak";
        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = true };
        private readonly object _lock = new object();
        public AppSettings Current { get; private set; } = new AppSettings();
        /// <summary>Non-null when settings could not be loaded; shown to the user once at startup.</summary>
        public string LoadError { get; private set; }

        public void Load()
        {
            AppPaths.EnsureMigrated();
            LoadError = null;
            if (!File.Exists(FilePath) && !File.Exists(BackupPath)) { Current = new AppSettings(); return; }
            try { Current = Read(FilePath); }
            catch (Exception ex)
            {
                Logger.Error("Settings.Load", ex);
                string broken = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                try { if (File.Exists(FilePath)) File.Copy(FilePath, broken, true); } catch { }
                try { Current = Read(BackupPath); LoadError = L.T("设置文件损坏，已从备份 settings.json.bak 恢复。\n损坏的文件已另存为：\n") + broken; }
                catch { Current = new AppSettings(); LoadError = L.T("设置文件损坏且没有可用备份，已恢复默认设置。\n损坏的文件已另存为：\n") + broken + L.T("\n\n错误：") + ex.Message; }
            }
            MergeDefaults(Current);
        }

        private static AppSettings Read(string path) =>
            JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Opts) ?? throw new InvalidDataException("empty settings");

        /// <summary>Adds hotkeys for actions introduced in newer versions; fills nulls.</summary>
        private static void MergeDefaults(AppSettings s)
        {
            var d = new AppSettings();
            if (s.Translate == null) s.Translate = new TranslateSettings();
            if (s.Hotkeys == null) s.Hotkeys = d.Hotkeys;
            foreach (var hk in d.Hotkeys)
                if (!s.Hotkeys.Any(h => string.Equals(h.Action, hk.Action, StringComparison.OrdinalIgnoreCase))) s.Hotkeys.Add(hk);
            if (s.Recording == null) s.Recording = d.Recording;
            if (s.Formula == null) s.Formula = d.Formula;
            if (s.Watermark == null) s.Watermark = d.Watermark;
            else
            {
                if (string.IsNullOrWhiteSpace(s.Watermark.Color)) s.Watermark.Color = "auto";
                if (string.Equals(s.Watermark.Text, AppIdentity.LegacyName, StringComparison.Ordinal))
                    s.Watermark.Text = AppIdentity.Name;
            }
            if (s.Translate == null) s.Translate = d.Translate;
            if (s.UploadTargets == null) s.UploadTargets = d.UploadTargets;
            if (string.IsNullOrWhiteSpace(s.Language)) s.Language = "auto";
            if (string.IsNullOrWhiteSpace(s.SaveFolder)) s.SaveFolder = d.SaveFolder;
            if (string.IsNullOrWhiteSpace(s.FileNameTemplate)) s.FileNameTemplate = d.FileNameTemplate;
            if (string.IsNullOrWhiteSpace(s.DefaultFormat)) s.DefaultFormat = d.DefaultFormat;
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    string tmp = FilePath + ".tmp";
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        JsonSerializer.Serialize(fs, Current, Opts);
                        fs.Flush(true);
                    }
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, BackupPath, true);
                    else File.Move(tmp, FilePath);
                }
                catch (Exception ex) { Logger.Error("Settings.Save", ex); App.Controller?.Notify("保存设置失败: " + ex.Message); }
            }
        }
    }
}
