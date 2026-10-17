using System;
using System.Collections;
using System.IO;
using System.Linq;
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
    public IEnumerator KeyboardInputUsesNativeFieldAndCandidatePopupWithoutTheVirtualPanel()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { KeyboardInput = new SkyKeyboardInputOptions(SkyKeyboardInputMode.SkyIme, initialLanguage: "zh-Hans") }))
        using (var target = Target("AB", 1))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule(new[] { new LexiconWord("hello", "你好", "ni hao") }));
            var services = SkyUnityServices.Shared; runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(PlayerRoot(0), player, "SkyPanelTest"))
            {
                var field = (GTextInput)target.Identity; field.SetXY(100, 300); field.SetSize(640, 130);
                var format = new TextFormat { font = "SkyPanelTest", size = 26, color = Color.white }; field.textFormat = format;
                Stage.inst.SetFocus(field.displayObject); yield return null;
                field.SetSelection(1, 0);
                Assert.That(player.OpenKeyboard(target, runtime).Succeeded, Is.True); Assert.That(panel.View.visible, Is.False);
                player.Handle(SkyControl.Text, text: "nihao");
                float deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                Assert.That(field.text, Is.EqualTo("AB"));
                Assert.That(field.parent.GetChild("compositionViewport").asCom.GetChild("SkyCompositionPreview").asTextField.text, Is.EqualTo("Ani'haoB"));
                Assert.That(GRoot.inst.GetChild("SkyInlineCandidates").visible, Is.True); yield return CapturePanels("fairygui-keyboard-inline");
                var candidate = GRoot.inst.GetChild("SkyInlineCandidates").asCom.GetChild("Candidate0").asCom;
                var candidateText = candidate.GetChildAt(1).asTextField;
                Assert.That(candidateText.textWidth, Is.LessThanOrEqualTo(candidateText.width)); Assert.That(candidateText.textHeight, Is.LessThanOrEqualTo(candidateText.height));
                player.Handle(SkyControl.ConfirmCandidate); services.Context.Drain();
                Assert.That(field.text, Is.EqualTo("A你好B")); Assert.That(player.Session, Is.Not.Null);
                player.Handle(SkyControl.Finish); Assert.That(field.editable, Is.True); Assert.That(field.inputTextField.alpha, Is.EqualTo(1));
            }
        }
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
    public IEnumerator GamepadNavigatesWideSpaceAndDisplaysNativeFocusBadge()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "fairy-navigation", InitialLanguage = "en" }))
        using (var router = new SkyInputRouter(SkyInputBackend.Custom))
        using (var target = Target("", 0))
        {
            runtime.RegisterLanguage(new BasicEnglishModule()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(PlayerRoot(0), player, "SkyPanelTest"))
            {
                player.Open(target, runtime); yield return null;
                var firstKey = panel.View.GetChild("keyboard").asCom.GetChild("key-0-0").asButton;
                Assert.That(firstKey.GetChild("focusBadge").visible, Is.False);
                router.Register(player); router.Bind("pad", SkyDeviceKind.Gamepad, player.PlayerId);
                for (int step = 0; step < 4; step++) player.Handle(SkyControl.KeyDown);
                Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("space"));
                var keys = panel.View.GetChild("keyboard").asCom;
                Assert.That(keys.GetChild("language").touchable, Is.False); Assert.That(keys.GetChild("completion").touchable, Is.False);
                var letter = keys.GetChild("key-3-4").asButton;
                Assert.That(keys.GetChild("space").width, Is.GreaterThan(letter.width * 5));
                player.Handle(SkyControl.KeyUp); Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("key-3-0"));
                var selected = keys.GetChild("key-3-0").asButton;
                Assert.That(selected.GetChild("focusOutline").visible, Is.True); Assert.That(selected.GetChild("focusBadge").visible, Is.True);
                router.SetConnected("pad", false); Assert.That(selected.GetChild("focusBadge").visible, Is.False);
                router.SetConnected("pad", true); Assert.That(selected.GetChild("focusBadge").visible, Is.True);
                player.Handle(SkyControl.Activate); Assert.That(panel.DraftField.text, Is.EqualTo("z"));
                player.Handle(SkyControl.Finish); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("z"));
            }
        }
    }
    [UnityTest]
    public IEnumerator TextViewportRespectsLineOptionsAndFocusPreservesCaretAndSelection()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions()))
        using (var target = Target("", 0))
        {
            runtime.RegisterLanguage(new BasicEnglishModule()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(_root, player, "SkyPanelTest"))
            {
                foreach (int minimum in new[] { 1, 3, 5 })
                {
                    player.Open(target, runtime, textInput: new SkyTextInputOptions(SkyTextInputType.MultiLine, minimum)); yield return null;
                    Assert.That(panel.View.GetChild("bodyBackground").height, Is.EqualTo(minimum * 36 + 16)); player.Handle(SkyControl.Cancel);
                }
                player.Open(target, runtime); yield return null; Assert.That(panel.View.GetChild("bodyBackground").height, Is.EqualTo(124));
                player.Handle(SkyControl.Text, text: "第一行\n第二行\n第三行\n第四行\n第五行");
                Assert.That(panel.View.GetChild("bodyBackground").height, Is.EqualTo(196));
                int caret = player.Session.Snapshot.Text.Caret;
                player.Handle(SkyControl.ToggleTextFocus); yield return null;
                Assert.That(panel.DraftField.inputTextField.focused, Is.True); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret));
                player.Handle(SkyControl.TextUp); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret - 4));
                player.Handle(SkyControl.SelectUp); Assert.That(player.Session.Snapshot.Text.Anchor, Is.EqualTo(caret - 4)); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret - 8));
                player.Handle(SkyControl.TextDown); player.Handle(SkyControl.TextDown);
                Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret));
                player.Handle(SkyControl.TextLeft); player.Handle(SkyControl.SelectLeft);
                var selected = player.Session.Snapshot.Text;
                Assert.That(selected.Anchor, Is.EqualTo(caret - 1)); Assert.That(selected.Caret, Is.EqualTo(caret - 2));
                yield return CapturePanels("fairygui-multiline-focus");
                player.Handle(SkyControl.ToggleTextFocus); yield return null;
                Assert.That(panel.DraftField.inputTextField.focused, Is.False); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(selected.Caret));
                Assert.That(panel.View.GetChild("hintGlyph4").asTextField.verticalAlign, Is.EqualTo(VertAlignType.Middle));
                player.Handle(SkyControl.Cancel); player.Open(target, runtime, textInput: new SkyTextInputOptions(SkyTextInputType.SingleLine)); yield return null;
                Assert.That(panel.DraftField.singleLine, Is.True); Assert.That(panel.View.GetChild("bodyBackground").height, Is.EqualTo(52));
                Assert.That(player.Handle(SkyControl.Newline), Is.False); yield return CapturePanels("fairygui-single-line");
            }
        }
    }
    [UnityTest]
    public IEnumerator InlineCompositionPreservesTextAndFloatingCandidatesFollowVisibleCaret()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans" }))
        using (var target = Target("AB😀CD", 4))
        {
            runtime.RegisterLanguage(new BasicEnglishModule());
            runtime.RegisterLanguage(new ChinesePinyinModule(new[] { new LexiconWord("very", "很", "hen", 100), new LexiconWord("fierce", "狠", "hen", 10) }));
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(PlayerRoot(0), player, "SkyPanelTest"))
            {
                player.Open(target, runtime); yield return null;
                player.Handle(SkyControl.SelectLeft); player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
                float deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                var viewport = panel.View.GetChild("compositionViewport").asCom;
                var preview = viewport.GetChild("compositionPreview").asTextField;
                var popup = panel.View.GetChild("candidatePopup").asCom; var keyboard = panel.View.GetChild("keyboard");
                Assert.That(preview.text, Is.EqualTo("ABhenCD")); Assert.That(viewport.visible, Is.True);
                Assert.That(panel.DraftField.text, Is.EqualTo("AB😀CD")); Assert.That(player.Session.Snapshot.Text.Text, Is.EqualTo("AB😀CD"));
                Assert.That(popup.visible, Is.True);
                yield return CapturePanels("fairygui-inline-composition");
                player.Handle(SkyControl.ConfirmCandidate); services.Context.Drain(); yield return null;
                Assert.That(panel.DraftField.text, Is.EqualTo("AB很CD")); Assert.That(viewport.visible, Is.False);
                Assert.That(popup.visible, Is.False); Assert.That(panel.DraftField.inputTextField.alpha, Is.EqualTo(1));
                foreach (var type in new[] { SkyTextInputType.MultiLine, SkyTextInputType.SingleLine })
                {
                    player.Handle(SkyControl.Cancel); player.Open(target, runtime, textInput: new SkyTextInputOptions(type));
                    player.Session.Process(InputOperation.SwitchLanguage("en")); player.Handle(SkyControl.SelectAll); player.Handle(SkyControl.Clear);
                    string text = type == SkyTextInputType.MultiLine ? "第一行\n第二行\n第三行\n第四行\n第五行\n第六行\n第七行\n" : new string('W', 120);
                    player.Handle(SkyControl.Text, text: text); player.Session.Process(InputOperation.SwitchLanguage("zh-Hans")); player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
                    deadline = Time.realtimeSinceStartup + 5;
                    while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                    Assert.That(preview.text, Is.EqualTo(text + "hen")); Assert.That(panel.DraftField.text, Is.EqualTo(text));
                    Assert.That(popup.x, Is.GreaterThanOrEqualTo(24)); Assert.That(popup.x + popup.width, Is.LessThanOrEqualTo(1096));
                    Assert.That(popup.y + popup.height, Is.LessThanOrEqualTo(keyboard.y - 8));
                    Assert.That(preview.y, Is.LessThanOrEqualTo(0));
                    yield return CapturePanels(type == SkyTextInputType.MultiLine ? "fairygui-inline-multiline" : "fairygui-inline-single-line");
                    player.Session.Process(InputOperation.CommitComposition()); services.Context.Drain();
                    Assert.That(panel.DraftField.text, Is.EqualTo(text + "很")); Assert.That(popup.visible, Is.False);
                }
                player.Handle(SkyControl.Cancel); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("AB😀CD"));
            }
        }
    }
    [UnityTest]
    public IEnumerator FullDictionaryRefreshesCandidatesAfterEachPartialChoice()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans" }))
        using (var target = Target("AB", 1))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule());
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(PlayerRoot(0), player, "SkyPanelTest"))
            {
                player.Open(target, runtime); player.Handle(SkyControl.Text, text: "nihaoshijie");
                foreach (string choice in new[] { "你", "好", "世界" })
                {
                    float deadline = Time.realtimeSinceStartup + 5;
                    while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                    var snapshot = player.Session.Snapshot;
                    Assert.That(snapshot.Querying, Is.False); Assert.That(snapshot.SelectedCandidate, Is.EqualTo(0));
                    if (choice == "好") yield return CapturePanels("fairygui-partial-selection");
                    int index = snapshot.Candidates.Candidates.ToList().FindIndex(candidate => candidate.Text == choice);
                    Assert.That(index, Is.GreaterThanOrEqualTo(0));
                    for (int step = 0; step < index; step++) player.Handle(SkyControl.NextCandidate);
                    var button = panel.View.GetChild("candidatePopup").asCom.GetChild("candidates").asCom.GetChild("candidate" + index).asButton;
                    Assert.That(button.visible, Is.True); Assert.That(button.title, Is.EqualTo(choice));
                    if (choice == "你") button.FireClick(false, true); else player.Handle(SkyControl.ConfirmCandidate);
                    services.Context.Drain(); yield return null;
                    if (choice == "世界") break;
                    Assert.That(player.Session.Snapshot.Composition.RawText, Is.EqualTo(choice == "你" ? "haoshijie" : "shijie"));
                    Assert.That(panel.DraftField.text, Is.EqualTo("AB"));
                    Assert.That(panel.View.GetChild("compositionViewport").asCom.GetChild("compositionPreview").asTextField.text,
                        Is.EqualTo("A" + player.Session.Snapshot.Composition.DisplayText + "B"));
                }
                Assert.That(panel.DraftField.text, Is.EqualTo("A你好世界B")); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("AB"));
                Assert.That(panel.View.GetChild("candidatePopup").visible, Is.False);
                yield return CapturePanels("fairygui-continuous-selection");
                player.Handle(SkyControl.Finish); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("A你好世界B"));
            }
        }
    }
    [UnityTest]
    public IEnumerator CandidatesUseNativeTextWidthsAndFollowGamepadSelection()
    {
        var words = new[] { "很", "狠", "痕", "很好", "很多", "很快", "很慢", "很高兴", "很有趣", "很漂亮", "很方便", "很久以前", "很久很久以前", "很长时间以后", "很高兴认识你", "很久没有见面了", "很想和你聊聊天", "很高兴今天能见到你", "很久没有收到你的来信", "很高兴能在这里和大家一起交流" };
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans" }))
        using (var target = Target("", 0))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule(words.Select((word, index) => new LexiconWord("candidate-" + index, word, "hen", 100 - index))));
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(PlayerRoot(0), player, "SkyPanelTest"))
            {
                player.Open(target, runtime); yield return null;
                player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
                float deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                Assert.That(player.Session.Snapshot.Candidates.Candidates.Count, Is.EqualTo(20));
                var candidates = panel.View.GetChild("candidatePopup").asCom.GetChild("candidates").asCom;
                var first = candidates.GetChild("candidate0").asButton; var phrase = candidates.GetChild("candidate7").asButton;
                Assert.That(first.width, Is.LessThan(70)); Assert.That(phrase.width, Is.GreaterThan(first.width));
                foreach (var button in candidates.GetChildren().Where(button => button.visible).Select(button => button.asButton))
                {
                    Assert.That(button.GetTextField().width, Is.GreaterThanOrEqualTo(button.GetTextField().textWidth));
                    Assert.That(button.x + button.width, Is.LessThanOrEqualTo(SkyKeyboardStyle.CandidateStripWidth));
                }
                yield return CapturePanels("fairygui-compact-candidates");
                for (int index = 0; index < 19; index++) player.Handle(SkyControl.NextCandidate);
                Assert.That(player.Session.Snapshot.SelectedCandidate, Is.EqualTo(19));
                var last = candidates.GetChild("candidate19").asButton;
                Assert.That(first.visible, Is.False); Assert.That(last.visible, Is.True);
                Assert.That(last.GetTextField().width, Is.GreaterThanOrEqualTo(last.GetTextField().textWidth));
                Assert.That(last.x + last.width, Is.LessThanOrEqualTo(SkyKeyboardStyle.CandidateStripWidth));
                yield return CapturePanels("fairygui-long-candidate");
                player.Handle(SkyControl.Backspace); services.Context.Drain(); deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                Assert.That(first.visible, Is.True);
                for (int index = 0; index < 19; index++) player.Handle(SkyControl.NextCandidate);
                string expected = last.title; last.FireClick(false, true); services.Context.Drain();
                Assert.That(panel.DraftField.text, Is.EqualTo(expected));
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
            runtime.RegisterLanguage(new ChinesePinyinModule(new[] { new LexiconWord("very", "很", "hen", 100), new LexiconWord("fierce", "狠", "hen", 10), new LexiconWord("mark", "痕", "hen", 5) }));
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            using (var panel = FairySkyPanel.Create(PlayerRoot(0), player, "SkyPanelTest"))
            {
                var native = (GTextInput)target.Identity; native.restrict = "[a-z]";
                Assert.That(player.Open(target, runtime).Succeeded, Is.True); player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
                float deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                Assert.That(player.Session.Snapshot.Candidates.Candidates[0].Text, Is.EqualTo("很"));
                yield return CapturePanels("fairygui-pinyin-keyboard");
                panel.View.GetChild("candidatePopup").asCom.GetChild("candidates").asCom.GetChild("candidate0").asButton.FireClick(false, true); services.Context.Drain();
                Assert.That(panel.DraftField.text, Is.EqualTo("很")); Assert.That(target.ReadSnapshot().Text, Is.Empty);
                player.Handle(SkyControl.Finish); services.Context.Drain(); Assert.That(player.Session, Is.Not.Null); Assert.That(panel.View.visible, Is.True);
                native.restrict = ""; player.Handle(SkyControl.Finish); services.Context.Drain();
                Assert.That(target.ReadSnapshot().Text, Is.EqualTo("很")); Assert.That(panel.View.visible, Is.False);
            }
        }
    }
    private IEnumerator CapturePanels(string name = "fairygui-default-panels")
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
        File.WriteAllBytes(Path.Combine(directory, name + "-" + Application.unityVersion + ".png"), pixels.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = previousTarget; camera.orthographicSize = previousSize; camera.transform.localPosition = previousPosition;
        texture.Release(); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(pixels);
    }
}
