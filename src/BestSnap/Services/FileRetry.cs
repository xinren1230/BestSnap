using System;
using System.IO;
using System.Threading.Tasks;

namespace BestSnap.Services
{
    /// <summary>Helpers for files that another component (e.g. the recorder's muxer) may still hold briefly.</summary>
    public static class FileRetry
    {
        /// <summary>Waits until the file can be opened exclusively (or attempts run out).</summary>
        public static async Task<bool> WaitUnlockedAsync(string path, int attempts = 20, int delayMs = 250)
        {
            for (int i = 0; i < attempts; i++)
            {
                try { using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return true; }
                catch (FileNotFoundException) { return false; }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                await Task.Delay(delayMs);
            }
            return false;
        }

        public static async Task MoveAsync(string src, string dst, bool overwrite = true, int attempts = 20, int delayMs = 250)
        {
            for (int i = 0; ; i++)
            {
                try { File.Move(src, dst, overwrite); return; }
                catch (IOException) when (i < attempts - 1) { await Task.Delay(delayMs); }
                catch (UnauthorizedAccessException) when (i < attempts - 1) { await Task.Delay(delayMs); }
            }
        }

        public static async Task CopyAsync(string src, string dst, bool overwrite = true, int attempts = 20, int delayMs = 250)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(src, dst, overwrite); return; }
                catch (IOException) when (i < attempts - 1) { await Task.Delay(delayMs); }
            }
        }
    }
}
