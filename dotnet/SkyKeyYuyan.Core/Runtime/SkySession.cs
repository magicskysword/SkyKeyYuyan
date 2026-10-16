using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SkyKeyYuyan.Core
{
    internal sealed class SkySession : ISkySession
    {
        private sealed class Request
        {
            internal readonly OperationId Id;
            internal readonly InputOperation Operation;
            internal OperationResult? Result;
            internal int Part;
            internal string? DeferredInput;
            internal Request(OperationId id, InputOperation operation) { Id = id; Operation = operation; }
        }
        private sealed class EngineSlot
        {
            private readonly object _gate = new object();
            private readonly Action<Exception> _report;
            internal readonly ILanguageEngine Engine;
            private int _queries;
            private bool _retired, _disposed;
            internal EngineSlot(ILanguageEngine engine, Action<Exception> report) { Engine = engine; _report = report; }
            internal void Retain() { lock (_gate) _queries++; }
            internal void Release() { lock (_gate) _queries--; TryDispose(); }
            internal void Retire() { lock (_gate) _retired = true; TryDispose(); }
            private void TryDispose()
            {
                lock (_gate) { if (!_retired || _queries != 0 || _disposed) return; _disposed = true; }
                try { Engine.Dispose(); } catch (Exception ex) { _report(ex); }
            }
        }
        private sealed class LiteralEngine : HistoryLanguageEngine { internal LiteralEngine() : base("literal") { } }

        private readonly SkyRuntime _runtime;
        private readonly Guid _id;
        private readonly ITextTarget _target, _editing;
        private readonly string _playerId;
        private readonly string _userId;
        private readonly Dictionary<string, ILanguageModule> _languages;
        private readonly EditingMode _mode;
        private readonly List<string> _diagnostics = new List<string>();
        private readonly List<ChoiceRecord> _draftChoices = new List<ChoiceRecord>();
        private ILanguageModule _module;
        private EngineSlot _slot;
        private CompositionSnapshot _composition;
        private TextSnapshot _text;
        private long _nextOperation, _generation = 1, _querySequence, _settingsVersion;
        private long _choiceSequence;
        private SessionLifecycle _lifecycle = SessionLifecycle.Active;
        private CandidatePage? _page;
        private int _selectedCandidate;
        private bool _querying, _literal, _completionEnabled, _needsResolution, _unknownFill;
        private CancellationTokenSource? _queryCancellation;
        private Request? _pending;
        private string? _stagedFinishText;
        private TextEditRequest? _unknownRequest;
        private TextEditResult? _lastEdit;
        private string? _diagnostic;

        internal SkySession(SkyRuntime runtime, Guid id, ITextTarget target, SessionOptions options, Dictionary<string, ILanguageModule> languages, string initial)
        {
            _runtime = runtime; _id = id; _target = target; _playerId = options.PlayerId; _languages = languages; _mode = options.EditingMode;
            _userId = options.UserId ?? runtime.DefaultUserId;
            if (string.IsNullOrWhiteSpace(_userId)) throw new ArgumentException("用户标识不能为空");
            _text = target.ReadSnapshot();
            _editing = _mode == EditingMode.Draft ? new MemoryTextTarget(_text.Text, _text.Anchor, _text.Caret) : target;
            _text = _editing.ReadSnapshot();
            _module = languages[initial];
            _completionEnabled = options.CompletionEnabled;
            _slot = CreateSlot(_module.CreateEngine());
            try
            {
                _slot.Engine.Process(CompositionCommand.SetCompletion(_completionEnabled));
                _composition = _slot.Engine.Snapshot;
                Snapshot = BuildSnapshot();
                _target.Changed += OnTargetChanged;
                _module.DataChanged += OnDataChanged;
                _runtime.Frequency.Changed += OnFrequencyChanged;
            }
            catch { _slot.Retire(); throw; }
        }

        public SessionSnapshot Snapshot { get; private set; }
        public event Action<SessionSnapshot>? StateChanged;
        public event Action<OperationResult>? OperationCompleted;
        public event Action<SessionResult>? Completed;
        internal void PrepareUser()
        {
            foreach (var language in _languages.Keys) _ = _runtime.Frequency.LoadAsync(_userId, language);
        }

        public OperationReceipt Process(InputOperation operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            var request = new Request(new OperationId(_id, Interlocked.Increment(ref _nextOperation)), operation);
            _runtime.Context.Post(() => Accept(request));
            return new OperationReceipt(request.Id, request.Result);
        }
        private void Accept(Request request)
        {
            string kind = request.Operation.Kind;
            if (_lifecycle == SessionLifecycle.Ended) { Complete(request, OperationStatus.Rejected, "会话已经结束"); return; }
            if (kind == "cancel")
            {
                CancelPending(); End(SessionEndReason.Cancelled); Complete(request, OperationStatus.Applied); return;
            }
            if (kind == "pause") { _lifecycle = SessionLifecycle.Paused; Complete(request, OperationStatus.Applied); return; }
            if (kind == "resume")
            {
                _lifecycle = SessionLifecycle.Active; Complete(request, OperationStatus.Applied);
                ContinuePending(); return;
            }
            if (_lifecycle == SessionLifecycle.Paused || _pending != null) { Complete(request, OperationStatus.Rejected, "会话暂停或正在等待转换"); return; }
            if (_needsResolution && kind != "resolve") { Complete(request, OperationStatus.Rejected, "需要先处理未知写入结果"); return; }
            _lastEdit = null;
            try { Execute(request); }
            catch (Exception ex) { Report("operation", ex.Message, request.Id); Complete(request, OperationStatus.Rejected, ex.Message); }
        }
        private void Execute(Request request)
        {
            var operation = request.Operation;
            switch (operation.Kind)
            {
                case "input":
                    ApplyStep(request, ProcessEngine(CompositionCommand.Insert(operation.Text), request.Id));
                    if (request.DeferredInput != null && request.Result == null) { _pending = request; ContinuePending(); return; }
                    break;
                case "backspace":
                    if (!_composition.IsEmpty) ApplyStep(request, ProcessEngine(CompositionCommand.Backspace(), request.Id));
                    else Delete(request, false);
                    break;
                case "candidate":
                    if (_page != null && _page.Candidates.Count > 0)
                    {
                        int next = _selectedCandidate + operation.Amount;
                        if (next >= _page.Candidates.Count && _page.HasMore) StartQuery(offset: _page.Query.Offset + _page.Candidates.Count, selection: next - _page.Candidates.Count);
                        else if (next < 0 && _page.Query.Offset > 0) StartQuery(offset: Math.Max(0, _page.Query.Offset - _page.Query.PageSize), selection: -1);
                        else _selectedCandidate = ((next % _page.Candidates.Count) + _page.Candidates.Count) % _page.Candidates.Count;
                    }
                    break;
                case "choose":
                    var candidate = _page?.Candidates.FirstOrDefault(item => item.Id == operation.Text);
                    if (_querying || _page == null || operation.QueryId != _page.Query.Id || candidate == null) { Complete(request, OperationStatus.Rejected, "候选已过期或尚未就绪"); return; }
                    ApplyStep(request, ProcessEngine(CompositionCommand.Choose(candidate), request.Id));
                    break;
                case "retry": RetryConversion(request); break;
                case "resolve": ResolveWrite(request); return;
                default:
                    _pending = request;
                    ContinuePending();
                    return;
            }
            Complete(request, _needsResolution ? OperationStatus.NeedsResolution : _lastEdit != null && !_lastEdit.Applied ? OperationStatus.Rejected : OperationStatus.Applied);
        }
        private void ContinuePending()
        {
            var request = _pending;
            if (request == null || _lifecycle != SessionLifecycle.Active || request.Result != null) return;
            try
            {
                while (_pending == request && request.Result == null && _lifecycle == SessionLifecycle.Active)
                {
                if (!FlushComposition(request)) { Publish(); return; }
                if (request.Result != null || _lifecycle == SessionLifecycle.Ended) return;
                switch (request.Operation.Kind)
                {
                    case "input":
                        string? deferred = request.DeferredInput; request.DeferredInput = null;
                        if (deferred != null) ApplyStep(request, ProcessEngine(CompositionCommand.Insert(deferred), request.Id));
                        if (request.Result != null) return;
                        if (request.DeferredInput != null) continue;
                        break;
                    case "finish":
                        if (_mode == EditingMode.Draft)
                        {
                            var host = _target.ReadSnapshot();
                            _lastEdit = ApplyTarget(_target, new TextEditRequest(request.Id, host.Version, 0, host.Text.Length, _stagedFinishText ?? _text.Text, request.Part++));
                            if (!_lastEdit.Applied)
                            {
                                _unknownFill = _lastEdit.Status == TextEditStatus.Unknown;
                                _stagedFinishText = null;
                                if (HandleEditFailure(request, _lastEdit, true)) return;
                            }
                        }
                        End(SessionEndReason.Finished, _mode == EditingMode.Draft ? _lastEdit : null);
                        break;
                    case "language": SwitchLanguage(request); break;
                    case "completion":
                        if ((_module.Capabilities & LanguageCapabilities.CompletionSwitch) == 0) { Complete(request, OperationStatus.Rejected, "当前实现没有补全开关"); return; }
                        _completionEnabled = request.Operation.Enabled; _settingsVersion++;
                        ApplyStep(request, ProcessEngine(CompositionCommand.SetCompletion(_completionEnabled), request.Id));
                        break;
                    case "navigate":
                        var navigation = _editing.Navigate(new TextNavigationRequest(request.Id, _text.Version, request.Operation.Navigation, request.Operation.Enabled));
                        _text = navigation.Snapshot;
                        if (navigation.Status != TextEditStatus.Accepted) { Complete(request, OperationStatus.Rejected, "导航未被目标接受"); return; }
                        break;
                    case "delete": Delete(request, true); break;
                    case "clear": Edit(request, 0, _text.Text.Length, ""); break;
                    case "newline": Edit(request, _text.SelectionStart, _text.SelectionLength, "\n"); break;
                    default: Complete(request, OperationStatus.Rejected, "无法识别输入操作"); return;
                }
                if (request.Result == null) Complete(request, _needsResolution ? OperationStatus.NeedsResolution : _lastEdit != null && !_lastEdit.Applied ? OperationStatus.Rejected : OperationStatus.Applied);
                }
            }
            catch (Exception ex) { _stagedFinishText = null; Report("operation", ex.Message, request.Id); Complete(request, OperationStatus.Rejected, ex.Message); }
        }
        private bool FlushComposition(Request request)
        {
            if (_stagedFinishText != null || _composition.IsEmpty) return true;
            if (!_literal && _querying) return false;
            if (!_literal && (_module.Capabilities & LanguageCapabilities.ConvertOnCommit) != 0 && _composition.RawText.Length > 0)
            {
                if (_querying) return false;
                if (_page != null && _page.Candidates.Count > 0)
                {
                    var step = ProcessEngine(CompositionCommand.Choose(_page.Candidates[_selectedCandidate]), request.Id);
                    ApplyStep(request, step);
                    if (request.Result != null || _needsResolution) return false;
                    if (step.CommitText != null) return true;
                    if (_querying) return false;
                }
            }
            var commit = ProcessEngine(CompositionCommand.Commit(), request.Id);
            ApplyStep(request, commit);
            return request.Result == null && !_needsResolution;
        }
        private void ApplyStep(Request request, EngineStep step)
        {
            _composition = step.Snapshot;
            if (step.DeferredInput != null) request.DeferredInput = step.DeferredInput;
            if (step.Choices.Count > 0)
            {
                var record = new ChoiceRecord(request.Id + ":" + (++_choiceSequence), request.Id, _userId, _module.LanguageId, step.Choices);
                if (_mode == EditingMode.Draft) _draftChoices.Add(record); else _runtime.Frequency.Record(record);
            }
            _page = null;
            if (step.CommitText != null)
            {
                if (_mode == EditingMode.Draft && request.Operation.Kind == "finish")
                {
                    var preview = new MemoryTextTarget(_text.Text, _text.Anchor, _text.Caret);
                    preview.ApplyEdit(new TextEditRequest(request.Id, 0, _text.SelectionStart, _text.SelectionLength, step.CommitText));
                    _stagedFinishText = preview.ReadSnapshot().Text;
                }
                else
                {
                    var result = Edit(request, _text.SelectionStart, _text.SelectionLength, step.CommitText);
                    if (result.Applied) { _composition = ProcessEngine(CompositionCommand.Clear(), request.Id).Snapshot; CancelQuery(); }
                }
            }
            else if (step.QueryRequested && !_literal && _composition.RawText.Length > 0) StartQuery();
            else CancelQuery();
        }
        private TextEditResult Edit(Request request, int start, int length, string text)
        {
            _lastEdit = ApplyTarget(_editing, new TextEditRequest(request.Id, _text.Version, start, length, text, request.Part++));
            _text = _lastEdit.Snapshot;
            if (!_lastEdit.Applied) HandleEditFailure(request, _lastEdit, ReferenceEquals(_editing, _target));
            return _lastEdit;
        }
        private TextEditResult ApplyTarget(ITextTarget target, TextEditRequest request)
        {
            try
            {
                var result = target.ApplyEdit(request);
                if (result.Status == TextEditStatus.Unknown) _unknownRequest = request;
                return result;
            }
            catch (Exception ex)
            {
                Report("text", ex.Message, request.Operation);
                _unknownRequest = request;
                TextSnapshot current;
                try { current = target.ReadSnapshot(); } catch { current = _text; }
                return new TextEditResult(TextEditStatus.Unknown, current, reason: ex.Message);
            }
        }
        private bool HandleEditFailure(Request request, TextEditResult result, bool host)
        {
            if (result.Status == TextEditStatus.Rejected && request.DeferredInput != null)
            {
                var retained = new CompositionSnapshot(_composition.Version, _composition.RawText + request.DeferredInput, _composition.SelectedText);
                request.DeferredInput = null;
                _composition = ProcessEngine(CompositionCommand.RestoreComposition(retained), request.Id).Snapshot;
            }
            if (result.Status == TextEditStatus.VersionConflict && host) End(SessionEndReason.ExternalChange);
            else if (result.Status == TextEditStatus.InvalidTarget) End(SessionEndReason.TargetInvalidated);
            else if (result.Status == TextEditStatus.Unknown) _needsResolution = true;
            Complete(request, _needsResolution ? OperationStatus.NeedsResolution : OperationStatus.Rejected, result.Reason);
            return true;
        }
        private void Delete(Request request, bool forward)
        {
            int start = _text.SelectionStart, length = _text.SelectionLength;
            if (length == 0)
            {
                if (forward) length = TextBoundary.Next(_text.Text, start) - start;
                else { int previous = TextBoundary.Previous(_text.Text, start); length = start - previous; start = previous; }
            }
            Edit(request, start, length, "");
        }
        private void SwitchLanguage(Request request)
        {
            var ids = _languages.Keys.ToArray();
            string next = request.Operation.Text;
            if (next.Length == 0) next = ids[(Array.IndexOf(ids, _module.LanguageId) + 1) % ids.Length];
            if (!_languages.TryGetValue(next, out var module)) { Complete(request, OperationStatus.Rejected, "语言不在当前会话列表中"); return; }
            if (ReferenceEquals(module, _module)) return;
            var replacement = CreateSlot(module.CreateEngine());
            try { replacement.Engine.Process(CompositionCommand.SetCompletion(_completionEnabled)); }
            catch { replacement.Retire(); throw; }
            CancelQuery(); _module.DataChanged -= OnDataChanged; _slot.Retire();
            _module = module; _slot = replacement; _generation++; _literal = false;
            _composition = _slot.Engine.Snapshot; _module.DataChanged += OnDataChanged;
            SkyRuntime.RememberLanguage(_playerId, _module.LanguageId);
        }
        private EngineSlot CreateSlot(ILanguageEngine engine) => new EngineSlot(engine, ex => Report("engine-dispose", ex.Message));
        private EngineStep ProcessEngine(CompositionCommand command, OperationId operation)
        {
            ICompositionCheckpoint? checkpoint = null;
            try { checkpoint = _slot.Engine.CaptureCheckpoint(); return _slot.Engine.Process(command); }
            catch (Exception first)
            {
                Report("engine", first.Message, operation);
                if (!_literal && checkpoint != null)
                {
                    EngineSlot? replacement = null;
                    try
                    {
                        replacement = CreateSlot(_module.CreateEngine());
                        if (!replacement.Engine.RestoreCheckpoint(checkpoint).Succeeded) throw new InvalidOperationException("检查点恢复失败");
                        var step = replacement.Engine.Process(command);
                        CancelQuery(); _slot.Retire(); _slot = replacement; _generation++;
                        Report("engine", "引擎已从检查点恢复", operation, true);
                        return step;
                    }
                    catch (Exception retry) { replacement?.Retire(); Report("restore", retry.Message, operation); }
                }
                EnterLiteral();
                return _slot.Engine.Process(command);
            }
        }
        private void EnterLiteral()
        {
            var replacement = CreateSlot(new LiteralEngine());
            replacement.Engine.Process(CompositionCommand.RestoreComposition(_composition));
            CancelQuery(); _slot.Retire(); _slot = replacement; _generation++; _literal = true; _page = null;
            _composition = replacement.Engine.Snapshot;
        }
        private void RetryConversion(Request request)
        {
            if (!_literal) return;
            EngineSlot? replacement = null;
            try
            {
                replacement = CreateSlot(_module.CreateEngine());
                replacement.Engine.Process(CompositionCommand.SetCompletion(_completionEnabled));
                var step = replacement.Engine.Process(CompositionCommand.RestoreComposition(_composition));
                _slot.Retire(); _slot = replacement; _generation++; _literal = false; _composition = step.Snapshot;
                if (step.QueryRequested && _composition.RawText.Length > 0) StartQuery();
                Report("restore", "语言转换已恢复", request.Id, true);
            }
            catch (Exception ex) { replacement?.Retire(); Report("restore", ex.Message, request.Id); }
        }
        private void ResolveWrite(Request request)
        {
            if (!_needsResolution) { Complete(request, OperationStatus.Rejected, "没有待处理的写入"); return; }
            _needsResolution = false;
            if (_unknownFill)
            {
                _unknownFill = false;
                if (!request.Operation.Enabled) { End(SessionEndReason.Cancelled); Complete(request, OperationStatus.Applied); return; }
                _pending = request;
                var host = _target.ReadSnapshot();
                _lastEdit = ApplyTarget(_target, new TextEditRequest(request.Id, host.Version, 0, host.Text.Length, _unknownRequest?.Text ?? _text.Text, request.Part++));
                if (_lastEdit.Applied) End(SessionEndReason.Finished, _lastEdit);
                else { _unknownFill = _lastEdit.Status == TextEditStatus.Unknown; HandleEditFailure(request, _lastEdit, true); return; }
            }
            else
            {
                _text = _editing.ReadSnapshot();
                if (request.Operation.Enabled && _unknownRequest != null)
                {
                    int start = Math.Min(_unknownRequest.Start, _text.Text.Length);
                    _lastEdit = Edit(request, start, Math.Min(_unknownRequest.Length, _text.Text.Length - start), _unknownRequest.Text);
                    if (_lastEdit.Applied) _composition = ProcessEngine(CompositionCommand.Clear(), request.Id).Snapshot;
                }
                else _composition = ProcessEngine(CompositionCommand.Clear(), request.Id).Snapshot;
            }
            Complete(request, _needsResolution ? OperationStatus.NeedsResolution : OperationStatus.Applied);
        }
        private void StartQuery(int attempt = 0, int offset = 0, int selection = 0)
        {
            CancelQuery();
            if (_literal || _composition.RawText.Length == 0) return;
            var query = new CandidateQuery(++_querySequence, _generation, _composition, _module.CaptureData(), _settingsVersion, _runtime.Frequency.Capture(_userId, _module.LanguageId), offset);
            var cancellation = new CancellationTokenSource(); _queryCancellation = cancellation;
            _querying = true; _page = null;
            var slot = _slot; slot.Retain(); _runtime.RetainWork();
            _ = RunQuery(slot, query, cancellation, attempt, selection);
        }
        private async Task RunQuery(EngineSlot slot, CandidateQuery query, CancellationTokenSource cancellation, int attempt, int selection)
        {
            CandidatePage? page = null; Exception? failure = null;
            try { page = await Task.Run(() => slot.Engine.QueryCandidatesAsync(query, cancellation.Token), cancellation.Token).ConfigureAwait(false); }
            catch (Exception ex) { failure = ex; }
            finally { slot.Release(); _runtime.ReleaseWork(); }
            bool cancelled = cancellation.IsCancellationRequested;
            cancellation.Dispose();
            _runtime.Context.Post(() =>
            {
                if (_lifecycle == SessionLifecycle.Ended || query.Id != _querySequence || query.Generation != _generation || !ReferenceEquals(slot, _slot)) return;
                if (ReferenceEquals(_queryCancellation, cancellation)) _queryCancellation = null;
                if (cancelled) return;
                if (query.Composition.Version != _composition.Version || query.Data.Version != _module.CaptureData().Version || query.SettingsVersion != _settingsVersion) return;
                _querying = false;
                if (failure == null && (page == null || !ReferenceEquals(page.Query, query))) failure = new InvalidOperationException("候选页与原查询不一致");
                if (failure != null)
                {
                    Report("query", failure.Message);
                    if (attempt == 0) { StartQuery(1, query.Offset, selection); Publish(); return; }
                    EnterLiteral();
                }
                else if (page != null) { _page = page; _selectedCandidate = Math.Max(0, selection < 0 ? page.Candidates.Count - 1 : Math.Min(selection, page.Candidates.Count - 1)); }
                Publish(); ContinuePending();
            });
        }
        private void CancelQuery()
        {
            var cancellation = _queryCancellation; _queryCancellation = null;
            if (cancellation != null) { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }
            _querying = false; _page = null; _querySequence++;
        }
        private void OnDataChanged() => _runtime.Context.Post(() => { if (_lifecycle == SessionLifecycle.Ended || _literal || _composition.RawText.Length == 0) return; StartQuery(); Publish(); });
        private void OnFrequencyChanged(FrequencyChange change)
        {
            if (change.UserId == _userId && change.LanguageId == _module.LanguageId) OnDataChanged();
        }
        private void OnTargetChanged(TextTargetChange change) => _runtime.Context.Post(() =>
        {
            if (_lifecycle == SessionLifecycle.Ended) return;
            if (_mode == EditingMode.Direct) _text = change.Snapshot;
            CancelPending(); End(change.Invalidated ? SessionEndReason.TargetInvalidated : SessionEndReason.ExternalChange);
        });
        private SessionSnapshot BuildSnapshot() => new SessionSnapshot(_id, _module.LanguageId, _module.EngineId, _lifecycle, _composition, _text, _page, _selectedCandidate, _querying, _literal, _completionEnabled, _pending?.Id, _diagnostic, _needsResolution);
        private void Publish() { Snapshot = BuildSnapshot(); Notify(StateChanged, Snapshot); }
        private void Complete(Request request, OperationStatus status, string? reason = null)
        {
            if (request.Result != null) return;
            if (ReferenceEquals(_pending, request)) _pending = null;
            request.Result = new OperationResult(request.Id, status, _lastEdit, reason);
            Publish(); Notify(OperationCompleted, request.Result);
        }
        private void CancelPending() { if (_pending != null) Complete(_pending, OperationStatus.Cancelled); }
        private void End(SessionEndReason reason, TextEditResult? fill = null)
        {
            if (_lifecycle == SessionLifecycle.Ended) return;
            _lifecycle = SessionLifecycle.Ended; CancelQuery();
            _pending = null;
            _target.Changed -= OnTargetChanged; _module.DataChanged -= OnDataChanged;
            _runtime.Frequency.Changed -= OnFrequencyChanged;
            if (reason == SessionEndReason.Finished) foreach (var record in _draftChoices) _runtime.Frequency.Record(record);
            _draftChoices.Clear();
            _runtime.Frequency.ReleaseSession(_id);
            var storageResults = _runtime.SaveUser(_userId, _languages.Keys);
            TextSnapshot final;
            try { final = _target.ReadSnapshot(); } catch { final = _text; }
            _slot.Retire();
            try
            {
                (_target as ITextOperationCache)?.ReleaseSession(_id);
                if (!ReferenceEquals(_editing, _target)) (_editing as ITextOperationCache)?.ReleaseSession(_id);
            }
            catch (Exception ex) { Report("text-cache", ex.Message); }
            _runtime.SessionEnded(this, _target.Identity, _id);
            Publish(); Notify(Completed, new SessionResult(reason, final, fill, _diagnostics, storageResults));
        }
        private void Report(string stage, string reason, OperationId? operation = null, bool recovered = false)
        {
            _diagnostic = reason; _diagnostics.Add(stage + ": " + reason);
            _runtime.Report(new SkyDiagnostic(stage, reason, _module.EngineId, _id, operation, recovered));
        }
        private void Notify<T>(Action<T>? handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); } catch (Exception ex) { Report("notification", ex.Message); }
        }
        internal void EndFromRuntime() => _runtime.Context.Post(() => { CancelPending(); End(SessionEndReason.RuntimeDisposed); });
        public void Dispose() => _runtime.Context.Post(() => { CancelPending(); End(SessionEndReason.Cancelled); });
    }
}
