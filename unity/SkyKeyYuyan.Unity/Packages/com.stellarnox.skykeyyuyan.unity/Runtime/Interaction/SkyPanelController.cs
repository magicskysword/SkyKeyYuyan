using System;
using System.Linq;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 输入设备及虚拟按键共用的面板动作。
    /// </summary>
    public enum SkyControl
    {
        /// <summary>
        /// 向左移动虚拟键盘焦点。
        /// </summary>
        KeyLeft,
        /// <summary>
        /// 向右移动虚拟键盘焦点。
        /// </summary>
        KeyRight,
        /// <summary>
        /// 向上移动虚拟键盘焦点。
        /// </summary>
        KeyUp,
        /// <summary>
        /// 向下移动虚拟键盘焦点。
        /// </summary>
        KeyDown,
        /// <summary>
        /// 激活当前虚拟按键。
        /// </summary>
        Activate,
        /// <summary>
        /// 输入文本。
        /// </summary>
        Text,
        /// <summary>
        /// 回退组字或正文。
        /// </summary>
        Backspace,
        /// <summary>
        /// 浏览前一个候选。
        /// </summary>
        PreviousCandidate,
        /// <summary>
        /// 浏览后一个候选。
        /// </summary>
        NextCandidate,
        /// <summary>
        /// 确认当前候选。
        /// </summary>
        ConfirmCandidate,
        /// <summary>
        /// 完成编辑。
        /// </summary>
        Finish,
        /// <summary>
        /// 取消编辑。
        /// </summary>
        Cancel,
        /// <summary>
        /// 循环语言。
        /// </summary>
        Language,
        /// <summary>
        /// 向左移动正文光标。
        /// </summary>
        TextLeft,
        /// <summary>
        /// 向右移动正文光标。
        /// </summary>
        TextRight,
        /// <summary>
        /// 向上移动正文光标。
        /// </summary>
        TextUp,
        /// <summary>
        /// 向下移动正文光标。
        /// </summary>
        TextDown,
        /// <summary>
        /// 删除光标后的字符。
        /// </summary>
        Delete,
        /// <summary>
        /// 全选正文。
        /// </summary>
        SelectAll,
        /// <summary>
        /// 清空正文。
        /// </summary>
        Clear,
        /// <summary>
        /// 输入换行。
        /// </summary>
        Newline,
        /// <summary>
        /// 切换虚拟键盘 Shift。
        /// </summary>
        Shift,
        /// <summary>
        /// 切换英语补全。
        /// </summary>
        Completion,
        /// <summary>
        /// 重试语言转换。
        /// </summary>
        Retry,
        /// <summary>
        /// 接受未知写入后的目标现状。
        /// </summary>
        AcceptCurrentText,
        /// <summary>
        /// 显式重试未知写入。
        /// </summary>
        RetryWrite,
        /// <summary>
        /// 切换正文编辑焦点。
        /// </summary>
        ToggleTextFocus,
        /// <summary>
        /// 向左扩展正文选区。
        /// </summary>
        SelectLeft,
        /// <summary>
        /// 向右扩展正文选区。
        /// </summary>
        SelectRight,
        /// <summary>
        /// 向上扩展正文选区。
        /// </summary>
        SelectUp,
        /// <summary>
        /// 向下扩展正文选区。
        /// </summary>
        SelectDown
    }
    /// <summary>
    /// 默认表现层使用的完整面板状态。
    /// </summary>
    public sealed class SkyPanelState
    {
        internal SkyPanelState(SessionSnapshot session, KeyboardLayout layout, string focusedKey, bool shift, OperationResult result, string problem, bool textFocused)
        { Session = session; Layout = layout; FocusedKey = focusedKey; Shift = shift; LastResult = result; Problem = problem; TextFocused = textFocused; }
        /// <summary>
        /// 会话的权威快照。
        /// </summary>
        public SessionSnapshot Session { get; }
        /// <summary>
        /// 当前语言布局，资源失败时为空。
        /// </summary>
        public KeyboardLayout Layout { get; }
        /// <summary>
        /// 当前虚拟键盘焦点。
        /// </summary>
        public string FocusedKey { get; }
        /// <summary>
        /// 是否使用 Shift 输出。
        /// </summary>
        public bool Shift { get; }
        /// <summary>
        /// 手柄当前是否编辑正文。
        /// </summary>
        public bool TextFocused { get; }
        /// <summary>
        /// 最近一次操作的执行结果。
        /// </summary>
        public OperationResult LastResult { get; }
        /// <summary>
        /// 布局等表现资源的诊断。
        /// </summary>
        public string Problem { get; }
    }
    /// <summary>
    /// 将两套默认面板的交互连接到统一会话。
    /// </summary>
    public sealed class SkyPanelController : IDisposable
    {
        private readonly ISkySession _session;
        private readonly SkyTextInputOptions _textInput;
        private KeyboardLayout _layout, _normalLayout;
        private KeyboardLayout _navigationSource, _navigationLayout;
        private SessionLifecycle _navigationLifecycle;
        private bool _navigationLiteral, _navigationResolving;
        private string _language, _focus = "", _problem;
        private bool _shift, _disposed, _resolving;
        private bool _textFocused;
        private double? _navigationColumn;
        private OperationResult _lastResult;
        /// <summary>
        /// 绑定已打开的会话。
        /// </summary>
        public SkyPanelController(ISkySession session, SkyTextInputOptions textInput = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _textInput = textInput ?? new SkyTextInputOptions();
            _session.StateChanged += OnState; _session.OperationCompleted += OnOperation;
            OnState(session.Snapshot);
        }
        /// <summary>
        /// 当前完整表现状态。
        /// </summary>
        public SkyPanelState State { get; private set; }
        /// <summary>
        /// 面板需要刷新。
        /// </summary>
        public event Action<SkyPanelState> Changed;
        /// <summary>
        /// 按当前语言能力和恢复状态判断虚拟按键是否可用。
        /// </summary>
        public bool IsKeyEnabled(KeyboardKey key)
        {
            var snapshot = _session.Snapshot;
            if (_disposed || snapshot.Lifecycle != SessionLifecycle.Active) return false;
            if (snapshot.NeedsResolution) return key.Action == "cancel" || key.Action == "accept-current" || key.Action == "retry-write";
            if (key.Action == "completion") return _session.Languages.Any(language => language.LanguageId == snapshot.LanguageId && (language.Capabilities & LanguageCapabilities.CompletionSwitch) != 0);
            if (key.Action == "retry") return snapshot.Literal;
            if (key.Action == "language") return _session.Languages.Count > 1;
            if (key.Action == "newline") return _textInput.Type == SkyTextInputType.MultiLine;
            return true;
        }
        private KeyboardLayout NavigationLayout(SessionSnapshot snapshot)
        {
            if (ReferenceEquals(_navigationSource, _layout) && _navigationLifecycle == snapshot.Lifecycle
                && _navigationLiteral == snapshot.Literal && _navigationResolving == snapshot.NeedsResolution) return _navigationLayout;
            _navigationSource = _layout; _navigationLifecycle = snapshot.Lifecycle;
            _navigationLiteral = snapshot.Literal; _navigationResolving = snapshot.NeedsResolution;
            // 可用性只随语言布局、生命周期和恢复状态变化，方向重复复用同一张导航图。
            var available = _layout.Keys.Where(IsKeyEnabled).ToArray();
            _navigationLayout = available.Length == 0 ? null : new KeyboardLayout(_layout.LanguageId, _layout.DataVersion, available);
            return _navigationLayout;
        }
        private void OnState(SessionSnapshot snapshot)
        {
            if (_language != snapshot.LanguageId)
            {
                _language = snapshot.LanguageId; _shift = false; _problem = null; _navigationColumn = null;
                try { _normalLayout = _session.GetKeyboard(); _focus = _normalLayout.Keys[0].Id; }
                catch (Exception ex) { _normalLayout = null; _focus = ""; _problem = ex.Message; }
                _layout = _normalLayout;
            }
            if (_resolving != snapshot.NeedsResolution)
            {
                _resolving = snapshot.NeedsResolution;
                _navigationColumn = null; if (_resolving) _textFocused = false;
                _layout = _resolving ? new KeyboardLayout(snapshot.LanguageId, "resolution.1", new[]
                {
                    new KeyboardKey("cancel", "取消", "", "", 0, 0, "cancel"),
                    new KeyboardKey("accept-current", "当前正文", "", "", 0, 1, "accept-current"),
                    new KeyboardKey("retry-write", "再次提交", "", "", 0, 2, "retry-write")
                }) : _normalLayout;
                _focus = _resolving ? "cancel" : _layout?.Keys[0].Id ?? "";
            }
            Publish(snapshot);
        }
        private void OnOperation(OperationResult result) { _lastResult = result; Publish(_session.Snapshot); }
        private void Publish(SessionSnapshot snapshot)
        {
            State = new SkyPanelState(snapshot, _layout, _focus, _shift, _lastResult, _problem, _textFocused);
            if (Changed == null) return;
            foreach (Action<SkyPanelState> handler in Changed.GetInvocationList())
                try { handler(State); } catch (Exception ex) { Debug.LogWarning("SKY panel: " + ex.Message); }
        }
        /// <summary>
        /// 以指针或宿主代码激活指定虚拟按键。
        /// </summary>
        public bool ActivateKey(string id)
        {
            if (_disposed || _layout == null || _session.Snapshot.Lifecycle != SessionLifecycle.Active) return false;
            KeyboardKey key;
            try { key = _layout[id]; } catch (System.Collections.Generic.KeyNotFoundException) { return false; }
            if (!IsKeyEnabled(key)) return false;
            _focus = id; _textFocused = false; _navigationColumn = null;
            switch (key.Action)
            {
                case "input": return Handle(SkyControl.Text, text: _shift ? key.Alternate : key.Output);
                case "shift": return Handle(SkyControl.Shift);
                case "backspace": return Handle(SkyControl.Backspace);
                case "newline": return Handle(SkyControl.Newline);
                case "language": return Handle(SkyControl.Language);
                case "finish": return Handle(SkyControl.Finish);
                case "cancel": return Handle(SkyControl.Cancel);
                case "left": return Handle(SkyControl.TextLeft);
                case "right": return Handle(SkyControl.TextRight);
                case "up": return Handle(SkyControl.TextUp);
                case "down": return Handle(SkyControl.TextDown);
                case "delete": return Handle(SkyControl.Delete);
                case "selectall": return Handle(SkyControl.SelectAll);
                case "clear": return Handle(SkyControl.Clear);
                case "completion": return Handle(SkyControl.Completion);
                case "retry": return Handle(SkyControl.Retry);
                case "accept-current": return Handle(SkyControl.AcceptCurrentText);
                case "retry-write": return Handle(SkyControl.RetryWrite);
                default: return false;
            }
        }
        /// <summary>
        /// 将交互焦点切换到正文或虚拟键盘，并保留各自的位置。
        /// </summary>
        public bool SetTextFocused(bool focused)
        {
            var snapshot = _session.Snapshot;
            if (_disposed || snapshot.Lifecycle != SessionLifecycle.Active || snapshot.NeedsResolution) return false;
            if (_textFocused != focused) { _textFocused = focused; Publish(snapshot); }
            return true;
        }
        /// <summary>
        /// 处理设备动作；正文导航可同时扩展选区。
        /// </summary>
        public bool Handle(SkyControl control, bool extend = false, string text = "")
        {
            var snapshot = _session.Snapshot;
            if (_disposed || snapshot.Lifecycle == SessionLifecycle.Ended || snapshot.Lifecycle == SessionLifecycle.Paused && control != SkyControl.Cancel) return false;
            InputOperation operation;
            switch (control)
            {
                case SkyControl.KeyLeft: case SkyControl.KeyRight: case SkyControl.KeyUp: case SkyControl.KeyDown:
                    if (_layout == null || _textFocused) return false;
                    var navigation = NavigationLayout(snapshot);
                    if (navigation == null) return false;
                    var direction = control == SkyControl.KeyLeft ? TextNavigation.Left : control == SkyControl.KeyRight ? TextNavigation.Right : control == SkyControl.KeyUp ? TextNavigation.Up : TextNavigation.Down;
                    if (direction == TextNavigation.Up || direction == TextNavigation.Down)
                    {
                        if (!_navigationColumn.HasValue) { var current = _layout.Keys.FirstOrDefault(key => key.Id == _focus) ?? _layout.Keys[0]; _navigationColumn = current.Column + current.ColumnSpan / 2d; }
                        _focus = navigation.Navigate(_focus, direction, _navigationColumn.Value);
                    }
                    else { _focus = navigation.Navigate(_focus, direction); _navigationColumn = null; }
                    Publish(snapshot); return true;
                case SkyControl.Activate: return !_textFocused && ActivateKey(_focus);
                case SkyControl.ToggleTextFocus: return SetTextFocused(!_textFocused);
                case SkyControl.Shift: _shift = !_shift; Publish(snapshot); return true;
                case SkyControl.Text: operation = InputOperation.Insert(text); break;
                case SkyControl.Backspace: operation = InputOperation.Backspace(); break;
                case SkyControl.PreviousCandidate: operation = InputOperation.MoveCandidate(-1); break;
                case SkyControl.NextCandidate: operation = InputOperation.MoveCandidate(1); break;
                case SkyControl.ConfirmCandidate:
                    var page = snapshot.Candidates;
                    if (snapshot.Querying || page == null || page.Candidates.Count == 0) return false;
                    operation = InputOperation.ConfirmCandidate(page.Query.Id, page.Candidates[snapshot.SelectedCandidate].Id); break;
                case SkyControl.Finish: operation = InputOperation.Finish(); break;
                case SkyControl.Cancel: operation = InputOperation.Cancel(); break;
                case SkyControl.Language: operation = InputOperation.SwitchLanguage(); break;
                case SkyControl.TextLeft: operation = InputOperation.Navigate(TextNavigation.Left, extend); break;
                case SkyControl.TextRight: operation = InputOperation.Navigate(TextNavigation.Right, extend); break;
                case SkyControl.TextUp: operation = InputOperation.Navigate(TextNavigation.Up, extend); break;
                case SkyControl.TextDown: operation = InputOperation.Navigate(TextNavigation.Down, extend); break;
                case SkyControl.SelectLeft: operation = InputOperation.Navigate(TextNavigation.Left, true); break;
                case SkyControl.SelectRight: operation = InputOperation.Navigate(TextNavigation.Right, true); break;
                case SkyControl.SelectUp: operation = InputOperation.Navigate(TextNavigation.Up, true); break;
                case SkyControl.SelectDown: operation = InputOperation.Navigate(TextNavigation.Down, true); break;
                case SkyControl.Delete: operation = InputOperation.DeleteForward(); break;
                case SkyControl.SelectAll: operation = InputOperation.SelectAll(); break;
                case SkyControl.Clear: operation = InputOperation.Clear(); break;
                case SkyControl.Newline:
                    if (_textInput.Type == SkyTextInputType.SingleLine) return false;
                    operation = InputOperation.Newline(); break;
                case SkyControl.Completion: operation = InputOperation.SetCompletion(!snapshot.CompletionEnabled); break;
                case SkyControl.Retry: operation = InputOperation.RetryConversion(); break;
                case SkyControl.AcceptCurrentText: operation = InputOperation.ResolveWrite(false); break;
                case SkyControl.RetryWrite: operation = InputOperation.ResolveWrite(true); break;
                default: return false;
            }
            _session.Process(operation); return true;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _session.StateChanged -= OnState; _session.OperationCompleted -= OnOperation; Changed = null;
        }
    }
}
