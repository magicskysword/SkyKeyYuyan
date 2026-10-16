using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 将后台语言计算和存档完成事件调度回 Unity 主线程。
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-32000)]
    [AddComponentMenu("")]
    public sealed class SkyUnityDispatcher : MonoBehaviour
    {
        private void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += SkyUnityServices.Pump;
#endif
        }
        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= SkyUnityServices.Pump;
#endif
        }
        private void Update() => SkyUnityServices.Pump();
        private void OnApplicationQuit() => SkyUnityServices.SaveBeforeExit();
    }
}
