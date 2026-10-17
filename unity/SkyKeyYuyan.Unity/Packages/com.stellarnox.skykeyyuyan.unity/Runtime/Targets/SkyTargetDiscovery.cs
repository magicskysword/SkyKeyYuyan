using System;
using System.Collections.Generic;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 带目标默认模式和语言的文本框注册。
    /// </summary>
    public sealed class SkyTargetBinding
    {
        /// <summary>
        /// 绑定文本框，未指定的配置沿用玩家默认值。
        /// </summary>
        public SkyTargetBinding(IUnityTextTarget target, EditingMode? mode = null, string language = null, SkyTextInputOptions textInput = null)
        { Target = target ?? throw new ArgumentNullException(nameof(target)); Mode = mode; InitialLanguage = language; TextInput = textInput; }
        /// <summary>
        /// 原生文本目标。
        /// </summary>
        public IUnityTextTarget Target { get; }
        /// <summary>
        /// 目标编辑模式覆盖。
        /// </summary>
        public EditingMode? Mode { get; set; }
        /// <summary>
        /// 目标初始语言覆盖。
        /// </summary>
        public string InitialLanguage { get; set; }
        /// <summary>
        /// 该目标的正文输入配置覆盖。
        /// </summary>
        public SkyTextInputOptions TextInput { get; set; }
    }
    /// <summary>
    /// 在宿主指定的玩家 UI 根内发现和持有文本适配器。
    /// </summary>
    public interface ISkyTargetDiscovery : IDisposable
    {
        /// <summary>
        /// 更新动态目标列表，移除的目标由发现器释放。
        /// </summary>
        IReadOnlyList<SkyTargetBinding> Scan();
    }
}
