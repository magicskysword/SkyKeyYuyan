using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 在原生事件系统执行前应用 SKY 的输入接管策略。
    /// </summary>
    [DefaultExecutionOrder(-30000)]
    [AddComponentMenu("")]
    public sealed class SkyNativeInputPump : MonoBehaviour
    {
        private void Update() => SkyNativeInputCoordination.Pump();
    }
}
