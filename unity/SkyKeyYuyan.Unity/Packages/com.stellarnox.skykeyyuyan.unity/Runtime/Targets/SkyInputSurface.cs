using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 标记 SKY 面板内部的控件根，原生目标发现跳过其草稿和功能控件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyInputSurface : MonoBehaviour
    {
        /// <summary>
        /// 检查完整父级链，包含未激活的面板。
        /// </summary>
        public static bool Contains(Transform node)
        {
            while (node != null) { if (node.GetComponent<SkyInputSurface>() != null) return true; node = node.parent; }
            return false;
        }
    }
}
