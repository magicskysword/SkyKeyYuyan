using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyKeyYuyan.UI.UGUI
{
    /// <summary>
    /// 保持正文原生焦点的面板功能按钮。
    /// </summary>
    public sealed class SkyPanelButton : Button
    {
        /// <inheritdoc>
        /// </inheritdoc>
        public override void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable()) DoStateTransition(SelectionState.Pressed, false);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void OnPointerUp(PointerEventData eventData) { if (IsActive()) DoStateTransition(SelectionState.Normal, false); }
    }
}
