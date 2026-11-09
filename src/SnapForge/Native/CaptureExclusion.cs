using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SnapForge.Native
{
    public static class CaptureExclusion
    {
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // Windows 10 2004+
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        /// <summary>Hide a window from screen capture/recording (no-op on older Windows).</summary>
        public static bool Apply(Window w)
        {
            try { return SetWindowDisplayAffinity(new WindowInteropHelper(w).EnsureHandle(), WDA_EXCLUDEFROMCAPTURE); } catch { return false; }
        }
    }
}
