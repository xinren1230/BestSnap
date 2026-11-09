using System;

namespace SnapForge.Plugins
{
    /// <summary>
    /// Editor shortcut policy. Cause of a “every key is a shortcut” report is not assumed
    /// (OS modifiers vs handler); letter tool keys are simply never allowed while typing.
    /// </summary>
    public static class EditorKeys
    {
        /// <summary>WPF type names that mean the user is entering text (including nested ComboBox editors).</summary>
        public static bool IsTypingTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            int tick = typeName.LastIndexOf('.');
            if (tick >= 0) typeName = typeName.Substring(tick + 1);
            switch (typeName)
            {
                case "TextBox":
                case "PasswordBox":
                case "RichTextBox":
                case "TextBoxBase":
                case "ComboBox":
                case "ComboBoxItem":
                case "EditableTextBox":
                case "HotkeyBox":
                case "DatePicker":
                case "DatePickerTextBox":
                case "DataGridCell":
                case "FlowDocumentScrollViewer":
                case "FlowDocumentReader":
                case "RichTextBoxView":
                    return true;
                default:
                    return typeName.EndsWith("TextBox", StringComparison.Ordinal);
            }
        }

        /// <summary>Single-letter tool keys (currently Eraser = E). Ignored when modifiers are down or the user is typing.</summary>
        public static bool IsLetterToolKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return key.Equals("E", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Letter tool shortcuts fire only when not typing, no Alt/Ctrl/Shift/Win, and the key is a tool letter.
        /// </summary>
        public static bool AllowLetterShortcut(bool typingFocus, bool anyModifier, string key) =>
            !typingFocus && !anyModifier && IsLetterToolKey(key);
    }
}
