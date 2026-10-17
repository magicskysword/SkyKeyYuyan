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
        /// <summary>
        /// 使用玩家默认正文输入配置。
        /// </summary>
        public bool UsePlayerTextInput = true;
        /// <summary>
        /// 该目标的正文输入类型。
        /// </summary>
        public SkyTextInputType InputType = SkyTextInputType.MultiLine;
        /// <summary>
        /// 多行正文的最低可见行数。
        /// </summary>
        [Range(1, 5)] public int MinimumVisibleLines = 3;
        /// <summary>
        /// 获取该目标的正文输入配置覆盖。
        /// </summary>
        public SkyTextInputOptions TextInput => UsePlayerTextInput ? null : new SkyTextInputOptions(InputType, MinimumVisibleLines);
        /// <summary>
        /// 使用玩家默认键盘配置。
        /// </summary>
        public bool UsePlayerKeyboardInput = true;
        /// <summary>
        /// 该目标的键盘输入法来源。
        /// </summary>
        public SkyKeyboardInputMode KeyboardMode = SkyKeyboardInputMode.Automatic;
        /// <summary>
        /// 键盘进入时显示完整虚拟键盘。
        /// </summary>
        public bool ForceVirtualKeyboard;
        /// <summary>
        /// 获取目标的键盘输入配置覆盖。
        /// </summary>
        public SkyKeyboardInputOptions KeyboardInput => UsePlayerKeyboardInput ? null : new SkyKeyboardInputOptions(KeyboardMode, ForceVirtualKeyboard, InitialLanguage);
    }
}
