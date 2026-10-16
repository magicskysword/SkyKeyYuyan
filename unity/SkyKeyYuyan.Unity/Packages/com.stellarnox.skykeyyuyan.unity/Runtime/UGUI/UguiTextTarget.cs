using System;
using SkyKeyYuyan.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.Unity.UGUI
{
    /// <summary>
    /// 将 UGUI InputField 连接到统一文本编辑契约。
    /// </summary>
    public sealed class UguiTextTarget : UnityTextTarget
    {
        private readonly InputField _field;
        private UguiValidationProxy _validation;
        private readonly TextGenerator _generator = new TextGenerator();
        private bool _keyboardOwned, _originalReadOnly;
        /// <summary>
        /// 绑定现有输入框，不改变其字体和验证配置。
        /// </summary>
        public UguiTextTarget(InputField field) : base(field) { _field = field != null ? field : throw new ArgumentNullException(nameof(field)); _field.onValueChanged.AddListener(OnChanged); UguiNavigationCoordination.EnsureRegistered(); }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override bool NativeAlive => _field != null;
        /// <inheritdoc>
        /// </inheritdoc>
        public override bool IsEditing => IsAlive && _field.isFocused;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override int CharacterLimit => _field.characterLimit;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override bool SingleLine => _field.lineType == InputField.LineType.SingleLine;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override string ReadText() => _field.text;
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void ReadSelection(out int anchor, out int caret) { anchor = _field.selectionAnchorPosition; caret = _field.selectionFocusPosition; }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void WriteSelection(int anchor, int caret) { _field.selectionAnchorPosition = anchor; _field.selectionFocusPosition = caret; _field.ForceLabelUpdate(); }
        /// <inheritdoc>
        /// </inheritdoc>
        protected override void WriteText(string text, int anchor, int caret)
        {
            var type = _field.contentType; var validation = _field.characterValidation; var validate = _field.onValidateInput;
            var line = _field.lineType; var input = _field.inputType; var keyboard = _field.keyboardType; int limit = _field.characterLimit;
            try
            {
                _field.onValidateInput = null; _field.characterValidation = InputField.CharacterValidation.None; _field.characterLimit = 0;
#if UNITY_EDITOR
                // Unity 编辑态的 SetTextWithoutNotify 仍发送事件，先设置序列化正文再统一通知。
                if (!Application.isPlaying) typeof(InputField).GetField("m_Text", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(_field, text);
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
            string working = snapshot.Text.Remove(start, length), accepted = "";
            for (int index = 0; index < text.Length; index++)
            {
                int position = start + accepted.Length;
                char value;
                if (_field.onValidateInput != null) value = _field.onValidateInput(working, position, text[index]);
                else if (_field.characterValidation == InputField.CharacterValidation.None) value = text[index];
                else
                {
                    if (_validation == null)
                    {
                        var probe = new GameObject("SkyKeyYuyan UGUI Validation", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                        probe.SetActive(false); _validation = probe.AddComponent<UguiValidationProxy>();
                    }
                    value = _validation.Check(_field, working, position, text[index], snapshot.Anchor, snapshot.Caret);
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
            var text = _field.textComponent;
            if (text == null || text.font == null) return base.VerticalPosition(snapshot, direction);
            var settings = text.GetGenerationSettings(text.rectTransform.rect.size); settings.verticalOverflow = VerticalWrapMode.Overflow; settings.richText = false;
            _generator.Populate(snapshot.Text, settings);
            var lines = _generator.lines; var characters = _generator.characters;
            if (lines.Count == 0 || characters.Count == 0) return base.VerticalPosition(snapshot, direction);
            int currentLine = 0;
            for (int index = 1; index < lines.Count && lines[index].startCharIdx <= snapshot.Caret; index++) currentLine = index;
            int nextLine = Mathf.Clamp(currentLine + direction, 0, lines.Count - 1);
            if (currentLine == nextLine) return snapshot.Caret;
            float x = PreferredX ?? characters[Math.Min(snapshot.Caret, characters.Count - 1)].cursorPos.x; PreferredX = x;
            int start = lines[nextLine].startCharIdx, end = nextLine + 1 < lines.Count ? lines[nextLine + 1].startCharIdx - 1 : snapshot.Text.Length;
            int best = start; float distance = float.MaxValue;
            for (int position = start; position <= end && position < characters.Count; position++)
            {
                float delta = Math.Abs(characters[position].cursorPos.x - x);
                if (delta < distance) { best = position; distance = delta; }
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
            if (NativeAlive) _field.onValueChanged.RemoveListener(OnChanged);
            if (_validation != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_validation.gameObject); else UnityEngine.Object.DestroyImmediate(_validation.gameObject); }
            ((IDisposable)_generator).Dispose();
        }
    }
}
