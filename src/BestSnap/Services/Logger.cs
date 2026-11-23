using System;
using System.IO;

namespace BestSnap.Services
{
    /// <summary>Tiny file logger: %AppData%\BestSnap\logs\yyyy-MM-dd.log (UTF-8).</summary>
    public static class Logger
    {
        private static readonly object Lock = new object();
        public static readonly string Folder = Path.Combine(SettingsService.Folder, "logs");

        public static void Info(string msg) => Write("INFO", msg);
        public static void Error(string where, Exception ex) => Write("ERROR", where + ": " + ex);
        public static void Error(string where, string msg) => Write("ERROR", where + ": " + (msg ?? ""));

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Folder);
                    File.AppendAllText(Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd") + ".log"),
                        $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
