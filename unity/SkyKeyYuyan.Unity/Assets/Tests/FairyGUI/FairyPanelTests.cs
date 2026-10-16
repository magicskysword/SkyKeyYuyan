using System;
using System.Collections;
using System.IO;
using FairyGUI;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.FairyGUI;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.UI.FairyGUI;
using SkyKeyYuyan.Unity;
using UnityEngine;
using UnityEngine.TestTools;

public class FairyPanelTests
{
    private GComponent _root;
    private Font _font;
    private DynamicFont _dynamicFont;
    [SetUp]
    public void Setup()
    {
        _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Malgun Gothic", "Arial Unicode MS", "Arial" }, 24);
        _dynamicFont = new DynamicFont("SkyPanelTest", _font); FontManager.RegisterFont(_dynamicFont);
        _root = new GComponent(); _root.SetSize(1920, 1080); GRoot.inst.AddChild(_root);
    }
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        _root.Dispose(); FontManager.UnregisterFont(_dynamicFont); _dynamicFont.Dispose(); UnityEngine.Object.Destroy(_font); yield return null;
    }
    private GComponent PlayerRoot(float x)
    { var parent = new GComponent(); _root.AddChild(parent); parent.SetXY(x, 0); parent.SetSize(960, 1080); return parent; }
    private FairyTextTarget Target(string text, int caret, int maxLength = 0)
    {
        var field = new GTextInput { text = text, maxLength = maxLength }; field.SetSize(240, 100); field.SetXY(-3000, 0); _root.AddChild(field);
        return new FairyTextTarget(field, caret, caret);
    }
    [UnityTest]
    public IEnumerator IndependentDraftsPreserveReverseSelectionAcrossFocusAndSave()
    {
        using (var runtime = new SkyRuntime())
        using (var first = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "玩家一" }) { KeyboardEnabled = true })
        using (var second = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "玩家二" }) { KeyboardEnabled = true })
        using (var firstTarget = Target("ABCD", 2))
        using (var secondTarget = Target("WXYZ", 1))
        {
            runtime.RegisterLanguage(new BasicEnglishModule()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var firstPanel = FairySkyPanel.Create(PlayerRoot(0), first, "SkyPanelTest"))
            using (var secondPanel = FairySkyPanel.Create(PlayerRoot(960), second, "SkyPanelTest"))
            {
                Assert.That(first.Open(firstTarget, runtime).Succeeded, Is.True); Assert.That(second.Open(secondTarget, runtime).Succeeded, Is.True);
                Assert.That(first.Session.Snapshot.Text.Caret, Is.EqualTo(2)); Assert.That(second.Session.Snapshot.Text.Caret, Is.EqualTo(1));
                first.Handle(SkyControl.Text, text: "qq"); first.Handle(SkyControl.TextLeft, true); second.Handle(SkyControl.SelectAll);
                Stage.inst.SetFocus(secondPanel.DraftField.displayObject); yield return null;
                var focus = Stage.inst.focus;
                firstPanel.View.GetChild("keyboard").asCom.GetChild("key-1-1").asButton.FireClick(false, true); yield return null;
                Assert.That(Stage.inst.focus, Is.SameAs(focus)); Assert.That(first.Session.Snapshot.Text.Text, Is.EqualTo("ABqwCD"));
                Assert.That(second.Session.Snapshot.Text.Anchor, Is.EqualTo(0)); Assert.That(second.Session.Snapshot.Text.Caret, Is.EqualTo(4));
                Assert.That(firstPanel.DraftField.inputTextField.numChildren, Is.GreaterThanOrEqualTo(3));
                first.Handle(SkyControl.TextLeft, true); first.Handle(SkyControl.TextLeft, true); yield return null;
                Assert.That(first.Session.Snapshot.Text.Anchor, Is.GreaterThan(first.Session.Snapshot.Text.Caret));
                yield return CapturePanels();
                first.Handle(SkyControl.Finish); Assert.That(firstTarget.ReadSnapshot().Text, Is.EqualTo("ABqwCD"));
                Assert.That(firstPanel.View.visible, Is.False); Assert.That(secondPanel.View.visible, Is.True);
                second.Handle(SkyControl.Cancel); Assert.That(secondTarget.ReadSnapshot().Text, Is.EqualTo("WXYZ"));
            }
        }
    }
    [UnityTest]
    public IEnumerator PinyinCandidateRejectedFillAndRetryUseNativePanel()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans", UserId = "fairy-ui-" + Guid.NewGuid().ToString("N") }))
        using (var target = Target("", 0, 1))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule(new[] { new LexiconWord("very", "很", "hen", 100) }));
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(PlayerRoot(0), player, "SkyPanelTest"))
            {
                var native = (GTextInput)target.Identity; native.restrict = "[a-z]";
                Assert.That(player.Open(target, runtime).Succeeded, Is.True); player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
                float deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                Assert.That(player.Session.Snapshot.Candidates.Candidates[0].Text, Is.EqualTo("很"));
                panel.View.GetChild("candidates").asCom.GetChild("candidate0").asButton.FireClick(false, true); services.Context.Drain();
                Assert.That(panel.DraftField.text, Is.EqualTo("很")); Assert.That(target.ReadSnapshot().Text, Is.Empty);
                player.Handle(SkyControl.Finish); services.Context.Drain(); Assert.That(player.Session, Is.Not.Null); Assert.That(panel.View.visible, Is.True);
                native.restrict = ""; player.Handle(SkyControl.Finish); services.Context.Drain();
                Assert.That(target.ReadSnapshot().Text, Is.EqualTo("很")); Assert.That(panel.View.visible, Is.False);
            }
        }
    }
    private IEnumerator CapturePanels()
    {
        var camera = StageCamera.main; var previousTarget = camera.targetTexture;
        var previousPosition = camera.transform.localPosition; float previousSize = camera.orthographicSize;
        float units = camera.GetComponent<StageCamera>().unitsPerPixel;
        camera.orthographicSize = 540 * units; camera.transform.localPosition = new Vector3(960 * units, -540 * units, previousPosition.z);
        var texture = new RenderTexture(1920, 1080, 24); texture.Create(); camera.targetTexture = texture;
        Stage.inst.ForceUpdate(); yield return null; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Reports")); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "fairygui-default-panels-" + Application.unityVersion + ".png"), pixels.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = previousTarget; camera.orthographicSize = previousSize; camera.transform.localPosition = previousPosition;
        texture.Release(); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(pixels);
    }
}
