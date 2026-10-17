using System;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 物理键盘使用的输入法来源。
    /// </summary>
    public enum SkyKeyboardInputMode
    {
        /// <summary>
        /// 优先使用系统输入法，宿主不支持时使用 SKY。
        /// </summary>
        [UnityEngine.InspectorName("自动")] Automatic,
        /// <summary>
        /// 使用宿主控件的系统输入法。
        /// </summary>
        [UnityEngine.InspectorName("使用系统 IME")] SystemIme,
        /// <summary>
        /// 使用 SKY 语言引擎。
        /// </summary>
        [UnityEngine.InspectorName("使用 SKY IME")] SkyIme
    }
    /// <summary>
    /// 一次键盘输入的模式、面板及默认语言配置。
    /// </summary>
    public sealed class SkyKeyboardInputOptions
    {
        /// <summary>
        /// 配置键盘输入；默认优先使用系统输入法并直接编辑目标。
        /// </summary>
        public SkyKeyboardInputOptions(SkyKeyboardInputMode mode = SkyKeyboardInputMode.Automatic, bool forceVirtualKeyboard = false, string initialLanguage = null)
        { Mode = mode; ForceVirtualKeyboard = forceVirtualKeyboard; InitialLanguage = initialLanguage; }
        /// <summary>
        /// 输入法来源。
        /// </summary>
        public SkyKeyboardInputMode Mode { get; }
        /// <summary>
        /// 键盘聚焦时是否显示完整虚拟键盘。
        /// </summary>
        public bool ForceVirtualKeyboard { get; }
        /// <summary>
        /// SKY 会话的默认语言，为空时沿用玩家配置。
        /// </summary>
        public string InitialLanguage { get; }
        /// <summary>
        /// 根据宿主提供的系统输入能力解析自动模式。
        /// </summary>
        public SkyKeyboardInputMode Resolve(bool systemImeSupported) => Mode == SkyKeyboardInputMode.Automatic
            ? systemImeSupported ? SkyKeyboardInputMode.SystemIme : SkyKeyboardInputMode.SkyIme : Mode;
        /// <summary>
        /// Unity 当前平台的内置桌面 IME 支持。
        /// </summary>
        public static bool PlatformSupportsSystemIme
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX
                return true;
#elif UNITY_STANDALONE_LINUX && UNITY_6000_5_OR_NEWER
                return true;
#else
                return false;
#endif
            }
        }
    }
    /// <summary>
    /// 可在原输入框旁显示 SKY 组字与候选的可选目标能力。
    /// </summary>
    public interface ISkyInlineKeyboardTarget
    {
        /// <summary>
        /// 原控件的单行或多行类型。
        /// </summary>
        SkyTextInputType KeyboardTextType { get; }
        /// <summary>
        /// 恢复从草稿交接到原框时的 UTF-16 选区。
        /// </summary>
        void RestoreKeyboardSelection(int anchor, int caret);
        /// <summary>
        /// 取出用户通过指针重新进入原输入框的请求。
        /// </summary>
        bool TakeKeyboardFocusRequest();
        /// <summary>
        /// 创建与当前会话同生命周期的行内显示。
        /// </summary>
        IDisposable CreateKeyboardView(ISkySession session);
    }
    /// <summary>
    /// 识别独立点按 Shift，排除长按及组合快捷键。
    /// </summary>
    public sealed class SkyShiftTap
    {
        private bool _held, _used;
        private double _pressed;
        /// <summary>
        /// 返回本次释放是否为独立点按。
        /// </summary>
        public bool Update(bool shift, bool otherKey, double time)
        {
            if (shift && !_held) { _pressed = time; _used = false; }
            _used |= otherKey;
            bool tapped = _held && !shift && !_used && time - _pressed <= .35;
            _held = shift; return tapped;
        }
        /// <summary>
        /// 输入接管改变时释放记录，已经按住的 Shift 等待松开。
        /// </summary>
        public void Reset(bool shift = false) { _held = shift; _used = shift; }
    }
}
