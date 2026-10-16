using System;
using System.Reflection;
using FairyGUI;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;
using UnityEngine;

namespace SkyKeyYuyan.FairyGUI
{
    /// <summary>
    /// 将 FairyGUI 的字符坐标和长度限制映射到 UTF-16 文本契约。
    /// </summary>
    public sealed class FairyTextTarget : UnityTextTarget
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo AnchorField = typeof(InputTextField).GetField("_selectionStart", PrivateInstance);
        private static readonly FieldInfo CaretField = typeof(InputTextField).GetField("_caretPosition", PrivateInstance);
        private static readonly MethodInfo UpdateCaretMethod = typeof(InputTextField).GetMethod("UpdateCaret", PrivateInstance);
        private static readonly MethodInfo ValidateMethod = typeof(InputTextField).GetMethod("ValidateInput", PrivateInstance);
        private readonly GTextInput _field;
        private bool _keyboardOwned, _originalEditable, _originalKeyboardInput, _originalDisableIme;
        /// <summary>
        /// 绑定已有文本框；SDK 必须保留双向选区和原生验证所需成员。
        /// </summary>
        public FairyTextTarget(GTextInput field) : base(field)
        {
            _field = field ?? throw new ArgumentNullException(nameof(field));
            if (AnchorField == null || CaretField == null || UpdateCaretMethod == null || ValidateMethod == null)
                throw new NotSupportedException("当前 FairyGUI 版本的选区或验证成员不兼容");
            _field.onChanged.Add(OnChanged);
            _field.inputTextField.onKeyDown.AddCapture(OnNativeKey);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override bool NativeAlive => !_field.isDisposed && !_field.inputTextField.isDisposed;
        /// <inheritdoc>
        /// </inheritdoc>
        public override bool IsEditing => IsAlive && _field.inputTextField.focused;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override int CharacterLimit => _field.maxLength;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override bool SingleLine => _field.singleLine;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override string ReadText() => _field.text;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void ReadSelection(out int anchor, out int caret)
        {
            string text = _field.text;
            var input = _field.inputTextField;
            int nativeCaret = input.caretPosition;
            int nativeAnchor = nativeCaret == input.selectionBeginIndex ? input.selectionEndIndex : input.selectionBeginIndex;
            anchor = Utf16Offset(text, nativeAnchor); caret = Utf16Offset(text, nativeCaret);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void WriteText(string text, int anchor, int caret) { _field.text = text; WriteSelection(anchor, caret); }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void WriteSelection(int anchor, int caret)
        {
            var input = _field.inputTextField;
            input.textField.Redraw();
            // 公开的负选区长度表示选择至文末；直接保留两个端点也避免夺取其他玩家的焦点。
            AnchorField.SetValue(input, CharacterOffset(_field.text, anchor));
            CaretField.SetValue(input, CharacterOffset(_field.text, caret));
            UpdateCaretMethod.Invoke(input, new object[] { false });
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void NotifyTextChanged(string text) => _field.inputTextField.DispatchEvent("onChanged");
        private void OnChanged() => NativeChanged();
        private void OnNativeKey(EventContext context) { if (_keyboardOwned) { context.StopPropagation(); context.PreventDefault(); } }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override string ValidateInsertion(TextSnapshot snapshot, int start, int length, string text)
        {
            if (string.IsNullOrEmpty(_field.restrict)) return text;
            string accepted = (string)ValidateMethod.Invoke(_field.inputTextField, new object[] { text });
            return accepted == text ? text : null;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override TextEditResult Plan(TextSnapshot before, TextEditRequest request, string input)
        {
            var preview = new MemoryTextTarget(before.Text, before.Anchor, before.Caret, singleLine: SingleLine);
            var plan = preview.ApplyEdit(new TextEditRequest(request.Operation, 0, request.Start, request.Length, input, request.Part));
            if (!plan.Applied || CharacterLimit <= 0) return plan;
            int outside = CharacterOffset(before.Text.Substring(0, plan.Start), plan.Start)
                + CharacterOffset(before.Text.Substring(plan.Start + plan.Length), before.Text.Length - plan.Start - plan.Length);
            int acceptedLength = Utf16Offset(plan.AcceptedText, Math.Max(0, CharacterLimit - outside));
            if (acceptedLength == plan.AcceptedText.Length) return plan;
            preview = new MemoryTextTarget(before.Text, before.Anchor, before.Caret, singleLine: SingleLine);
            var limited = preview.ApplyEdit(new TextEditRequest(request.Operation, 0, plan.Start, plan.Length, plan.AcceptedText.Substring(0, acceptedLength), request.Part));
            return new TextEditResult(TextEditStatus.Truncated, limited.Snapshot, limited.AcceptedText, limited.Start, limited.Length);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override int VerticalPosition(TextSnapshot snapshot, int direction)
        {
            var textField = _field.inputTextField.textField;
            textField.Redraw(); var positions = textField.charPositions;
            if (positions.Count == 0) return base.VerticalPosition(snapshot, direction);
            var current = positions[Math.Min(CharacterOffset(snapshot.Text, snapshot.Caret), positions.Count - 1)];
            int nextLine = Mathf.Clamp(current.lineIndex + direction, 0, textField.lines.Count - 1);
            if (nextLine == current.lineIndex) return snapshot.Caret;
            float x = PreferredX ?? current.offsetX; PreferredX = x;
            int best = current.charIndex; float distance = float.MaxValue;
            foreach (var position in positions)
            {
                if (position.lineIndex != nextLine) continue;
                float delta = Math.Abs(position.offsetX - x);
                if (delta < distance) { best = position.charIndex; distance = delta; }
            }
            return Utf16Offset(snapshot.Text, best);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void SetKeyboardOwned(bool owned)
        {
            if (_keyboardOwned == owned || !NativeAlive) return;
            if (owned)
            {
                _originalEditable = _field.editable; _originalKeyboardInput = _field.keyboardInput; _originalDisableIme = _field.disableIME;
                _field.editable = false; _field.keyboardInput = false; _field.disableIME = true;
            }
            else { _field.editable = _originalEditable; _field.keyboardInput = _originalKeyboardInput; _field.disableIME = _originalDisableIme; }
            _keyboardOwned = owned;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void EndEditing() { if (IsEditing) Stage.inst.focus = null; }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void DisposeNative()
        {
            if (!NativeAlive) return;
            _field.onChanged.Remove(OnChanged);
            _field.inputTextField.onKeyDown.RemoveCapture(OnNativeKey);
        }
        private static int Utf16Offset(string text, int characters)
        {
            int position = 0;
            while (characters-- > 0 && position < text.Length) position = TextBoundary.Next(text, position);
            return position;
        }
        private static int CharacterOffset(string text, int utf16)
        {
            int position = 0, characters = 0, end = TextBoundary.Floor(text, utf16);
            while (position < end) { position = TextBoundary.Next(text, position); characters++; }
            return characters;
        }
    }
}
