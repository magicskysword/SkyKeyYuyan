using System;
using FairyGUI;
using SkyKeyYuyan.Core;
using UnityEngine;

namespace SkyKeyYuyan.UI.FairyGUI
{
    // 未占用 Stage 全局焦点的草稿仍按自身排版显示选区。
    internal sealed class FairyDraftSelection : IDisposable
    {
        private readonly GTextInput _field;
        private readonly Shape _caret = new Shape { touchable = false };
        private readonly SelectionShape _selection = new SelectionShape { touchable = false, color = new Color(0.2f, 0.65f, 0.8f, 0.4f) };
        internal FairyDraftSelection(GTextInput field)
        {
            _field = field; _caret.DrawRect(0, Color.clear, new Color32(102, 222, 212, 255));
            field.inputTextField.AddChildAt(_selection, 0); field.inputTextField.AddChild(_caret);
        }
        internal void Render(TextSnapshot snapshot)
        {
            var input = _field.inputTextField; var text = input.textField; text.Redraw();
            _caret.visible = false; _selection.Clear();
            if (text.charPositions.Count == 0 || text.lines.Count == 0) return;
            int anchor = ScalarIndex(snapshot.Text, snapshot.Anchor), caret = ScalarIndex(snapshot.Text, snapshot.Caret);
            var active = text.charPositions[Math.Min(caret, text.charPositions.Count - 1)];
            if (anchor == caret)
            {
                if (input.focused && _field.editable) return;
                var line = text.lines[active.lineIndex];
                _caret.SetSize(2, line.height > 0 ? line.height : text.textFormat.size);
                _caret.SetXY(text.x + active.offsetX, text.y + line.y); _caret.visible = true;
            }
            else if (!input.focused)
            {
                var start = text.charPositions[Math.Min(Math.Min(anchor, caret), text.charPositions.Count - 1)];
                var end = text.charPositions[Math.Min(Math.Max(anchor, caret), text.charPositions.Count - 1)];
                text.GetLinesShape(start.lineIndex, start.offsetX, end.lineIndex, end.offsetX, false, _selection.rects);
                _selection.xy = text.xy; _selection.Refresh();
            }
        }
        private static int ScalarIndex(string text, int offset)
        {
            int count = 0;
            for (int index = 0; index < Math.Min(offset, text.Length); index++, count++)
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])) index++;
            return count;
        }
        public void Dispose() { if (!_caret.isDisposed) _caret.Dispose(); if (!_selection.isDisposed) _selection.Dispose(); }
    }
}
