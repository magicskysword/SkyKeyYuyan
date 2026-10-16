using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Chinese.Tests
{
    public class PinyinTests
    {
        private static ChinesePinyinModule Module()
        {
            var module = new ChinesePinyinModule(new[]
            {
                new LexiconWord("ni", "你", "ni", 100), new LexiconWord("mud", "泥", "ni", 20),
                new LexiconWord("hao", "好", "hao", 100), new LexiconWord("hello", "你好", "ni hao", 100),
                new LexiconWord("world", "世界", "shi jie", 80), new LexiconWord("very", "很", "hen", 100),
                new LexiconWord("xian", "先", "xian", 100), new LexiconWord("xian.city", "西安", "xi an", 80),
                new LexiconWord("green", "绿色", "lǜ sè", 60)
            });
            Assert.That(module.Initialize(new MemoryResourceProvider()).Succeeded, Is.True);
            return module;
        }
        private static Task<CandidatePage> Query(ILanguageEngine engine, ILanguageModule module, IReadOnlyDictionary<string, long>? frequencies = null) => engine.QueryCandidatesAsync(new CandidateQuery(1, 1, engine.Snapshot, module.CaptureData(), frequencies: frequencies), CancellationToken.None);
        [Test]
        public async Task ContinuousPinyinProvidesWholeSentenceAndPartialWordCandidates()
        {
            using var module = Module(); using var engine = module.CreateEngine();
            engine.Process(CompositionCommand.Insert("nihaoshijie"));
            var page = await Query(engine, module);
            Assert.That(page.Candidates[0].Text, Is.EqualTo("你好世界"));
            Assert.That(page.Candidates[0].Words.Select(word => word.WordId), Is.EqualTo(new[] { "hello", "world" }));
            Assert.That(page.Candidates.Any(candidate => candidate.Text == "你好" && candidate.ConsumedLength == 5), Is.True);
        }
        [Test]
        public async Task PartialChoiceAndTypedLettersShareUndoHistory()
        {
            using var module = Module(); using var engine = module.CreateEngine();
            engine.Process(CompositionCommand.Insert("nihaoshijie"));
            var page = await Query(engine, module);
            engine.Process(CompositionCommand.Choose(page.Candidates.Single(candidate => candidate.Text == "你好")));
            engine.Process(CompositionCommand.Insert("a"));
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.DisplayText, Is.EqualTo("你好shijie"));
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.RawText, Is.EqualTo("nihaoshijie"));
            Assert.That(engine.Process(CompositionCommand.Backspace()).Snapshot.RawText, Is.EqualTo("nihaoshiji"));
        }
        [TestCase("lvse")]
        [TestCase("lüse")]
        [TestCase("lu:se")]
        [TestCase("lǜsè")]
        public async Task UmlautAndToneVariantsPreserveConsumedRawRange(string spelling)
        {
            using var module = Module(); using var engine = module.CreateEngine();
            engine.Process(CompositionCommand.Insert(spelling));
            var page = await Query(engine, module);
            Assert.That(page.Candidates[0].Text, Is.EqualTo("绿色"));
            Assert.That(page.Candidates[0].ConsumedLength, Is.EqualTo(spelling.Length));
        }
        [Test]
        public async Task ApostropheForcesSyllableBoundary()
        {
            using var module = Module(); using var engine = module.CreateEngine();
            engine.Process(CompositionCommand.Insert("xi'an"));
            var page = await Query(engine, module);
            Assert.That(page.Candidates.Select(candidate => candidate.Text), Does.Contain("西安").And.Not.Contain("先"));
        }
        [Test]
        public async Task PersonalFrequencyAndHostWeightsCanReorderHomophones()
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("ni"));
            var personal = await Query(engine, module, new Dictionary<string, long> { ["mud"] = 100 });
            Assert.That(personal.Candidates[0].Text, Is.EqualTo("泥"));
            module.UpsertWord(new LexiconWord("game.mud", "泥", "nǐ", 10000));
            var host = await Query(engine, module);
            Assert.That(host.Candidates[0].Text, Is.EqualTo("泥"));
            Assert.That(host.Candidates[0].Words[0].WordId, Is.EqualTo("mud"));
        }
        [Test]
        public void MixedTextInputConvertsSegmentsAndPreservesPunctuation()
        {
            using var runtime = Runtime(); var target = new MemoryTextTarget();
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            session.Process(InputOperation.Insert("nihao,shijie"));
            WaitFor(() => session.Snapshot.PendingOperation == null && session.Snapshot.Composition.RawText == "shijie");
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("你好,"));
            session.Process(InputOperation.Finish()); WaitFor(() => session.Snapshot.Lifecycle == SessionLifecycle.Ended);
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("你好,世界"));
        }
        [Test]
        public void CompositionCanExceedTargetCapacityAndZeroTruncationEndsIt()
        {
            using var runtime = Runtime(); var target = new MemoryTextTarget("A", maxLength: 1);
            using var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
            session.Process(InputOperation.Insert("hen")); WaitFor(() => session.Snapshot.Candidates != null);
            var page = session.Snapshot.Candidates!;
            var receipt = session.Process(InputOperation.ConfirmCandidate(page.Query.Id, page.Candidates[0].Id));
            Assert.That(receipt.Result!.Edit!.Status, Is.EqualTo(TextEditStatus.Truncated));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("A"));
            Assert.That(session.Snapshot.Composition.IsEmpty, Is.True);
        }
        [Test]
        public void DraftValidationRejectsFillWithoutLosingComposition()
        {
            using var runtime = Runtime(); var target = new MemoryTextTarget(validator: _ => false);
            using var session = runtime.OpenSession(new SessionOptions(), target).Session!;
            session.Process(InputOperation.Insert("nihao")); WaitFor(() => session.Snapshot.Candidates != null);
            session.Process(InputOperation.Finish());
            Assert.That(session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Active));
            Assert.That(session.Snapshot.Composition.DisplayText, Is.EqualTo("你好"));
            Assert.That(session.Snapshot.Text.Text, Is.Empty);
            target.Validator = null;
            session.Process(InputOperation.Finish());
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("你好"));
        }
        [Test]
        public async Task UnmatchedSpellingCanBeCommittedLiterally()
        {
            using var module = Module(); using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("zzz"));
            Assert.That((await Query(engine, module)).Candidates, Is.Empty);
            Assert.That(engine.Process(CompositionCommand.Commit()).CommitText, Is.EqualTo("zzz"));
        }
        private static SkyRuntime Runtime()
        {
            var runtime = new SkyRuntime(); runtime.RegisterLanguage(Module()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false }); return runtime;
        }
        private static void WaitFor(Func<bool> condition) => Assert.That(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), Is.True, "中文转换未在期限内完成");
    }
}
