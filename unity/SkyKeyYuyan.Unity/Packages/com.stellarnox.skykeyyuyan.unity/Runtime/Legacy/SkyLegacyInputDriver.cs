using System;
using System.Collections.Generic;
using System.Linq;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity.Legacy
{
    /// <summary>
    /// Legacy Input 的设备采样入口，可由宿主提供平台映射。
    /// </summary>
    public interface ILegacyInputSource
    {
        /// <summary>
        /// 当前输入帧的身份，同帧键盘文本只消费一次。
        /// </summary>
        long Frame { get; }
        /// <summary>
        /// 保留 Unity 手柄槽位顺序的设备名称。
        /// </summary>
        string[] JoystickNames();
        /// <summary>
        /// 读取已经配置的输入轴。
        /// </summary>
        float Axis(string name);
        /// <summary>
        /// 读取 Unity 按键状态。
        /// </summary>
        bool Key(KeyCode key);
        /// <summary>
        /// 本帧由系统键盘布局生成的文本。
        /// </summary>
        string Text { get; }
    }
    /// <summary>
    /// 直接读取 Unity Legacy Input。
    /// </summary>
    public sealed class UnityLegacyInputSource : ILegacyInputSource
    {
        /// <inheritdoc>
        /// </inheritdoc>
        public long Frame => Time.frameCount;
        /// <inheritdoc>
        /// </inheritdoc>
        public string[] JoystickNames() => Input.GetJoystickNames();
        /// <inheritdoc>
        /// </inheritdoc>
        public float Axis(string name) => Input.GetAxisRaw(name);
        /// <inheritdoc>
        /// </inheritdoc>
        public bool Key(KeyCode key) => Input.GetKey(key);
        /// <inheritdoc>
        /// </inheritdoc>
        public string Text => Input.inputString;
    }
    /// <summary>
    /// 一个 Legacy 手柄槽位的轴名称和按钮映射。
    /// </summary>
    [Serializable]
    public sealed class LegacyGamepadProfile
    {
        /// <summary>
        /// 为一到八号手柄生成默认轴名称。
        /// </summary>
        public LegacyGamepadProfile(int joystick)
        {
            if (joystick < 1 || joystick > 8) throw new ArgumentOutOfRangeException(nameof(joystick));
            Joystick = joystick; string prefix = "SkyPad" + joystick + ".";
            LeftX = prefix + "LX"; LeftY = prefix + "LY"; RightX = prefix + "RX"; RightY = prefix + "RY";
            DpadX = prefix + "DX"; DpadY = prefix + "DY"; LeftTrigger = prefix + "LT";
        }
        /// <summary>
        /// Unity 的手柄槽位，从一开始。
        /// </summary>
        public int Joystick { get; }
        /// <summary>
        /// 左摇杆水平轴。
        /// </summary>
        public string LeftX;
        /// <summary>
        /// 左摇杆垂直轴，向上为正。
        /// </summary>
        public string LeftY;
        /// <summary>
        /// 右摇杆水平轴。
        /// </summary>
        public string RightX;
        /// <summary>
        /// 右摇杆垂直轴，向上为正。
        /// </summary>
        public string RightY;
        /// <summary>
        /// 方向键水平轴。
        /// </summary>
        public string DpadX;
        /// <summary>
        /// 方向键垂直轴，向上为正。
        /// </summary>
        public string DpadY;
        /// <summary>
        /// 左扳机轴，按下为正。
        /// </summary>
        public string LeftTrigger;
        /// <summary>
        /// A 按钮索引。
        /// </summary>
        public int Activate = 0;
        /// <summary>
        /// B 按钮索引。
        /// </summary>
        public int Backspace = 1;
        /// <summary>
        /// X 按钮索引。
        /// </summary>
        public int Finish = 2;
        /// <summary>
        /// Select 按钮索引。
        /// </summary>
        public int Language = 6;
        /// <summary>
        /// 确认候选的 Y 按钮索引。
        /// </summary>
        public int ConfirmCandidate = 3;
        /// <summary>
        /// LB 按钮索引。
        /// </summary>
        public int PreviousCandidate = 4;
        /// <summary>
        /// RB 按钮索引。
        /// </summary>
        public int NextCandidate = 5;
        /// <summary>
        /// L3 按钮索引。
        /// </summary>
        public int FocusLeft = 8;
        /// <summary>
        /// R3 按钮索引。
        /// </summary>
        public int FocusRight = 9;
    }
    /// <summary>
    /// 将 Legacy Input 的明确设备绑定转换为统一操作。
    /// </summary>
    public sealed class SkyLegacyInputDriver : ISkyInputDriver
    {
        private sealed class Binding
        {
            internal string Id;
            internal LegacyGamepadProfile Profile;
            internal bool TextFocused, LeftReady = true, RightReady = true;
            internal int FocusButtons;
            internal readonly Dictionary<SkyControl, SkyInputRepeat> Repeats = new Dictionary<SkyControl, SkyInputRepeat>();
        }
        private readonly SkyInputRouter _router;
        private readonly ILegacyInputSource _source;
        private readonly Dictionary<int, Binding> _pads = new Dictionary<int, Binding>();
        private readonly HashSet<string> _invalidAxes = new HashSet<string>(StringComparer.Ordinal);
        private Binding _keyboard;
        private bool _disposed;
        private long _sequence;
        private long _textFrame = long.MinValue;
        /// <summary>
        /// 使用所选 Legacy 后端和可替换的采样入口。
        /// </summary>
        public SkyLegacyInputDriver(SkyInputRouter router, ILegacyInputSource source = null)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            if (router.Backend != SkyInputBackend.Legacy) throw new ArgumentException("路由器没有选择 Legacy Input", nameof(router));
            _source = source ?? new UnityLegacyInputSource();
        }
        /// <summary>
        /// 轴和扳机的动作阈值。
        /// </summary>
        public float Threshold { get; set; } = 0.55f;
        /// <summary>
        /// 初次按住后的重复延迟。
        /// </summary>
        public double RepeatDelay { get; set; } = 0.4;
        /// <summary>
        /// 后续重复间隔。
        /// </summary>
        public double RepeatInterval { get; set; } = 0.075;
        /// <summary>
        /// 轴配置失败等后端诊断，每个无效轴报告一次。
        /// </summary>
        public event Action<ServiceResult> Diagnostic;
        /// <summary>
        /// 显式绑定一到八号手柄槽位。
        /// </summary>
        public string BindGamepad(int joystick, string playerId, LegacyGamepadProfile profile = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SkyLegacyInputDriver));
            profile = profile ?? new LegacyGamepadProfile(joystick);
            if (profile.Joystick != joystick) throw new ArgumentException("手柄配置槽位不一致", nameof(profile));
            string id = "legacy/gamepad/" + joystick;
            _router.Bind(id, SkyDeviceKind.Gamepad, playerId);
            if (!_pads.ContainsKey(joystick)) _pads.Add(joystick, new Binding { Id = id, Profile = profile });
            return id;
        }
        /// <summary>
        /// 绑定 Legacy 的逻辑键盘源。
        /// </summary>
        public string BindKeyboard(string playerId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SkyLegacyInputDriver));
            const string id = "legacy/keyboard"; _router.Bind(id, SkyDeviceKind.Keyboard, playerId);
            if (_keyboard == null) _keyboard = new Binding { Id = id };
            return id;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Poll(double time)
        {
            if (_disposed) return;
            string[] names = _source.JoystickNames();
            foreach (var binding in _pads.Values.ToArray())
            {
                var profile = binding.Profile;
                bool connected = profile.Joystick <= names.Length && !string.IsNullOrEmpty(names[profile.Joystick - 1]);
                _router.SetConnected(binding.Id, connected);
                if (!connected) { foreach (var repeat in binding.Repeats.Values) repeat.Reset(); binding.FocusButtons = 0; continue; }
                int clicks = (Button(profile, profile.FocusLeft) ? 1 : 0) | (Button(profile, profile.FocusRight) ? 2 : 0);
                if ((clicks & ~binding.FocusButtons) != 0) _router.Route(SkyInputBackend.Legacy, binding.Id, SkyControl.ToggleTextFocus, sequence: ++_sequence);
                binding.FocusButtons = clicks;
                bool focused = _router.TryGetPlayer(binding.Id, out var player) && player.Panel != null && player.Panel.State.TextFocused;
                if (binding.TextFocused != focused) { binding.TextFocused = focused; binding.LeftReady = binding.RightReady = false; }
                var direction = new Vector2(Axis(profile.DpadX), Axis(profile.DpadY));
                if (direction.sqrMagnitude < 0.1f) direction = new Vector2(Axis(profile.LeftX), Axis(profile.LeftY));
                direction = Ready(direction, ref binding.LeftReady); var right = Ready(new Vector2(Axis(profile.RightX), Axis(profile.RightY)), ref binding.RightReady);
                Directions(binding, !focused ? direction : Vector2.zero, time, false, false);
                Directions(binding, focused ? direction : Vector2.zero, time, true, false);
                Directions(binding, focused ? right : Vector2.zero, time, true, true);
                Emit(binding, SkyControl.Activate, Button(profile, profile.Activate), time);
                Emit(binding, SkyControl.Backspace, Button(profile, profile.Backspace), time, true);
                Emit(binding, SkyControl.Finish, Button(profile, profile.Finish), time);
                Emit(binding, SkyControl.Language, Button(profile, profile.Language), time);
                Emit(binding, SkyControl.PreviousCandidate, Button(profile, profile.PreviousCandidate) || !focused && (right.x < -Threshold || right.y > Threshold), time, true);
                Emit(binding, SkyControl.NextCandidate, Button(profile, profile.NextCandidate) || !focused && (right.x > Threshold || right.y < -Threshold), time, true);
                Emit(binding, SkyControl.ConfirmCandidate, Button(profile, profile.ConfirmCandidate), time);
            }
            if (_keyboard != null) PollKeyboard(_keyboard, time);
        }
        private float Axis(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || _invalidAxes.Contains(name)) return 0;
            try { return _source.Axis(name); }
            catch (Exception ex)
            {
                _invalidAxes.Add(name);
                var result = new ServiceResult(false, "legacy-axis:" + name, ex.Message);
                if (Diagnostic != null) foreach (Action<ServiceResult> handler in Diagnostic.GetInvocationList()) try { handler(result); } catch { }
                else Debug.LogWarning("SKY legacy axis " + name + ": " + ex.Message);
                return 0;
            }
        }
        private bool Button(LegacyGamepadProfile profile, int button) => button >= 0 && button < 20 && _source.Key((KeyCode)((int)KeyCode.Joystick1Button0 + (profile.Joystick - 1) * 20 + button));
        private Vector2 Ready(Vector2 value, ref bool ready)
        {
            if (Mathf.Abs(value.x) <= Threshold && Mathf.Abs(value.y) <= Threshold) ready = true;
            if (!ready) return Vector2.zero;
            return Mathf.Abs(value.x) > Mathf.Abs(value.y) ? new Vector2(value.x, 0) : new Vector2(0, value.y);
        }
        private void Directions(Binding binding, Vector2 value, double time, bool text, bool extend)
        {
            Emit(binding, !text ? SkyControl.KeyLeft : extend ? SkyControl.SelectLeft : SkyControl.TextLeft, value.x < -Threshold, time, true);
            Emit(binding, !text ? SkyControl.KeyRight : extend ? SkyControl.SelectRight : SkyControl.TextRight, value.x > Threshold, time, true);
            Emit(binding, !text ? SkyControl.KeyUp : extend ? SkyControl.SelectUp : SkyControl.TextUp, value.y > Threshold, time, true);
            Emit(binding, !text ? SkyControl.KeyDown : extend ? SkyControl.SelectDown : SkyControl.TextDown, value.y < -Threshold, time, true);
        }
        private void PollKeyboard(Binding binding, double time)
        {
            string text = _textFrame != _source.Frame ? _source.Text ?? "" : "";
            _textFrame = _source.Frame;
            if (text.Length != 0)
            {
                string printable = new string(text.Where(value => !char.IsControl(value)).ToArray());
                if (printable.Length != 0) _router.Route(SkyInputBackend.Legacy, binding.Id, SkyControl.Text, text: printable, sequence: ++_sequence);
            }
            bool extend = _source.Key(KeyCode.LeftShift) || _source.Key(KeyCode.RightShift);
            Emit(binding, SkyControl.TextLeft, _source.Key(KeyCode.LeftArrow), time, true, extend);
            Emit(binding, SkyControl.TextRight, _source.Key(KeyCode.RightArrow), time, true, extend);
            Emit(binding, SkyControl.TextUp, _source.Key(KeyCode.UpArrow), time, true, extend);
            Emit(binding, SkyControl.TextDown, _source.Key(KeyCode.DownArrow), time, true, extend);
            Emit(binding, SkyControl.Backspace, _source.Key(KeyCode.Backspace), time, true);
            Emit(binding, SkyControl.Delete, _source.Key(KeyCode.Delete), time, true);
            bool enter = _source.Key(KeyCode.Return) || _source.Key(KeyCode.KeypadEnter);
            bool control = _source.Key(KeyCode.LeftControl) || _source.Key(KeyCode.RightControl);
            bool multiline = _router.TryGetPlayer(binding.Id, out var player) && player.Panel != null && player.Panel.State.TextFocused && player.TextInput.Type == SkyTextInputType.MultiLine;
            Emit(binding, SkyControl.Newline, enter && multiline && !control, time);
            Emit(binding, SkyControl.Finish, enter && (!multiline || control), time);
            Emit(binding, SkyControl.Cancel, _source.Key(KeyCode.Escape), time);
            Emit(binding, SkyControl.Language, _source.Key(KeyCode.Tab), time);
            Emit(binding, SkyControl.SelectAll, (_source.Key(KeyCode.LeftControl) || _source.Key(KeyCode.RightControl)) && _source.Key(KeyCode.A), time);
        }
        private void Emit(Binding binding, SkyControl control, bool pressed, double time, bool repeat = false, bool extend = false)
        {
            if (!binding.Repeats.TryGetValue(control, out var gate)) binding.Repeats.Add(control, gate = new SkyInputRepeat());
            gate.Delay = RepeatDelay; gate.Interval = RepeatInterval;
            if (gate.Pulse(pressed, time, repeat)) _router.Route(SkyInputBackend.Legacy, binding.Id, control, extend, sequence: ++_sequence);
        }
        /// <summary>
        /// 宿主修正轴配置后允许重新读取。
        /// </summary>
        public void RetryAxes() => _invalidAxes.Clear();
        /// <summary>
        /// 解除手柄槽位绑定，之后可重新分配玩家或映射。
        /// </summary>
        public void UnbindGamepad(int joystick)
        {
            if (!_pads.TryGetValue(joystick, out var binding)) return;
            _pads.Remove(joystick); _router.Unbind(binding.Id);
        }
        /// <summary>
        /// 解除逻辑键盘绑定。
        /// </summary>
        public void UnbindKeyboard()
        {
            if (_keyboard == null) return;
            _router.Unbind(_keyboard.Id); _keyboard = null; _textFrame = long.MinValue;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; foreach (var binding in _pads.Values) _router.Unbind(binding.Id);
            if (_keyboard != null) _router.Unbind(_keyboard.Id);
            _pads.Clear(); _keyboard = null;
        }
    }
}
