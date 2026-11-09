using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SnapForge.Native
{
    /// <summary>Snapshot of visible top-level windows (z-order) and their child controls, in screen pixels.</summary>
    public static class WindowEnumerator
    {
        private delegate bool EnumProc(IntPtr hwnd, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int n);

        /// <summary>Top-level window; child controls are enumerated lazily the first time the cursor enters it.</summary>
        public class Win
        {
            public IntPtr Handle; public NativeMethods.RECT Rect;
            private List<NativeMethods.RECT> _children;
            public List<NativeMethods.RECT> Children
            {
                get
                {
                    if (_children != null) return _children;
                    var list = new List<NativeMethods.RECT>();
                    EnumChildWindows(Handle, (c, l2) =>
                    {
                        if (NativeMethods.IsWindowVisible(c) && NativeMethods.GetWindowRect(c, out var cr) && cr.Width > 4 && cr.Height > 4) list.Add(cr);
                        return true;
                    }, IntPtr.Zero);
                    return _children = list;
                }
            }
        }

        /// <summary>Visible top-level windows in z-order, excluding SnapForge's own windows and the desktop.</summary>
        public static List<Win> Snapshot()
        {
            var list = new List<Win>();
            EnumWindows((h, l) =>
            {
                if (!NativeMethods.IsWindowVisible(h) || IsIconic(h) || NativeMethods.IsOwnWindow(h)) return true;
                var sb = new StringBuilder(64); GetClassName(h, sb, 64);
                var cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW") return true;
                var r = NativeMethods.GetWindowBounds(h);
                if (r.Width < 4 || r.Height < 4) return true;
                list.Add(new Win { Handle = h, Rect = r });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>Smallest rect (window or child control) containing the point in the front-most window.</summary>
        public static NativeMethods.RECT? HitTest(List<Win> wins, int x, int y)
        {
            foreach (var w in wins)
            {
                if (!Contains(w.Rect, x, y)) continue;
                var best = w.Rect;
                foreach (var c in w.Children)
                    if (Contains(c, x, y) && (long)c.Width * c.Height < (long)best.Width * best.Height) best = c;
                return best;
            }
            return null;
        }

        /// <summary>Foreground window unless it is ours (toolbar/tray) – then the top-most other app window.</summary>
        public static IntPtr ResolveTargetWindow()
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && !NativeMethods.IsOwnWindow(fg)) return NativeMethods.GetAncestor(fg, NativeMethods.GA_ROOT);
            var all = Snapshot();
            return all.Count > 0 ? all[0].Handle : IntPtr.Zero;
        }

        private static bool Contains(NativeMethods.RECT r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }
}
