using System;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity.CanvasUI;
using TMPro;
using UnityEngine;

namespace SkyKeyYuyan.Unity.TMP
{
    /// <summary>
    /// 使用字符串选区坐标适配 TMP_InputField。
    /// </summary>
    public sealed class TmpTextTarget : UnityTextTarget, ISkyInlineKeyboardTarget
    {
        private readonly TMP_InputField _field;
        private TmpValidationProxy _validation;
        private bool _keyboardOwned, _originalReadOnly, _focusRequested;
        private readonly SkyKeyboardFocusObserver _focus;
        /// <summary>
        /// 绑定现有 TMP 输入框。
        /// </summary>
        public TmpTextTarget(TMP_InputField field) : base(field)
        {
            _field = field != null ? field : throw new ArgumentNullException(nameof(field)); _field.onValueChanged.AddListener(OnChanged);
            _focus = field.GetComponent<SkyKeyboardFocusObserver>() ?? field.gameObject.AddComponent<SkyKeyboardFocusObserver>(); _focus.Requested += OnFocusRequested;
        }
        private void OnFocusRequested() { _focusRequested = true; }
        /// <inheritdoc>
        /// </inheritdoc>
        public bool TakeKeyboardFocusRequest() { bool value = _focusRequested; _focusRequested = false; return value; }
        /// <inheritdoc>
        /// </inheritdoc>
        public SkyTextInputType KeyboardTextType => SingleLine ? SkyTextInputType.SingleLine : SkyTextInputType.MultiLine;
        /// <inheritdoc>
        /// </inheritdoc>
        public void RestoreKeyboardSelection(int anchor, int caret) => WriteSelection(anchor, caret);
        /// <inheritdoc>
        /// </inheritdoc>
        protected override bool NativeAlive => _field != null;
        /// <inheritdoc>
        /// </inheritdoc>
        public override bool IsEditing => IsAlive && _field.isFocused;
        /// <inheritdoc>
        /// </inheritdoc>
        public IDisposable CreateKeyboardView(ISkySession session) => new TmpKeyboardView(session, _field);
        /// <inheritdoc>
        /// </inheritdoc>
        protected override int CharacterLimit => _field.characterLimit;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override bool SingleLine => _field.lineType == TMP_InputField.LineType.SingleLine;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override string ReadText() => _field.text;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void ReadSelection(out int anchor, out int caret) { anchor = _field.selectionStringAnchorPosition; caret = _field.selectionStringFocusPosition; }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void WriteSelection(int anchor, int caret) { _field.selectionStringAnchorPosition = anchor; _field.selectionStringFocusPosition = caret; _field.ForceLabelUpdate(); }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void WriteText(string text, int anchor, int caret)
        {
            var type = _field.contentType; var validation = _field.characterValidation; var validate = _field.onValidateInput;
            var line = _field.lineType; var input = _field.inputType; var keyboard = _field.keyboardType; int limit = _field.characterLimit;
            try
            {
                _field.onValidateInput = null; _field.characterValidation = TMP_InputField.CharacterValidation.None; _field.characterLimit = 0;
#if UNITY_EDITOR
                // TMP 编辑态的 SetTextWithoutNotify 仍发送事件，先设置序列化正文再统一通知。
                if (!Application.isPlaying) typeof(TMP_InputField).GetField("m_Text", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(_field, text);
                else
#endif
                    _field.SetTextWithoutNotify(text);
            }
            finally { _field.contentType = type; _field.lineType = line; _field.inputType = input; _field.keyboardType = keyboard; _field.characterValidation = validation; _field.onValidateInput = validate; _field.characterLimit = limit; }
            WriteSelection(anchor, caret);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void NotifyTextChanged(string text) => _field.onValueChanged.Invoke(text);
        private void OnChanged(string _) => NativeChanged();
        /// <inheritdoc>
        /// </inheritdoc>
        protected override string ValidateInsertion(TextSnapshot snapshot, int start, int length, string text)
        {
            string working = snapshot.Text.Remove(start, length), prefix = working.Substring(0, start), suffix = working.Substring(start), accepted = "";
            if (_field.onValidateInput == null && _field.characterValidation == TMP_InputField.CharacterValidation.CustomValidator)
            {
                if (_field.inputValidator == null) return null;
                int position = start;
                foreach (char value in text) if (_field.inputValidator.Validate(ref working, ref position, value) == 0) return null;
                return working.Length >= prefix.Length + suffix.Length && working.StartsWith(prefix, StringComparison.Ordinal) && working.EndsWith(suffix, StringComparison.Ordinal) ? working.Substring(prefix.Length, working.Length - prefix.Length - suffix.Length) : null;
            }
            foreach (char original in text)
            {
                int position = start + accepted.Length;
                char value;
                if (_field.onValidateInput != null) value = _field.onValidateInput(working, position, original);
                else if (_field.characterValidation == TMP_InputField.CharacterValidation.None) value = original;
                else
                {
                    if (_validation == null)
                    {
                        var probe = new GameObject("SkyKeyYuyan TMP Validation", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                        probe.SetActive(false); _validation = probe.AddComponent<TmpValidationProxy>();
                    }
                    value = _validation.Check(_field, working, position, original, snapshot.Anchor, snapshot.Caret);
                }
                if (value == 0) return null;
                accepted += value; working = working.Insert(position, value.ToString());
            }
            return accepted;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override int VerticalPosition(TextSnapshot snapshot, int direction)
        {
            var component = _field.textComponent;
            if (component == null || component.font == null) return base.VerticalPosition(snapshot, direction);
            _field.ForceLabelUpdate(); component.ForceMeshUpdate(true, true);
            var info = component.textInfo;
            if (info.characterCount == 0 || info.lineCount == 0) return base.VerticalPosition(snapshot, direction);
            int current = 0;
            for (int index = 0; index < info.characterCount; index++) { if (info.characterInfo[index].index > snapshot.Caret) break; current = index; }
            var character = info.characterInfo[current];
            int nextLine = Mathf.Clamp(character.lineNumber + direction, 0, info.lineCount - 1);
            if (nextLine == character.lineNumber) return snapshot.Caret;
            float x = PreferredX ?? (snapshot.Caret > character.index ? character.xAdvance : character.origin); PreferredX = x;
            var line = info.lineInfo[nextLine];
            int best = Math.Min(snapshot.Text.Length, info.characterInfo[line.firstCharacterIndex].index); float distance = float.MaxValue;
            for (int index = line.firstCharacterIndex; index <= line.lastCharacterIndex && index < info.characterCount; index++)
            {
                var candidate = info.characterInfo[index];
                if (candidate.index <= snapshot.Text.Length && Math.Abs(candidate.origin - x) < distance) { best = candidate.index; distance = Math.Abs(candidate.origin - x); }
                int end = Math.Min(snapshot.Text.Length, candidate.index + candidate.stringLength);
                if (index == line.lastCharacterIndex && Math.Abs(candidate.xAdvance - x) < distance) best = end;
            }
            return best;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void SetKeyboardOwned(bool owned)
        {
            if (_keyboardOwned == owned || !NativeAlive) return;
            if (owned) { _originalReadOnly = _field.readOnly; _field.readOnly = true; } else _field.readOnly = _originalReadOnly;
            _keyboardOwned = owned;
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public override void EndEditing() { if (NativeAlive) _field.DeactivateInputField(); }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void DisposeNative()
        {
            if (_focus != null) _focus.Requested -= OnFocusRequested;
            if (NativeAlive) _field.onValueChanged.RemoveListener(OnChanged);
            if (_validation != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_validation.gameObject); else UnityEngine.Object.DestroyImmediate(_validation.gameObject); }
        }
    }
}
