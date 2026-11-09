using SnapForge.Plugins;
using Xunit;

namespace SnapForge.Tests
{
    public class EditorKeysTests
    {
        [Theory]
        [InlineData("TextBox")]
        [InlineData("System.Windows.Controls.TextBox")]
        [InlineData("PasswordBox")]
        [InlineData("RichTextBox")]
        [InlineData("ComboBox")]
        [InlineData("ComboBoxItem")]
        [InlineData("HotkeyBox")]
        [InlineData("DatePickerTextBox")]
        [InlineData("MyCustomTextBox")]
        public void TypingTypeNamesAreDetected(string name) => Assert.True(EditorKeys.IsTypingTypeName(name));

        [Theory]
        [InlineData("Button")]
        [InlineData("ToggleButton")]
        [InlineData("Canvas")]
        [InlineData("Window")]
        [InlineData("Slider")]
        [InlineData(null)]
        [InlineData("")]
        public void NonTypingTypeNamesAreRejected(string name) => Assert.False(EditorKeys.IsTypingTypeName(name));

        [Fact]
        public void LetterShortcut_BlockedWhileTyping()
        {
            Assert.False(EditorKeys.AllowLetterShortcut(true, false, "E"));
            Assert.False(EditorKeys.AllowLetterShortcut(true, false, "e"));
        }

        [Fact]
        public void LetterShortcut_BlockedWithModifiers()
        {
            Assert.False(EditorKeys.AllowLetterShortcut(false, true, "E"));
        }

        [Fact]
        public void LetterShortcut_EraserEWhenIdle()
        {
            Assert.True(EditorKeys.AllowLetterShortcut(false, false, "E"));
            Assert.True(EditorKeys.AllowLetterShortcut(false, false, "e"));
        }

        [Fact]
        public void LetterShortcut_OtherLettersNeverFire()
        {
            foreach (var k in new[] { "A", "R", "T", "S", "H", "Z", "C", "Delete", "Escape", "Space" })
                Assert.False(EditorKeys.AllowLetterShortcut(false, false, k));
        }
    }
}
