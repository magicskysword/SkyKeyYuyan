using System;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 默认面板的正文输入类型。
    /// </summary>
    public enum SkyTextInputType
    {
        /// <summary>
        /// 单行正文。
        /// </summary>
        SingleLine,
        /// <summary>
        /// 可换行的正文。
        /// </summary>
        MultiLine
    }
    /// <summary>
    /// 每次输入使用的不可变正文视口配置。
    /// </summary>
    public sealed class SkyTextInputOptions
    {
        /// <summary>
        /// 创建单行或多行输入，多行最低可见行数限制在一至五行。
        /// </summary>
        public SkyTextInputOptions(SkyTextInputType type = SkyTextInputType.MultiLine, int minimumVisibleLines = 3)
        {
            if (type != SkyTextInputType.SingleLine && type != SkyTextInputType.MultiLine) throw new ArgumentOutOfRangeException(nameof(type));
            Type = type; MinimumVisibleLines = type == SkyTextInputType.SingleLine ? 1 : Mathf.Clamp(minimumVisibleLines, 1, 5);
        }
        /// <summary>
        /// 正文输入类型。
        /// </summary>
        public SkyTextInputType Type { get; }
        /// <summary>
        /// 正文不足时仍保留的可见行数。
        /// </summary>
        public int MinimumVisibleLines { get; }
    }
}
