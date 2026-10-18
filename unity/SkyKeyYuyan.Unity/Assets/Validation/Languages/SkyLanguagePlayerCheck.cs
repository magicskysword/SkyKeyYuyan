using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.Languages.English;
using SkyKeyYuyan.Unity;
using UnityEngine;

namespace SkyKeyYuyan.Validation
{
    internal static class SkyLanguagePlayerCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => SkyPlayerValidation.Register("语言资源与候选查询性能", Run);
        private static async Task Run()
        {
            var resources = SkyUnityServices.Shared.CreateOptions().Resources;
            using (var chinese = new ChinesePinyinModule())
            using (var english = new EnglishCompletionModule())
            {
                long before = GC.GetTotalMemory(true);
                var timer = Stopwatch.StartNew();
                var chineseResult = await chinese.InitializeAsync(resources, CancellationToken.None);
                var englishResult = await english.InitializeAsync(resources, CancellationToken.None);
                timer.Stop();
                SkyPlayerValidation.Require(chineseResult.Succeeded && englishResult.Succeeded, "Player 语言资源初始化失败");
                long retained = GC.GetTotalMemory(true) - before;
                SkyPlayerValidation.RecordMeasurement("language/init-repeat", chinese.CaptureData().Words.Count + english.CaptureData().Words.Count, new[] { timer.Elapsed.TotalMilliseconds }, retained);
                await Query(chinese, "n"); await Query(chinese, "nihaoshijie");
                await Query(chinese, "nh", "你好"); await Query(chinese, "nhao", "你好");
                await Query(chinese, "nhsj", "你好世界"); await Query(chinese, "nihaoshij", "你好世界");
                await Query(chinese, "zh"); await Query(chinese, "sh");
                await Query(english, "c"); await Query(english, "compu");
            }
        }
        private static async Task Query(ILanguageModule module, string input, string expected = null)
        {
            using (var engine = module.CreateEngine())
            {
                engine.Process(CompositionCommand.Insert(input));
                var query = new CandidateQuery(1, 1, engine.Snapshot, module.CaptureData());
                for (int warmup = 0; warmup < 10; warmup++) await engine.QueryCandidatesAsync(query, CancellationToken.None);
                var samples = new double[64];
                for (int index = 0; index < samples.Length; index++)
                {
                    long started = Stopwatch.GetTimestamp();
                    var task = engine.QueryCandidatesAsync(query, CancellationToken.None);
                    int sample = index;
                    // 在查询任务完成处采样，避免将主线程下一帧的调度间隔计入查询耗时。
                    await task.ContinueWith(completed =>
                    {
                        var page = completed.GetAwaiter().GetResult();
                        SkyPlayerValidation.Require(page.Candidates.Count > 0, "Player 查询缺少候选");
                        if (expected != null) SkyPlayerValidation.Require(page.Candidates[0].Text == expected, "Player 首选候选错误：" + input);
                        samples[sample] = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                SkyPlayerValidation.RecordMeasurement("query/" + module.LanguageId + "/" + input, module.CaptureData().Words.Count, samples);
            }
        }
    }
}
