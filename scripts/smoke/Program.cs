using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.Languages.English;
using SkyKeyYuyan.Languages.Korean;

var context = new PumpExecutionContext();
var cases = new List<string>();
using (var basic = new SkyRuntime())
{
    basic.RegisterLanguage(new BasicEnglishModule());
    Require(basic.Initialize(new RuntimeOptions { ReplaceDefault = false, ExecutionContext = context }).Succeeded, "基础英语初始化失败");
    var target = new MemoryTextTarget("ABCD", 2, 2, 5);
    using var session = basic.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session!;
    session.Process(InputOperation.Insert("XYZ"));
    session.Process(InputOperation.Cancel());
    Require(target.ReadSnapshot().Text == "ABXCD", "原框长度限制结果错误");
    cases.Add("基础英语与原框长度限制");
}

using var runtime = new SkyRuntime();
runtime.RegisterLanguage(new ChinesePinyinModule());
runtime.RegisterLanguage(new EnglishCompletionModule());
runtime.RegisterLanguage(new KoreanModule());
var initialized = runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, ExecutionContext = context });
Require(initialized.Succeeded, initialized.Result.Reason ?? "语言初始化失败");
Check("zh-Hans", "hen", "很", "很");
Check("zh-Hans", "nihaoshijie", "你好世界");
Check("zh-Hans", "nh", "你好", "你好");
Check("zh-Hans", "nhao", "你好", "你好");
Check("zh-Hans", "nhsj", "你好世界", "你好世界");
Check("zh-Hans", "nihaoshij", "你好世界", "你好世界");
using (var session = runtime.OpenSession(new SessionOptions { InitialLanguage = "zh-Hans" }, new MemoryTextTarget()).Session!)
{
    session.Process(InputOperation.Insert("nhsj"));
    Wait(() => !session.Snapshot.Querying && session.Snapshot.Candidates != null);
    var page = session.Snapshot.Candidates!;
    var hello = page.Candidates.Single(value => value.Text == "你好" && value.ConsumedLength == 2);
    session.Process(InputOperation.ConfirmCandidate(page.Query.Id, hello.Id));
    Wait(() => !session.Snapshot.Querying && session.Snapshot.Composition.RawText == "sj");
    session.Process(InputOperation.Backspace());
    Wait(() => !session.Snapshot.Querying && session.Snapshot.Composition.RawText == "nhsj" && session.Snapshot.Candidates != null);
    page = session.Snapshot.Candidates!;
    session.Process(InputOperation.ConfirmCandidate(page.Query.Id, page.Candidates[0].Id));
    Wait(() => session.Snapshot.Composition.IsEmpty);
    Require(session.Snapshot.Text.Text == "你好世界", "中文简拼分段回退结果错误");
    session.Process(InputOperation.Cancel());
    cases.Add("中文简拼分段选择与回退");
}
Check("en", "compu", "computer", "computer");
using (var session = runtime.OpenSession(new SessionOptions { InitialLanguage = "en" }, new MemoryTextTarget()).Session!)
{
    session.Process(InputOperation.SetCompletion(false));
    session.Process(InputOperation.Insert("RawCase"));
    Require(session.Snapshot.Text.Text == "RawCase", "关闭补全后大小写错误");
    session.Process(InputOperation.Cancel());
    cases.Add("英语补全开关与大小写");
}
var koreanTarget = new MemoryTextTarget();
using (var session = runtime.OpenSession(new SessionOptions { InitialLanguage = "ko" }, koreanTarget).Session!)
{
    session.Process(InputOperation.Insert("rksk"));
    Require(session.Snapshot.Composition.DisplayText == "가나", "韩语组合错误");
    session.Process(InputOperation.Backspace());
    Require(session.Snapshot.Composition.DisplayText == "간", "韩语回退错误");
    session.Process(InputOperation.Finish());
    Wait(() => session.Snapshot.Lifecycle == SessionLifecycle.Ended);
    Require(koreanTarget.ReadSnapshot().Text == "간", "韩语保存错误");
    cases.Add("韩语组合与回退");
}

var report = new
{
    passed = true,
    runtime = Environment.Version.ToString(),
    assembly = typeof(SkyRuntime).Assembly.GetName().Version?.ToString(),
    resources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "sky-data"), "*.bytes", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(AppContext.BaseDirectory, path)).Order().ToArray(),
    cases
};
if (args.Length != 1) throw new ArgumentException("需要结果 JSON 路径");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"NuGet consumer passed: {cases.Count} cases");

void Check(string language, string input, string expected, string? choice = null)
{
    var target = new MemoryTextTarget();
    using var session = runtime.OpenSession(new SessionOptions { InitialLanguage = language }, target).Session!;
    session.Process(InputOperation.Insert(input));
    Wait(() => !session.Snapshot.Querying && session.Snapshot.Candidates != null);
    if (choice != null)
    {
        var page = session.Snapshot.Candidates!;
        var candidate = page.Candidates.SingleOrDefault(value => value.Text == choice);
        Require(candidate != null, "发布词库缺少候选：" + choice);
        session.Process(InputOperation.ConfirmCandidate(page.Query.Id, candidate!.Id));
    }
    session.Process(InputOperation.Finish());
    Wait(() => session.Snapshot.Lifecycle == SessionLifecycle.Ended);
    Require(target.ReadSnapshot().Text == expected, "转换结果错误：" + input);
    cases.Add(language + " " + input + " → " + expected);
}
void Wait(Func<bool> ready)
{
    var timer = Stopwatch.StartNew();
    while (!ready())
    {
        context.Drain();
        if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException("输入流程超时");
        Thread.Sleep(1);
    }
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
