using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Languages.Chinese.Tests
{
    public class GeneratedDataTests
    {
        private ChinesePinyinModule _module = null!;
        [OneTimeSetUp]
        public void LoadDictionary()
        {
            _module = new ChinesePinyinModule();
            var result = _module.Initialize(new RuntimeOptions().Resources);
            Assert.That(result.Succeeded, Is.True, result.Reason);
        }
        [OneTimeTearDown]
        public void ReleaseDictionary() => _module.Dispose();
        [TestCase("nihao", "你好")]
        [TestCase("nihaoshijie", "你好世界")]
        [TestCase("hen", "很")]
        [TestCase("nin", "您")]
        [TestCase("dun", "吨")]
        [TestCase("yinhang", "银行")]
        [TestCase("xingwei", "行为")]
        [TestCase("chongqing", "重庆")]
        [TestCase("zhongwen", "中文")]
        [TestCase("shurufa", "输入法")]
        public async Task PublishedDictionaryProvidesCommonInput(string spelling, string expected)
        {
            Assert.That(_module.CaptureData().Words.Count, Is.GreaterThan(200000));
            using var engine = _module.CreateEngine(); engine.Process(CompositionCommand.Insert(spelling));
            var page = await engine.QueryCandidatesAsync(new CandidateQuery(1, 1, engine.Snapshot, _module.CaptureData()), CancellationToken.None);
            Assert.That(page.Candidates.Select(candidate => candidate.Text), Does.Contain(expected));
            if (spelling == "nihaoshijie") Assert.That(page.Candidates[0].Text, Is.EqualTo(expected));
        }
    }
}
