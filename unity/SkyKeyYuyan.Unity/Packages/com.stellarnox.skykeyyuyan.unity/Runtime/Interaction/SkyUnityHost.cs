using System;
using System.Collections.Generic;
using System.Linq;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 协调玩家、目标发现、后端和 Unity 生命周期。
    /// </summary>
    public sealed class SkyUnityHost : IDisposable
    {
        private sealed class Player
        {
            internal SkyPlayerContext Context;
            internal ISkyTargetDiscovery[] Sources;
            internal readonly List<SkyTargetBinding> Manual = new List<SkyTargetBinding>();
            internal SkyTargetBinding[] Discovered = Array.Empty<SkyTargetBinding>();
            internal readonly Dictionary<object, bool> Editing = new Dictionary<object, bool>();
            internal SkyTargetBinding Pending;
        }
        private readonly Dictionary<string, Player> _players = new Dictionary<string, Player>(StringComparer.Ordinal);
        private ISkyInputDriver _driver;
        private double _nextScan;
        private bool _disposed;
        private bool _focused = true, _applicationPaused, _suspended;
        /// <summary>
        /// 固定该宿主的唯一设备后端。
        /// </summary>
        public SkyUnityHost(SkyInputBackend backend) { Router = new SkyInputRouter(backend); }
        /// <summary>
        /// 玩家设备路由器。
        /// </summary>
        public SkyInputRouter Router { get; }
        /// <summary>
        /// 新会话使用的显式运行时；为空时使用全局默认实例。
        /// </summary>
        public ISkyRuntime Runtime { get; set; }
        /// <summary>
        /// 动态 UI 扫描间隔，单位秒。
        /// </summary>
        public double DiscoveryInterval { get; set; } = 0.25;
        /// <summary>
        /// 自动打开输入等宿主阶段的失败结果。
        /// </summary>
        public event Action<ServiceResult> Diagnostic;
        /// <summary>
        /// 由宿主持有并推进一个后端，替换时释放旧后端。
        /// </summary>
        public void SetDriver(ISkyInputDriver driver)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SkyUnityHost));
            if (ReferenceEquals(_driver, driver)) return;
            _driver?.Dispose(); _driver = driver;
        }
        /// <summary>
        /// 创建玩家并接管发现器的生命周期。
        /// </summary>
        public SkyPlayerContext AddPlayer(SkyPlayerOptions options, params ISkyTargetDiscovery[] sources)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SkyUnityHost));
            var context = new SkyPlayerContext(options);
            if (_players.ContainsKey(context.PlayerId)) { context.Dispose(); throw new ArgumentException("玩家身份重复", nameof(options)); }
            var entry = new Player { Context = context, Sources = sources?.ToArray() ?? Array.Empty<ISkyTargetDiscovery>() };
            _players.Add(context.PlayerId, entry); Router.Register(context); context.SetPaused("host", _suspended); _nextScan = 0; return context;
        }
        /// <summary>
        /// 显式注册由宿主持有的特殊目标。
        /// </summary>
        public void RegisterTarget(string playerId, SkyTargetBinding target) { _players[playerId].Manual.Add(target ?? throw new ArgumentNullException(nameof(target))); }
        /// <summary>
        /// 解除特殊目标注册并取消其活动会话。
        /// </summary>
        public void UnregisterTarget(string playerId, IUnityTextTarget target)
        {
            var player = _players[playerId];
            player.Manual.RemoveAll(binding => ReferenceEquals(binding.Target.Identity, target.Identity));
            if (ReferenceEquals(player.Context.Target?.Identity, target.Identity)) player.Context.Handle(SkyControl.Cancel);
        }
        /// <summary>
        /// 扫描动态目标、同步生命周期并推进一次输入后端。
        /// </summary>
        public void Tick(double time)
        {
            if (_disposed || _suspended) return;
            var players = _players.Values.ToArray();
            if (time >= _nextScan)
            {
                _nextScan = time + Math.Max(0.01, DiscoveryInterval);
                foreach (var player in players)
                {
                    var discovered = new List<SkyTargetBinding>();
                    foreach (var source in player.Sources)
                        try { discovered.AddRange(source.Scan()); }
                        catch (Exception ex) { Report(new ServiceResult(false, "target-discovery:" + player.Context.PlayerId, ex.Message)); }
                    player.Discovered = discovered.ToArray();
                }
            }
            foreach (var player in players)
            {
                player.Context.Poll();
                var bindings = player.Manual.Concat(player.Discovered).GroupBy(binding => binding.Target.Identity).Select(group => group.First()).ToArray();
                var identities = new HashSet<object>();
                foreach (var binding in bindings)
                {
                    var target = binding.Target; identities.Add(target.Identity); target.Poll();
                    bool editing = player.Context.FollowEditing && target.IsAlive && Router.HasEnabledDevice(player.Context.PlayerId) && target.IsEditing;
                    bool before = player.Editing.TryGetValue(target.Identity, out var was) && was;
                    player.Editing[target.Identity] = editing;
                    if (editing && !before && !ReferenceEquals(player.Context.Target?.Identity, target.Identity))
                    {
                        player.Pending = binding;
                        if (player.Context.Session != null) player.Context.Handle(SkyControl.Cancel);
                    }
                }
                foreach (var old in player.Editing.Keys.Where(identity => !identities.Contains(identity)).ToArray()) player.Editing.Remove(old);
                if (player.Pending != null && player.Context.Session == null)
                {
                    var target = player.Pending; player.Pending = null;
                    if (target.Target.IsAlive && target.Target.IsEditing && Router.HasEnabledDevice(player.Context.PlayerId))
                    {
                        var result = player.Context.Open(target.Target, Runtime, target.Mode, target.InitialLanguage, target.TextInput);
                        if (!result.Succeeded) Report(result);
                    }
                }
            }
            _driver?.Poll(time);
        }
        /// <summary>
        /// 映射应用前后台状态。
        /// </summary>
        public void SetApplicationFocused(bool focused) { _focused = focused; Router.SetApplicationFocused(_focused && !_applicationPaused); }
        /// <summary>
        /// 应用暂停独立于焦点状态。
        /// </summary>
        public void SetApplicationPaused(bool paused) { _applicationPaused = paused; Router.SetApplicationFocused(_focused && !_applicationPaused); }
        /// <summary>
        /// 宿主暂时停止推进时保留并暂停所有玩家。
        /// </summary>
        public void SetSuspended(bool suspended) { _suspended = suspended; foreach (var player in _players.Values) player.Context.SetPaused("host", suspended); }
        /// <summary>
        /// 释放一位玩家的会话与发现器。
        /// </summary>
        public void RemovePlayer(string playerId)
        {
            if (!_players.TryGetValue(playerId, out var player)) return;
            Router.Unregister(playerId); _players.Remove(playerId); player.Context.Dispose();
            foreach (var source in player.Sources) source.Dispose();
        }
        private void Report(ServiceResult result)
        {
            if (Diagnostic == null) { Debug.LogWarning("SKY " + result.Stage + ": " + result.Reason); return; }
            foreach (Action<ServiceResult> handler in Diagnostic.GetInvocationList()) try { handler(result); } catch { }
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _driver?.Dispose(); _driver = null;
            foreach (string id in _players.Keys.ToArray()) RemovePlayer(id);
            Router.Dispose();
        }
    }
}
