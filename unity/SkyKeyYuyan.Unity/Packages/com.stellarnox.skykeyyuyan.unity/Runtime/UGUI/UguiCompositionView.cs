using System;
using System.Collections.Generic;
using SkyKeyYuyan.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.Unity.UGUI
{
    /// <summary>
    /// 在原生正文上绘制临时组字、下划线和光标。
    /// </summary>
    public sealed class UguiCompositionView : IDisposable
    {
        private readonly InputField _field;
        private readonly Text _label;
        private readonly UguiCompositionMarks _marks;
        private readonly TextGenerator _generator = new TextGenerator();
        private bool _active;
        private Color _color, _caretColor, _selectionColor;
        /// <summary>
        /// 光标在正文父级内的位置。
        /// </summary>
        public Vector2 Caret { get; private set; }
        /// <summary>
        /// 当前排版的行高。
        /// </summary>
        public float LineHeight { get; private set; }
        /// <summary>
        /// 组字光标的世界坐标。
        /// </summary>
        public Vector3 WorldCaret { get; private set; }

        /// <summary>
        /// 为给定输入框创建独立显示，不修改其正文。
        /// </summary>
        public UguiCompositionView(InputField field)
        {
            _field = field;
            var owner = new GameObject("CompositionPreview", typeof(RectTransform)); owner.transform.SetParent(field.textComponent.transform.parent, false);
            _label = owner.AddComponent<Text>(); _label.raycastTarget = false; _label.supportRichText = false;
            _label.alignment = TextAnchor.UpperLeft; _label.verticalOverflow = VerticalWrapMode.Overflow;
            var rect = _label.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            var marks = new GameObject("CompositionMarks", typeof(RectTransform), typeof(CanvasRenderer)); marks.transform.SetParent(owner.transform, false);
            var markRect = (RectTransform)marks.transform; markRect.anchorMin = Vector2.zero; markRect.anchorMax = Vector2.one;
            markRect.pivot = new Vector2(0, 1); markRect.offsetMin = markRect.offsetMax = Vector2.zero;
            _marks = marks.AddComponent<UguiCompositionMarks>(); _marks.raycastTarget = false; _marks.color = SkyKeyboardStyle.Accent;
            owner.SetActive(false);
        }
        /// <summary>
        /// 更新当前组字显示。
        /// </summary>
        public void Render(SkyCompositionText composition)
        {
            var native = _field.textComponent;
            if (_active != composition.Active)
            {
                _active = composition.Active;
                if (_active)
                {
                    _color = native.color; _caretColor = _field.caretColor; _selectionColor = _field.selectionColor;
                    native.color = new Color(_color.r, _color.g, _color.b, 0);
                    _field.caretColor = _field.selectionColor = Color.clear;
                }
                else { native.color = _color; _field.caretColor = _caretColor; _field.selectionColor = _selectionColor; }
                _label.gameObject.SetActive(_active);
            }
            if (!_active) return;
            _label.font = native.font; _label.fontSize = native.fontSize; _label.fontStyle = native.fontStyle;
            _label.alignment = native.alignment;
            _label.lineSpacing = native.lineSpacing; _label.color = _color; _label.text = composition.Text;
            _label.horizontalOverflow = _field.lineType == InputField.LineType.SingleLine ? HorizontalWrapMode.Overflow : HorizontalWrapMode.Wrap;
            var size = native.rectTransform.rect.size;
            _label.rectTransform.sizeDelta = size;
            // 显示层生成完整文字网格，由正文视口统一裁切滚动后的内容。
            float width = _field.lineType == InputField.LineType.SingleLine ? Mathf.Max(size.x, _label.preferredWidth) : size.x;
            _label.rectTransform.sizeDelta = new Vector2(width, Mathf.Max(size.y, _label.preferredHeight));
            var settings = _label.GetGenerationSettings(_label.rectTransform.rect.size); settings.verticalOverflow = VerticalWrapMode.Overflow;
            _generator.Populate(composition.Text, settings);
            if (_generator.lines.Count == 0) return;
            float units = _label.pixelsPerUnit;
            int caretLine = LineAt(composition.Caret);
            var line = _generator.lines[caretLine]; LineHeight = line.height / units;
            var caret = Point(composition.Caret);
            float scrollX = _field.lineType == InputField.LineType.SingleLine ? Mathf.Max(0, caret.x + 2 - size.x) : 0;
            float scrollY = Mathf.Max(0, caret.y + LineHeight - size.y);
            _label.rectTransform.localRotation = native.rectTransform.localRotation; _label.rectTransform.localScale = native.rectTransform.localScale;
            _label.rectTransform.position = native.rectTransform.TransformPoint(new Vector3(native.rectTransform.rect.xMin, native.rectTransform.rect.yMax));
            _label.rectTransform.anchoredPosition += new Vector2(-scrollX, scrollY);
            var body = (RectTransform)_field.transform;
            var parent = (RectTransform)body.parent;
            var caretPoint = parent.InverseTransformPoint(_label.rectTransform.TransformPoint(new Vector3(caret.x, -caret.y)));
            Caret = new Vector2(caretPoint.x - parent.rect.xMin, parent.rect.yMax - caretPoint.y);
            _marks.Rects.Clear();
            for (int index = LineAt(composition.UnconvertedStart); index <= caretLine; index++)
            {
                var current = _generator.lines[index]; int first = Math.Max(composition.UnconvertedStart, current.startCharIdx);
                int end = Math.Min(composition.Caret, index + 1 < _generator.lines.Count ? _generator.lines[index + 1].startCharIdx : composition.Text.Length);
                float x = Point(first).x, right = end > first ? RightEdge(end - 1) : x;
                if (right > x) _marks.Rects.Add(new Rect(x, current.topY / units - current.height / units + 2, right - x, 2));
            }
            _marks.Rects.Add(new Rect(caret.x, -caret.y - LineHeight, 2, LineHeight)); _marks.SetVerticesDirty();
            _marks.rectTransform.ForceUpdateRectTransforms();
            WorldCaret = _marks.rectTransform.TransformPoint(new Vector3(caret.x, -caret.y - LineHeight));
        }
        private int LineAt(int offset)
        {
            int line = 0;
            while (line + 1 < _generator.lines.Count && _generator.lines[line + 1].startCharIdx <= offset) line++;
            return line;
        }
        private Vector2 Point(int offset)
        {
            var characters = _generator.characters;
            float x = offset < characters.Count ? characters[offset].cursorPos.x / _label.pixelsPerUnit : offset > 0 ? RightEdge(offset - 1) : 0;
            return new Vector2(x, -_generator.lines[LineAt(offset)].topY / _label.pixelsPerUnit);
        }
        private float RightEdge(int index)
        {
            var characters = _generator.characters;
            if (characters.Count == 0) return 0;
            var item = characters[Math.Min(index, characters.Count - 1)]; return (item.cursorPos.x + item.charWidth) / _label.pixelsPerUnit;
        }
        public void Dispose()
        {
            if (_active && _field != null) { _field.textComponent.color = _color; _field.caretColor = _caretColor; _field.selectionColor = _selectionColor; }
            ((IDisposable)_generator).Dispose();
            if (_label != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_label.gameObject); else UnityEngine.Object.DestroyImmediate(_label.gameObject); }
        }
    }
    internal sealed class UguiCompositionMarks : MaskableGraphic
    {
        internal readonly List<Rect> Rects = new List<Rect>();
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            foreach (var rect in Rects)
            {
                int index = helper.currentVertCount;
                helper.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero); helper.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
                helper.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero); helper.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
                helper.AddTriangle(index, index + 1, index + 2); helper.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
