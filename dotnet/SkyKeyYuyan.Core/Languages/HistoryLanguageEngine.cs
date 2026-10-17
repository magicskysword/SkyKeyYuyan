using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 以不可变历史节点维护组字，供具体语言实现组合和候选算法。
    /// </summary>
    public abstract class HistoryLanguageEngine : ILanguageEngine
    {
        private sealed class TextChain
        {
            internal readonly TextChain? Previous;
            internal readonly string Part;
            internal readonly int Length;
            internal TextChain(string part, TextChain? previous = null) { Part = part; Previous = previous; Length = part.Length + (previous?.Length ?? 0); }
            internal TextChain Append(string part) => new TextChain(part, this);
            public override string ToString()
            {
                var parts = new Stack<string>();
                for (TextChain? node = this; node != null; node = node.Previous) parts.Push(node.Part);
                var text = new StringBuilder(Length);
                foreach (string part in parts) text.Append(part);
                return text.ToString();
            }
        }
        private sealed class Node
        {
            internal readonly TextChain Raw, Selected;
            internal readonly Node? Previous;
            internal Node(TextChain raw, TextChain selected, Node? previous) { Raw = raw; Selected = selected; Previous = previous; }
        }
        private sealed class Checkpoint : ICompositionCheckpoint
        {
            public string EngineId { get; }
            public int FormatVersion => 1;
            internal readonly Node State;
            internal readonly bool Completion;
            internal Checkpoint(string engineId, Node state, bool completion) { EngineId = engineId; State = state; Completion = completion; }
        }

        private Node _state = new Node(new TextChain(""), new TextChain(""), null);
        private long _version;
        private bool _disposed;
        private CompositionSnapshot? _cachedSnapshot;

        /// <summary>
        /// 创建指定实现的历史状态机。
        /// </summary>
        protected HistoryLanguageEngine(string engineId) { EngineId = engineId; }
        /// <summary>
        /// 具体引擎身份。
        /// </summary>
        protected string EngineId { get; }
        /// <summary>
        /// 当前补全开关。
        /// </summary>
        protected bool CompletionEnabled { get; private set; } = true;
        /// <summary>
        /// 是否对当前语言生成候选。
        /// </summary>
        protected virtual bool UsesCandidates => false;
        /// <summary>
        /// 是否每次输入都提出正文提交。
        /// </summary>
        protected virtual bool DirectInput => false;
        /// <inheritdoc>
        /// </inheritdoc>
        public CompositionSnapshot Snapshot
        {
            get
            {
                if (_cachedSnapshot != null && _cachedSnapshot.Version == _version) return _cachedSnapshot;
                string raw = _state.Raw.ToString(), selected = _state.Selected.ToString();
                return _cachedSnapshot = new CompositionSnapshot(_version, raw, selected, selected + Render(raw), _state.Previous != null || raw.Length > 0 || selected.Length > 0);
            }
        }

        /// <summary>
        /// 将原始拼写映射为展示内容。
        /// </summary>
        protected virtual string Render(string raw) => raw;
        /// <summary>
        /// 生成正文提交内容，允许临时显示使用额外的排版标记。
        /// </summary>
        protected virtual string GetCommitText() => Snapshot.DisplayText;
        /// <summary>
        /// 判断字符是否结束当前词内拼写。
        /// </summary>
        protected virtual bool IsTerminator(char value) => char.IsWhiteSpace(value) || char.IsPunctuation(value);

        /// <inheritdoc>
        /// </inheritdoc>
        public virtual EngineStep Process(CompositionCommand command)
        {
            if (_disposed) throw new ObjectDisposedException(EngineId);
            switch (command.Kind)
            {
                case "input":
                    for (int offset = 0; offset < command.Text.Length;)
                    {
                        int next = TextBoundary.Next(command.Text, offset);
                        Push(_state.Raw.Append(command.Text.Substring(offset, next - offset)), _state.Selected);
                        offset = next;
                    }
                    return new EngineStep(Snapshot, DirectInput || (!UsesCandidates && command.Text.Any(IsTerminator)) ? GetCommitText() : null, UsesCandidates);
                case "backspace":
                    if (_state.Previous != null) { _state = _state.Previous; _version++; }
                    else if (_state.Raw.Length > 0) { string raw = _state.Raw.ToString(); _state = new Node(new TextChain(raw.Substring(0, TextBoundary.Previous(raw, raw.Length))), _state.Selected, null); _version++; }
                    else if (_state.Selected.Length > 0) { string selected = _state.Selected.ToString(); _state = new Node(_state.Raw, new TextChain(selected.Substring(0, TextBoundary.Previous(selected, selected.Length))), null); _version++; }
                    return new EngineStep(Snapshot, queryRequested: UsesCandidates && !Snapshot.IsEmpty);
                case "choose":
                    var candidate = command.Candidate ?? throw new ArgumentException("候选不能为空");
                    if (candidate.ConsumedLength > _state.Raw.Length) throw new ArgumentException("候选消耗范围超出拼写");
                    Push(new TextChain(_state.Raw.ToString().Substring(candidate.ConsumedLength)), _state.Selected.Append(candidate.Text));
                    return new EngineStep(Snapshot, _state.Raw.Length == 0 ? GetCommitText() : null, _state.Raw.Length > 0 && UsesCandidates, candidate.Words);
                case "commit": return new EngineStep(Snapshot, Snapshot.IsEmpty ? null : GetCommitText());
                case "clear": _state = new Node(new TextChain(""), new TextChain(""), null); _version++; return new EngineStep(Snapshot);
                case "completion": CompletionEnabled = command.Enabled; _version++; return new EngineStep(Snapshot, queryRequested: UsesCandidates && !Snapshot.IsEmpty);
                case "restore": _state = new Node(new TextChain(command.Text), new TextChain(command.SelectedText), null); _version++; return new EngineStep(Snapshot, queryRequested: UsesCandidates);
                default: throw new ArgumentException("无法识别语言命令", nameof(command));
            }
        }

        private void Push(TextChain raw, TextChain selected) { _state = new Node(raw, selected, _state); _version++; }

        /// <inheritdoc>
        /// </inheritdoc>
        public virtual Task<CandidatePage> QueryCandidatesAsync(CandidateQuery query, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new CandidatePage(query, Array.Empty<Candidate>())); }
        /// <inheritdoc>
        /// </inheritdoc>
        public ICompositionCheckpoint CaptureCheckpoint() => new Checkpoint(EngineId, _state, CompletionEnabled);
        /// <inheritdoc>
        /// </inheritdoc>
        public ServiceResult RestoreCheckpoint(ICompositionCheckpoint checkpoint)
        {
            if (!(checkpoint is Checkpoint state) || state.EngineId != EngineId || checkpoint.FormatVersion != 1) return new ServiceResult(false, "restore", "检查点与引擎不兼容");
            _state = state.State; CompletionEnabled = state.Completion; _version++;
            return new ServiceResult(true, "restore");
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public virtual void Dispose() { _disposed = true; }
    }
}
