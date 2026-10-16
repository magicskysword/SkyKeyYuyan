using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 两套默认表现共用的尺寸、色彩与键帽几何。
    /// </summary>
    public static class SkyKeyboardStyle
    {
        public const float Width = 1120, Height = 700, ContentWidth = 1072, KeyboardHeight = 336;
        public static readonly Color32 Panel = new Color32(30, 37, 44, 252), Inset = new Color32(23, 30, 37, 255);
        public static readonly Color32 Key = new Color32(54, 63, 74, 255), Function = new Color32(44, 53, 63, 255);
        public static readonly Color32 Border = new Color32(79, 91, 103, 255), Accent = new Color32(100, 230, 204, 255);
        public static readonly Color32 Primary = new Color32(76, 196, 172, 255), Active = new Color32(44, 88, 81, 255);
        public static readonly Color32 Text = new Color32(236, 242, 246, 255), Muted = new Color32(155, 171, 184, 255), DarkText = new Color32(9, 31, 29, 255);
        /// <summary>
        /// 将逻辑列与键帽宽度映射为默认键盘区域内的矩形。
        /// </summary>
        public static Rect KeyRect(KeyboardLayout layout, KeyboardKey key)
        {
            float unit = ContentWidth / layout.ColumnCount, pitch = Mathf.Min(56, KeyboardHeight / layout.RowCount);
            return new Rect(key.Column * unit, key.Row * pitch, Mathf.Max(1, key.ColumnSpan * unit - 8), Mathf.Max(1, pitch - 8));
        }
        /// <summary>
        /// 取得普通、功能、激活或焦点键的填色。
        /// </summary>
        public static Color32 KeyColor(KeyboardKey key, bool focused, bool active)
        { return focused ? Accent : key.Action == "finish" ? Primary : active ? Active : key.Action == "input" && key.Output != " " ? Key : Function; }
        /// <summary>
        /// 取得输入状态下的键帽标签。
        /// </summary>
        public static string KeyLabel(KeyboardKey key, bool shift, bool completion)
        {
            if (key.Action == "shift") return shift ? "Shift ↑" : key.Label;
            if (key.Action == "completion") return completion ? "补全 开" : "补全 关";
            return key.Action == "input" && key.Output != " " && shift ? key.Alternate : key.Label;
        }
    }
}
