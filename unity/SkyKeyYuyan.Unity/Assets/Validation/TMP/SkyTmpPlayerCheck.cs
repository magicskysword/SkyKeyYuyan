using System;
using System.Threading.Tasks;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity.TMP;
using TMPro;
using UnityEngine;

namespace SkyKeyYuyan.Validation
{
    internal static class SkyTmpPlayerCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => SkyPlayerValidation.Register("TMP 代理对选区与长度截断", Run);
        private static Task Run()
        {
            var owner = new GameObject("TMP Validation", typeof(RectTransform), typeof(Canvas));
            var font = Font.CreateDynamicFontFromOSFont("Arial", 20); var asset = TMP_FontAsset.CreateFontAsset(font);
            try
            {
                var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(owner.transform, false);
                var text = label.GetComponent<TextMeshProUGUI>(); text.font = asset;
                var field = owner.AddComponent<TMP_InputField>(); field.textComponent = text; field.textViewport = (RectTransform)owner.transform;
                field.text = "A😀B"; field.selectionStringAnchorPosition = 3; field.selectionStringFocusPosition = 1; field.characterLimit = 4;
                using (var target = new TmpTextTarget(field))
                {
                    var snapshot = target.ReadSnapshot(); SkyPlayerValidation.Require(snapshot.Anchor == 3 && snapshot.Caret == 1, "TMP 反向选区坐标错误");
                    var result = target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 1), snapshot.Version, 1, 2, "XYZ"));
                    SkyPlayerValidation.Require(result.Status == TextEditStatus.Truncated && field.text == "AXYB", "TMP 长度截断未保留正文前后缀");
                }
            }
            finally { UnityEngine.Object.Destroy(owner); UnityEngine.Object.Destroy(asset); UnityEngine.Object.Destroy(font); }
            return Task.CompletedTask;
        }
    }
}
