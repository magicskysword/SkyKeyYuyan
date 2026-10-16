using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 覆盖一个原生文本框的输入默认值。
    /// </summary>
    public sealed class SkyTextTargetSettings : MonoBehaviour
    {
        /// <summary>
        /// 使用玩家默认编辑模式。
        /// </summary>
        public bool UsePlayerEditingMode = true;
        /// <summary>
        /// 该目标的显式编辑模式。
        /// </summary>
        public EditingMode EditingMode = EditingMode.Draft;
        /// <summary>
        /// 初始语言，为空时沿用玩家最近使用的语言。
        /// </summary>
        public string InitialLanguage;
    }
}
