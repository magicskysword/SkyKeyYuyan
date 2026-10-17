using System;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity.CanvasUI;
using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.Unity.UGUI
{
    internal sealed class UguiKeyboardView : IDisposable
    {
        private readonly ISkySession _session;
        private readonly InputField _field;
        private readonly UguiCompositionView _composition;
        private readonly SkyCanvasCandidates _candidates;
        internal UguiKeyboardView(ISkySession session, InputField field)
        {
            _session = session; _field = field; _composition = new UguiCompositionView(field);
            _candidates = new SkyCanvasCandidates(session, (RectTransform)field.transform, CreateLabel);
            session.StateChanged += Render; Render(session.Snapshot);
            Canvas.willRenderCanvases += Refresh;
        }
        private void Refresh() { Render(_session.Snapshot); }
        private SkyCanvasCandidateLabel CreateLabel(Transform parent)
        {
            var owner = new GameObject("Label", typeof(RectTransform), typeof(Text)); owner.transform.SetParent(parent, false);
            var rect = (RectTransform)owner.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(8, 2); rect.offsetMax = new Vector2(-8, -2);
            var text = owner.GetComponent<Text>(); text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.supportRichText = false;
            return new SkyCanvasCandidateLabel((value, selected) =>
            {
                text.font = _field.textComponent.font; text.fontSize = Mathf.Clamp(_field.textComponent.fontSize, 14, 24);
                text.text = value; text.color = selected ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Text;
            }, () => text.preferredWidth);
        }
        private void Render(SessionSnapshot snapshot)
        {
            if (_field == null) return;
            _composition.Render(new SkyCompositionText(snapshot));
            _candidates.Render(snapshot, _composition.WorldCaret, _composition.LineHeight);
        }
        public void Dispose() { Canvas.willRenderCanvases -= Refresh; _session.StateChanged -= Render; _candidates.Dispose(); _composition.Dispose(); }
    }
}
