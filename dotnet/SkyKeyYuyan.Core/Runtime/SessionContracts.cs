using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 正文编辑的工作模式。
    /// </summary>
    public enum EditingMode
    {
        /// <summary>
        /// 每次提交直接写入宿主目标。
        /// </summary>
        Direct,
        /// <summary>
        /// 在独立草稿中编辑，完成时回填全文。
        /// </summary>
        Draft
    }
    /// <summary>
    /// 会话生命周期。
    /// </summary>
    public enum SessionLifecycle
    {
        /// <summary>
        /// 正常接收输入。
        /// </summary>
        Active,
        /// <summary>
        /// 保留状态并暂停内容编辑。
        /// </summary>
        Paused,
        /// <summary>
        /// 已经结束。
        /// </summary>
        Ended
    }
    /// <summary>
    /// 一次操作的处理状态。
    /// </summary>
    public enum OperationStatus
    {
        /// <summary>
        /// 操作已成功完成。
        /// </summary>
        Applied,
        /// <summary>
        /// 操作等待执行上下文或候选结果。
        /// </summary>
        Pending,
        /// <summary>
        /// 当前状态不能接受操作。
        /// </summary>
        Rejected,
        /// <summary>
        /// 操作未完成，需要明确处理未知写入结果。
        /// </summary>
        NeedsResolution,
        /// <summary>
        /// 操作已经取消。
        /// </summary>
        Cancelled
    }
    /// <summary>
    /// 会话结束的业务原因。
    /// </summary>
    public enum SessionEndReason
    {
        /// <summary>
        /// 用户完成编辑。
        /// </summary>
        Finished,
        /// <summary>
        /// 用户取消编辑。
        /// </summary>
        Cancelled,
        /// <summary>
        /// 宿主修改了正文。
        /// </summary>
        ExternalChange,
        /// <summary>
        /// 宿主文本目标失效。
        /// </summary>
        TargetInvalidated,
        /// <summary>
        /// 宿主释放了运行时。
        /// </summary>
        RuntimeDisposed
    }
    /// <summary>
    /// 一次打开输入时固定的会话配置。
    /// </summary>
    public sealed class SessionOptions
    {
        /// <summary>
        /// 输入路由所属玩家。
        /// </summary>
        public string PlayerId { get; set; } = "player";
        /// <summary>
        /// 词频用户；为空时使用运行时默认用户。
        /// </summary>
        public string? UserId { get; set; }
        /// <summary>
        /// 可用语言列表；为空时使用运行时配置列表。
        /// </summary>
        public IReadOnlyList<string>? Languages { get; set; }
        /// <summary>
        /// 初始语言；为空时优先沿用玩家最近的语言。
        /// </summary>
        public string? InitialLanguage { get; set; }
        /// <summary>
        /// 本次正文编辑模式。
        /// </summary>
        public EditingMode EditingMode { get; set; } = EditingMode.Draft;
        /// <summary>
        /// 根据原文快照创建独立草稿目标；为空时使用内存草稿。目标及其原生资源由宿主持有。
        /// </summary>
        public Func<TextSnapshot, ITextTarget>? DraftTargetFactory { get; set; }
        /// <summary>
        /// 英语补全的初始开关。
        /// </summary>
        public bool CompletionEnabled { get; set; } = true;
    }
    /// <summary>
    /// 与设备无关的不可变输入操作。
    /// </summary>
    public sealed class InputOperation
    {
        private InputOperation(string kind, string text = "", int amount = 0, long queryId = 0, TextNavigation navigation = TextNavigation.Left, bool enabled = false)
        { Kind = kind; Text = text; Amount = amount; QueryId = queryId; Navigation = navigation; Enabled = enabled; }
        /// <summary>
        /// 操作种类。
        /// </summary>
        public string Kind { get; }
        /// <summary>
        /// 文本、候选或语言参数。
        /// </summary>
        public string Text { get; }
        /// <summary>
        /// 候选导航偏移。
        /// </summary>
        public int Amount { get; }
        /// <summary>
        /// 候选所属查询。
        /// </summary>
        public long QueryId { get; }
        /// <summary>
        /// 正文导航方向。
        /// </summary>
        public TextNavigation Navigation { get; }
        /// <summary>
        /// 开关或扩展选区参数。
        /// </summary>
        public bool Enabled { get; }
        /// <summary>
        /// 输入文本片段。
        /// </summary>
        public static InputOperation Insert(string text) => new InputOperation("input", text ?? throw new ArgumentNullException(nameof(text)));
        /// <summary>
        /// 回退组字，或在正文中退格。
        /// </summary>
        public static InputOperation Backspace() => new InputOperation("backspace");
        /// <summary>
        /// 删除光标后的字符或当前选区。
        /// </summary>
        public static InputOperation DeleteForward() => new InputOperation("delete");
        /// <summary>
        /// 清空正文。
        /// </summary>
        public static InputOperation Clear() => new InputOperation("clear");
        /// <summary>
        /// 选择全部正文。
        /// </summary>
        public static InputOperation SelectAll() => Navigate(TextNavigation.SelectAll);
        /// <summary>
        /// 输入换行。
        /// </summary>
        public static InputOperation Newline() => new InputOperation("newline");
        /// <summary>
        /// 提交当前组字，保留提交后的光标位置并继续编辑。
        /// </summary>
        public static InputOperation CommitComposition() => new InputOperation("commit");
        /// <summary>
        /// 移动正文光标或扩展选区。
        /// </summary>
        public static InputOperation Navigate(TextNavigation direction, bool extend = false) => new InputOperation("navigate", navigation: direction, enabled: extend);
        /// <summary>
        /// 在候选中相对导航。
        /// </summary>
        public static InputOperation MoveCandidate(int amount) => new InputOperation("candidate", amount: amount);
        /// <summary>
        /// 确认已经展示的有效候选。
        /// </summary>
        public static InputOperation ConfirmCandidate(long queryId, string candidateId) => new InputOperation("choose", candidateId, queryId: queryId);
        /// <summary>
        /// 切换指定语言，为空时循环切换。
        /// </summary>
        public static InputOperation SwitchLanguage(string languageId = "") => new InputOperation("language", languageId);
        /// <summary>
        /// 切换会话内英语补全状态。
        /// </summary>
        public static InputOperation SetCompletion(bool enabled) => new InputOperation("completion", enabled: enabled);
        /// <summary>
        /// 完成编辑。
        /// </summary>
        public static InputOperation Finish() => new InputOperation("finish");
        /// <summary>
        /// 按编辑模式取消会话。
        /// </summary>
        public static InputOperation Cancel() => new InputOperation("cancel");
        /// <summary>
        /// 保留输入状态并暂停。
        /// </summary>
        public static InputOperation Pause() => new InputOperation("pause");
        /// <summary>
        /// 恢复暂停中的会话。
        /// </summary>
        public static InputOperation Resume() => new InputOperation("resume");
        /// <summary>
        /// 根据当前保留内容重试语言转换。
        /// </summary>
        public static InputOperation RetryConversion() => new InputOperation("retry");
        /// <summary>
        /// 明确处理未知写入；true 表示再次提交，false 表示接受目标现状。
        /// </summary>
        public static InputOperation ResolveWrite(bool retry) => new InputOperation("resolve", enabled: retry);
    }
    /// <summary>
    /// 一次操作的最终执行结果。
    /// </summary>
    public sealed class OperationResult
    {
        /// <summary>
        /// 创建操作结果。
        /// </summary>
        public OperationResult(OperationId operation, OperationStatus status, TextEditResult? edit = null, string? reason = null)
        { Operation = operation; Status = status; Edit = edit; Reason = reason; }
        /// <summary>
        /// 操作标识。
        /// </summary>
        public OperationId Operation { get; }
        /// <summary>
        /// 最终状态。
        /// </summary>
        public OperationStatus Status { get; }
        /// <summary>
        /// 关联的文本编辑结果。
        /// </summary>
        public TextEditResult? Edit { get; }
        /// <summary>
        /// 失败或拒绝原因。
        /// </summary>
        public string? Reason { get; }
    }
    /// <summary>
    /// 操作接纳时立即返回的回执。
    /// </summary>
    public sealed class OperationReceipt
    {
        /// <summary>
        /// 创建接纳回执。
        /// </summary>
        public OperationReceipt(OperationId operation, OperationResult? result = null) { Operation = operation; Result = result; }
        /// <summary>
        /// 操作标识。
        /// </summary>
        public OperationId Operation { get; }
        /// <summary>
        /// 当时已经得到的最终结果，为空时等待完成事件。
        /// </summary>
        public OperationResult? Result { get; }
        /// <summary>
        /// 接纳时的处理状态。
        /// </summary>
        public OperationStatus Status => Result?.Status ?? OperationStatus.Pending;
    }
    /// <summary>
    /// 表现层可直接读取的不可变会话状态。
    /// </summary>
    public sealed class SessionSnapshot
    {
        internal SessionSnapshot(Guid sessionId, string languageId, string engineId, SessionLifecycle lifecycle, CompositionSnapshot composition, TextSnapshot text, CandidatePage? candidates, int selectedCandidate, bool querying, bool literal, bool completion, OperationId? pending, string? diagnostic, bool needsResolution)
        { SessionId = sessionId; LanguageId = languageId; EngineId = engineId; Lifecycle = lifecycle; Composition = composition; Text = text; Candidates = candidates; SelectedCandidate = selectedCandidate; Querying = querying; Literal = literal; CompletionEnabled = completion; PendingOperation = pending; Diagnostic = diagnostic; NeedsResolution = needsResolution; }
        /// <summary>
        /// 会话标识。
        /// </summary>
        public Guid SessionId { get; }
        /// <summary>
        /// 活动语言。
        /// </summary>
        public string LanguageId { get; }
        /// <summary>
        /// 活动实现。
        /// </summary>
        public string EngineId { get; }
        /// <summary>
        /// 生命周期。
        /// </summary>
        public SessionLifecycle Lifecycle { get; }
        /// <summary>
        /// 当前组字。
        /// </summary>
        public CompositionSnapshot Composition { get; }
        /// <summary>
        /// 原框或草稿的正文和选区。
        /// </summary>
        public TextSnapshot Text { get; }
        /// <summary>
        /// 当前有效候选页。
        /// </summary>
        public CandidatePage? Candidates { get; }
        /// <summary>
        /// 当前候选索引。
        /// </summary>
        public int SelectedCandidate { get; }
        /// <summary>
        /// 是否正在查询。
        /// </summary>
        public bool Querying { get; }
        /// <summary>
        /// 是否已进入字面输入。
        /// </summary>
        public bool Literal { get; }
        /// <summary>
        /// 当前英语补全开关。
        /// </summary>
        public bool CompletionEnabled { get; }
        /// <summary>
        /// 等待转换完成的操作。
        /// </summary>
        public OperationId? PendingOperation { get; }
        /// <summary>
        /// 当前恢复或错误说明。
        /// </summary>
        public string? Diagnostic { get; }
        /// <summary>
        /// 是否需要用户处理未知写入。
        /// </summary>
        public bool NeedsResolution { get; }
    }
    /// <summary>
    /// 会话结束时交付给宿主的结果。
    /// </summary>
    public sealed class SessionResult
    {
        internal SessionResult(SessionEndReason reason, TextSnapshot text, TextEditResult? fill, IReadOnlyList<string> diagnostics, IReadOnlyList<FrequencyStoreResult> storageResults)
        { Reason = reason; Text = text; FillResult = fill; Diagnostics = Array.AsReadOnly(diagnostics.ToArray()); StorageResults = Array.AsReadOnly(storageResults.ToArray()); }
        /// <summary>
        /// 会话结束原因。
        /// </summary>
        public SessionEndReason Reason { get; }
        /// <summary>
        /// 宿主目标最终正文。
        /// </summary>
        public TextSnapshot Text { get; }
        /// <summary>
        /// 草稿回填结果。
        /// </summary>
        public TextEditResult? FillResult { get; }
        /// <summary>
        /// 会话期间的诊断。
        /// </summary>
        public IReadOnlyList<string> Diagnostics { get; }
        /// <summary>
        /// 会话结束时的词频保存结果及后台等待状态。
        /// </summary>
        public IReadOnlyList<FrequencyStoreResult> StorageResults { get; }
    }
    /// <summary>
    /// 接收统一操作并发布完整状态。
    /// </summary>
    public interface ISkySession : IDisposable
    {
        /// <summary>
        /// 本会话固定的语言菜单。
        /// </summary>
        IReadOnlyList<LanguageInfo> Languages { get; }
        /// <summary>
        /// 取得当前语言的默认键盘布局。
        /// </summary>
        KeyboardLayout GetKeyboard();
        /// <summary>
        /// 当前完整快照。
        /// </summary>
        SessionSnapshot Snapshot { get; }
        /// <summary>
        /// 快照替换后的通知。
        /// </summary>
        event Action<SessionSnapshot> StateChanged;
        /// <summary>
        /// 每个已接纳操作的最终结果。
        /// </summary>
        event Action<OperationResult> OperationCompleted;
        /// <summary>
        /// 每个会话发布一次的结束结果。
        /// </summary>
        event Action<SessionResult> Completed;
        /// <summary>
        /// 接纳一次统一操作。
        /// </summary>
        OperationReceipt Process(InputOperation operation);
    }
}
