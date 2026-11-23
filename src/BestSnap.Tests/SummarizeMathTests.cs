using System.Collections.Generic;
using BestSnap.Plugins;
using Xunit;

namespace BestSnap.Tests
{
    public class SummarizeMathTests
    {
        [Fact]
        public void LooksChinese_ByCjkRatio()
        {
            Assert.True(SummarizeMath.LooksChinese("今天天气很好，我们开会。"));
            Assert.False(SummarizeMath.LooksChinese("Hello world, this is English."));
        }

        [Fact]
        public void SplitSentences_ZhAndEn()
        {
            var zh = SummarizeMath.SplitSentences("你好。世界！行吗？");
            Assert.Equal(3, zh.Count);
            var en = SummarizeMath.SplitSentences("Hello. World? Yes!");
            Assert.Equal(3, en.Count);
        }

        [Fact]
        public void ChunkCues_SplitsOnCharBudgetWithoutBreakingACue()
        {
            var cues = new List<TranscriptCue>();
            for (int i = 0; i < 6; i++)
                cues.Add(new TranscriptCue { Start = i, End = i + 1, Text = new string('a', 20) });
            var chunks = SummarizeMath.ChunkCues(cues, maxChars: 50);
            Assert.True(chunks.Count >= 2);
            int n = 0;
            foreach (var c in chunks) n += c.Count;
            Assert.Equal(6, n);
            foreach (var c in chunks) Assert.NotEmpty(c);
        }

        [Fact]
        public void ChunkText_KeepsSentencesTogether()
        {
            var chunks = SummarizeMath.ChunkText("One. Two. Three. Four.", 12);
            Assert.True(chunks.Count >= 2);
            Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));
        }

        [Fact]
        public void ExtractiveMarkdown_HasSectionsAndActionItems()
        {
            var cues = new[]
            {
                new TranscriptCue { Start = 0, End = 2, Text = "We discussed the launch plan in detail today." },
                new TranscriptCue { Start = 2, End = 4, Text = "The launch plan needs more testing before Friday." },
                new TranscriptCue { Start = 4, End = 6, Text = "Please send the notes to the team." },
                new TranscriptCue { Start = 6, End = 8, Text = "The launch plan is the main topic of this meeting." },
            };
            var md = SummarizeMath.ExtractiveMarkdown(cues, keyCount: 3);
            Assert.Contains("# Summary", md);
            Assert.Contains("basic on-device", md);
            Assert.Contains("## Overview", md);
            Assert.Contains("## Key points", md);
            Assert.Contains("## Action items", md);
            Assert.Contains("Please send", md);
            Assert.Contains("[00:04]", md);
        }

        [Fact]
        public void ExtractiveMarkdown_ChineseHeadingsAndAction()
        {
            var cues = new[]
            {
                new TranscriptCue { Start = 1, End = 3, Text = "今天我们讨论了发布计划。" },
                new TranscriptCue { Start = 3, End = 5, Text = "请务必在周五前完成测试。" },
                new TranscriptCue { Start = 5, End = 7, Text = "发布计划是本次会议的重点。" },
            };
            var md = SummarizeMath.ExtractiveMarkdown(cues);
            Assert.Contains("# 摘要", md);
            Assert.Contains("本机抽取式摘要", md);
            Assert.Contains("## 待办", md);
            Assert.Contains("周五", md);
        }

        [Fact]
        public void IsAction_MatchesZhAndEn()
        {
            Assert.True(SummarizeMath.IsAction("Please update the doc."));
            Assert.True(SummarizeMath.IsAction("我们需要明天发版。"));
            Assert.False(SummarizeMath.IsAction("The weather is nice."));
        }
    }
}
