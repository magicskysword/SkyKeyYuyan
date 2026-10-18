using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Chinese.Tests
{
    public class PinyinAbbreviationTests
    {
        private static ChinesePinyinModule Module()
        {
            var module = new ChinesePinyinModule(new[]
            {
                new LexiconWord("you", "你", "ni", 100),
                new LexiconWord("hello", "你好", "ni hao", 100),
                new LexiconWord("world", "世界", "shi jie", 80),
                new LexiconWord("china", "中国", "zhong guo", 100),
                new LexiconWord("green", "绿色", "lv se", 100),
                new LexiconWord("first", "先", "xian", 100),
                new LexiconWord("city", "西安", "xi an", 80)
            });
            Assert.That(module.Initialize(new MemoryResourceProvider()).Succeeded, Is.True);
            return module;
        }

        private static Task<CandidatePage> Query(ILanguageEngine engine, ILanguageModule module, IReadOnlyDictionary<string, long>? counts = null)
            => engine.QueryCandidatesAsync(new CandidateQuery(1, 1, engine.Snapshot, module.CaptureData(), frequencies: counts), CancellationToken.None);

        [TestCase("nh", "n'h")]
        [TestCase("nhao", "n'hao")]
        [TestCase("nih", "ni'h")]
        [TestCase("n'h", "n'h")]
        [TestCase("N’H", "N’H")]
        public async Task AbbreviatedHelloKeepsRawInputPreviewAndIdentity(string raw, string display)
        {
            using var module = Module(); using var engine = module.CreateEngine();
            engine.Process(CompositionCommand.Insert(raw));
            Assert.That(engine.Snapshot.RawText, Is.EqualTo(raw)); Assert.That(engine.Snapshot.DisplayText, Is.EqualTo(display));
            var page = await Query(engine, module); var hello = page.Candidates[0];
            Assert.That(hello.Text, Is.EqualTo("你好")); Assert.That(hello.ConsumedLength, Is.EqualTo(raw.Length));
            Assert.That(hello.Words.Single().WordId, Is.EqualTo("hello"));
            Assert.That(engine.Process(CompositionCommand.Choose(hello)).CommitText, Is.EqualTo("你好"));
        }

        [TestCase("nhsj")]
        [TestCase("nihaosj")]
        [TestCase("nhshijie")]
        [TestCase("nihaoshij")]
        public async Task MixedSentencesIncludeTheirOriginalWordIdentities(string raw)
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert(raw));
            var page = await Query(engine, module); var sentence = page.Candidates[0];
            Assert.That(sentence.Text, Is.EqualTo("你好世界")); Assert.That(sentence.ConsumedLength, Is.EqualTo(raw.Length));
            Assert.That(sentence.Words.Select(word => word.WordId), Is.EqualTo(new[] { "hello", "world" }));
        }

        [TestCase("zg")]
        [TestCase("zhg")]
        [TestCase("zhongg")]
        public async Task InitialAndDigraphFormsUseTheSameWord(string raw)
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert(raw));
            var page = await Query(engine, module);
            Assert.That(page.Candidates[0].Text, Is.EqualTo("中国")); Assert.That(page.Candidates[0].Words.Single().WordId, Is.EqualTo("china"));
        }

        [Test]
        public async Task PartialSelectionAndCheckpointRestoreTypedAbbreviations()
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("N'HSJ"));
            var page = await Query(engine, module); var hello = page.Candidates.Single(candidate => candidate.Text == "你好" && candidate.ConsumedLength == 3);
            Assert.That(engine.Process(CompositionCommand.Choose(hello)).Snapshot.RawText, Is.EqualTo("SJ"));
            Assert.That(engine.Snapshot.SelectedText, Is.EqualTo("你好"));
            var checkpoint = engine.CaptureCheckpoint();
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.RawText, Is.EqualTo("N'HSJ"));
            engine.RestoreCheckpoint(checkpoint);
            var remaining = await Query(engine, module);
            Assert.That(engine.Process(CompositionCommand.Choose(remaining.Candidates[0])).CommitText, Is.EqualTo("你好世界"));
        }

        [TestCase("lu:s")]
        [TestCase("lǜs")]
        [TestCase("lu\u0308s")]
        public async Task NormalizedMixedSpellingsConsumeTheOriginalCharacters(string raw)
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert(raw));
            var page = await Query(engine, module);
            Assert.That(page.Candidates[0].Text, Is.EqualTo("绿色")); Assert.That(page.Candidates[0].ConsumedLength, Is.EqualTo(raw.Length));
        }

        [Test]
        public async Task PersonalFrequencyAndDictionarySnapshotsApplyToAbbreviations()
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("nh"));
            var before = new CandidateQuery(1, 1, engine.Snapshot, module.CaptureData());
            Assert.That(module.UpsertWord(new LexiconWord("greeting", "您好", "nin hao", 10)).Succeeded, Is.True);
            var old = await engine.QueryCandidatesAsync(before, CancellationToken.None);
            Assert.That(old.Candidates.Select(candidate => candidate.Text), Does.Not.Contain("您好"));
            var personal = await Query(engine, module, new Dictionary<string, long> { ["greeting"] = 100 });
            Assert.That(personal.Candidates[0].Text, Is.EqualTo("您好"));
            Assert.That(module.RemoveWord("greeting").Succeeded, Is.True);
            var removed = await Query(engine, module);
            Assert.That(removed.Candidates.Select(candidate => candidate.Text), Does.Not.Contain("您好"));
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(20)]
        public async Task AbbreviationPagesRemainStableAndMergeReadings(int pageSize)
        {
            var words = Enumerable.Range(0, 75).Select(index => new LexiconWord("word-" + index, "候选" + index.ToString("D3"), "ni hao", 1)).ToList();
            words.Add(new LexiconWord("reading-low", "重读", "ni hao", 2));
            words.Add(new LexiconWord("reading-high", "重读", "nin hao", 9));
            using var module = new ChinesePinyinModule(words); module.Initialize(new MemoryResourceProvider());
            using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("nh"));
            var counts = new Dictionary<string, long> { ["word-74"] = 1000 };
            var actual = new List<string>();
            for (int offset = 0; ; offset += pageSize)
            {
                var page = await engine.QueryCandidatesAsync(new CandidateQuery(offset + 1, 1, engine.Snapshot, module.CaptureData(), frequencies: counts, offset: offset, pageSize: pageSize), CancellationToken.None);
                actual.AddRange(page.Candidates.Select(candidate => candidate.Words.Single().WordId));
                if (!page.HasMore) break;
                Assert.That(offset, Is.LessThan(100));
            }
            var expected = new[] { "word-74", "reading-high" }.Concat(Enumerable.Range(0, 74).Select(index => "word-" + index));
            Assert.That(actual, Is.EqualTo(expected));
            var outside = await engine.QueryCandidatesAsync(new CandidateQuery(99, 1, engine.Snapshot, module.CaptureData(), offset: int.MaxValue), CancellationToken.None);
            Assert.That(outside.Candidates, Is.Empty); Assert.That(outside.HasMore, Is.False);
        }

        [Test]
        public async Task MultipleReadingsKeepDistinctSentencePathsAvailable()
        {
            var words = new[] { "na", "nai", "nan", "nang", "nao", "ne", "nei", "nen", "neng", "ni" }
                .Select((spelling, index) => new LexiconWord("reading-" + index, "甲", spelling + " hao", 100)).ToList();
            words.Add(new LexiconWord("other", "乙", "nu hao", 90));
            words.Add(new LexiconWord("world", "世界", "shi jie", 80));
            using var module = new ChinesePinyinModule(words); module.Initialize(new MemoryResourceProvider());
            using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("nhsj"));
            var page = await Query(engine, module);
            Assert.That(page.Candidates.Where(candidate => candidate.ConsumedLength == 4).Select(candidate => candidate.Text), Does.Contain("甲世界").And.Contain("乙世界"));
        }

        [Test]
        public async Task ExplicitFinalBoundaryClosesTheIncompleteSyllable()
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("niha'"));
            var page = await Query(engine, module);
            Assert.That(page.Candidates.Any(candidate => candidate.Text == "你好" && candidate.ConsumedLength == 5), Is.False);
        }
    }
}
