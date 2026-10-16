using System.Threading.Tasks;
using FairyGUI;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.FairyGUI;
using SkyKeyYuyan.UI.FairyGUI;
using SkyKeyYuyan.Unity;
using UnityEngine;

namespace SkyKeyYuyan.Validation
{
    internal static class SkyFairyPlayerCheck
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => SkyPlayerValidation.Register("FairyGUI 双草稿与原生验证", Run);
        private static async Task Run()
        {
            var root = new GComponent(); root.SetSize(1920, 1080); GRoot.inst.AddChild(root);
            var font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Malgun Gothic", "Arial" }, 24);
            var dynamicFont = new DynamicFont("SkyValidation", font); FontManager.RegisterFont(dynamicFont);
            try
            {
                var firstRoot = new GComponent(); firstRoot.SetSize(960, 1080); root.AddChild(firstRoot);
                var secondRoot = new GComponent(); secondRoot.SetXY(960, 0); secondRoot.SetSize(960, 1080); root.AddChild(secondRoot);
                var firstField = new GTextInput { text = "A😀B", maxLength = 3, restrict = "[A-Z]" }; root.AddChild(firstField); firstField.SetSize(200, 100);
                var secondField = new GTextInput { text = "CD" }; root.AddChild(secondField); secondField.SetSize(200, 100);
                using (var first = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "fairy-one", InitialLanguage = "en" }))
                using (var second = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "fairy-two", InitialLanguage = "en" }))
                using (var firstTarget = new FairyTextTarget(firstField, 3, 1))
                using (var secondTarget = new FairyTextTarget(secondField, 1, 1))
                using (var firstPanel = FairySkyPanel.Create(firstRoot, first, "SkyValidation"))
                using (var secondPanel = FairySkyPanel.Create(secondRoot, second, "SkyValidation"))
                {
                    SkyPlayerValidation.Require(first.Open(firstTarget, SkyRuntime.Default).Succeeded && second.Open(secondTarget, SkyRuntime.Default).Succeeded, "FairyGUI 会话打开失败");
                    SkyPlayerValidation.Require(first.Session.Snapshot.Text.Anchor == 3 && first.Session.Snapshot.Text.Caret == 1, "FairyGUI 反向选区未恢复");
                    first.Handle(SkyControl.Text, text: "Z"); second.Handle(SkyControl.SelectAll); await Task.Yield();
                    SkyPlayerValidation.Require(firstPanel.DraftField.text == "AZB" && firstField.text == "A😀B", "FairyGUI 草稿范围错误");
                    first.Handle(SkyControl.Finish); second.Handle(SkyControl.Cancel);
                    SkyPlayerValidation.Require(firstField.text == "AZB" && secondField.text == "CD", "FairyGUI 保存和取消互相影响");
                }
            }
            finally { root.Dispose(); FontManager.UnregisterFont(dynamicFont); dynamicFont.Dispose(); UnityEngine.Object.Destroy(font); }
        }
    }
}
