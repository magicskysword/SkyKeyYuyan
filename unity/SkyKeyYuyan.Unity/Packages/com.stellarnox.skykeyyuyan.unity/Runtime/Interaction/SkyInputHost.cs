using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 将 Unity 主循环、失焦和场景销毁连接到输入宿主。
    /// </summary>
    [DefaultExecutionOrder(-31000)]
    public sealed class SkyInputHost : MonoBehaviour
    {
        /// <summary>
        /// 当前宿主；通过 Configure 显式选择后端。
        /// </summary>
        public SkyUnityHost Host { get; private set; }
        /// <summary>
        /// 创建并持有新的宿主，释放先前配置。
        /// </summary>
        public SkyUnityHost Configure(SkyInputBackend backend)
        {
            Host?.Dispose(); Host = new SkyUnityHost(backend); Host.SetApplicationFocused(Application.isFocused); Host.SetSuspended(!isActiveAndEnabled); return Host;
        }
        private void Update() { Host?.Tick(Time.unscaledTime); }
        private void OnApplicationFocus(bool focused) { Host?.SetApplicationFocused(focused); }
        private void OnApplicationPause(bool paused) { Host?.SetApplicationPaused(paused); }
        private void OnDisable() { Host?.SetSuspended(true); }
        private void OnEnable() { Host?.SetSuspended(false); Host?.SetApplicationFocused(Application.isFocused); }
        private void OnDestroy() { Host?.Dispose(); Host = null; }
    }
}
