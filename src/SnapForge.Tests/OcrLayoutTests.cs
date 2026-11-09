using SnapForge.Plugins;
using Xunit;

namespace SnapForge.Tests
{
    public class OcrLayoutTests
    {
        static OcrSpan B(string t, double x0, double y0, double x1, double y1) => new OcrSpan(t, x0, y0, x1, y1);

        [Fact]
        public void Format_SortsTopToBottomThenLeftToRight()
        {
            var boxes = new[]
            {
                B("Bottom", 0, 22, 40, 38),
                B("right", 50, 0, 90, 16),
                B("left", 0, 1, 40, 15),
            };
            Assert.Equal("left right\nBottom", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_MergesSameVisualLineWithSpace()
        {
            var boxes = new[]
            {
                B("Hello", 0, 0, 40, 16),
                B("World", 48, 1, 96, 17),
            };
            Assert.Equal("Hello World", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_InsertsParagraphBreakOnLargeVerticalGap()
        {
            var boxes = new[]
            {
                B("First", 0, 0, 50, 16),
                B("Second", 0, 50, 60, 66),
            };
            Assert.Equal("First\n\nSecond", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_PreservesRoughIndent()
        {
            var boxes = new[]
            {
                B("Title", 0, 0, 50, 16),
                B("body", 40, 20, 90, 36),
            };
            string s = OcrLayout.Format(boxes);
            Assert.StartsWith("Title\n", s);
            Assert.Matches(@"Title\n +body", s);
        }

        [Fact]
        public void Format_AlignsSimpleColumnsWithTabs()
        {
            var boxes = new[]
            {
                B("A", 0, 0, 20, 16), B("B", 100, 0, 120, 16),
                B("C", 0, 20, 20, 36), B("D", 100, 20, 120, 36),
            };
            Assert.Equal("A\tB\nC\tD", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_JoinsWrappedCjkWithoutSpace()
        {
            var boxes = new[]
            {
                B("你好世界", 0, 0, 80, 16),
                B("这是续行", 0, 18, 80, 34),
            };
            Assert.Equal("你好世界这是续行", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_JoinsWrappedLatinWithSpace()
        {
            var boxes = new[]
            {
                B("hello", 0, 0, 40, 16),
                B("world", 0, 18, 50, 34),
            };
            Assert.Equal("hello world", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_DehyphenatesWrappedLatin()
        {
            var boxes = new[]
            {
                B("exam-", 0, 0, 50, 16),
                B("ple", 0, 18, 30, 34),
            };
            Assert.Equal("example", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_KeepsSentenceBreak()
        {
            var boxes = new[]
            {
                B("Done.", 0, 0, 50, 16),
                B("Next", 0, 18, 40, 34),
            };
            Assert.Equal("Done.\nNext", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_ThenPostProcess_KeepsCjkLatinRules()
        {
            var boxes = new[]
            {
                B("升级到Windows", 0, 0, 80, 16),
                B("11系统", 84, 0, 130, 16),
            };
            Assert.Equal("升级到 Windows 11 系统", OcrText.PostProcess(OcrLayout.Format(boxes)));
        }

        [Fact]
        public void Format_NoSpaceAroundFullwidthPunctOnSameLine()
        {
            var boxes = new[]
            {
                B("第一段：", 0, 0, 50, 16),
                B("SnapForge", 52, 0, 120, 16),
                B("FPS", 130, 0, 160, 16),
                B("。", 162, 0, 178, 16),
            };
            Assert.Equal("第一段：SnapForge FPS。", OcrText.PostProcess(OcrLayout.Format(boxes)));
        }

        [Fact]
        public void Format_TwoOrdinaryLines_NoParagraphBreak()
        {
            // ocr_test.png: two stacked lines, normal leading — must not insert a blank line.
            var boxes = new[]
            {
                B("升级到 Windows 11 系统", 10, 8, 380, 32),
                B("Hello World, open source tool 截图", 10, 36, 520, 60),
            };
            Assert.Equal("升级到 Windows 11 系统\nHello World, open source tool 截图", OcrLayout.Format(boxes));
        }

        [Fact]
        public void Format_ParagraphBreakRequiresLargeCenterAndWhitespace()
        {
            // ocr_para.png-like: single-spaced pairs, real whitespace only between sections.
            // Small-font line and dark band are consecutive (no extra blank).
            var boxes = new[]
            {
                B("第一段：工具", 0, 0, 200, 20),
                B("It supports capture", 0, 24, 200, 44),
                B("第二段：录屏", 0, 80, 200, 100),
                B("Version 1.2.0", 0, 104, 200, 124),
                B("小字测试：Thisis a small font line。", 0, 160, 200, 176),
                B("深色背景：White text", 0, 180, 200, 196),
            };
            Assert.Equal("第一段：工具\nIt supports capture\n\n第二段：录屏\nVersion 1.2.0\n\n小字测试：Thisis a small font line。\n深色背景：White text", OcrLayout.Format(boxes));
        }
    }
}
