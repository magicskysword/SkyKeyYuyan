using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SkyKeyYuyan.Unity.InputSystem
{
    /// <summary>
    /// 将明确绑定的 Input System 设备转换为默认面板动作。
    /// </summary>
    public sealed class SkyInputSystemDriver : ISkyInputDriver
    {
        private sealed class Binding
        {
            internal InputDevice Device;
            internal string Id;
            internal readonly Dictionary<SkyControl, SkyInputRepeat> Repeats = new Dictionary<SkyControl, SkyInputRepeat>();
            internal readonly Queue<string> Text = new Queue<string>();
            internal Action<char> TextHandler;
            internal char HighSurrogate;
            internal bool ImeOwned, ImeBefore;
        }
        private readonly SkyInputRouter _router;
        private readonly Dictionary<int, Binding> _devices = new Dictionary<int, Binding>();
        private bool _disposed;
        private long _sequence;
        /// <summary>
        /// 使用已选择 Input System 后端的路由器。
        /// </summary>
        public SkyInputSystemDriver(SkyInputRouter router)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            if (router.Backend != SkyInputBackend.InputSystem) throw new ArgumentException("路由器没有选择 Input System", nameof(router));
        }
        /// <summary>
        /// 摇杆、方向键和扳机的动作阈值。
        /// </summary>
        public float Threshold { get; set; } = 0.55f;
        /// <summary>
        /// 按住动作的初次重复延迟。
        /// </summary>
        public double RepeatDelay { get; set; } = 0.4;
        /// <summary>
        /// 按住动作的重复间隔。
        /// </summary>
        public double RepeatInterval { get; set; } = 0.075;
        /// <summary>
        /// 绑定指定手柄，不自动接管其他设备。
        /// </summary>
        public string Bind(Gamepad device, string playerId) => BindDevice(device, playerId, SkyDeviceKind.Gamepad).Id;
        /// <summary>
        /// 绑定指定键盘；仍由玩家的键盘开关决定是否路由。
        /// </summary>
        public string Bind(Keyboard device, string playerId)
        {
            var binding = BindDevice(device, playerId, SkyDeviceKind.Keyboard);
            if (binding.TextHandler == null)
            {
                binding.TextHandler = value => QueueText(binding, value);
                device.onTextInput += binding.TextHandler;
            }
            return binding.Id;
        }
        private Binding BindDevice(InputDevice device, string playerId, SkyDeviceKind kind)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SkyInputSystemDriver));
            if (device == null) throw new ArgumentNullException(nameof(device));
            string id = "input-system/" + device.deviceId;
            _router.Bind(id, kind, playerId);
            if (_devices.TryGetValue(device.deviceId, out var existing)) return existing;
            var binding = new Binding { Device = device, Id = id }; _devices.Add(device.deviceId, binding); return binding;
        }
        private void QueueText(Binding binding, char value)
        {
            if (!_router.CanRoute(binding.Id)) { binding.HighSurrogate = '\0'; return; }
            if (char.IsHighSurrogate(value)) { binding.HighSurrogate = value; return; }
            if (char.IsLowSurrogate(value))
            {
                if (binding.HighSurrogate != '\0') binding.Text.Enqueue(new string(new[] { binding.HighSurrogate, value }));
                binding.HighSurrogate = '\0'; return;
            }
            binding.HighSurrogate = '\0';
            if (!char.IsControl(value)) binding.Text.Enqueue(value.ToString());
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Poll(double time)
        {
            if (_disposed) return;
            foreach (var binding in _devices.Values.ToArray())
            {
                bool connected = binding.Device.added && binding.Device.enabled;
                _router.SetConnected(binding.Id, connected);
                if (!connected) { foreach (var repeat in binding.Repeats.Values) repeat.Reset(); binding.Text.Clear(); continue; }
                if (binding.Device is Gamepad pad) PollGamepad(binding, pad, time);
                else if (binding.Device is Keyboard keyboard) PollKeyboard(binding, keyboard, time);
            }
        }
        private void PollGamepad(Binding binding, Gamepad pad, double time)
        {
            Vector2 direction = pad.dpad.ReadValue(); if (direction.sqrMagnitude < 0.1f) direction = pad.leftStick.ReadValue();
            Directions(binding, direction, time, false, false);
            Directions(binding, pad.rightStick.ReadValue(), time, true, pad.leftTrigger.ReadValue() >= Threshold);
            Emit(binding, SkyControl.Activate, pad.buttonSouth.isPressed, time);
            Emit(binding, SkyControl.Backspace, pad.buttonEast.isPressed, time, true);
            Emit(binding, SkyControl.Finish, pad.buttonWest.isPressed, time);
            Emit(binding, SkyControl.Language, pad.buttonNorth.isPressed, time);
            Emit(binding, SkyControl.PreviousCandidate, pad.leftShoulder.isPressed, time, true);
            Emit(binding, SkyControl.NextCandidate, pad.rightShoulder.isPressed, time, true);
            Emit(binding, SkyControl.ConfirmCandidate, pad.leftStickButton.isPressed || pad.rightStickButton.isPressed, time);
        }
        private void Directions(Binding binding, Vector2 value, double time, bool text, bool extend)
        {
            Emit(binding, text ? SkyControl.TextLeft : SkyControl.KeyLeft, value.x < -Threshold, time, true, extend);
            Emit(binding, text ? SkyControl.TextRight : SkyControl.KeyRight, value.x > Threshold, time, true, extend);
            Emit(binding, text ? SkyControl.TextUp : SkyControl.KeyUp, value.y > Threshold, time, true, extend);
            Emit(binding, text ? SkyControl.TextDown : SkyControl.KeyDown, value.y < -Threshold, time, true, extend);
        }
        private void PollKeyboard(Binding binding, Keyboard keyboard, double time)
        {
            bool owned = _router.TryGetPlayer(binding.Id, out var player) && player.KeyboardEnabled && player.Session != null;
            if (owned)
            {
                if (!binding.ImeOwned) { binding.ImeBefore = keyboard.imeSelected.isPressed; binding.ImeOwned = true; }
                keyboard.SetIMEEnabled(false);
            }
            else RestoreIme(binding);
            bool extend = keyboard.shiftKey.isPressed;
            while (binding.Text.Count != 0) _router.Route(SkyInputBackend.InputSystem, binding.Id, SkyControl.Text, text: binding.Text.Dequeue(), sequence: ++_sequence);
            Emit(binding, SkyControl.TextLeft, keyboard.leftArrowKey.isPressed, time, true, extend);
            Emit(binding, SkyControl.TextRight, keyboard.rightArrowKey.isPressed, time, true, extend);
            Emit(binding, SkyControl.TextUp, keyboard.upArrowKey.isPressed, time, true, extend);
            Emit(binding, SkyControl.TextDown, keyboard.downArrowKey.isPressed, time, true, extend);
            Emit(binding, SkyControl.Backspace, keyboard.backspaceKey.isPressed, time, true);
            Emit(binding, SkyControl.Delete, keyboard.deleteKey.isPressed, time, true);
            Emit(binding, SkyControl.Finish, keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed, time);
            Emit(binding, SkyControl.Cancel, keyboard.escapeKey.isPressed, time);
            Emit(binding, SkyControl.Language, keyboard.tabKey.isPressed, time);
            Emit(binding, SkyControl.SelectAll, keyboard.ctrlKey.isPressed && keyboard.aKey.isPressed, time);
        }
        private void Emit(Binding binding, SkyControl control, bool pressed, double time, bool repeat = false, bool extend = false)
        {
            if (!binding.Repeats.TryGetValue(control, out var gate)) binding.Repeats.Add(control, gate = new SkyInputRepeat());
            gate.Delay = RepeatDelay; gate.Interval = RepeatInterval;
            if (gate.Pulse(pressed, time, repeat)) _router.Route(SkyInputBackend.InputSystem, binding.Id, control, extend, sequence: ++_sequence);
        }
        private static void RestoreIme(Binding binding)
        {
            if (!binding.ImeOwned) return;
            if (binding.Device is Keyboard keyboard && keyboard.added) keyboard.SetIMEEnabled(binding.ImeBefore);
            binding.ImeOwned = false;
        }
        /// <summary>
        /// 解除一个设备，并释放其输入订阅。
        /// </summary>
        public void Unbind(InputDevice device)
        {
            if (device == null || !_devices.TryGetValue(device.deviceId, out var binding)) return;
            if (device is Keyboard keyboard && binding.TextHandler != null) keyboard.onTextInput -= binding.TextHandler;
            RestoreIme(binding); _devices.Remove(device.deviceId); _router.Unbind(binding.Id);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose() { if (_disposed) return; _disposed = true; foreach (var binding in _devices.Values.ToArray()) Unbind(binding.Device); }
    }
}
