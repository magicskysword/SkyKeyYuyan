using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.Unity.UGUI
{
    /// <summary>
    /// 在独立控件状态上调用 UGUI 的原生字符验证。
    /// </summary>
    [AddComponentMenu("")]
    public sealed class UguiValidationProxy : InputField
    {
        /// <summary>
        /// 验证一个字符，保留实际控件的选区条件。
        /// </summary>
        public char Check(InputField source, string text, int position, char value, int anchor, int caret)
        {
            m_Text = text; m_CaretPosition = anchor; m_CaretSelectPosition = caret;
            characterValidation = source.characterValidation;
            return Validate(text, position, value);
        }
    }
}
