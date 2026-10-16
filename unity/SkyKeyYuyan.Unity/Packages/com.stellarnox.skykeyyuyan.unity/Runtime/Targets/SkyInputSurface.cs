using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 标记 SKY 面板内部的控件根，原生目标发现跳过其草稿和功能控件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyInputSurface : MonoBehaviour { }
}
