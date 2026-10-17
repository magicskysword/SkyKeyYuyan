using System;
using SkyKeyYuyan.Core;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 将临时组字投影到正文选区，提供表现层使用的文字与范围。
    /// </summary>
    public sealed class SkyCompositionText
    {
        /// <summary>
        /// 根据会话快照创建独立于正文目标的显示内容。
        /// </summary>
        public SkyCompositionText(SessionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Active = !snapshot.Composition.IsEmpty;
            Start = Active ? snapshot.Text.SelectionStart : snapshot.Text.Caret;
            Length = Active ? snapshot.Composition.DisplayText.Length : 0;
            UnconvertedStart = Start + (Active ? Math.Min(snapshot.Composition.SelectedText.Length, Length) : 0);
            Text = Active ? snapshot.Text.Text.Remove(Start, snapshot.Text.SelectionLength).Insert(Start, snapshot.Composition.DisplayText) : snapshot.Text.Text;
        }
        /// <summary>
        /// 当前是否显示临时组字。
        /// </summary>
        public bool Active { get; }
        /// <summary>
        /// 包含临时组字的完整显示内容。
        /// </summary>
        public string Text { get; }
        /// <summary>
        /// 组字起点的 UTF-16 偏移。
        /// </summary>
        public int Start { get; }
        /// <summary>
        /// 临时组字的 UTF-16 长度。
        /// </summary>
        public int Length { get; }
        /// <summary>
        /// 尚未转换片段在显示正文中的 UTF-16 起点。
        /// </summary>
        public int UnconvertedStart { get; }
        /// <summary>
        /// 临时显示光标的 UTF-16 偏移。
        /// </summary>
        public int Caret => Start + Length;
    }
}
