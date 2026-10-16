using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SkyKeyYuyan.Core.Tests
{
    public class LanguageContractTests
    {
        [Test]
        public void EnglishKeepsProposedTextUntilTargetAcceptsIt()
        {
            using var module = new BasicEnglishModule();
            using var engine = module.CreateEngine();
            Assert.That(engine.Process(CompositionCommand.Insert("hello")).CommitText, Is.EqualTo("hello"));
            Assert.That(engine.Snapshot.DisplayText, Is.EqualTo("hello"));
            engine.Process(CompositionCommand.Clear());
            Assert.That(engine.Snapshot.IsEmpty, Is.True);
        }

        [Test]
        public void CheckpointRestoresIndependentHistoryAfterEngineRebuild()
        {
            using var module = new BasicEnglishModule();
            using var original = module.CreateEngine();
            original.Process(CompositionCommand.Insert("a"));
            original.Process(CompositionCommand.Insert("b"));
            var checkpoint = original.CaptureCheckpoint();
            original.Process(CompositionCommand.Insert("c"));
            using var rebuilt = module.CreateEngine();
            Assert.That(rebuilt.RestoreCheckpoint(checkpoint).Succeeded, Is.True);
            Assert.That(rebuilt.Process(CompositionCommand.Backspace()).Snapshot.DisplayText, Is.EqualTo("a"));
            Assert.That(original.Snapshot.DisplayText, Is.EqualTo("abc"));
        }

        [Test]
        public async Task QueryUsesCapturedFrequencyValuesAndCanRunAfterNewInput()
        {
            using var engine = new BasicEnglishModule().CreateEngine();
            engine.Process(CompositionCommand.Insert("a"));
            var counts = new Dictionary<string, long> { ["word"] = 1 };
            var query = new CandidateQuery(1, 1, engine.Snapshot, new LanguageDataSnapshot(1), frequencies: counts);
            counts["word"] = 5;
            engine.Process(CompositionCommand.Insert("b"));
            var page = await engine.QueryCandidatesAsync(query, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(page.Query.Composition.RawText, Is.EqualTo("a"));
                Assert.That(page.Query.Frequencies["word"], Is.EqualTo(1));
                Assert.That(engine.Snapshot.RawText, Is.EqualTo("ab"));
            });
        }
    }
}
