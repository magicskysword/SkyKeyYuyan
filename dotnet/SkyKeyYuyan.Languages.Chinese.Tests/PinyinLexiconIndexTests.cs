using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Chinese.Tests
{
    public class PinyinLexiconIndexTests
    {
        private static PinyinLexiconIndex Index() => new PinyinLexiconIndex(new[]
        {
            new LexiconWord("hello", "你好", "ni hao"),
            new LexiconWord("world", "世界", "shi jie"),
            new LexiconWord("china", "中国", "zhong guo"),
            new LexiconWord("travel", "出国", "chu guo"),
            new LexiconWord("first", "先", "xian"),
            new LexiconWord("city", "西安", "xi an"),
            new LexiconWord("green", "绿色", "lv se")
        });

        [TestCase("nihao")]
        [TestCase("nh")]
        [TestCase("nih")]
        [TestCase("nhao")]
        [TestCase("n'h")]
        [TestCase("NIHǍO")]
        public void SpellingVariantsKeepWordIdentityAndConsumeTypedRange(string raw)
        {
            var index = Index(); var graph = index.Parse(new PinyinSpelling.Input(raw), CancellationToken.None);
            var matches = index.MatchWords(graph, 0, CancellationToken.None).Where(match => match.Word.Id == "hello" && match.End == graph.Input.Key.Length).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1));
            Assert.That(graph.Input.Consumed(matches[0].End), Is.EqualTo(raw.Length));
            Assert.That(matches[0].Word.Spelling, Is.EqualTo("ni hao"));
        }

        [TestCase("zg", "china")]
        [TestCase("zhg", "china")]
        [TestCase("zhongg", "china")]
        [TestCase("cg", "travel")]
        [TestCase("chg", "travel")]
        [TestCase("shj", "world")]
        [TestCase("sj", "world")]
        public void InitialsAndDigraphsMatchSyllableSequences(string raw, string expected)
        {
            var index = Index(); var graph = index.Parse(new PinyinSpelling.Input(raw), CancellationToken.None);
            Assert.That(index.MatchWords(graph, 0, CancellationToken.None).Any(match => match.End == raw.Length && match.Word.Id == expected), Is.True);
        }

        [Test]
        public void ExplicitBoundariesConstrainFullAndAbbreviatedSpellings()
        {
            var index = Index();
            foreach (string raw in new[] { "xi'an", "x'a" })
            {
                var graph = index.Parse(new PinyinSpelling.Input(raw), CancellationToken.None);
                var words = index.MatchWords(graph, 0, CancellationToken.None).Where(match => match.End == graph.Input.Key.Length).Select(match => match.Word.Id);
                Assert.That(words, Does.Contain("city").And.Not.Contain("first"));
            }
        }

        [Test]
        public void CompletionIsRestrictedToTheFinalOpenSyllable()
        {
            var index = Index();
            var open = index.Parse(new PinyinSpelling.Input("niha"), CancellationToken.None);
            Assert.That(index.MatchWords(open, 0, CancellationToken.None).Any(match => match.Word.Id == "hello" && match.End == 4), Is.True);
            var closed = index.Parse(new PinyinSpelling.Input("niha'"), CancellationToken.None);
            Assert.That(index.MatchWords(closed, 0, CancellationToken.None).Any(match => match.Word.Id == "hello" && match.End == 4), Is.False);
            var middle = index.Parse(new PinyinSpelling.Input("nihashijie"), CancellationToken.None);
            Assert.That(index.MatchWords(middle, 0, CancellationToken.None).Any(match => match.Word.Id == "hello" && match.End == 4), Is.False);
        }

        [Test]
        public void ExactSpellingKeepsItsLowestCostAcrossAmbiguousEdges()
        {
            var index = Index(); var graph = index.Parse(new PinyinSpelling.Input("nihao"), CancellationToken.None);
            var matches = index.MatchWords(graph, 0, CancellationToken.None).Where(match => match.Word.Id == "hello" && match.End == 5).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1)); Assert.That(matches[0].Penalty, Is.Zero);
        }

        [Test]
        public void MatchingCanStartAfterAnEarlierWord()
        {
            var index = Index(); var graph = index.Parse(new PinyinSpelling.Input("nhshij"), CancellationToken.None);
            var matches = index.MatchWords(graph, 2, CancellationToken.None).Where(match => match.Word.Id == "world").ToArray();
            Assert.That(matches, Has.Length.EqualTo(1)); Assert.That(matches[0].End, Is.EqualTo(6));
        }

        [Test]
        public void EmptyDictionaryAndCancelledQueriesAreHandled()
        {
            var empty = new PinyinLexiconIndex(Array.Empty<LexiconWord>());
            var graph = empty.Parse(new PinyinSpelling.Input("nh"), CancellationToken.None);
            Assert.That(empty.MatchWords(graph, 0, CancellationToken.None), Is.Empty);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => empty.Parse(new PinyinSpelling.Input("nh"), cancellation.Token));
            Assert.Throws<OperationCanceledException>(() => empty.MatchWords(graph, 0, cancellation.Token).ToArray());
        }
    }
}
