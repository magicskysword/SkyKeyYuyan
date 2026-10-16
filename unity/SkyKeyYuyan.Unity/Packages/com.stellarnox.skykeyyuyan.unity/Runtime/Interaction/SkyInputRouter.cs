using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 宿主选定的输入事件来源。
    /// </summary>
    public enum SkyInputBackend
    {
        /// <summary>
        /// 宿主自定义输入。
        /// </summary>
        Custom,
        /// <summary>
        /// Unity Legacy Input。
        /// </summary>
        Legacy,
        /// <summary>
        /// Unity Input System。
        /// </summary>
        InputSystem
    }
    /// <summary>
    /// 可以分别启停的设备类别。
    /// </summary>
    public enum SkyDeviceKind
    {
        /// <summary>
        /// 物理键盘。
        /// </summary>
        Keyboard,
        /// <summary>
        /// 手柄。
        /// </summary>
        Gamepad,
        /// <summary>
        /// 宿主界面的鼠标或触摸指针；通过解除绑定关闭。
        /// </summary>
        Pointer
    }
    /// <summary>
    /// 将一个后端的设备事件唯一分配到玩家，协调断线与应用暂停。
    /// </summary>
    public sealed class SkyInputRouter : IDisposable
    {
        private sealed class Binding
        {
            internal SkyPlayerContext Player;
            internal SkyDeviceKind Kind;
            internal bool Connected = true;
            internal long LastSequence;
        }
        private readonly Dictionary<string, SkyPlayerContext> _players = new Dictionary<string, SkyPlayerContext>(StringComparer.Ordinal);
        private readonly Dictionary<string, Binding> _bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
        private readonly Dictionary<SkyPlayerContext, Action> _policies = new Dictionary<SkyPlayerContext, Action>();
        private bool _disposed, _focused = true;
        /// <summary>
        /// 固定唯一事件后端。
        /// </summary>
        public SkyInputRouter(SkyInputBackend backend) { Backend = backend; }
        /// <summary>
        /// 接纳的事件后端。
        /// </summary>
        public SkyInputBackend Backend { get; }
        /// <summary>
        /// 查询设备当前所属玩家。
        /// </summary>
        public bool TryGetPlayer(string deviceId, out SkyPlayerContext player)
        {
            if (_bindings.TryGetValue(deviceId, out var binding)) { player = binding.Player; return true; }
            player = null; return false;
        }
        /// <summary>
        /// 设备是否可以向活动会话发送内容动作。
        /// </summary>
        public bool CanRoute(string deviceId) => !_disposed && _bindings.TryGetValue(deviceId, out var binding) && binding.Connected && Enabled(binding)
            && binding.Player.Session != null && binding.Player.Session.Snapshot.Lifecycle == Core.SessionLifecycle.Active;
        /// <summary>
        /// 玩家当前是否具有已连接且启用的设备，并且应用处于前台。
        /// </summary>
        public bool HasEnabledDevice(string playerId) => !_disposed && _focused && _players.TryGetValue(playerId, out var player)
            && _bindings.Values.Any(binding => ReferenceEquals(binding.Player, player) && binding.Connected && Enabled(binding));
        /// <summary>
        /// 解除玩家及其设备绑定，不释放玩家对象。
        /// </summary>
        public void Unregister(string playerId)
        {
            if (!_players.TryGetValue(playerId, out var player)) return;
            foreach (var id in _bindings.Where(pair => ReferenceEquals(pair.Value.Player, player)).Select(pair => pair.Key).ToArray()) _bindings.Remove(id);
            if (_policies.TryGetValue(player, out var callback)) { player.DevicePolicyChanged -= callback; _policies.Remove(player); }
            player.SetPaused("devices", true); _players.Remove(playerId);
        }
        /// <summary>
        /// 注册玩家；设备绑定完成后会话可以恢复活动。
        /// </summary>
        public void Register(SkyPlayerContext player)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SkyInputRouter));
            if (player == null) throw new ArgumentNullException(nameof(player));
            _players.Add(player.PlayerId, player);
            player.SetPaused("application-focus", !_focused);
            Action changed = () => UpdateAvailability(player);
            _policies.Add(player, changed); player.DevicePolicyChanged += changed; UpdateAvailability(player);
        }
        /// <summary>
        /// 绑定宿主给出的稳定设备标识，拒绝同设备同时属于两个玩家。
        /// </summary>
        public void Bind(string deviceId, SkyDeviceKind kind, string playerId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SkyInputRouter));
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("设备身份不能为空", nameof(deviceId));
            var player = _players[playerId];
            if (_bindings.TryGetValue(deviceId, out var known))
            {
                if (!ReferenceEquals(known.Player, player) || known.Kind != kind) throw new InvalidOperationException("设备已经绑定到其他玩家或类别");
                return;
            }
            _bindings.Add(deviceId, new Binding { Player = player, Kind = kind }); UpdateAvailability(player);
        }
        /// <summary>
        /// 解除设备分配；失去最后一个已启用设备时保留并暂停会话。
        /// </summary>
        public void Unbind(string deviceId)
        {
            if (!_bindings.TryGetValue(deviceId, out var binding)) return;
            _bindings.Remove(deviceId); UpdateAvailability(binding.Player);
        }
        /// <summary>
        /// 更新设备连接状态，保留原绑定供重连恢复。
        /// </summary>
        public void SetConnected(string deviceId, bool connected)
        {
            if (!_bindings.TryGetValue(deviceId, out var binding)) return;
            binding.Connected = connected; UpdateAvailability(binding.Player);
        }
        /// <summary>
        /// 路由一次动作；提供正递增序号时，同一设备的重复或晚到动作被忽略。
        /// </summary>
        public bool Route(SkyInputBackend backend, string deviceId, SkyControl control, bool extend = false, string text = "", long sequence = 0)
        {
            if (_disposed || backend != Backend || !_bindings.TryGetValue(deviceId, out var binding) || !binding.Connected || !Enabled(binding)) return false;
            if (sequence > 0) { if (sequence <= binding.LastSequence) return false; binding.LastSequence = sequence; }
            return binding.Player.Handle(control, extend, text);
        }
        /// <summary>
        /// 应用失焦保留所有会话；重新获得焦点后仍需满足设备条件。
        /// </summary>
        public void SetApplicationFocused(bool focused) { _focused = focused; foreach (var player in _players.Values) player.SetPaused("application-focus", !focused); }
        private static bool Enabled(Binding binding) => binding.Kind == SkyDeviceKind.Pointer || (binding.Kind == SkyDeviceKind.Keyboard ? binding.Player.KeyboardEnabled : binding.Player.GamepadEnabled);
        private void UpdateAvailability(SkyPlayerContext player) => player.SetPaused("devices", !_bindings.Values.Any(binding => ReferenceEquals(binding.Player, player) && binding.Connected && Enabled(binding)));
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var policy in _policies) { policy.Key.DevicePolicyChanged -= policy.Value; policy.Key.SetPaused("devices", true); }
            _policies.Clear(); _bindings.Clear(); _players.Clear();
        }
    }
}
