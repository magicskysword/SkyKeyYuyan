using System;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 标识一个会话接纳的操作。
    /// </summary>
    public readonly struct OperationId : IEquatable<OperationId>
    {
        /// <summary>
        /// 创建会话内的操作标识。
        /// </summary>
        public OperationId(Guid sessionId, long sequence) { SessionId = sessionId; Sequence = sequence; }

        /// <summary>
        /// 所属会话。
        /// </summary>
        public Guid SessionId { get; }

        /// <summary>
        /// 会话内递增序号。
        /// </summary>
        public long Sequence { get; }

        /// <inheritdoc />
        public bool Equals(OperationId other) => SessionId == other.SessionId && Sequence == other.Sequence;
        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is OperationId other && Equals(other);
        /// <inheritdoc />
        public override int GetHashCode() => SessionId.GetHashCode() ^ Sequence.GetHashCode();
        /// <inheritdoc />
        public override string ToString() => SessionId.ToString("N") + ":" + Sequence;
    }

    /// <summary>
    /// 文本和有方向的选区的不可变快照，位置采用 UTF-16 偏移。
    /// </summary>
    public sealed class TextSnapshot
    {
        /// <summary>
        /// 创建正文快照，并将选区调整到完整字符边界。
        /// </summary>
        public TextSnapshot(string text, int anchor, int caret, long version)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Anchor = TextBoundary.Floor(text, anchor);
            Caret = TextBoundary.Floor(text, caret);
            Version = version;
        }

        /// <summary>
        /// 已接受的完整正文。
        /// </summary>
        public string Text { get; }
        /// <summary>
        /// 选区锚点。
        /// </summary>
        public int Anchor { get; }
        /// <summary>
        /// 选区活动端。
        /// </summary>
        public int Caret { get; }
        /// <summary>
        /// 目标修订号。
        /// </summary>
        public long Version { get; }
        /// <summary>
        /// 选区起点。
        /// </summary>
        public int SelectionStart => Math.Min(Anchor, Caret);
        /// <summary>
        /// 选区长度。
        /// </summary>
        public int SelectionLength => Math.Abs(Anchor - Caret);
    }

    /// <summary>
    /// 文本目标的确定执行结果。
    /// </summary>
    public enum TextEditStatus
    {
        /// <summary>
        /// 请求已完整接受。
        /// </summary>
        Accepted,
        /// <summary>
        /// 请求已按长度限制截断并执行。
        /// </summary>
        Truncated,
        /// <summary>
        /// 请求未通过验证。
        /// </summary>
        Rejected,
        /// <summary>
        /// 目标版本已经改变。
        /// </summary>
        VersionConflict,
        /// <summary>
        /// 目标已经失效。
        /// </summary>
        InvalidTarget,
        /// <summary>
        /// 无法确定请求是否生效。
        /// </summary>
        Unknown
    }

    /// <summary>
    /// 对指定版本正文执行的一次范围替换。
    /// </summary>
    public sealed class TextEditRequest
    {
        /// <summary>
        /// 创建替换请求；同一操作的不同编辑用 part 区分。
        /// </summary>
        public TextEditRequest(OperationId operation, long expectedVersion, int start, int length, string text, int part = 0)
        { Operation = operation; ExpectedVersion = expectedVersion; Start = start; Length = length; Text = text ?? throw new ArgumentNullException(nameof(text)); Part = part; }
        /// <summary>
        /// 操作归属。
        /// </summary>
        public OperationId Operation { get; }
        /// <summary>
        /// 操作内的编辑序号。
        /// </summary>
        public int Part { get; }
        /// <summary>
        /// 预期目标版本。
        /// </summary>
        public long ExpectedVersion { get; }
        /// <summary>
        /// 替换起点。
        /// </summary>
        public int Start { get; }
        /// <summary>
        /// 替换长度。
        /// </summary>
        public int Length { get; }
        /// <summary>
        /// 请求插入的文本。
        /// </summary>
        public string Text { get; }
    }

    /// <summary>
    /// 范围替换的实际结果。
    /// </summary>
    public sealed class TextEditResult
    {
        /// <summary>
        /// 创建编辑结果。
        /// </summary>
        public TextEditResult(TextEditStatus status, TextSnapshot snapshot, string acceptedText = "", int start = 0, int length = 0, string? reason = null)
        { Status = status; Snapshot = snapshot; AcceptedText = acceptedText; Start = start; Length = length; Reason = reason; }
        /// <summary>
        /// 执行状态。
        /// </summary>
        public TextEditStatus Status { get; }
        /// <summary>
        /// 编辑后的权威快照。
        /// </summary>
        public TextSnapshot Snapshot { get; }
        /// <summary>
        /// 实际插入的文本。
        /// </summary>
        public string AcceptedText { get; }
        /// <summary>
        /// 实际替换起点。
        /// </summary>
        public int Start { get; }
        /// <summary>
        /// 实际替换长度。
        /// </summary>
        public int Length { get; }
        /// <summary>
        /// 可定位的失败原因。
        /// </summary>
        public string? Reason { get; }
        /// <summary>
        /// 是否具有已生效的确定结果。
        /// </summary>
        public bool Applied => Status == TextEditStatus.Accepted || Status == TextEditStatus.Truncated;
    }

    /// <summary>
    /// 正文导航方向及范围动作。
    /// </summary>
    public enum TextNavigation
    {
        /// <summary>
        /// 向左移动一个字符。
        /// </summary>
        Left,
        /// <summary>
        /// 向右移动一个字符。
        /// </summary>
        Right,
        /// <summary>
        /// 移到上一排。
        /// </summary>
        Up,
        /// <summary>
        /// 移到下一排。
        /// </summary>
        Down,
        /// <summary>
        /// 移到当前排开头。
        /// </summary>
        Home,
        /// <summary>
        /// 移到当前排结尾。
        /// </summary>
        End,
        /// <summary>
        /// 移到正文开头。
        /// </summary>
        DocumentStart,
        /// <summary>
        /// 移到正文结尾。
        /// </summary>
        DocumentEnd,
        /// <summary>
        /// 选择全部正文。
        /// </summary>
        SelectAll
    }

    /// <summary>
    /// 对指定版本选区执行的导航。
    /// </summary>
    public sealed class TextNavigationRequest
    {
        /// <summary>
        /// 创建导航请求。
        /// </summary>
        public TextNavigationRequest(OperationId operation, long expectedVersion, TextNavigation direction, bool extend = false)
        { Operation = operation; ExpectedVersion = expectedVersion; Direction = direction; Extend = extend; }
        /// <summary>
        /// 操作归属。
        /// </summary>
        public OperationId Operation { get; }
        /// <summary>
        /// 预期目标版本。
        /// </summary>
        public long ExpectedVersion { get; }
        /// <summary>
        /// 导航动作。
        /// </summary>
        public TextNavigation Direction { get; }
        /// <summary>
        /// 是否仅移动选区活动端。
        /// </summary>
        public bool Extend { get; }
    }

    /// <summary>
    /// 导航的实际结果。
    /// </summary>
    public sealed class TextNavigationResult
    {
        /// <summary>
        /// 创建导航结果。
        /// </summary>
        public TextNavigationResult(TextEditStatus status, TextSnapshot snapshot) { Status = status; Snapshot = snapshot; }
        /// <summary>
        /// 执行状态。
        /// </summary>
        public TextEditStatus Status { get; }
        /// <summary>
        /// 导航后的快照。
        /// </summary>
        public TextSnapshot Snapshot { get; }
    }

    /// <summary>
    /// 文本目标向会话报告的外部变化。
    /// </summary>
    public sealed class TextTargetChange
    {
        /// <summary>
        /// 创建外部变化通知。
        /// </summary>
        public TextTargetChange(TextSnapshot snapshot, bool invalidated = false) { Snapshot = snapshot; Invalidated = invalidated; }
        /// <summary>
        /// 变化后的权威快照。
        /// </summary>
        public TextSnapshot Snapshot { get; }
        /// <summary>
        /// 目标是否已经失效。
        /// </summary>
        public bool Invalidated { get; }
    }

    /// <summary>
    /// 提供正文、选区、验证和导航，并报告宿主修改。
    /// </summary>
    public interface ITextTarget
    {
        /// <summary>
        /// 宿主控件的稳定引用身份，用于跨运行时仲裁编辑权。
        /// </summary>
        object Identity { get; }
        /// <summary>
        /// 读取当前权威快照。
        /// </summary>
        TextSnapshot ReadSnapshot();
        /// <summary>
        /// 执行范围替换；重复的操作和编辑序号返回原结果。
        /// </summary>
        TextEditResult ApplyEdit(TextEditRequest request);
        /// <summary>
        /// 按目标实际排版移动选区。
        /// </summary>
        TextNavigationResult Navigate(TextNavigationRequest request);
        /// <summary>
        /// 上报外部正文修改或目标失效。
        /// </summary>
        event Action<TextTargetChange> Changed;
    }

    /// <summary>
    /// 允许文本目标在会话结束后释放用于恢复的操作回执。
    /// </summary>
    public interface ITextOperationCache
    {
        /// <summary>
        /// 释放已结束会话的操作记录。
        /// </summary>
        void ReleaseSession(Guid sessionId);
    }
}
