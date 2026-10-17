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
    }
    /// <summary>
    /// 管理一位玩家的独立会话、面板状态和暂停原因。
    /// </summary>
    public sealed class SkyPlayerContext : IDisposable
    {
        private readonly SkyPlayerOptions _options;
        private readonly HashSet<string> _pauses = new HashSet<string>(StringComparer.Ordinal);
        private IUnityTextTarget _target, _draft;
        private bool _keyboardEnabled, _gamepadEnabled = true, _disposed;
        /// <summary>
        /// 固定玩家身份及会话默认值。
        /// </summary>
        public SkyPlayerContext(SkyPlayerOptions options)
        {
            if (options == null || string.IsNullOrWhiteSpace(options.PlayerId)) throw new ArgumentException("玩家配置及身份不能为空", nameof(options));
            _options = new SkyPlayerOptions { PlayerId = options.PlayerId, UserId = options.UserId, Languages = options.Languages?.ToArray(), InitialLanguage = options.InitialLanguage, EditingMode = options.EditingMode, CompletionEnabled = options.CompletionEnabled, TextInput = options.TextInput ?? new SkyTextInputOptions() };
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
        /// 是否接管键盘；默认关闭。
        /// </summary>
        public bool KeyboardEnabled
        {
            get => _keyboardEnabled;
            set { if (_keyboardEnabled == value) return; _keyboardEnabled = value; _target?.SetKeyboardOwned(value); _draft?.SetKeyboardOwned(value); UpdateNativePolicy(); DevicePolicyChanged?.Invoke(); }
        }
        /// <summary>
        /// 是否路由手柄；默认启用。
        /// </summary>
        public bool GamepadEnabled
        {
            get => _gamepadEnabled;
            set { if (_gamepadEnabled == value) return; _gamepadEnabled = value; UpdateNativePolicy(); DevicePolicyChanged?.Invoke(); }
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
        public ServiceResult Open(IUnityTextTarget target, ISkyRuntime runtime = null, EditingMode? mode = null, string language = null, SkyTextInputOptions textInput = null)
        {
            if (_disposed || Session != null) return new ServiceResult(false, "session", "玩家已经打开输入或已释放");
            runtime = runtime ?? SkyRuntime.Default;
            if (runtime == null || target == null || !target.IsAlive) return new ServiceResult(false, "session", "运行时或文本目标不可用");
            try
            {
                TextInput = textInput ?? _options.TextInput;
                var options = new SessionOptions { PlayerId = _options.PlayerId, UserId = _options.UserId, Languages = _options.Languages, InitialLanguage = language ?? _options.InitialLanguage, EditingMode = mode ?? _options.EditingMode, CompletionEnabled = _options.CompletionEnabled };
                if (DraftTargetFactory != null) options.DraftTargetFactory = initial =>
                {
                    var draft = DraftTargetFactory(initial); _draft = draft as IUnityTextTarget; return draft;
                };
                var opened = runtime.OpenSession(options, target);
                if (!opened.Succeeded) { _draft = null; return new ServiceResult(false, "session", opened.Reason); }
                Session = opened.Session; _target = target; Session.Completed += OnCompleted;
                Panel = new SkyPanelController(Session, TextInput);
                _target.SetKeyboardOwned(KeyboardEnabled); _draft?.SetKeyboardOwned(KeyboardEnabled);
                UpdateNativePolicy();
                if (_pauses.Count != 0) Session.Process(InputOperation.Pause());
                Notify(Opened, this);
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
            Release(false);
            if (result.Reason == SessionEndReason.Finished || result.Reason == SessionEndReason.Cancelled || result.Reason == SessionEndReason.RuntimeDisposed) EndNativeEditing(target);
            Notify(Completed, result);
        }
        private void Release(bool cancel = true)
        {
            var session = Session; Session = null;
            if (session != null) { session.Completed -= OnCompleted; if (cancel) session.Dispose(); }
            Panel?.Dispose(); Panel = null;
            _target?.SetKeyboardOwned(false); _draft?.SetKeyboardOwned(false); _target = null; _draft = null;
            UpdateNativePolicy();
            if (session != null) Notify(Closed, this);
        }
        private static void EndNativeEditing(IUnityTextTarget target)
        {
            try { if (target != null && target.IsAlive) target.EndEditing(); }
            catch (Exception ex) { Debug.LogWarning("SKY native end: " + ex.Message); }
        }
        private void UpdateNativePolicy() => SkyNativeInputCoordination.Set(this, Session != null && KeyboardEnabled, Session != null && GamepadEnabled);
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
