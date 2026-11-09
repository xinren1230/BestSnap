using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapForge.Models;
using SnapForge.Services;

namespace SnapForge.Views
{
    internal static class OverlayNative
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

        public static void Place(Window w, Int32Rect phys, bool clickThrough)
        {
            var h = new WindowInteropHelper(w).Handle;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (clickThrough) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
            SetWindowPos(h, new IntPtr(-1), phys.X, phys.Y, phys.Width, phys.Height, 0x0010 | 0x0040); // NOACTIVATE|SHOWWINDOW, topmost
        }
    }

    /// <summary>
    /// Shows key combinations and mouse clicks at the bottom centre of the recorded area.
    /// Deliberately NOT capture-excluded (see App.IsRecordedOverlay) so it appears in the recording.
    /// Plain typing (letters/digits without Ctrl/Alt/Win) is not shown, so typed text/passwords don't end up in the video.
    /// </summary>
    public class KeystrokeOverlay : Window
    {
        private readonly Int32Rect _area;
        private readonly bool _clicks;
        private readonly Border _pill;
        private readonly TextBlock _text = new TextBlock { Foreground = Brushes.White, FontSize = 26, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI") };
        private readonly DispatcherTimer _hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        private IntPtr _kb, _ms;
        private OverlayNative.HookProc _kbProc, _msProc; // keep delegates alive

        public KeystrokeOverlay(Int32Rect recordArea, bool showClicks)
        {
            _area = recordArea; _clicks = showClicks;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
            ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false; IsHitTestVisible = false;
            _pill = new Border { Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)), CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 8, 18, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24), Child = _text, Visibility = Visibility.Collapsed };
            Content = new Grid { Children = { _pill } };
            _hide.Tick += (s, e) => { _hide.Stop(); _pill.Visibility = Visibility.Collapsed; };
            SourceInitialized += (s, e) =>
            {
                int h = Math.Min(_area.Height, 160);
                OverlayNative.Place(this, new Int32Rect(_area.X, _area.Y + _area.Height - h, _area.Width, h), clickThrough: true);
                var mod = OverlayNative.GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName);
                _kbProc = KbHook; _kb = OverlayNative.SetWindowsHookEx(13, _kbProc, mod, 0);       // WH_KEYBOARD_LL
                if (_clicks) { _msProc = MsHook; _ms = OverlayNative.SetWindowsHookEx(14, _msProc, mod, 0); } // WH_MOUSE_LL
            };
            Closed += (s, e) => { if (_kb != IntPtr.Zero) OverlayNative.UnhookWindowsHookEx(_kb); if (_ms != IntPtr.Zero) OverlayNative.UnhookWindowsHookEx(_ms); _kb = _ms = IntPtr.Zero; _hide.Stop(); };
        }

        private static bool Down(int vk) => (OverlayNative.GetAsyncKeyState(vk) & 0x8000) != 0;
        private static List<string> Mods()
        {
            var m = new List<string>();
            if (Down(0x11)) m.Add("Ctrl"); if (Down(0x12)) m.Add("Alt"); if (Down(0x10)) m.Add("Shift"); if (Down(0x5B) || Down(0x5C)) m.Add("Win");
            return m;
        }

        private void Show(string s) { _text.Text = s; _pill.Visibility = Visibility.Visible; _hide.Stop(); _hide.Start(); }

        private IntPtr KbHook(int code, IntPtr w, IntPtr l)
        {
            try
            {
                int msg = w.ToInt32();
                if (code >= 0 && (msg == 0x100 || msg == 0x104)) // WM_KEYDOWN / WM_SYSKEYDOWN
                {
                    var k = Marshal.PtrToStructure<OverlayNative.KBDLLHOOKSTRUCT>(l);
                    int vk = (int)k.vkCode;
                    bool isMod = vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;
                    if (!isMod)
                    {
                        var mods = Mods();
                        var key = KeyInterop.KeyFromVirtualKey(vk);
                        bool plainChar = (key >= Key.A && key <= Key.Z) || (key >= Key.D0 && key <= Key.D9) || (key >= Key.NumPad0 && key <= Key.NumPad9) || key == Key.Space || (key >= Key.Oem1 && key <= Key.Oem102);
                        bool onlyShift = mods.Count == 0 || (mods.Count == 1 && mods[0] == "Shift");
                        if (!(plainChar && onlyShift)) { mods.Add(KeyName(key)); Show(string.Join(" + ", mods)); }
                    }
                }
            }
            catch { }
            return OverlayNative.CallNextHookEx(_kb, code, w, l);
        }

        private IntPtr MsHook(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code >= 0)
                {
                    string b = w.ToInt32() switch { 0x201 => L.T("左键"), 0x204 => L.T("右键"), 0x207 => L.T("中键"), _ => null };
                    if (b != null) { var mods = Mods(); mods.Add("🖱 " + b); Show(string.Join(" + ", mods)); }
                }
            }
            catch { }
            return OverlayNative.CallNextHookEx(_ms, code, w, l);
        }

        private static string KeyName(Key k) => k switch
        {
            Key.Return => "Enter", Key.Escape => "Esc", Key.Back => "Backspace", Key.Delete => "Del", Key.Space => "Space",
            Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓", Key.Next => "PgDn", Key.Prior => "PgUp", Key.Snapshot => "PrtSc",
            >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
            Key.OemPlus => "+", Key.OemMinus => "-", Key.OemComma => ",", Key.OemPeriod => ".", Key.Oem2 => "/", Key.Oem1 => ";", Key.Oem7 => "'", Key.Oem4 => "[", Key.Oem6 => "]", Key.Oem5 => "\\", Key.Oem3 => "`",
            _ => k.ToString()
        };
    }

    /// <summary>
    /// Webcam picture-in-picture bubble (circle or rounded rectangle) fed by the plugin's IWebcamSource.
    /// Not capture-excluded, so it is recorded; drag it to move.
    /// </summary>
    public class WebcamBubble : Window
    {
        private readonly Image _img = new Image { Stretch = Stretch.UniformToFill };
        private WriteableBitmap _wb;
        private IDisposable _session;
        private int _pending;

        public WebcamBubble(Int32Rect area, RecordingSettings o)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
            ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
            var host = new Border { Background = Brushes.Black, Child = _img, BorderBrush = Brushes.White, BorderThickness = new Thickness(0) };
            Content = host;
            bool circle = o.WebcamCircle;
            SizeChanged += (s, e) =>
            {
                var r = new Rect(0, 0, ActualWidth, ActualHeight);
                host.Clip = circle ? (Geometry)new EllipseGeometry(r) : new RectangleGeometry(r, 12, 12);
            };
            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            int size = Math.Clamp(o.WebcamSize, 80, 800);
            int w = circle ? size : size * 4 / 3, h = size, m = 16;
            int x = o.WebcamCorner is WebcamCorner.BottomLeft or WebcamCorner.TopLeft ? area.X + m : area.X + area.Width - w - m;
            int y = o.WebcamCorner is WebcamCorner.TopLeft or WebcamCorner.TopRight ? area.Y + m : area.Y + area.Height - h - m;
            SourceInitialized += (s, e) => OverlayNative.Place(this, new Int32Rect(x, y, w, h), clickThrough: false);
            Loaded += async (s, e) =>
            {
                var cam = PluginHost.Webcam;
                if (cam == null) { Close(); return; }
                try { _session = await cam.StartAsync(o.WebcamName ?? o.WebcamDevice, OnFrame); if (!IsVisible) { _session?.Dispose(); _session = null; } }
                catch (Exception ex) { Logger.Error("Webcam", ex); App.Controller?.Notify(L.T("摄像头启动失败") + ": " + ex.Message); Close(); }
            };
            Closed += (s, e) => { _session?.Dispose(); _session = null; };
        }

        private void OnFrame(byte[] px, int w, int h)
        {
            if (System.Threading.Interlocked.Exchange(ref _pending, 1) == 1) return; // drop frames while the UI is busy
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_wb == null || _wb.PixelWidth != w || _wb.PixelHeight != h) { _wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null); _img.Source = _wb; }
                    _wb.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
                }
                catch { }
                finally { System.Threading.Interlocked.Exchange(ref _pending, 0); }
            }), DispatcherPriority.Render);
        }
    }
}
