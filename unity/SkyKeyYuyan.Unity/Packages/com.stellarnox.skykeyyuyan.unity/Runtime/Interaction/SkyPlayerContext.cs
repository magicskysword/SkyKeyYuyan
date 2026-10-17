using System;
using System.Collections.Generic;
using System.Linq;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 玩家的会话默认配置。
    /// </summary>
    public sealed class SkyPlayerOptions
    {
        /// <summary>
        /// 玩家身份。
        /// </summary>
        public string PlayerId { get; set; } = "player";
        /// <summary>
        /// 词频用户，为空时使用运行时默认值。
        /// </summary>
        public string UserId { get; set; }
        /// <summary>
        /// 可选语言，为空时使用运行时列表。
        /// </summary>
        public IReadOnlyList<string> Languages { get; set; }
        /// <summary>
        /// 显式初始语言。
        /// </summary>
        public string InitialLanguage { get; set; }
        /// <summary>
        /// 默认使用独立草稿。
        /// </summary>
        public EditingMode EditingMode { get; set; } = EditingMode.Draft;
        /// <summary>
        /// 英语补全初始开关。
        /// </summary>
        public bool CompletionEnabled { get; set; } = true;
        /// <summary>
        /// 默认正文输入类型和最低可见行数。
        /// </summary>
        public SkyTextInputOptions TextInput { get; set; } = new SkyTextInputOptions();
        /// <summary>
        /// 物理键盘的输入方式。
        /// </summary>
        public SkyKeyboardInputOptions KeyboardInput { get; set; } = new SkyKeyboardInputOptions();
        /// <summary>
        /// 宿主可覆盖平台的系统 IME 能力探测。
        /// </summary>
        public Func<bool> SystemImeSupported { get; set; }
    }
    /// <summary>
    /// 管理一位玩家的独立会话、面板状态和暂停原因。
    /// </summary>
    public sealed class SkyPlayerContext : IDisposable
    {
        private readonly SkyPlayerOptions _options;
        private readonly HashSet<string> _pauses = new HashSet<string>(StringComparer.Ordinal);
        private IUnityTextTarget _target, _draft;
        private bool _gamepadEnabled = true, _disposed;
        private SkyKeyboardInputOptions _keyboardInput, _activeKeyboardInput;
        private IDisposable _keyboardView;
        private bool _retainTargetFocus;
        internal void FinishForKeyboard() { _retainTargetFocus = true; Handle(SkyControl.Finish); }
        /// <summary>
        /// 后续会话使用的默认键盘配置。
        /// </summary>
        public SkyKeyboardInputOptions KeyboardInput => _keyboardInput;
        /// <summary>
        /// 设置后续会话使用的键盘配置。
        /// </summary>
        public void ConfigureKeyboardInput(SkyKeyboardInputOptions options)
        { _keyboardInput = options ?? throw new ArgumentNullException(nameof(options)); DevicePolicyChanged?.Invoke(); }
        internal bool KeyboardSettingsMatch(SkyKeyboardInputOptions options) => _activeKeyboardInput != null &&
            _activeKeyboardInput.Mode == options.Mode && _activeKeyboardInput.ForceVirtualKeyboard == options.ForceVirtualKeyboard && _activeKeyboardInput.InitialLanguage == options.InitialLanguage;
        /// <summary>
        /// 当前解析后的键盘输入方式。
        /// </summary>
        public SkyKeyboardInputMode KeyboardMode => (_activeKeyboardInput ?? _keyboardInput).Resolve(_options.SystemImeSupported?.Invoke() ?? SkyKeyboardInputOptions.PlatformSupportsSystemIme);
        /// <summary>
        /// 当前会话是否由物理键盘进入。
        /// </summary>
        public bool KeyboardSession { get; private set; }
        /// <summary>
        /// 当前会话是否显示完整虚拟键盘。
        /// </summary>
        public bool VirtualKeyboardVisible { get; private set; } = true;
        /// <summary>
        /// 最近使用的设备类别。
        /// </summary>
        public SkyDeviceKind LastDevice { get; internal set; } = SkyDeviceKind.Keyboard;
        private bool _gamepadConnected;
        /// <summary>
        /// 当前玩家是否绑定了已连接的手柄。
        /// </summary>
        public bool GamepadConnected => _gamepadConnected;
        internal void SetGamepadConnected(bool connected)
        { if (_gamepadConnected == connected) return; _gamepadConnected = connected; Panel?.SetGamepadConnected(connected && _gamepadEnabled); UpdateNativePolicy(); }
        /// <summary>
        /// 固定玩家身份及会话默认值。
        /// </summary>
        public SkyPlayerContext(SkyPlayerOptions options)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.PlayerId)) throw new ArgumentException("玩家配置及身份不能为空", nameof(options));
            _options = new SkyPlayerOptions { PlayerId = options.PlayerId, UserId = options.UserId, Languages = options.Languages?.ToArray(), InitialLanguage = options.InitialLanguage, EditingMode = options.EditingMode, CompletionEnabled = options.CompletionEnabled, TextInput = options.TextInput ?? new SkyTextInputOptions(), SystemImeSupported = options.SystemImeSupported };
            _keyboardInput = options.KeyboardInput ?? new SkyKeyboardInputOptions();
            TextInput = _options.TextInput;
        }
        /// <summary>
        /// 固定的玩家身份。
        /// </summary>
        public string PlayerId => _options.PlayerId;
        /// <summary>
        /// 当前活动会话。
        /// </summary>
        public ISkySession Session { get; private set; }
        /// <summary>
        /// 当前统一面板控制器。
        /// </summary>
        public SkyPanelController Panel { get; private set; }
        /// <summary>
        /// 原输入目标。
        /// </summary>
        public IUnityTextTarget Target => _target;
        /// <summary>
        /// 当前输入使用的正文视口配置。
        /// </summary>
        public SkyTextInputOptions TextInput { get; private set; }
        /// <summary>
        /// 为默认表现提供原生草稿目标，由表现持有其生命周期。
        /// </summary>
        public Func<TextSnapshot, ITextTarget> DraftTargetFactory { get; set; }
        /// <summary>
        /// 是否自动跟随注册目标的原生编辑态。
        /// </summary>
        public bool FollowEditing { get; set; } = true;
        /// <summary>
        /// 是否由 SKY 接管键盘；设置此兼容开关会显式选择 SKY 或系统输入。
        /// </summary>
        public bool KeyboardEnabled
        {
            get => KeyboardMode == SkyKeyboardInputMode.SkyIme;
            set
            {
                var current = _activeKeyboardInput ?? _keyboardInput;
                _keyboardInput = new SkyKeyboardInputOptions(value ? SkyKeyboardInputMode.SkyIme : SkyKeyboardInputMode.SystemIme, current.ForceVirtualKeyboard, current.InitialLanguage);
                if (_activeKeyboardInput != null) _activeKeyboardInput = _keyboardInput;
                _target?.SetKeyboardOwned(value); _draft?.SetKeyboardOwned(value); UpdateNativePolicy(); DevicePolicyChanged?.Invoke();
            }
        }
        /// <summary>
        /// 是否路由手柄；默认启用。
        /// </summary>
        public bool GamepadEnabled
        {
            get => _gamepadEnabled;
            set { if (_gamepadEnabled == value) return; _gamepadEnabled = value; Panel?.SetGamepadConnected(_gamepadConnected && value); UpdateNativePolicy(); DevicePolicyChanged?.Invoke(); }
        }
        /// <summary>
        /// 成功打开会话及面板控制器。
        /// </summary>
        public event Action<SkyPlayerContext> Opened;
        /// <summary>
        /// 活动输入已关闭，包含玩家释放及打开失败后的清理。
        /// </summary>
        public event Action<SkyPlayerContext> Closed;
        /// <summary>
        /// 会话完成并已解除原生输入接管。
        /// </summary>
        public event Action<SessionResult> Completed;
        internal event Action DevicePolicyChanged;
        /// <summary>
        /// 在指定目标打开输入，可按次覆盖原框或草稿模式。
        /// </summary>
        public ServiceResult Open(IUnityTextTarget target, ISkyRuntime runtime, EditingMode? mode, string language)
        { return Open(target, runtime, mode, language, null); }
        /// <summary>
        /// 在指定目标打开输入，并按次选择正文输入类型及最低显示行数。
        /// </summary>
        public ServiceResult Open(IUnityTextTarget target, ISkyRuntime runtime = null, EditingMode? mode = null, string language = null, SkyTextInputOptions textInput = null)
        { return OpenCore(target, runtime, mode, language, textInput, false, null); }
        /// <summary>
        /// 使用键盘配置进入目标，系统模式由原生控件直接处理输入。
        /// </summary>
        public ServiceResult OpenKeyboard(IUnityTextTarget target, ISkyRuntime runtime = null, string language = null, SkyTextInputOptions textInput = null, SkyKeyboardInputOptions keyboardInput = null)
        { return OpenCore(target, runtime, null, language, textInput, true, keyboardInput ?? _keyboardInput); }
        private ServiceResult OpenCore(IUnityTextTarget target, ISkyRuntime runtime, EditingMode? mode, string language, SkyTextInputOptions textInput, bool keyboard, SkyKeyboardInputOptions keyboardInput)
        {
            if (_disposed || Session != null) return new ServiceResult(false, "session", "玩家已经打开输入或已释放");
            runtime = runtime ?? SkyRuntime.Default;
            if (target == null || !target.IsAlive) return new ServiceResult(false, "session", "文本目标不可用");
            var input = keyboardInput ?? _keyboardInput;
            if (keyboard && input.Resolve(_options.SystemImeSupported?.Invoke() ?? SkyKeyboardInputOptions.PlatformSupportsSystemIme) == SkyKeyboardInputMode.SystemIme && !input.ForceVirtualKeyboard)
                return new ServiceResult(true, "system-ime");
            if (runtime == null) return new ServiceResult(false, "session", "运行时不可用");
            try
            {
                KeyboardSession = keyboard; VirtualKeyboardVisible = !keyboard || input.ForceVirtualKeyboard;
                _activeKeyboardInput = input;
                TextInput = textInput ?? (keyboard && target is ISkyInlineKeyboardTarget typed ? new SkyTextInputOptions(typed.KeyboardTextType, _options.TextInput.MinimumVisibleLines) : _options.TextInput);
                var options = new SessionOptions { PlayerId = _options.PlayerId, UserId = _options.UserId, Languages = _options.Languages, InitialLanguage = language ?? (keyboard ? input.InitialLanguage : null) ?? _options.InitialLanguage, EditingMode = VirtualKeyboardVisible ? mode ?? _options.EditingMode : EditingMode.Direct, CompletionEnabled = _options.CompletionEnabled };
                if (DraftTargetFactory != null && VirtualKeyboardVisible) options.DraftTargetFactory = initial =>
                {
                    var draft = DraftTargetFactory(initial); _draft = draft as IUnityTextTarget; return draft;
                };
                var opened = runtime.OpenSession(options, target);
                if (!opened.Succeeded) { Release(false); return new ServiceResult(false, "session", opened.Reason); }
                Session = opened.Session; _target = target; Session.Completed += OnCompleted;
                Panel = new SkyPanelController(Session, TextInput);
                Panel.SetGamepadConnected(_gamepadConnected && _gamepadEnabled);
                _target.SetKeyboardOwned(KeyboardEnabled); _draft?.SetKeyboardOwned(KeyboardEnabled);
                UpdateNativePolicy();
                if (_pauses.Count != 0) Session.Process(InputOperation.Pause());
                Notify(Opened, this);
                if (keyboard) Panel.SetTextFocused(true);
                if (!VirtualKeyboardVisible && target is ISkyInlineKeyboardTarget inline) _keyboardView = inline.CreateKeyboardView(Session);
                return new ServiceResult(true, "session");
            }
            catch (Exception ex)
            {
                Release(); return new ServiceResult(false, "session", ex.Message);
            }
        }
        /// <summary>
        /// 轮询原框与原生草稿，发现外部改动或销毁。
        /// </summary>
        public void Poll() { _target?.Poll(); _draft?.Poll(); }
        /// <summary>
        /// 分别维护设备、应用焦点和宿主暂停条件。
        /// </summary>
        public void SetPaused(string reason, bool paused)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("暂停原因不能为空", nameof(reason));
            bool wasPaused = _pauses.Count != 0;
            if (paused) _pauses.Add(reason); else _pauses.Remove(reason);
            if (Session == null || wasPaused == (_pauses.Count != 0)) return;
            Session.Process(_pauses.Count != 0 ? InputOperation.Pause() : InputOperation.Resume());
        }
        /// <summary>
        /// 处理属于该玩家的统一面板动作。
        /// </summary>
        public bool Handle(SkyControl control, bool extend = false, string text = "") => Panel != null && Panel.Handle(control, extend, text);
        private void OnCompleted(SessionResult result)
        {
            var target = _target;
            bool retain = _retainTargetFocus && result.Reason == SessionEndReason.Finished;
            var selection = Session?.Snapshot.Text;
            Release(false);
            if (retain && selection != null && target is ISkyInlineKeyboardTarget inline && target.IsAlive)
            {
                string text = target.ReadSnapshot().Text;
                inline.RestoreKeyboardSelection(TextBoundary.Floor(text, Math.Min(selection.Anchor, text.Length)), TextBoundary.Floor(text, Math.Min(selection.Caret, text.Length)));
            }
            if (!retain && (result.Reason == SessionEndReason.Finished || result.Reason == SessionEndReason.Cancelled || result.Reason == SessionEndReason.RuntimeDisposed)) EndNativeEditing(target);
            Notify(Completed, result);
        }
        private void Release(bool cancel = true)
        {
            var session = Session; Session = null;
            _keyboardView?.Dispose(); _keyboardView = null;
            if (session != null) { session.Completed -= OnCompleted; if (cancel) session.Dispose(); }
            Panel?.Dispose(); Panel = null;
            _target?.SetKeyboardOwned(false); _draft?.SetKeyboardOwned(false); _target = null; _draft = null;
            _activeKeyboardInput = null; KeyboardSession = false; VirtualKeyboardVisible = true;
            _retainTargetFocus = false;
            UpdateNativePolicy();
            if (session != null) Notify(Closed, this);
        }
        private static void EndNativeEditing(IUnityTextTarget target)
        {
            try { if (target != null && target.IsAlive) target.EndEditing(); }
            catch (Exception ex) { Debug.LogWarning("SKY native end: " + ex.Message); }
        }
        private void UpdateNativePolicy() => SkyNativeInputCoordination.Set(this, Session != null && KeyboardEnabled, Session != null && GamepadEnabled && _gamepadConnected);
        private static void Notify<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(value); } catch (Exception ex) { Debug.LogWarning("SKY player notification: " + ex.Message); }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; var target = _target; Release(); EndNativeEditing(target);
            Opened = null; Closed = null; Completed = null; DevicePolicyChanged = null;
        }
    }
}
