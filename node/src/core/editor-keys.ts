/** Port of SnapForge.Plugins.EditorKeys. */

const TYPING = new Set([
  "TextBox",
  "PasswordBox",
  "RichTextBox",
  "TextBoxBase",
  "ComboBox",
  "ComboBoxItem",
  "EditableTextBox",
  "HotkeyBox",
  "DatePicker",
  "DatePickerTextBox",
  "DataGridCell",
  "FlowDocumentScrollViewer",
  "FlowDocumentReader",
  "RichTextBoxView",
  "INPUT",
  "TEXTAREA",
  "SELECT",
]);

export function isTypingTypeName(typeName: string | null | undefined): boolean {
  if (!typeName) return false;
  const tick = typeName.lastIndexOf(".");
  if (tick >= 0) typeName = typeName.slice(tick + 1);
  if (TYPING.has(typeName)) return true;
  return typeName.endsWith("TextBox") || typeName === "input" || typeName === "textarea";
}

export function isLetterToolKey(key: string | null | undefined): boolean {
  if (!key) return false;
  return key.toUpperCase() === "E";
}

export function allowLetterShortcut(typingFocus: boolean, anyModifier: boolean, key: string): boolean {
  return !typingFocus && !anyModifier && isLetterToolKey(key);
}
