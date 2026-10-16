using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 语言模块向运行时声明的输入行为。
    /// </summary>
    [Flags]
    public enum LanguageCapabilities
    {
        /// <summary>
        /// 直接输入或仅组合字符。
        /// </summary>
        None = 0,
        /// <summary>
        /// 可以生成候选。
        /// </summary>
        Candidates = 1,
        /// <summary>
        /// 完成组字时可以自动接受当前转换。
        /// </summary>
        ConvertOnCommit = 2,
        /// <summary>
        /// 支持会话内补全开关。
        /// </summary>
        CompletionSwitch = 4
    }
    /// <summary>
    /// 含阶段信息的可识别服务结果。
    /// </summary>
    public sealed class ServiceResult
    {
        /// <summary>
        /// 创建服务结果。
        /// </summary>
        public ServiceResult(bool succeeded, string stage = "", string? reason = null, bool cancelled = false)
        { Succeeded = succeeded; Stage = stage; Reason = reason; Cancelled = cancelled; }
        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool Succeeded { get; }
        /// <summary>
        /// 执行阶段。
        /// </summary>
        public string Stage { get; }
        /// <summary>
        /// 失败原因。
        /// </summary>
        public string? Reason { get; }
        /// <summary>
        /// 是否被取消。
        /// </summary>
        public bool Cancelled { get; }
    }

    /// <summary>
    /// 提供平台无关的语言资源读取。
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>
        /// 读取完整资源，缺失时抛出可定位的异常。
        /// </summary>
        byte[] Read(string resourceId);
        /// <summary>
        /// 异步读取完整资源。
        /// </summary>
        Task<byte[]> ReadAsync(string resourceId, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 一条具有稳定身份的词典记录。
    /// </summary>
    public sealed class LexiconWord
    {
        private static readonly char[] FieldSeparators = { '\t', '\r', '\n' };
        /// <summary>
        /// 创建词条，拼写由对应语言模块规范化。
        /// </summary>
        public LexiconWord(string id, string text, string spelling, double weight = 1)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(spelling)) throw new ArgumentException("词条身份、文本和拼写不能为空");
            if (id.IndexOfAny(FieldSeparators) >= 0 || text.IndexOfAny(FieldSeparators) >= 0 || spelling.IndexOfAny(FieldSeparators) >= 0) throw new ArgumentException("词条字段不能包含制表符或换行");
            if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
            Id = id; Text = text; Spelling = spelling; Weight = weight;
        }
        /// <summary>
        /// 跨数据构建稳定的词条身份。
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// 输出文本。
        /// </summary>
        public string Text { get; }
        /// <summary>
        /// 匹配拼写。
        /// </summary>
        public string Spelling { get; }
        /// <summary>
        /// 基础排序权重。
        /// </summary>
        public double Weight { get; }
    }

    /// <summary>
    /// 一个查询固定使用的不可变词典视图。
    /// </summary>
    public sealed class LanguageDataSnapshot
    {
        /// <summary>
        /// 复制词条并固定词典版本和可选索引。
        /// </summary>
        public LanguageDataSnapshot(long version, IEnumerable<LexiconWord>? words = null, object? index = null)
        { Version = version; Words = Array.AsReadOnly((words ?? Enumerable.Empty<LexiconWord>()).ToArray()); Index = index; }
        /// <summary>
        /// 词典版本。
        /// </summary>
        public long Version { get; }
        /// <summary>
        /// 有效词条集合。
        /// </summary>
        public IReadOnlyList<LexiconWord> Words { get; }
        /// <summary>
        /// 语言模块构建的只读查询索引。
        /// </summary>
        public object? Index { get; }
    }

    /// <summary>
    /// 可独立恢复的语言组合快照。
    /// </summary>
    public sealed class CompositionSnapshot
    {
        /// <summary>
        /// 创建组字快照。
        /// </summary>
        public CompositionSnapshot(long version, string rawText = "", string selectedText = "", string? displayText = null, bool canUndo = false)
        { Version = version; RawText = rawText; SelectedText = selectedText; DisplayText = displayText ?? selectedText + rawText; CanUndo = canUndo; }
        /// <summary>
        /// 组字版本。
        /// </summary>
        public long Version { get; }
        /// <summary>
        /// 尚未转换的原始拼写。
        /// </summary>
        public string RawText { get; }
        /// <summary>
        /// 已选的组字片段。
        /// </summary>
        public string SelectedText { get; }
        /// <summary>
        /// 组字区显示文本。
        /// </summary>
        public string DisplayText { get; }
        /// <summary>
        /// 是否存在可回退步骤。
        /// </summary>
        public bool CanUndo { get; }
        /// <summary>
        /// 组字是否为空。
        /// </summary>
        public bool IsEmpty => RawText.Length == 0 && SelectedText.Length == 0;
    }

    /// <summary>
    /// 一次候选选择所涉及的已有词条。
    /// </summary>
    public sealed class WordUsage
    {
        /// <summary>
        /// 创建词条使用事实。
        /// </summary>
        public WordUsage(string wordId, long count = 1) { WordId = wordId; Count = count; }
        /// <summary>
        /// 稳定词条标识。
        /// </summary>
        public string WordId { get; }
        /// <summary>
        /// 本次出现次数。
        /// </summary>
        public long Count { get; }
    }

    /// <summary>
    /// 当前查询中的一个可选择转换结果。
    /// </summary>
    public sealed class Candidate
    {
        /// <summary>
        /// 创建从剩余拼写起点消耗指定范围的候选。
        /// </summary>
        public Candidate(string id, string text, int consumedLength, IEnumerable<WordUsage>? words = null, double score = 0)
        {
            if (consumedLength <= 0) throw new ArgumentOutOfRangeException(nameof(consumedLength));
            Id = id; Text = text; ConsumedLength = consumedLength;
            Words = Array.AsReadOnly((words ?? Enumerable.Empty<WordUsage>()).ToArray()); Score = score;
        }
        /// <summary>
        /// 查询内候选标识。
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// 显示及输出文本。
        /// </summary>
        public string Text { get; }
        /// <summary>
        /// 消耗剩余拼写的 UTF-16 长度。
        /// </summary>
        public int ConsumedLength { get; }
        /// <summary>
        /// 组成结果的已有词条。
        /// </summary>
        public IReadOnlyList<WordUsage> Words { get; }
        /// <summary>
        /// 当前排序分值。
        /// </summary>
        public double Score { get; }
    }

    /// <summary>
    /// 与引擎可变状态隔离的完整候选请求。
    /// </summary>
    public sealed class CandidateQuery
    {
        /// <summary>
        /// 固定组字、词典、设置和词频视图。
        /// </summary>
        public CandidateQuery(long id, long generation, CompositionSnapshot composition, LanguageDataSnapshot data, long settingsVersion = 0, IReadOnlyDictionary<string, long>? frequencies = null, int offset = 0, int pageSize = 20)
        {
            if (offset < 0 || pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(offset));
            Id = id; Generation = generation; Composition = composition; Data = data; SettingsVersion = settingsVersion;
            Frequencies = new ReadOnlyDictionary<string, long>(frequencies == null ? new Dictionary<string, long>() : frequencies.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            Offset = offset; PageSize = pageSize;
        }
        /// <summary>
        /// 会话内递增查询标识。
        /// </summary>
        public long Id { get; }
        /// <summary>
        /// 引擎实例代次。
        /// </summary>
        public long Generation { get; }
        /// <summary>
        /// 查询时的完整组字。
        /// </summary>
        public CompositionSnapshot Composition { get; }
        /// <summary>
        /// 查询期间保持有效的词典视图。
        /// </summary>
        public LanguageDataSnapshot Data { get; }
        /// <summary>
        /// 会话设置版本。
        /// </summary>
        public long SettingsVersion { get; }
        /// <summary>
        /// 查询时的用户词频副本。
        /// </summary>
        public IReadOnlyDictionary<string, long> Frequencies { get; }
        /// <summary>
        /// 候选页起点。
        /// </summary>
        public int Offset { get; }
        /// <summary>
        /// 候选页容量。
        /// </summary>
        public int PageSize { get; }
    }

    /// <summary>
    /// 带原查询身份的不可变候选页。
    /// </summary>
    public sealed class CandidatePage
    {
        /// <summary>
        /// 创建候选页。
        /// </summary>
        public CandidatePage(CandidateQuery query, IEnumerable<Candidate> candidates, bool hasMore = false)
        { Query = query; Candidates = Array.AsReadOnly(candidates.ToArray()); HasMore = hasMore; }
        /// <summary>
        /// 原查询。
        /// </summary>
        public CandidateQuery Query { get; }
        /// <summary>
        /// 有序候选。
        /// </summary>
        public IReadOnlyList<Candidate> Candidates { get; }
        /// <summary>
        /// 是否可以继续请求下一页。
        /// </summary>
        public bool HasMore { get; }
    }

    /// <summary>
    /// 一次同步语言处理命令。
    /// </summary>
    public sealed class CompositionCommand
    {
        private CompositionCommand(string kind, string text = "", Candidate? candidate = null, bool enabled = false, string selectedText = "") { Kind = kind; Text = text; Candidate = candidate; Enabled = enabled; SelectedText = selectedText; }
        /// <summary>
        /// 命令种类。
        /// </summary>
        public string Kind { get; }
        /// <summary>
        /// 输入或恢复的文本。
        /// </summary>
        public string Text { get; }
        /// <summary>
        /// 经过会话版本验证的候选。
        /// </summary>
        public Candidate? Candidate { get; }
        /// <summary>
        /// 功能开关值。
        /// </summary>
        public bool Enabled { get; }
        /// <summary>
        /// 恢复时保留的已选片段。
        /// </summary>
        public string SelectedText { get; }
        /// <summary>
        /// 输入原始文本。
        /// </summary>
        public static CompositionCommand Insert(string text) => new CompositionCommand("input", text);
        /// <summary>
        /// 回退一个组字步骤。
        /// </summary>
        public static CompositionCommand Backspace() => new CompositionCommand("backspace");
        /// <summary>
        /// 确认有效候选。
        /// </summary>
        public static CompositionCommand Choose(Candidate candidate) => new CompositionCommand("choose", candidate: candidate);
        /// <summary>
        /// 提交当前显示内容。
        /// </summary>
        public static CompositionCommand Commit() => new CompositionCommand("commit");
        /// <summary>
        /// 清空已经接受的组字。
        /// </summary>
        public static CompositionCommand Clear() => new CompositionCommand("clear");
        /// <summary>
        /// 切换英语补全状态。
        /// </summary>
        public static CompositionCommand SetCompletion(bool enabled) => new CompositionCommand("completion", enabled: enabled);
        /// <summary>
        /// 从当前保留内容重新开始转换。
        /// </summary>
        public static CompositionCommand RestoreText(string text) => new CompositionCommand("restore", text);
        /// <summary>
        /// 根据当前原始拼写和已选片段重建转换。
        /// </summary>
        public static CompositionCommand RestoreComposition(CompositionSnapshot snapshot) => new CompositionCommand("restore", snapshot.RawText, selectedText: snapshot.SelectedText);
    }

    /// <summary>
    /// 同步语言处理提出的状态及副作用需求。
    /// </summary>
    public sealed class EngineStep
    {
        /// <summary>
        /// 创建处理结果，提交文本为 null 时表示继续组字。
        /// </summary>
        public EngineStep(CompositionSnapshot snapshot, string? commitText = null, bool queryRequested = false, IEnumerable<WordUsage>? choices = null, string? deferredInput = null)
        { Snapshot = snapshot; CommitText = commitText; QueryRequested = queryRequested; Choices = Array.AsReadOnly((choices ?? Enumerable.Empty<WordUsage>()).ToArray()); DeferredInput = deferredInput; }
        /// <summary>
        /// 最新组字快照。
        /// </summary>
        public CompositionSnapshot Snapshot { get; }
        /// <summary>
        /// 等待文本目标接受的文本。
        /// </summary>
        public string? CommitText { get; }
        /// <summary>
        /// 是否需要候选查询。
        /// </summary>
        public bool QueryRequested { get; }
        /// <summary>
        /// 本次成功选择的词条。
        /// </summary>
        public IReadOnlyList<WordUsage> Choices { get; }
        /// <summary>
        /// 先提交当前组字后，在同一操作中继续处理的输入。
        /// </summary>
        public string? DeferredInput { get; }
    }

    /// <summary>
    /// 引擎可识别的不可变恢复状态。
    /// </summary>
    public interface ICompositionCheckpoint
    {
        /// <summary>
        /// 引擎标识。
        /// </summary>
        string EngineId { get; }
        /// <summary>
        /// 状态格式版本。
        /// </summary>
        int FormatVersion { get; }
    }

    /// <summary>
    /// 处理一个会话中的语言组合。
    /// </summary>
    public interface ILanguageEngine : IDisposable
    {
        /// <summary>
        /// 当前不可变组字。
        /// </summary>
        CompositionSnapshot Snapshot { get; }
        /// <summary>
        /// 同步处理命令。
        /// </summary>
        EngineStep Process(CompositionCommand command);
        /// <summary>
        /// 仅使用请求数据及只读资源查询候选。
        /// </summary>
        Task<CandidatePage> QueryCandidatesAsync(CandidateQuery query, CancellationToken cancellationToken);
        /// <summary>
        /// 捕获组字与回退历史。
        /// </summary>
        ICompositionCheckpoint CaptureCheckpoint();
        /// <summary>
        /// 恢复兼容检查点。
        /// </summary>
        ServiceResult RestoreCheckpoint(ICompositionCheckpoint checkpoint);
    }

    /// <summary>
    /// 管理语言共享资源并创建独立引擎。
    /// </summary>
    public interface ILanguageModule : IDisposable
    {
        /// <summary>
        /// 语言标识。
        /// </summary>
        string LanguageId { get; }
        /// <summary>
        /// 实现标识。
        /// </summary>
        string EngineId { get; }
        /// <summary>
        /// 用于语言菜单的名称。
        /// </summary>
        string DisplayName { get; }
        /// <summary>
        /// 候选、自动转换和可调整设置的能力。
        /// </summary>
        LanguageCapabilities Capabilities { get; }
        /// <summary>
        /// 共享数据变更通知。
        /// </summary>
        event Action DataChanged;
        /// <summary>
        /// 同步准备共享资源。
        /// </summary>
        ServiceResult Initialize(IResourceProvider resources);
        /// <summary>
        /// 异步准备共享资源。
        /// </summary>
        Task<ServiceResult> InitializeAsync(IResourceProvider resources, CancellationToken cancellationToken);
        /// <summary>
        /// 创建会话独占的语言实例。
        /// </summary>
        ILanguageEngine CreateEngine();
        /// <summary>
        /// 取得不可变的共享数据视图。
        /// </summary>
        LanguageDataSnapshot CaptureData();
        /// <summary>
        /// 添加或更新宿主词条。
        /// </summary>
        ServiceResult UpsertWord(LexiconWord word);
        /// <summary>
        /// 删除宿主词条。
        /// </summary>
        ServiceResult RemoveWord(string wordId);
    }
}
