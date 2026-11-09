using System;
using Microsoft.Win32;
using BestSnap.Plugins;

namespace BestSnap.Services
{
    /// <summary>Per-user autostart via HKCU\Software\Microsoft\Windows\CurrentVersion\Run (no admin needed).</summary>
    public static class AutoStart
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static string Command => "\"" + Environment.ProcessPath + "\" --tray";

        static bool PointsAtUs(string v)
        {
            if (string.IsNullOrEmpty(v)) return false;
            var path = Environment.ProcessPath ?? "";
            if (path.Length > 0 && v.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return v.IndexOf(AppIdentity.ExeFileName, StringComparison.OrdinalIgnoreCase) >= 0
                || v.IndexOf(AppIdentity.LegacyName + ".exe", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(Key);
                    return PointsAtUs(k?.GetValue(AppIdentity.Name) as string)
                        || PointsAtUs(k?.GetValue(AppIdentity.LegacyName) as string);
                }
                catch { return false; }
            }
        }

        /// <summary>Copy a leftover SnapForge Run value to BestSnap and delete the old name.</summary>
        public static void MigrateLegacy()
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(Key);
                var old = k?.GetValue(AppIdentity.LegacyName) as string;
                if (string.IsNullOrEmpty(old)) return;
                if (k.GetValue(AppIdentity.Name) == null) k.SetValue(AppIdentity.Name, Command);
                k.DeleteValue(AppIdentity.LegacyName, false);
            }
            catch (Exception ex) { Logger.Error("AutoStart.Migrate", ex); }
        }

        public static void Set(bool on)
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(Key);
                if (on) k.SetValue(AppIdentity.Name, Command);
                else if (k.GetValue(AppIdentity.Name) != null) k.DeleteValue(AppIdentity.Name, false);
                if (k.GetValue(AppIdentity.LegacyName) != null) k.DeleteValue(AppIdentity.LegacyName, false);
            }
            catch (Exception ex) { Logger.Error("AutoStart", ex); throw; }
        }
    }
}
