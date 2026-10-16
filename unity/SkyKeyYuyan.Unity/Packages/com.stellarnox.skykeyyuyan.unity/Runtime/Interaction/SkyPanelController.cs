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
        RetryWrite
    }
    /// <summary>
    /// 默认表现层使用的完整面板状态。
    /// </summary>
    public sealed class SkyPanelState
    {
        internal SkyPanelState(SessionSnapshot session, KeyboardLayout layout, string focusedKey, bool shift, OperationResult result, string problem)
        { Session = session; Layout = layout; FocusedKey = focusedKey; Shift = shift; LastResult = result; Problem = problem; }
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
        private KeyboardLayout _layout;
        private string _language, _focus = "", _problem;
        private bool _shift, _disposed;
        private OperationResult _lastResult;
        /// <summary>
        /// 绑定已打开的会话。
        /// </summary>
        public SkyPanelController(ISkySession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
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
            if (snapshot.NeedsResolution) return key.Action == "cancel";
            if (key.Action == "completion") return _session.Languages.Any(language => language.LanguageId == snapshot.LanguageId && (language.Capabilities & LanguageCapabilities.CompletionSwitch) != 0);
            if (key.Action == "retry") return snapshot.Literal;
            if (key.Action == "language") return _session.Languages.Count > 1;
            return true;
        }
        private void OnState(SessionSnapshot snapshot)
        {
            if (_language != snapshot.LanguageId)
            {
                _language = snapshot.LanguageId; _shift = false; _problem = null;
                try { _layout = _session.GetKeyboard(); _focus = _layout.Keys[0].Id; }
                catch (Exception ex) { _layout = null; _focus = ""; _problem = ex.Message; }
            }
            Publish(snapshot);
        }
        private void OnOperation(OperationResult result) { _lastResult = result; Publish(_session.Snapshot); }
        private void Publish(SessionSnapshot snapshot)
        {
            State = new SkyPanelState(snapshot, _layout, _focus, _shift, _lastResult, _problem);
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
            _focus = id;
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
                default: return false;
            }
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
                    if (_layout == null) return false;
                    var available = _layout.Keys.Where(IsKeyEnabled).ToArray();
                    if (available.Length == 0) return false;
                    var navigation = new KeyboardLayout(_layout.LanguageId, _layout.DataVersion, available);
                    _focus = navigation.Navigate(_focus, control == SkyControl.KeyLeft ? TextNavigation.Left : control == SkyControl.KeyRight ? TextNavigation.Right : control == SkyControl.KeyUp ? TextNavigation.Up : TextNavigation.Down);
                    Publish(snapshot); return true;
                case SkyControl.Activate: return ActivateKey(_focus);
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
                case SkyControl.Delete: operation = InputOperation.DeleteForward(); break;
                case SkyControl.SelectAll: operation = InputOperation.SelectAll(); break;
                case SkyControl.Clear: operation = InputOperation.Clear(); break;
                case SkyControl.Newline: operation = InputOperation.Newline(); break;
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
