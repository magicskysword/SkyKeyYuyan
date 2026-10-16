using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.English.Tests
{
    public class GeneratedDataTests
    {
        [Test]
        public void ResourceKeyboardMatchesTheIndependentBasicEnglishLayout()
        {
            using var module = new EnglishCompletionModule();
            var actual = module.LoadKeyboard(new RuntimeOptions().Resources);
            Assert.That(actual.Keys.Select(key => new { key.Id, key.Label, key.Output, key.Alternate, key.Row, key.Column, key.ColumnSpan, key.Action }),
                Is.EqualTo(KeyboardLayout.BasicEnglish.Keys.Select(key => new { key.Id, key.Label, key.Output, key.Alternate, key.Row, key.Column, key.ColumnSpan, key.Action })));
        }
        [Test]
        public async Task PublishedDictionaryLoadsOfflineAndCompletesCommonWords()
        {
            using var module = new EnglishCompletionModule();
            var result = module.Initialize(new RuntimeOptions().Resources);
            Assert.That(result.Succeeded, Is.True, result.Reason);
            Assert.That(module.CaptureData().Words.Count, Is.GreaterThan(250000));
            using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert("hel"));
            var page = await engine.QueryCandidatesAsync(new CandidateQuery(1, 1, engine.Snapshot, module.CaptureData()), CancellationToken.None);
            Assert.That(page.Candidates[0].Text, Is.EqualTo("hello"));
            Assert.That(page.Candidates.Select(candidate => candidate.Text), Does.Contain("help"));
        }
    }
}
