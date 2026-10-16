using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.Languages.English;
using SkyKeyYuyan.Languages.Korean;

if (args.Length != 3) throw new ArgumentException("需要仓库路径、报告路径和处理器名称");
var resources = new DirectoryResourceProvider(Path.Combine(args[0], "data/generated"));
var context = new PumpExecutionContext();
var initialization = new List<object>();
var measurements = new List<Measurement>();
using (var first = CreateRuntime(out var modules)) initialization.Add(Initialize(first, modules, false));
using var runtime = CreateRuntime(out var languages);
initialization.Add(Initialize(runtime, languages, true));
var chinese = languages.Single(module => module.LanguageId == "zh-Hans");
var english = languages.Single(module => module.EngineId == "en.completion");
foreach (string input in new[] { "n", "hen", "nihaoshijie", "zhonghuarenmingongheguo", "nihaoshijiewoshizhongguoren" }) Query(chinese, input);
foreach (string input in new[] { "c", "compu", "trans" }) Query(english, input);
using (var first = chinese.CreateEngine())
using (var second = english.CreateEngine())
{
    first.Process(CompositionCommand.Insert("nihaoshijie")); second.Process(CompositionCommand.Insert("compu"));
    var firstQuery = new CandidateQuery(1, 1, first.Snapshot, chinese.CaptureData());
    var secondQuery = new CandidateQuery(1, 1, second.Snapshot, english.CaptureData());
    measurements.Add(Measure("query/dual-pair", 200, () => Task.WhenAll(first.QueryCandidatesAsync(firstQuery, CancellationToken.None), second.QueryCandidatesAsync(secondQuery, CancellationToken.None)).GetAwaiter().GetResult()));
}
Feedback(1); Feedback(2);

Collect(); long beforeHistory = GC.GetTotalMemory(true);
using var history = chinese.CreateEngine();
history.Process(CompositionCommand.Insert(new string('a', 10000)));
var checkpoint = history.CaptureCheckpoint();
long historyBytes = GC.GetTotalMemory(true) - beforeHistory;
measurements.Add(Measure("history/capture", 1000, () => checkpoint = history.CaptureCheckpoint()));
using var restored = chinese.CreateEngine();
measurements.Add(Measure("history/restore", 1000, () => Require(restored.RestoreCheckpoint(checkpoint).Succeeded, "恢复失败")));
measurements.Add(Measure("history/restore-and-render", 200, () =>
{
    Require(restored.RestoreCheckpoint(checkpoint).Succeeded, "恢复失败");
    Require(restored.Snapshot.RawText.Length == 10000, "恢复后拼写长度错误");
}));
restored.Process(CompositionCommand.Backspace());
Require(restored.Snapshot.RawText.Length == 9999, "恢复后的历史回退错误");
GC.KeepAlive(history); GC.KeepAlive(checkpoint);

var archive = new MemoryFrequencyStore();
foreach (int size in new[] { 100, 10000 })
{
    var counts = Enumerable.Range(0, size).ToDictionary(index => "word-" + index, index => (long)index + 1);
    var snapshot = new FrequencySnapshot("benchmark", "zh-Hans", 1, counts);
    measurements.Add(Measure("memory-store/save/" + size, 200, () => Require(archive.Save(snapshot).Result.Succeeded, "存档失败")));
    measurements.Add(Measure("memory-store/load/" + size, 200, () => Require(archive.Load("benchmark", "zh-Hans").Snapshot?.Counts.Count == size, "读档失败")));
}
var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0], "data/manifests/sources.json")));
var report = new
{
    generatedAtUtc = DateTime.UtcNow,
    environment = new { os = RuntimeInformation.OSDescription, processor = args[2], logicalProcessors = Environment.ProcessorCount, architecture = RuntimeInformation.ProcessArchitecture.ToString(), framework = RuntimeInformation.FrameworkDescription, serverGc = System.Runtime.GCSettings.IsServerGC, configuration = "Release" },
    dataVersion = source.RootElement.GetProperty("dataVersion").GetString(),
    words = new { chinese = chinese.CaptureData().Words.Count, english = english.CaptureData().Words.Count },
    initialization,
    history = new { scalars = 10000, retainedManagedBytes = historyBytes },
    processPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
    measurements
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
foreach (var value in measurements) Console.WriteLine($"{value.Name}: P95 {value.P95Ms:F3} ms, {value.AllocatedBytesPerOperation:F0} bytes/op");

SkyRuntime CreateRuntime(out ILanguageModule[] modules)
{
    var result = new SkyRuntime();
    modules = new ILanguageModule[] { new BasicEnglishModule(), new ChinesePinyinModule(), new EnglishCompletionModule(), new KoreanModule() };
    foreach (var module in modules) Require(result.RegisterLanguage(module).Succeeded, "语言登记失败");
    return result;
}
object Initialize(SkyRuntime target, ILanguageModule[] modules, bool asynchronous)
{
    Collect(); long before = GC.GetTotalMemory(true), allocation = GC.GetTotalAllocatedBytes(true);
    var options = new RuntimeOptions
    {
        ReplaceDefault = false, AutoSaveFrequencies = false, Resources = resources, ExecutionContext = context,
        LanguageEngines = new Dictionary<string, string> { ["en"] = "en.completion" }
    };
    var timer = Stopwatch.StartNew();
    var result = asynchronous ? target.InitializeAsync(options, CancellationToken.None).GetAwaiter().GetResult() : target.Initialize(options);
    timer.Stop(); Require(result.Succeeded, result.Result.Reason ?? "初始化失败");
    long allocated = GC.GetTotalAllocatedBytes(true) - allocation;
    long retained = GC.GetTotalMemory(true) - before;
    GC.KeepAlive(modules);
    return new { mode = asynchronous ? "async-repeat-process" : "sync-first-process", elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = allocated, retainedManagedBytes = retained };
}
void Query(ILanguageModule module, string input)
{
    using var engine = module.CreateEngine(); engine.Process(CompositionCommand.Insert(input));
    var query = new CandidateQuery(1, 1, engine.Snapshot, module.CaptureData());
    measurements.Add(Measure("query/" + module.LanguageId + "/" + input, 200, () => engine.QueryCandidatesAsync(query, CancellationToken.None).GetAwaiter().GetResult()));
}
void Feedback(int players)
{
    var sessions = Enumerable.Range(0, players).Select(index => runtime.OpenSession(new SessionOptions { InitialLanguage = "zh-Hans", PlayerId = "benchmark-" + index }, new MemoryTextTarget()).Session!).ToArray();
    var samples = new List<double>();
    long allocation = GC.GetTotalAllocatedBytes(true); int[] collections = Collections();
    try
    {
        for (int word = 0; word < 100; word++)
        {
            foreach (char character in "hen")
            {
                foreach (var session in sessions)
                {
                    long published = 0, started = 0;
                    string expected = session.Snapshot.Composition.RawText + character;
                    void Changed(SessionSnapshot snapshot) { if (published == 0 && snapshot.Composition.RawText == expected) published = Stopwatch.GetTimestamp(); }
                    session.StateChanged += Changed;
                    started = Stopwatch.GetTimestamp(); session.Process(InputOperation.Insert(character.ToString()));
                    Wait(() => published != 0);
                    session.StateChanged -= Changed;
                    samples.Add(Stopwatch.GetElapsedTime(started, published).TotalMilliseconds);
                }
                Wait(() => sessions.All(session => !session.Snapshot.Querying));
            }
            foreach (var session in sessions)
            {
                var page = session.Snapshot.Candidates ?? throw new InvalidOperationException("候选为空");
                var candidate = page.Candidates.First(value => value.Text == "很");
                session.Process(InputOperation.ConfirmCandidate(page.Query.Id, candidate.Id));
            }
        }
        measurements.Add(Summarize("feedback/" + players + "-players", samples.ToArray(), GC.GetTotalAllocatedBytes(true) - allocation, collections));
    }
    finally { foreach (var session in sessions) session.Dispose(); }
}
void Wait(Func<bool> ready)
{
    var timer = Stopwatch.StartNew();
    while (!ready())
    {
        context.Drain();
        if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException("主循环等待超时");
        Thread.Yield();
    }
}
static Measurement Measure(string name, int count, Action action)
{
    for (int i = 0; i < 20; i++) action();
    Collect(); var samples = new double[count];
    long allocation = GC.GetTotalAllocatedBytes(true); int[] collections = Collections();
    for (int i = 0; i < count; i++)
    {
        long start = Stopwatch.GetTimestamp(); action(); samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    return Summarize(name, samples, GC.GetTotalAllocatedBytes(true) - allocation, collections);
}
static Measurement Summarize(string name, double[] raw, long allocated, int[] collections)
{
    var sorted = raw.Order().ToArray();
    double Percentile(double value) => sorted[Math.Clamp((int)Math.Ceiling(value * sorted.Length) - 1, 0, sorted.Length - 1)];
    return new Measurement(name, raw.Length, Percentile(.5), Percentile(.95), Percentile(.99), sorted[^1], allocated / (double)raw.Length, Collections().Zip(collections, (after, before) => after - before).ToArray(), raw);
}
static int[] Collections() => new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
internal sealed record Measurement(string Name, int Count, double MedianMs, double P95Ms, double P99Ms, double MaxMs, double AllocatedBytesPerOperation, int[] Collections, double[] RawMs);
