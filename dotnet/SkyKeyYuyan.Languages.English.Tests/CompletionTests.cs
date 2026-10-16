using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.English.Tests
{
    public class CompletionTests
    {
        private static EnglishCompletionModule Module()
        {
            var module = new EnglishCompletionModule(new[] { new LexiconWord("hello", "hello", "hello", 10), new LexiconWord("help", "help", "help", 2), new LexiconWord("world", "world", "world", 5) });
            Assert.That(module.Initialize(new MemoryResourceProvider()).Succeeded, Is.True);
            return module;
        }
        private static async Task<CandidatePage> Query(ILanguageEngine engine, ILanguageModule module, string text, IReadOnlyDictionary<string, long>? counts = null)
        {
            engine.Process(CompositionCommand.Clear()); engine.Process(CompositionCommand.Insert(text));
            return await engine.QueryCandidatesAsync(new CandidateQuery(1, 1, engine.Snapshot, module.CaptureData(), frequencies: counts), CancellationToken.None);
        }
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(20)]
        public async Task PrefixPagesPreserveTiesAndPersonalFrequencyAcrossBoundaries(int pageSize)
        {
            var entries = Enumerable.Range(0, 100).Select(index =>
            {
                string word = "qa" + (char)('a' + index / 26) + (char)('a' + index % 26);
                return new LexiconWord("word-" + index, word, word);
            }).ToList();
            entries.Add(new LexiconWord("first-reading", "same", "qzaa"));
            entries.Add(new LexiconWord("second-reading", "same", "qzab"));
            using var module = new EnglishCompletionModule(entries); module.Initialize(new MemoryResourceProvider());
            using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("q"));
            var counts = new Dictionary<string, long> { ["word-99"] = 100000 };
            var actual = new List<string>();
            for (int offset = 0; ; offset += pageSize)
            {
                var page = await engine.QueryCandidatesAsync(new CandidateQuery(offset + 1, 1, engine.Snapshot, module.CaptureData(), frequencies: counts, offset: offset, pageSize: pageSize), CancellationToken.None);
                actual.AddRange(page.Candidates.Select(candidate => candidate.Words.Single().WordId));
                if (!page.HasMore) break;
                Assert.That(page.Candidates.Count, Is.EqualTo(pageSize));
            }
            Assert.That(actual, Is.EqualTo(new[] { "word-99" }.Concat(Enumerable.Range(0, 99).Select(index => "word-" + index)).Concat(new[] { "first-reading", "second-reading" })));
            var beyond = await engine.QueryCandidatesAsync(new CandidateQuery(1000, 1, engine.Snapshot, module.CaptureData(), offset: int.MaxValue, pageSize: 20), CancellationToken.None);
            Assert.That(beyond.Candidates, Is.Empty); Assert.That(beyond.HasMore, Is.False);
        }
        [TestCase("hel", "hello")]
        [TestCase("Hel", "Hello")]
        [TestCase("H", "Hello")]
        [TestCase("HEL", "HELLO")]
        [TestCase("hEl", "hEllo")]
        public async Task PrefixCandidatesKeepInputCase(string raw, string expected)
        {
            using var module = Module(); using var engine = module.CreateEngine();
            var page = await Query(engine, module, raw);
            Assert.That(page.Candidates[0].Text, Is.EqualTo(expected));
            Assert.That(page.Candidates[0].ConsumedLength, Is.EqualTo(raw.Length));
        }
        [Test]
        public async Task UserFrequencyReordersWordsWithoutChangingBaseWeights()
        {
            using var module = Module(); using var engine = module.CreateEngine();
            var page = await Query(engine, module, "hel", new Dictionary<string, long> { ["help"] = 100 });
            Assert.That(page.Candidates[0].Text, Is.EqualTo("help"));
            Assert.That(module.CaptureData().Words.Single(word => word.Id == "hello").Weight, Is.EqualTo(10));
        }
        [Test]
        public async Task CustomOverrideKeepsCanonicalIdentityAndOldViewsRemainValid()
        {
            using var module = Module(); using var engine = module.CreateEngine();
            var old = module.CaptureData();
            module.UpsertWord(new LexiconWord("game.hello", "hello", "HELLO", 999));
            var updated = module.CaptureData();
            Assert.That(updated.Words.Count(word => word.Text == "hello"), Is.EqualTo(1));
            Assert.That(updated.Words.Single(word => word.Text == "hello").Id, Is.EqualTo("hello"));
            Assert.That(updated.Words.Single(word => word.Text == "hello").Weight, Is.EqualTo(999));
            Assert.That(old.Words.Single(word => word.Id == "hello").Weight, Is.EqualTo(10));
            module.RemoveWord("game.hello");
            Assert.That(module.CaptureData().Words.Single(word => word.Id == "hello").Weight, Is.EqualTo(10));
            var page = await engine.QueryCandidatesAsync(new CandidateQuery(1, 1, new CompositionSnapshot(1, "hel"), updated), CancellationToken.None);
            Assert.That(page.Candidates[0].Text, Is.EqualTo("hello"));
        }
        [Test]
        public void FinishKeepsOriginalSpellingAndExplicitSelectionAddsNoSpace()
        {
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(Module()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            var original = new MemoryTextTarget();
            using var session = runtime.OpenSession(new SessionOptions(), original).Session!;
            session.Process(InputOperation.Insert("Hel")); WaitFor(() => session.Snapshot.Candidates != null);
            session.Process(InputOperation.Finish());
            Assert.That(original.ReadSnapshot().Text, Is.EqualTo("Hel"));
            var accepted = new MemoryTextTarget();
            using var next = runtime.OpenSession(new SessionOptions(), accepted).Session!;
            next.Process(InputOperation.Insert("hel")); WaitFor(() => next.Snapshot.Candidates != null);
            var page = next.Snapshot.Candidates!;
            next.Process(InputOperation.ConfirmCandidate(page.Query.Id, page.Candidates[0].Id)); next.Process(InputOperation.Finish());
            Assert.That(accepted.ReadSnapshot().Text, Is.EqualTo("hello"));
        }
        [Test]
        public void CompletionSwitchOnlyAffectsItsOwnSession()
        {
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(Module()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            using var first = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            using var second = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            first.Process(InputOperation.Insert("hel")); WaitFor(() => first.Snapshot.Candidates != null);
            first.Process(InputOperation.SetCompletion(false));
            Assert.That(first.Snapshot.Text.Text, Is.EqualTo("hel"));
            first.Process(InputOperation.Insert("lo"));
            Assert.That(first.Snapshot.Text.Text, Is.EqualTo("hello"));
            Assert.That(second.Snapshot.CompletionEnabled, Is.True);
            first.Process(InputOperation.SetCompletion(true)); first.Process(InputOperation.Insert("wo"));
            Assert.That(first.Snapshot.Composition.RawText, Is.EqualTo("wo"));
        }
        [Test]
        public void TextFragmentsWithSeparatorsPreserveOriginalSpellingInOrder()
        {
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(Module()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            var target = new MemoryTextTarget();
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            session.Process(InputOperation.Insert("Hel, wor"));
            WaitFor(() => session.Snapshot.PendingOperation == null && session.Snapshot.Composition.RawText == "wor");
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("Hel, "));
            session.Process(InputOperation.Finish()); WaitFor(() => session.Snapshot.Lifecycle == SessionLifecycle.Ended);
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("Hel, wor"));
        }
        [Test]
        public void CandidateNavigationCanCrossPageBoundaries()
        {
            var words = Enumerable.Range(0, 45).Select(index => new LexiconWord("word" + index, "he" + (char)('a' + index / 26) + (char)('a' + index % 26), "he" + (char)('a' + index / 26) + (char)('a' + index % 26))).ToArray();
            using var runtime = new SkyRuntime(); runtime.RegisterLanguage(new EnglishCompletionModule(words)); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            using var session = runtime.OpenSession(new SessionOptions(), new MemoryTextTarget()).Session!;
            session.Process(InputOperation.Insert("he")); WaitFor(() => session.Snapshot.Candidates != null);
            var first = session.Snapshot.Candidates!;
            session.Process(InputOperation.MoveCandidate(20)); WaitFor(() => session.Snapshot.Candidates?.Query.Offset == 20);
            Assert.That(session.Snapshot.Candidates!.Candidates[0].Text, Is.Not.EqualTo(first.Candidates[0].Text));
            session.Process(InputOperation.MoveCandidate(-1)); WaitFor(() => session.Snapshot.Candidates?.Query.Offset == 0);
            Assert.That(session.Snapshot.SelectedCandidate, Is.EqualTo(19));
        }
        [Test]
        public async Task VersionedResourceCanBeLoadedAfterAnEarlierFailure()
        {
            using var module = new EnglishCompletionModule();
            var bad = new MemoryResourceProvider(new Dictionary<string, byte[]> { [module.ResourceId] = Encoding.UTF8.GetBytes("SKY\t2\ten\t1\nhello\thello\thello\t10\n") });
            Assert.That(module.Initialize(bad).Succeeded, Is.False);
            var good = new MemoryResourceProvider(new Dictionary<string, byte[]> { [module.ResourceId] = Encoding.UTF8.GetBytes("SKY\t1\ten\t1\nhello\thello\thello\t10\n") });
            Assert.That((await module.InitializeAsync(good, CancellationToken.None)).Succeeded, Is.True);
            Assert.That(module.CaptureData().Words[0].Id, Is.EqualTo("hello"));
        }
        [Test]
        public void HostEntriesRegisteredBeforeInitializationUseBaseIdentityAndKeepRemovedHostIdentity()
        {
            using var module = new EnglishCompletionModule(new[] { new LexiconWord("base", "hello", "hello", 10) });
            module.UpsertWord(new LexiconWord("override", "hello", "HELLO", 20));
            module.UpsertWord(new LexiconWord("planet-first", "planet", "planet")); module.RemoveWord("planet-first");
            Assert.That(module.Initialize(new MemoryResourceProvider()).Succeeded, Is.True);
            Assert.That(module.CaptureData().Words.Single().Id, Is.EqualTo("base"));
            Assert.That(module.CaptureData().Words.Single().Weight, Is.EqualTo(20));
            module.UpsertWord(new LexiconWord("planet-second", "planet", "planet", 30));
            Assert.That(module.CaptureData().Words.Single(word => word.Text == "planet").Id, Is.EqualTo("planet-first"));
            module.RemoveWord("override");
            Assert.That(module.CaptureData().Words.Single(word => word.Text == "hello").Weight, Is.EqualTo(10));
        }
        [Test]
        public async Task Utf8ResourceAcceptsBomAndRejectsMalformedRowsWithoutBlockingRetry()
        {
            using var module = new EnglishCompletionModule();
            byte[] invalid = Encoding.UTF8.GetBytes("SKY\t1\ten\t1\nhello\thello\thello\t10\textra\n");
            Assert.That(module.Initialize(new MemoryResourceProvider(new Dictionary<string, byte[]> { [module.ResourceId] = invalid })).Succeeded, Is.False);
            byte[] invalidEncoding = Encoding.UTF8.GetBytes("SKY\t1\ten\t1\nhello\thello\thello\t10\n").Concat(new byte[] { 0xff }).ToArray();
            Assert.That(module.Initialize(new MemoryResourceProvider(new Dictionary<string, byte[]> { [module.ResourceId] = invalidEncoding })).Succeeded, Is.False);
            var valid = new MemoryResourceProvider(new Dictionary<string, byte[]> { [module.ResourceId] = Encoding.UTF8.GetBytes("\ufeffSKY\t1\ten\t1\r\nhello\thello\thello\t10\r\nhelp\thelp\thelp\t2\n") });
            Assert.That((await module.InitializeAsync(valid, CancellationToken.None)).Succeeded, Is.True);
            using var engine = module.CreateEngine();
            var page = await Query(engine, module, "hel");
            Assert.That(page.Candidates.Select(candidate => candidate.Text), Is.EqualTo(new[] { "hello", "help" }));
        }
        private static void WaitFor(Func<bool> condition) => Assert.That(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), Is.True, "候选未在期限内完成");
    }
}
