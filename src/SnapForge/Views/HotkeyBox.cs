using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapForge.Services;

namespace SnapForge.Views
{
    /// <summary>Press-to-record hotkey field: focus it and press the combination; Backspace/Delete clears, Esc cancels.</summary>
    public class HotkeyBox : TextBox
    {
        private string _before;
        public event Action Changed;
        public string Gesture { get => Text; set => Text = value ?? ""; }

        public HotkeyBox()
        {
            IsReadOnly = true; IsReadOnlyCaretVisible = false; Cursor = Cursors.Hand; MinWidth = 180;
            ToolTip = L.T("点击后直接按下组合键；Backspace 清除；Esc 取消");
            GotKeyboardFocus += (s, e) => { _before = Text; Background = new SolidColorBrush(Color.FromRgb(255, 250, 220)); };
            LostKeyboardFocus += (s, e) => { ClearValue(BackgroundProperty); if (!HotkeyService.TryParse(Text, out _, out _) && Text.Length > 0) Text = _before; };
            InputMethod.SetIsInputMethodEnabled(this, false);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (key == Key.Snapshot) return; // PrintScreen only arrives reliably as KeyUp
            Handle(key);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Snapshot) { e.Handled = true; Handle(key); }
        }

        private void Handle(Key key)
        {
            var mods = Keyboard.Modifiers;
            bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
            if (key == Key.Escape && mods == ModifierKeys.None) { Text = _before; MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); return; }
            if ((key == Key.Back || key == Key.Delete) && mods == ModifierKeys.None) { Text = ""; Changed?.Invoke(); return; }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
            { Text = HotkeyService.Format(mods, Key.None, win).TrimEnd('+').Replace("+None", "") + "+…"; return; }
            bool isFn = key >= Key.F1 && key <= Key.F24;
            if (mods == ModifierKeys.None && !win && !isFn && key != Key.Snapshot && key != Key.Pause && key != Key.Scroll)
            { Text = L.T("需要配合 Ctrl/Alt/Shift/Win"); return; }
            Text = HotkeyService.Format(mods, key, win);
            Changed?.Invoke();
        }
    }
}
