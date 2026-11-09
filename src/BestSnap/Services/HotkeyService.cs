using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using BestSnap.Native;

namespace BestSnap.Services
{
    /// <summary>Registers system-wide hotkeys using a hidden message-only HwndSource.</summary>
    public sealed class HotkeyService : IDisposable
    {
        private readonly HwndSource _source;
        private readonly Dictionary<int, Action> _handlers = new Dictionary<int, Action>();
        private int _nextId = 0x5F00;

        public HotkeyService()
        {
            var p = new HwndSourceParameters("BestSnapHotkeys") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
        }

        public bool Register(string gesture, Action handler, out string error)
        {
            error = null;
            if (!TryParse(gesture, out uint mods, out uint vk)) { error = L.T("无法解析热键: ") + gesture; return false; }
            int id = _nextId++;
            if (!NativeMethods.RegisterHotKey(_source.Handle, id, mods | NativeMethods.MOD_NOREPEAT, vk)) { error = L.T("热键已被占用: ") + gesture; return false; }
            _handlers[id] = handler;
            return true;
        }

        public void UnregisterAll()
        {
            foreach (var id in _handlers.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
            _handlers.Clear();
        }

        /// <summary>True if no other program owns the gesture (tests with a temporary thread hotkey).</summary>
        public static bool IsSystemFree(string gesture)
        {
            if (!TryParse(gesture, out uint mods, out uint vk)) return true;
            const int probe = 0xBFF0;
            if (!NativeMethods.RegisterHotKey(IntPtr.Zero, probe, mods | NativeMethods.MOD_NOREPEAT, vk)) return false;
            NativeMethods.UnregisterHotKey(IntPtr.Zero, probe);
            return true;
        }

        /// <summary>Canonical text for a key press, e.g. "Ctrl+Shift+A", "PrintScreen", "F1".</summary>
        public static string Format(ModifierKeys m, Key k, bool win)
        {
            var parts = new List<string>();
            if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (win || m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(k == Key.Snapshot ? "PrintScreen" : k >= Key.D0 && k <= Key.D9 ? k.ToString() : k.ToString());
            return string.Join("+", parts);
        }

        public static bool TryParse(string gesture, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(gesture)) return false;
            foreach (var raw in gesture.Split('+'))
            {
                var part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; break;
                    case "alt": mods |= NativeMethods.MOD_ALT; break;
                    case "shift": mods |= NativeMethods.MOD_SHIFT; break;
                    case "win": mods |= NativeMethods.MOD_WIN; break;
                    case "printscreen": case "prtsc": vk = 0x2C; break;
                    default:
                        if (Enum.TryParse(part, true, out Key k)) vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                        else return false;
                        break;
                }
            }
            return vk != 0;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue(w.ToInt32(), out var h)) { handled = true; h(); }
            return IntPtr.Zero;
        }

        public void Dispose() { UnregisterAll(); _source.Dispose(); }
    }
}
