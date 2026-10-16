using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyKeyYuyan.UI.UGUI
{
    /// <summary>
    /// 为独立草稿保留选区和光标显示的正文控件。
    /// </summary>
    public sealed class SkyDraftInputField : InputField
    {
        /// <summary>
        /// 会话活动期间允许多个草稿保持各自光标。
        /// </summary>
        public bool RetainEditing { get; set; }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void OnDeselect(BaseEventData eventData) { if (!RetainEditing) base.OnDeselect(eventData); }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void OnSelect(BaseEventData eventData) { if (!RetainEditing || !isFocused) base.OnSelect(eventData); }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void OnPointerClick(PointerEventData eventData) { if (!RetainEditing || !isFocused) base.OnPointerClick(eventData); }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void LateUpdate()
        {
            bool before = isFocused; int anchor = selectionAnchorPosition, caret = selectionFocusPosition;
            base.LateUpdate();
            if (RetainEditing && !before && isFocused)
            {
                // 原生首次激活会全选，草稿继续使用会话提供的初始选区。
                selectionAnchorPosition = anchor; selectionFocusPosition = caret; ForceLabelUpdate();
            }
        }
    }
}
