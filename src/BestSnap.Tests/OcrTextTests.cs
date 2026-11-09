using System.Collections.Generic;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class OcrTextTests
    {
        [Fact]
        public void JoinCjk_RemovesSpacesOnlyBetweenCjk()
        {
            Assert.Equal("中文", OcrText.JoinCjk("中 文"));
            Assert.Equal("中文测试", OcrText.JoinCjk("中 文 测 试"));
            Assert.Equal("こんにちは", OcrText.JoinCjk("こ ん に ち は"));
            Assert.Equal("한글", OcrText.JoinCjk("한 글"));
        }

        [Fact]
        public void JoinCjk_KeepsLatinSpaces()
        {
            Assert.Equal("Windows 11", OcrText.JoinCjk("Windows 11"));
            Assert.Equal("Hello World", OcrText.JoinCjk("Hello World"));
            Assert.Equal("使用 Windows 11 系统", OcrText.JoinCjk("使 用 Windows 11 系 统"));
        }

        [Fact]
        public void JoinCjk_DoesNotTreatFullwidthLatinAsCjk()
        {
            Assert.Equal("Ｗｉｎｄｏｗｓ １１", OcrText.JoinCjk("Ｗｉｎｄｏｗｓ １１"));
        }

        [Fact]
        public void JoinCjk_PreservesNewlines()
        {
            Assert.Equal("中文\n测试", OcrText.JoinCjk("中 文\n测 试"));
            Assert.Equal("Windows 11\nHello", OcrText.JoinCjk("Windows 11\nHello"));
        }

        [Fact]
        public void RestoreLatinSpaces_InsertsSpaceAtWideGap()
        {
            // Even with unit timesteps, a 4-step hole is > 1.8× median (1).
            var glyphs = new List<(string, double)>
            {
                ("W", 0), ("i", 1), ("n", 2), ("d", 3), ("o", 4), ("w", 5), ("s", 6),
                ("1", 10), ("1", 11)
            };
            Assert.Equal("Windows 11", OcrText.RestoreLatinSpaces(glyphs));
        }

        [Fact]
        public void RestoreLatinSpaces_DoesNotSplitTightLatin()
        {
            var glyphs = new List<(string, double)>();
            string s = "Windows11";
            for (int i = 0; i < s.Length; i++) glyphs.Add((s[i].ToString(), i));
            Assert.Equal("Windows11", OcrText.RestoreLatinSpaces(glyphs));
        }

        /// <summary>
        /// PP-OCR CTC keeps several timesteps per Latin letter (often ~8–12), and a few pairs
        /// may land on adjacent steps. Using the min gap as baseline would then insert a space
        /// between nearly every letter. The median must be the calibration.
        /// </summary>
        [Fact]
        public void RestoreLatinSpaces_RealisticCtc_DoesNotSplitLetters()
        {
            // One adjacent pair (gap 1) plus typical ~9-step letter spacing — must stay one word.
            var windows = Ctc("Windows", 0, 1, 10, 19, 28, 36, 45);
            Assert.Equal("Windows", OcrText.RestoreLatinSpaces(windows));

            var hello = Ctc("Hello", 0, 1, 10, 19, 28);
            Assert.Equal("Hello", OcrText.RestoreLatinSpaces(hello));
        }

        [Fact]
        public void RestoreLatinSpaces_RealisticCtc_InsertsWordSpacesOnly()
        {
            // Letter steps ~8–10; word breaks ~24–28 (clearly > 1.8× median). One CTC-adjacent pair inside "Windows".
            var line1 = Ctc("升级到Windows 11系统",
                0, 8, 17,                 // 升级到
                42, 43, 52, 61, 70, 79, 88, // Windows (17→42 CJK–Latin gap; one gap-1 pair)
                116, 125,                 // 11  (88→116 = 28)
                150, 159);                // 系统 (125→150 Latin–CJK gap)
            Assert.Equal("升级到 Windows 11 系统", OcrText.RestoreLatinSpaces(line1));

            var line2 = Ctc("Hello World, open source tool截图",
                0, 1, 10, 19, 28,         // Hello (adjacent H-e)
                56, 65, 74, 83, 92,       // World  (28→56 = 28)
                93,                       // comma (tight to World)
                121, 130, 139, 148,       // open  (93→121 after comma)
                176, 185, 194, 203, 212, 221, // source
                249, 258, 267, 276,       // tool
                301, 310);                // 截图 (276→301 Latin–CJK gap)
            Assert.Equal("Hello World, open source tool 截图", OcrText.RestoreLatinSpaces(line2));
        }

        [Fact]
        public void RestoreLatinSpaces_KeepsModelEmittedSpacesBetweenLatinTokens()
        {
            // Realistic letter steps with an actual space class from the recognizer.
            var glyphs = new List<(string, double)>
            {
                ("H", 0), ("e", 9), ("l", 18), ("l", 27), ("o", 36),
                (" ", 44),
                ("W", 52), ("o", 61), ("r", 70), ("l", 79), ("d", 88)
            };
            Assert.Equal("Hello World", OcrText.RestoreLatinSpaces(glyphs));
        }

        [Fact]
        public void RestoreLatinSpaces_InsertsAfterCommaEvenWhenPacked()
        {
            Assert.Equal("a, bc", OcrText.RestoreLatinSpaces(Ctc("a,bc", 0, 9, 28, 37)));
            // CTC after ',' is often a letter-sized step; still insert before a Latin letter.
            Assert.Equal("a, bc", OcrText.RestoreLatinSpaces(Ctc("a,bc", 0, 9, 10, 19)));
            Assert.Equal("1,000", OcrText.RestoreLatinSpaces(Ctc("1,000", 0, 9, 10, 19, 28)));
        }

        [Fact]
        public void RestoreLatinSpaces_InsertsCjkLatinEvenWhenPacked()
        {
            var packed = Ctc("到Win", 0, 9, 18, 27);
            Assert.Equal("到 Win", OcrText.RestoreLatinSpaces(packed));
        }

        [Fact]
        public void RestoreLatinSpaces_DoesNotInsertBetweenCjk()
        {
            var glyphs = new List<(string, double)>
            {
                ("中", 0), ("文", 4), ("测", 8), ("试", 12)
            };
            Assert.Equal("中文测试", OcrText.RestoreLatinSpaces(glyphs));
        }

        [Fact]
        public void RestoreLatinSpaces_KeepsExistingSpaceGlyph()
        {
            var glyphs = new List<(string, double)> { ("Windows", 0), (" ", 8), ("11", 10) };
            Assert.Equal("Windows 11", OcrText.RestoreLatinSpaces(glyphs));
        }

        [Fact]
        public void SplitLetterDigit_Windows11()
        {
            Assert.Equal("Windows 11", OcrText.SplitLetterDigit("Windows11"));
            Assert.Equal("Win10", OcrText.SplitLetterDigit("Win10"));
            Assert.Equal("MP4", OcrText.SplitLetterDigit("MP4"));
        }

        [Fact]
        public void SplitLetterDigit_LeavesShortTokens()
        {
            Assert.Equal("H2O", OcrText.SplitLetterDigit("H2O"));
            Assert.Equal("v2", OcrText.SplitLetterDigit("v2"));
        }

        [Fact]
        public void PostProcess_FixesMixedLine()
        {
            Assert.Equal("使用 Windows 11 系统", OcrText.PostProcess("使 用 Windows11 系 统"));
        }

        [Fact]
        public void PostProcess_InsertsCjkLatinAndCommaSpacesWithoutPositions()
        {
            // Real PP-OCR path: RestoreLatinSpaces runs per det box (CTC gaps ≈ letter steps),
            // then boxes are joined and PostProcess is the pass that actually spaces these.
            Assert.Equal("升级到 Windows 11 系统", OcrText.PostProcess("升级到Windows 11系统"));
            Assert.Equal("Hello World, open source tool 截图", OcrText.PostProcess("Hello World,open source tool截图"));
            Assert.Equal("使用 Windows 11 系统", OcrText.PostProcess("使用 Windows 11 系统"));
            Assert.Equal("1,000", OcrText.PostProcess("1,000"));
        }

        [Fact]
        public void PostProcess_NoSpaceAroundFullwidthPunct()
        {
            Assert.Equal("第一段：BestSnap 是", OcrText.PostProcess("第一段： BestSnap 是"));
            Assert.Equal("60 FPS。", OcrText.PostProcess("60 FPS 。"));
            Assert.Equal("12345。", OcrText.PostProcess("12345 。"));
            Assert.Equal("“白字黑底”", OcrText.PostProcess("“ 白字黑底 ”"));
            Assert.Equal("第一段：BestSnap", OcrText.PostProcess("第一段：BestSnap"));
        }

        [Fact]
        public void PostProcess_KeepsMp4()
        {
            Assert.Equal("录屏支持 MP4 和 GIF 格式", OcrText.PostProcess("录屏支持 MP4 和 GIF 格式"));
        }

        [Fact]
        public void RestoreLatinSpaces_PrefersPackedThisisOverGuessingAWordBreak()
        {
            // 1.4× median is not a clear word gap — leaving Thisis is better than "Th isis" / "W orld".
            var glyphs = Ctc("Thisis", 0, 10, 20, 30, 44, 54);
            Assert.Equal("Thisis", OcrText.RestoreLatinSpaces(glyphs));
            Assert.Equal("Thisis", OcrText.RestoreLatinSpaces(glyphs, em: 20));
        }

        [Fact]
        public void RestoreLatinSpaces_DoesNotSplitWorldWithUnevenLetterPitch()
        {
            // o→r gap 13 vs median ~10: 1.25×em rule split "W orld"; 1.8× median does not.
            var world = Ctc("World", 0, 10, 23, 33, 43);
            Assert.Equal("World", OcrText.RestoreLatinSpaces(world));
            Assert.Equal("World", OcrText.RestoreLatinSpaces(world, em: 20));
        }

        [Fact]
        public void RestoreLatinSpaces_DoesNotSplitMp4()
        {
            var glyphs = Ctc("MP4", 0, 10, 20);
            Assert.Equal("MP4", OcrText.RestoreLatinSpaces(glyphs));
            Assert.Equal("MP4", OcrText.RestoreLatinSpaces(glyphs, em: 20));
        }

        [Fact]
        public void IsCjk_ExcludesAsciiAndFullwidthLatin()
        {
            Assert.True(OcrText.IsCjk('中'));
            Assert.True(OcrText.IsCjk('あ'));
            Assert.True(OcrText.IsCjk('한'));
            Assert.False(OcrText.IsCjk('W'));
            Assert.False(OcrText.IsCjk('1'));
            Assert.False(OcrText.IsCjk(' '));
            Assert.False(OcrText.IsCjk('Ｗ'));
            Assert.False(OcrText.IsCjk('１'));
        }

        /// <summary>Build per-glyph CTC timesteps. Spaces in <paramref name="text"/> are word breaks only (not glyphs).</summary>
        static List<(string, double)> Ctc(string text, params double[] positions)
        {
            var glyphs = new List<(string, double)>();
            int i = 0;
            foreach (var c in text)
            {
                if (c == ' ') continue;
                glyphs.Add((c.ToString(), positions[i++]));
            }
            return glyphs;
        }
    }
}
