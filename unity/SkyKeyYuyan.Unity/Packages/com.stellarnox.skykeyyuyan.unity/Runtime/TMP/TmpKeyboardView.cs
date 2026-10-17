using System;
using System.Collections.Generic;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity.CanvasUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.Unity.TMP
{
    internal sealed class TmpKeyboardView : IDisposable
    {
        private readonly TMP_InputField _field;
        private readonly ISkySession _session;
        private readonly TextMeshProUGUI _preview;
        private readonly SkyCanvasCandidates _candidates;
        private readonly List<Image> _marks = new List<Image>();
        private readonly Image _caret;
        private bool _active;
        private float _alpha;
        private Color _caretColor, _selectionColor;
        internal TmpKeyboardView(ISkySession session, TMP_InputField field)
        {
            _session = session; _field = field;
            var owner = new GameObject("SkyCompositionPreview", typeof(RectTransform), typeof(TextMeshProUGUI));
            owner.transform.SetParent(field.textComponent.transform.parent, false); _preview = owner.GetComponent<TextMeshProUGUI>();
            _preview.raycastTarget = false; _preview.richText = false; _caret = Mark(); owner.SetActive(false);
            _candidates = new SkyCanvasCandidates(session, (RectTransform)field.transform, CreateLabel);
            session.StateChanged += Render; Render(session.Snapshot);
            Canvas.willRenderCanvases += Refresh;
        }
        private void Refresh() { Render(_session.Snapshot); }
        private SkyCanvasCandidateLabel CreateLabel(Transform parent)
        {
            var owner = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); owner.transform.SetParent(parent, false);
            var text = owner.GetComponent<TextMeshProUGUI>(); text.raycastTarget = false; text.richText = false; text.alignment = TextAlignmentOptions.Center;
            var rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(8, 2); rect.offsetMax = new Vector2(-8, -2);
            return new SkyCanvasCandidateLabel((value, selected) =>
            {
                text.font = _field.textComponent.font; text.fontSize = Mathf.Clamp(_field.textComponent.fontSize, 14, 24);
                text.text = value; text.color = selected ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Text;
            }, () => text.preferredWidth);
        }
        private Image Mark()
        {
            var owner = new GameObject("CompositionMark", typeof(RectTransform), typeof(Image)); owner.transform.SetParent(_preview.transform, false);
            var mark = owner.GetComponent<Image>(); mark.raycastTarget = false; mark.color = SkyKeyboardStyle.Accent; return mark;
        }
        private int Character(int offset)
        {
            var info = _preview.textInfo;
            for (int index = 0; index < info.characterCount; index++) if (info.characterInfo[index].index >= offset) return index;
            return info.characterCount;
        }
        private void Render(SessionSnapshot snapshot)
        {
            if (_field == null) return;
            var composition = new SkyCompositionText(snapshot); var native = _field.textComponent;
            if (_active != composition.Active)
            {
                _active = composition.Active;
                if (_active) { _alpha = native.alpha; _caretColor = _field.caretColor; _selectionColor = _field.selectionColor; native.alpha = 0; _field.caretColor = _field.selectionColor = Color.clear; }
                else Restore();
                _preview.gameObject.SetActive(_active);
            }
            if (!_active) { _candidates.Render(snapshot, Vector3.zero, 0); return; }
            var rect = _preview.rectTransform; var original = native.rectTransform;
            rect.anchorMin = original.anchorMin; rect.anchorMax = original.anchorMax; rect.pivot = original.pivot;
            rect.sizeDelta = original.sizeDelta; rect.anchoredPosition = original.anchoredPosition;
            _preview.font = native.font; _preview.fontSharedMaterial = native.fontSharedMaterial; _preview.fontSize = native.fontSize;
            _preview.fontStyle = native.fontStyle; _preview.alignment = native.alignment; _preview.margin = native.margin;
            _preview.lineSpacing = native.lineSpacing; _preview.paragraphSpacing = native.paragraphSpacing;
            var color = native.color; color.a = _alpha; _preview.color = color; _preview.overflowMode = TextOverflowModes.Overflow;
#if UNITY_6000_0_OR_NEWER
            _preview.textWrappingMode = _field.lineType == TMP_InputField.LineType.SingleLine ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
#else
            _preview.enableWordWrapping = _field.lineType != TMP_InputField.LineType.SingleLine;
#endif
            _preview.text = composition.Text; _preview.ForceMeshUpdate();
            var info = _preview.textInfo;
            if (info.characterCount == 0) return;
            int end = Character(composition.Caret), start = Character(composition.UnconvertedStart);
            var current = info.characterInfo[Mathf.Min(end, info.characterCount - 1)];
            if (end == info.characterCount) current = info.characterInfo[info.characterCount - 1];
            float x = end < info.characterCount ? current.origin : current.xAdvance;
            float height = Mathf.Max(native.fontSize, current.ascender - current.descender);
            var point = new Vector3(x, current.ascender);
            var viewport = _field.textViewport != null ? _field.textViewport : (RectTransform)_field.transform;
            var visible = viewport.InverseTransformPoint(rect.TransformPoint(point));
            var delta = Vector2.zero;
            if (_field.lineType == TMP_InputField.LineType.SingleLine) delta.x = Mathf.Min(0, viewport.rect.xMax - visible.x - 4);
            delta.y = Mathf.Max(0, viewport.rect.yMin - visible.y + height);
            rect.anchoredPosition += delta;
            int markIndex = 0;
            for (int line = 0; line < info.lineCount; line++)
            {
                int first = Mathf.Max(start, info.lineInfo[line].firstCharacterIndex), last = Mathf.Min(end - 1, info.lineInfo[line].lastCharacterIndex);
                if (last < first) continue;
                if (markIndex == _marks.Count) _marks.Add(Mark());
                var mark = _marks[markIndex++]; mark.gameObject.SetActive(true);
                Position(mark.rectTransform, new Vector2(info.characterInfo[first].origin, info.lineInfo[line].baseline - 2), new Vector2(Mathf.Max(0, info.characterInfo[last].xAdvance - info.characterInfo[first].origin), 2));
            }
            for (int index = markIndex; index < _marks.Count; index++) _marks[index].gameObject.SetActive(false);
            Position(_caret.rectTransform, point, new Vector2(2, height));
            _candidates.Render(snapshot, rect.TransformPoint(point - new Vector3(0, height)), height);
        }
        private void Position(RectTransform rect, Vector2 point, Vector2 size)
        { rect.anchorMin = rect.anchorMax = _preview.rectTransform.pivot; rect.pivot = new Vector2(0, 1); rect.anchoredPosition = point; rect.sizeDelta = size; }
        private void Restore()
        { if (_field != null) { _field.textComponent.alpha = _alpha; _field.caretColor = _caretColor; _field.selectionColor = _selectionColor; } }
        public void Dispose()
        {
            Canvas.willRenderCanvases -= Refresh; _session.StateChanged -= Render; if (_active) Restore(); _candidates.Dispose();
            if (_preview != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_preview.gameObject); else UnityEngine.Object.DestroyImmediate(_preview.gameObject); }
        }
    }
}
