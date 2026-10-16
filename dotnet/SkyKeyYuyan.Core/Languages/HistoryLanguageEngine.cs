using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 以不可变历史节点维护组字，供具体语言实现组合和候选算法。
    /// </summary>
    public abstract class HistoryLanguageEngine : ILanguageEngine
    {
        private sealed class Node
        {
            internal readonly string Raw, Selected;
            internal readonly Node? Previous;
            internal Node(string raw, string selected, Node? previous) { Raw = raw; Selected = selected; Previous = previous; }
        }
        private sealed class Checkpoint : ICompositionCheckpoint
        {
            public string EngineId { get; }
            public int FormatVersion => 1;
            internal readonly Node State;
            internal readonly bool Completion;
            internal Checkpoint(string engineId, Node state, bool completion) { EngineId = engineId; State = state; Completion = completion; }
        }

        private Node _state = new Node("", "", null);
        private long _version;
        private bool _disposed;

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
        /// <inheritdoc />
        public CompositionSnapshot Snapshot => new CompositionSnapshot(_version, _state.Raw, _state.Selected, _state.Selected + Render(_state.Raw), _state.Previous != null);

        /// <summary>
        /// 将原始拼写映射为展示内容。
        /// </summary>
        protected virtual string Render(string raw) => raw;
        /// <summary>
        /// 判断字符是否结束当前词内拼写。
        /// </summary>
        protected virtual bool IsTerminator(char value) => char.IsWhiteSpace(value) || char.IsPunctuation(value);

        /// <inheritdoc />
        public virtual EngineStep Process(CompositionCommand command)
        {
            if (_disposed) throw new ObjectDisposedException(EngineId);
            switch (command.Kind)
            {
                case "input":
                    Push(_state.Raw + command.Text, _state.Selected);
                    return new EngineStep(Snapshot, DirectInput || (!UsesCandidates && command.Text.Any(IsTerminator)) ? Snapshot.DisplayText : null, UsesCandidates);
                case "backspace":
                    if (_state.Previous != null) { _state = _state.Previous; _version++; }
                    return new EngineStep(Snapshot, queryRequested: UsesCandidates && !Snapshot.IsEmpty);
                case "choose":
                    var candidate = command.Candidate ?? throw new ArgumentException("候选不能为空");
                    if (candidate.ConsumedLength > _state.Raw.Length) throw new ArgumentException("候选消耗范围超出拼写");
                    Push(_state.Raw.Substring(candidate.ConsumedLength), _state.Selected + candidate.Text);
                    return new EngineStep(Snapshot, _state.Raw.Length == 0 ? Snapshot.DisplayText : null, _state.Raw.Length > 0 && UsesCandidates, candidate.Words);
                case "commit": return new EngineStep(Snapshot, Snapshot.IsEmpty ? null : Snapshot.DisplayText);
                case "clear": _state = new Node("", "", null); _version++; return new EngineStep(Snapshot);
                case "completion": CompletionEnabled = command.Enabled; _version++; return new EngineStep(Snapshot, queryRequested: UsesCandidates && !Snapshot.IsEmpty);
                case "restore": _state = new Node(command.Text, "", null); _version++; return new EngineStep(Snapshot, queryRequested: UsesCandidates);
                default: throw new ArgumentException("无法识别语言命令", nameof(command));
            }
        }

        private void Push(string raw, string selected) { _state = new Node(raw, selected, _state); _version++; }

        /// <inheritdoc />
        public virtual Task<CandidatePage> QueryCandidatesAsync(CandidateQuery query, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new CandidatePage(query, Array.Empty<Candidate>())); }
        /// <inheritdoc />
        public ICompositionCheckpoint CaptureCheckpoint() => new Checkpoint(EngineId, _state, CompletionEnabled);
        /// <inheritdoc />
        public ServiceResult RestoreCheckpoint(ICompositionCheckpoint checkpoint)
        {
            if (!(checkpoint is Checkpoint state) || state.EngineId != EngineId || checkpoint.FormatVersion != 1) return new ServiceResult(false, "restore", "检查点与引擎不兼容");
            _state = state.State; CompletionEnabled = state.Completion; _version++;
            return new ServiceResult(true, "restore");
        }
        /// <inheritdoc />
        public virtual void Dispose() { _disposed = true; }
    }
}
