using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SkyKeyYuyan.Unity.CanvasUI
{
    /// <summary>
    /// 记录原输入框的指针焦点请求。
    /// </summary>
    public sealed class SkyKeyboardFocusObserver : MonoBehaviour, IPointerDownHandler
    {
        /// <summary>
        /// 用户点入原输入框。
        /// </summary>
        public event Action Requested;
        /// <inheritdoc>
        /// </inheritdoc>
        public void OnPointerDown(PointerEventData eventData)
        { if (eventData.button == PointerEventData.InputButton.Left) Requested?.Invoke(); }
    }
}
