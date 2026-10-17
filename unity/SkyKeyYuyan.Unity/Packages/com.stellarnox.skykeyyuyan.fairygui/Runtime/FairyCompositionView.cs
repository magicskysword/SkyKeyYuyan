using System;
using System.Collections.Generic;
using FairyGUI;
using SkyKeyYuyan.Unity;
using UnityEngine;

namespace SkyKeyYuyan.FairyGUI
{
    /// <summary>
    /// 在原生正文上绘制临时组字、下划线和光标。
    /// </summary>
    public sealed class FairyCompositionView : IDisposable
    {
        private readonly GTextInput _field;
        private readonly GTextField _label;
        private readonly GComponent _viewport;
        private readonly GGraph _caret = new GGraph { touchable = false };
        private readonly List<GGraph> _marks = new List<GGraph>();
        private readonly List<Rect> _rects = new List<Rect>();
        private float _nativeAlpha;
        private bool _active;
        /// <summary>
        /// 光标在正文父级内的位置。
        /// </summary>
        public Vector2 Caret { get; private set; }
        /// <summary>
        /// 当前排版行高。
        /// </summary>
        public float LineHeight { get; private set; }
        /// <summary>
        /// 光标在 Stage 中的位置。
        /// </summary>
        public Vector2 GlobalCaret { get; private set; }

        /// <summary>
        /// 为原输入框创建临时显示。
        /// </summary>
        public FairyCompositionView(GTextInput field, GTextField label)
        {
            _field = field; _label = label;
            _viewport = new GComponent { name = "compositionViewport", touchable = false }; field.parent.AddChild(_viewport);
            _viewport.AddChild(_label); _viewport.AddChild(_caret);
            _label.touchable = false; _label.UBBEnabled = false; _label.autoSize = AutoSizeType.None; ((TextField)_label.displayObject).EnableCharPositionSupport();
            _viewport.visible = false;
        }
        /// <summary>
        /// 测量当前输入框样式中的文字高度。
        /// </summary>
        public float Measure(string text)
        {
            _label.textFormat = _field.textFormat; _label.singleLine = _field.singleLine;
            _label.autoSize = _field.singleLine ? AutoSizeType.Both : AutoSizeType.Height;
            _label.SetSize(_field.width, _field.height); _label.text = text; return _label.textHeight;
        }
        /// <summary>
        /// 更新组字与滚动位置。
        /// </summary>
        public void Render(SkyCompositionText composition)
        {
            if (_active != composition.Active)
            {
                _active = composition.Active;
                if (_active) { _nativeAlpha = _field.inputTextField.alpha; _field.inputTextField.alpha = 0; }
                else _field.inputTextField.alpha = _nativeAlpha;
                _viewport.visible = _active;
            }
            if (!_active) return;
            _viewport.SetXY(_field.x, _field.y); _viewport.SetSize(_field.width, _field.height);
            _viewport.container.clipRect = new Rect(0, 0, _field.width, _field.height);
            _label.textFormat = _field.textFormat; _label.singleLine = _field.singleLine;
            _label.autoSize = _field.singleLine ? AutoSizeType.Both : AutoSizeType.Height;
            _label.SetSize(_field.width, _field.height); _label.text = composition.Text;
            var text = (TextField)_label.displayObject; text.Redraw();
            if (text.charPositions.Count == 0 || text.lines.Count == 0) return;
            var first = text.charPositions[Math.Min(ScalarIndex(composition.Text, composition.UnconvertedStart), text.charPositions.Count - 1)];
            var last = text.charPositions[Math.Min(ScalarIndex(composition.Text, composition.Caret), text.charPositions.Count - 1)];
            var line = text.lines[last.lineIndex]; LineHeight = line.height > 0 ? line.height : text.textFormat.size;
            float scrollX = _field.singleLine ? Mathf.Max(0, last.offsetX + 2 - _field.width) : 0;
            float scrollY = Mathf.Max(0, line.y + LineHeight - _field.height);
            _label.SetXY(-scrollX, -scrollY);
            GlobalCaret = _label.LocalToGlobal(new Vector2(last.offsetX, line.y + LineHeight));
            Caret = new Vector2(_field.x + last.offsetX - scrollX, _field.y + line.y - scrollY);
            _rects.Clear(); text.GetLinesShape(first.lineIndex, first.offsetX, last.lineIndex, last.offsetX, false, _rects);
            while (_marks.Count < _rects.Count) { var mark = new GGraph { touchable = false }; _viewport.AddChild(mark); _marks.Add(mark); }
            for (int index = 0; index < _marks.Count; index++)
            {
                var mark = _marks[index]; mark.visible = index < _rects.Count;
                if (!mark.visible) continue;
                var rect = _rects[index]; mark.SetXY(rect.x - scrollX, rect.y + rect.height - scrollY - 2);
                mark.SetSize(rect.width, 2); mark.shape.DrawRect(0, Color.clear, SkyKeyboardStyle.Accent);
            }
            _caret.SetXY(last.offsetX - scrollX, line.y - scrollY); _caret.SetSize(2, LineHeight); _caret.shape.DrawRect(0, Color.clear, SkyKeyboardStyle.Accent);
        }
        private static int ScalarIndex(string text, int offset)
        {
            int result = 0;
            for (int index = 0; index < Math.Min(offset, text.Length); index++, result++)
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])) index++;
            return result;
        }
        public void Dispose() { if (_active && !_field.isDisposed) _field.inputTextField.alpha = _nativeAlpha; if (!_viewport.isDisposed) _viewport.Dispose(); }
    }
}
