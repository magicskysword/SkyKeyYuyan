using System;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace SkyKeyYuyan.Unity.TMP
{
    /// <summary>
    /// 在独立控件状态上调用 TMP 的原生字符验证。
    /// </summary>
    [AddComponentMenu("")]
    public sealed class TmpValidationProxy : TMP_InputField
    {
        private static readonly FieldInfo RegexField = typeof(TMP_InputField).GetField("m_RegexValue", BindingFlags.Instance | BindingFlags.NonPublic);
        /// <summary>
        /// 固定待验证正文、选区与正则配置。
        /// </summary>
        public char Check(TMP_InputField source, string text, int position, char value, int anchor, int caret)
        {
            m_Text = text; m_StringPosition = anchor; m_StringSelectPosition = caret;
            characterValidation = source.characterValidation;
            if (characterValidation == CharacterValidation.Regex)
            {
                // TMP 将正则配置保存在私有序列化字段中，独立验证器需要相同配置。
                if (RegexField == null) throw new NotSupportedException("当前 TMP 版本未暴露兼容的正则验证配置");
                RegexField.SetValue(this, RegexField.GetValue(source));
            }
            return Validate(text, position, value);
        }
    }
}
