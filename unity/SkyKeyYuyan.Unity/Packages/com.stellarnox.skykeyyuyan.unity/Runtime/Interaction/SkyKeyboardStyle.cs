using System.Collections.Generic;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 两套默认表现共用的尺寸、色彩与键帽几何。
    /// </summary>
    public static class SkyKeyboardStyle
    {
        public const float Width = 1120, Height = 624, ContentWidth = 1072, KeyboardHeight = 336;
        public const float CandidateStripWidth = 860, CandidateGap = 8;
        public const float CandidatePopupHeight = 68;
        public const float BodyLineHeight = 36;
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
        /// <summary>
        /// 根据字体测得的文字宽度保留两侧留白，并限制在候选区内。
        /// </summary>
        public static float CandidateWidth(float textWidth) => Mathf.Clamp(Mathf.Ceil(textWidth) + 24, 48, CandidateStripWidth);
        /// <summary>
        /// 将候选浮窗放到光标所在行下方，空间不足时翻到上方并限制在面板内。
        /// </summary>
        public static Rect CandidatePopupRect(Vector2 caret, float lineHeight, float width, float keyboardTop)
        {
            width = Mathf.Clamp(width, 1, ContentWidth);
            float bottom = keyboardTop - 8, y = caret.y + lineHeight + 6;
            if (y + CandidatePopupHeight > bottom) y = caret.y - CandidatePopupHeight - 6;
            y = Mathf.Clamp(y, 56, Mathf.Max(56, bottom - CandidatePopupHeight));
            return new Rect(Mathf.Clamp(caret.x, 24, Width - 24 - width), y, width, CandidatePopupHeight);
        }
        /// <summary>
        /// 按最低行数及当前内容计算正文视口高度，超出五行的内容由原生控件滚动。
        /// </summary>
        public static float BodyHeight(SkyTextInputOptions options, float preferredTextHeight)
        {
            int lines = options.Type == SkyTextInputType.SingleLine ? 1 : Mathf.Clamp(Mathf.CeilToInt(preferredTextHeight / BodyLineHeight), options.MinimumVisibleLines, 5);
            return lines * BodyLineHeight + 16;
        }
        /// <summary>
        /// 取得包含选中项且所有按钮均完整位于候选区的连续区间。
        /// </summary>
        public static void CandidateRange(IReadOnlyList<float> widths, int selected, out int first, out int end)
        {
            first = end = 0;
            if (widths.Count == 0) return;
            selected = Mathf.Clamp(selected, 0, widths.Count - 1);
            float used = 0;
            for (int index = 0; index <= selected; index++)
            {
                used += widths[index] + CandidateGap;
                while (first < index && used - CandidateGap > CandidateStripWidth)
                    used -= widths[first++] + CandidateGap;
            }
            used = 0;
            for (end = first; end < widths.Count; end++)
            {
                float next = widths[end] + (end == first ? 0 : CandidateGap);
                if (used + next > CandidateStripWidth) break;
                used += next;
            }
        }
    }
}
