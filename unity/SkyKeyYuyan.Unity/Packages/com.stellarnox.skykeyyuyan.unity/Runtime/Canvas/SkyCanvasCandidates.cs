using System;
using System.Collections.Generic;
using SkyKeyYuyan.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyKeyYuyan.Unity.CanvasUI
{
    /// <summary>
    /// 由原生文字组件提供的候选文字与尺寸。
    /// </summary>
    public sealed class SkyCanvasCandidateLabel
    {
        internal readonly Action<string, bool> Render;
        internal readonly Func<float> Width;
        /// <summary>
        /// 绑定文字渲染与宽度测量。
        /// </summary>
        public SkyCanvasCandidateLabel(Action<string, bool> render, Func<float> width) { Render = render; Width = width; }
    }
    /// <summary>
    /// 显示在原输入框光标旁的 Canvas 候选浮窗。
    /// </summary>
    public sealed class SkyCanvasCandidates : IDisposable
    {
        private sealed class Item
        {
            internal SkyInlineCandidateButton Button;
            internal Image Image;
            internal SkyCanvasCandidateLabel Label;
            internal float Width;
        }
        private readonly ISkySession _session;
        private readonly RectTransform _canvas, _popup;
        private readonly Func<Transform, SkyCanvasCandidateLabel> _createLabel;
        private readonly List<Item> _items = new List<Item>();
        /// <summary>
        /// 创建使用原控件文字样式的候选浮窗。
        /// </summary>
        public SkyCanvasCandidates(ISkySession session, RectTransform target, Func<Transform, SkyCanvasCandidateLabel> createLabel)
        {
            _session = session; _createLabel = createLabel;
            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            _canvas = (RectTransform)canvas.rootCanvas.transform;
            var owner = new GameObject("SkyInlineCandidates", typeof(RectTransform), typeof(Image), typeof(SkyInputSurface));
            _popup = (RectTransform)owner.transform; _popup.SetParent(_canvas, false);
            _popup.anchorMin = _popup.anchorMax = _canvas.pivot; _popup.pivot = new Vector2(0, 1);
            owner.GetComponent<Image>().color = SkyKeyboardStyle.Inset; owner.SetActive(false);
        }
        /// <summary>
        /// 按当前查询显示候选，并保持浮窗位于 Canvas 内。
        /// </summary>
        public void Render(SessionSnapshot snapshot, Vector3 worldCaret, float lineHeight)
        {
            if (_popup == null) return;
            var page = snapshot.Candidates; int count = page?.Candidates.Count ?? 0;
            _popup.gameObject.SetActive(count > 0 && !snapshot.Querying && !snapshot.Composition.IsEmpty);
            if (!_popup.gameObject.activeSelf) return;
            float budget = Mathf.Max(40, Mathf.Min(860, _canvas.rect.width - 24));
            while (_items.Count < count)
            {
                var owner = new GameObject("Candidate" + _items.Count, typeof(RectTransform), typeof(Image), typeof(SkyInlineCandidateButton));
                var rect = (RectTransform)owner.transform; rect.SetParent(_popup, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
                var button = owner.GetComponent<SkyInlineCandidateButton>(); button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.targetGraphic = owner.GetComponent<Image>();
                _items.Add(new Item { Button = button, Image = owner.GetComponent<Image>(), Label = _createLabel(owner.transform) });
            }
            int selected = Mathf.Clamp(snapshot.SelectedCandidate, 0, count - 1);
            for (int index = 0; index < count; index++)
            {
                var item = _items[index]; var candidate = page.Candidates[index];
                item.Label.Render((index + 1) + " " + candidate.Text, index == selected);
                item.Width = Mathf.Min(budget, Mathf.Max(52, item.Label.Width() + 20));
                long query = page.Query.Id; string id = candidate.Id;
                item.Button.Click = () => _session.Process(InputOperation.ConfirmCandidate(query, id));
                item.Button.interactable = snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution;
            }
            int first = selected, end = selected + 1; float width = _items[selected].Width;
            while (first > 0 && width + _items[first - 1].Width + 6 <= budget) width += _items[--first].Width + 6;
            while (end < count && width + _items[end].Width + 6 <= budget) width += _items[end++].Width + 6;
            float x = 6;
            for (int index = 0; index < _items.Count; index++)
            {
                var item = _items[index]; bool visible = index >= first && index < end;
                item.Button.gameObject.SetActive(visible); if (!visible) continue;
                var rect = (RectTransform)item.Button.transform; rect.anchoredPosition = new Vector2(x, -6); rect.sizeDelta = new Vector2(item.Width, 44);
                item.Image.color = index == selected ? SkyKeyboardStyle.Active : SkyKeyboardStyle.Function; x += item.Width + 6;
            }
            float height = 56;
            var point = _canvas.InverseTransformPoint(worldCaret);
            float y = point.y - 4;
            if (y - height < _canvas.rect.yMin + 8) y = point.y + lineHeight + height + 4;
            _popup.sizeDelta = new Vector2(x, height);
            _popup.anchoredPosition = new Vector2(Mathf.Clamp(point.x, _canvas.rect.xMin + 8, Mathf.Max(_canvas.rect.xMin + 8, _canvas.rect.xMax - x - 8)), Mathf.Clamp(y, _canvas.rect.yMin + height + 8, _canvas.rect.yMax - 8));
            _popup.SetAsLastSibling();
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        { if (_popup != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_popup.gameObject); else UnityEngine.Object.DestroyImmediate(_popup.gameObject); } }
    }
    internal sealed class SkyInlineCandidateButton : Button
    {
        internal Action Click;
        private Action _pressed;
        public override void OnPointerDown(PointerEventData eventData)
        { if (eventData.button == PointerEventData.InputButton.Left && IsInteractable()) { _pressed = Click; DoStateTransition(SelectionState.Pressed, false); } }
        public override void OnPointerUp(PointerEventData eventData) { if (IsActive()) DoStateTransition(SelectionState.Normal, false); }
        public override void OnPointerClick(PointerEventData eventData)
        { if (eventData.button == PointerEventData.InputButton.Left && IsInteractable()) { var click = _pressed ?? Click; _pressed = null; click?.Invoke(); } }
    }
}
