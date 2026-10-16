using System;

namespace SkyKeyYuyan.Core
{
    /// <summary>
    /// 在 UTF-16 偏移和完整 Unicode 标量边界之间转换。
    /// </summary>
    public static class TextBoundary
    {
        /// <summary>
        /// 夹紧偏移，并在代理对内部向前对齐。
        /// </summary>
        public static int Floor(string text, int offset)
        {
            int value = Math.Max(0, Math.Min(text.Length, offset));
            return value > 0 && value < text.Length && char.IsHighSurrogate(text[value - 1]) && char.IsLowSurrogate(text[value]) ? value - 1 : value;
        }

        /// <summary>
        /// 夹紧偏移，并在代理对内部向后对齐。
        /// </summary>
        public static int Ceiling(string text, int offset)
        {
            int value = Math.Max(0, Math.Min(text.Length, offset));
            return value > 0 && value < text.Length && char.IsHighSurrogate(text[value - 1]) && char.IsLowSurrogate(text[value]) ? value + 1 : value;
        }

        /// <summary>
        /// 获取前一个完整字符边界。
        /// </summary>
        public static int Previous(string text, int offset) => Floor(text, Math.Max(0, offset - 1));

        /// <summary>
        /// 获取后一个完整字符边界。
        /// </summary>
        public static int Next(string text, int offset) => Ceiling(text, Math.Min(text.Length, offset + 1));

        /// <summary>
        /// 在 UTF-16 容量内截取完整字符前缀。
        /// </summary>
        public static string Prefix(string text, int capacity) => text.Substring(0, Floor(text, Math.Max(0, capacity)));
    }
}
