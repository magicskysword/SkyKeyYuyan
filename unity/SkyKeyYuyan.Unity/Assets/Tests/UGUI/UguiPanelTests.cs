using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.UI.UGUI;
using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class UguiPanelTests
{
    private GameObject _root, _events;
    private Canvas _canvas;
    private Font _font;
    [SetUp]
    public void Setup()
    {
        _root = new GameObject("UI Tests", typeof(Canvas), typeof(GraphicRaycaster)); _canvas = _root.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _events = new GameObject("Event System", typeof(EventSystem));
        _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Malgun Gothic", "Arial Unicode MS", "Arial" }, 24);
    }
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        UnityEngine.Object.Destroy(_root); UnityEngine.Object.Destroy(_events); UnityEngine.Object.Destroy(_font); yield return null;
    }
    private RectTransform PlayerRoot(string name, float start, float end)
    {
        var root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); root.SetParent(_root.transform, false);
        root.anchorMin = new Vector2(start, 0); root.anchorMax = new Vector2(end, 1); root.offsetMin = root.offsetMax = Vector2.zero; return root;
    }
    private UguiTextTarget Target(string text, int caret, int limit = 0)
    {
        var owner = new GameObject("Original", typeof(RectTransform), typeof(Image)); owner.transform.SetParent(_root.transform, false);
        owner.GetComponent<RectTransform>().anchoredPosition = new Vector2(-3000, 0);
        var label = new GameObject("Text", typeof(RectTransform), typeof(Text)); label.transform.SetParent(owner.transform, false); label.GetComponent<Text>().font = _font;
        var input = owner.AddComponent<InputField>(); input.textComponent = label.GetComponent<Text>(); input.text = text; input.characterLimit = limit; input.selectionAnchorPosition = input.selectionFocusPosition = caret;
        return new UguiTextTarget(input);
    }
    [UnityTest]
    public IEnumerator TwoPanelsRetainNativeCaretSelectionAndPointerFocus()
    {
        using (var runtime = new SkyRuntime())
        using (var first = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "玩家一" }) { KeyboardEnabled = true })
        using (var second = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "玩家二" }) { KeyboardEnabled = true })
        using (var firstTarget = Target("ABCD", 2))
        using (var secondTarget = Target("WXYZ", 1))
        {
            runtime.RegisterLanguage(new BasicEnglishModule()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, AutoSaveFrequencies = false });
            var firstPanel = UguiSkyPanel.Create(PlayerRoot("One", 0, 0.5f), first, _font);
            var secondPanel = UguiSkyPanel.Create(PlayerRoot("Two", 0.5f, 1), second, _font);
            first.Open(firstTarget, runtime); second.Open(secondTarget, runtime);
            first.Handle(SkyControl.ToggleTextFocus); second.Handle(SkyControl.ToggleTextFocus); yield return null; yield return null;
            Assert.That(firstPanel.DraftField.isFocused, Is.True); Assert.That(secondPanel.DraftField.isFocused, Is.True);
            Assert.That(firstPanel.DraftField.caretPosition, Is.EqualTo(2)); Assert.That(secondPanel.DraftField.caretPosition, Is.EqualTo(1));
            first.Handle(SkyControl.Text, text: "qq"); first.Handle(SkyControl.TextLeft, true); second.Handle(SkyControl.SelectAll);
            var selection = second.Session.Snapshot.Text;
            var focus = EventSystem.current.currentSelectedGameObject;
            var button = firstPanel.GetComponentsInChildren<SkyPanelButton>().First(item => item.name == "key-1-1");
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            button.OnPointerDown(pointer); button.OnPointerUp(pointer); button.OnPointerClick(pointer);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(focus));
            Assert.That(first.Session.Snapshot.Text.Text, Is.EqualTo("ABqwCD"));
            Assert.That(second.Session.Snapshot.Text.Anchor, Is.EqualTo(selection.Anchor)); Assert.That(second.Session.Snapshot.Text.Caret, Is.EqualTo(selection.Caret));
            CapturePanels();
            first.Handle(SkyControl.Finish);
            Assert.That(firstTarget.ReadSnapshot().Text, Is.EqualTo("ABqwCD"));
            Assert.That(firstPanel.gameObject.activeSelf, Is.False); Assert.That(secondPanel.gameObject.activeSelf, Is.True);
            second.Handle(SkyControl.Cancel); Assert.That(secondTarget.ReadSnapshot().Text, Is.EqualTo("WXYZ"));
        }
    }
    [UnityTest]
    public IEnumerator GamepadSkipsInactiveFunctionsAndNavigatesWideSpaceByCenter()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "keyboard-navigation", InitialLanguage = "en" }))
        using (var target = Target("", 0))
        {
            runtime.RegisterLanguage(new BasicEnglishModule()); runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, AutoSaveFrequencies = false });
            var panel = UguiSkyPanel.Create(PlayerRoot("One", 0, 1), player, _font);
            player.Open(target, runtime); yield return null; yield return null;
            for (int step = 0; step < 4; step++) player.Handle(SkyControl.KeyDown);
            Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("space"));
            var buttons = panel.GetComponentsInChildren<SkyPanelButton>();
            Assert.That(buttons.Single(button => button.name == "language").interactable, Is.False);
            Assert.That(buttons.Single(button => button.name == "completion").interactable, Is.False);
            var space = buttons.Single(button => button.name == "space").GetComponent<RectTransform>();
            var letter = buttons.Single(button => button.name == "key-3-4").GetComponent<RectTransform>();
            Assert.That(space.rect.width, Is.GreaterThan(letter.rect.width * 5));
            player.Handle(SkyControl.KeyUp); Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("key-3-0"));
            Assert.That(buttons.Single(button => button.name == "key-3-0").transform.Find("FocusBadge").gameObject.activeSelf, Is.True);
            player.Handle(SkyControl.Activate); Assert.That(panel.DraftField.text, Is.EqualTo("z"));
            player.Handle(SkyControl.Finish); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("z"));
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
            var panel = UguiSkyPanel.Create(PlayerRoot("One", 0, 1), player, _font);
            foreach (int minimum in new[] { 1, 3, 5 })
            {
                player.Open(target, runtime, textInput: new SkyTextInputOptions(SkyTextInputType.MultiLine, minimum)); yield return null;
                Assert.That(((RectTransform)panel.DraftField.transform).rect.height, Is.EqualTo(minimum * 36 + 16)); player.Handle(SkyControl.Cancel);
            }
            player.Open(target, runtime); yield return null;
            Assert.That(((RectTransform)panel.DraftField.transform).rect.height, Is.EqualTo(124));
            player.Handle(SkyControl.Text, text: "第一行\n第二行\n第三行\n第四行\n第五行");
            Assert.That(((RectTransform)panel.DraftField.transform).rect.height, Is.EqualTo(196));
            int caret = player.Session.Snapshot.Text.Caret;
            player.Handle(SkyControl.ToggleTextFocus); yield return null; yield return null;
            Assert.That(panel.DraftField.isFocused, Is.True); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret));
            player.Handle(SkyControl.TextUp); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret - 4));
            player.Handle(SkyControl.SelectUp); Assert.That(player.Session.Snapshot.Text.Anchor, Is.EqualTo(caret - 4)); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret - 8));
            player.Handle(SkyControl.TextDown); player.Handle(SkyControl.TextDown);
            Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(caret));
            player.Handle(SkyControl.TextLeft); player.Handle(SkyControl.SelectLeft);
            var selected = player.Session.Snapshot.Text;
            Assert.That(selected.Anchor, Is.EqualTo(caret - 1)); Assert.That(selected.Caret, Is.EqualTo(caret - 2));
            CapturePanels("ugui-multiline-focus");
            player.Handle(SkyControl.ToggleTextFocus); yield return null;
            Assert.That(panel.DraftField.isFocused, Is.False); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(selected.Caret));
            var glyph = panel.GetComponentsInChildren<Text>().Single(text => text.name == "Glyph" && text.text == "Start");
            Assert.That(glyph.alignment, Is.EqualTo(TextAnchor.MiddleCenter)); Assert.That(glyph.alignByGeometry, Is.True);
            player.Handle(SkyControl.Cancel); player.Open(target, runtime, textInput: new SkyTextInputOptions(SkyTextInputType.SingleLine)); yield return null;
            Assert.That(panel.DraftField.lineType, Is.EqualTo(InputField.LineType.SingleLine));
            Assert.That(((RectTransform)panel.DraftField.transform).rect.height, Is.EqualTo(52)); Assert.That(player.Handle(SkyControl.Newline), Is.False);
            CapturePanels("ugui-single-line");
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
            var panel = UguiSkyPanel.Create(PlayerRoot("One", 0, 1), player, _font);
            player.Open(target, runtime); yield return null;
            player.Handle(SkyControl.SelectLeft); player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
            float deadline = Time.realtimeSinceStartup + 5;
            while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
            var preview = panel.GetComponentsInChildren<Text>(true).Single(text => text.name == "CompositionPreview");
            var popup = (RectTransform)panel.transform.Find("Frame/CandidatePopup");
            var keyboard = (RectTransform)panel.transform.Find("Frame/Keyboard");
            Assert.That(preview.text, Is.EqualTo("ABhenCD")); Assert.That(preview.gameObject.activeSelf, Is.True);
            Assert.That(panel.DraftField.text, Is.EqualTo("AB😀CD")); Assert.That(player.Session.Snapshot.Text.Text, Is.EqualTo("AB😀CD"));
            Assert.That(popup.gameObject.activeSelf, Is.True);
            yield return null; CapturePanels("ugui-inline-composition");
            player.Handle(SkyControl.ConfirmCandidate); services.Context.Drain(); yield return null;
            Assert.That(panel.DraftField.text, Is.EqualTo("AB很CD")); Assert.That(preview.gameObject.activeSelf, Is.False);
            Assert.That(popup.gameObject.activeSelf, Is.False); Assert.That(panel.DraftField.textComponent.color.a, Is.EqualTo(1));
            foreach (var type in new[] { SkyTextInputType.MultiLine, SkyTextInputType.SingleLine })
            {
                player.Handle(SkyControl.Cancel); player.Open(target, runtime, textInput: new SkyTextInputOptions(type));
                player.Session.Process(InputOperation.SwitchLanguage("en")); player.Handle(SkyControl.SelectAll); player.Handle(SkyControl.Clear);
                string text = type == SkyTextInputType.MultiLine ? "第一行\n第二行\n第三行\n第四行\n第五行\n第六行\n第七行\n" : new string('W', 120);
                player.Handle(SkyControl.Text, text: text); player.Session.Process(InputOperation.SwitchLanguage("zh-Hans")); player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
                deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                Assert.That(preview.text, Is.EqualTo(text + "hen")); Assert.That(panel.DraftField.text, Is.EqualTo(text));
                Assert.That(popup.anchoredPosition.x, Is.GreaterThanOrEqualTo(24));
                Assert.That(popup.anchoredPosition.x + popup.rect.width, Is.LessThanOrEqualTo(1096));
                Assert.That(-popup.anchoredPosition.y + popup.rect.height, Is.LessThanOrEqualTo(-keyboard.anchoredPosition.y - 8));
                Assert.That(preview.rectTransform.anchoredPosition.y, Is.GreaterThanOrEqualTo(-8));
                yield return null; Canvas.ForceUpdateCanvases();
                var vertices = preview.cachedTextGenerator.verts;
                Assert.That(vertices.Count, Is.GreaterThanOrEqualTo(8));
                Vector3 lastGlyph = Vector3.zero;
                for (int index = vertices.Count - 8; index < vertices.Count - 4; index++) lastGlyph += vertices[index].position / 4;
                var bodyRect = (RectTransform)panel.DraftField.transform;
                Assert.That(bodyRect.rect.Contains(bodyRect.InverseTransformPoint(preview.rectTransform.TransformPoint(lastGlyph / preview.pixelsPerUnit))), Is.True);
                CapturePanels(type == SkyTextInputType.MultiLine ? "ugui-inline-multiline" : "ugui-inline-single-line");
                Assert.That(panel.DraftField.PreparePointerFocus(), Is.True); services.Context.Drain();
                Assert.That(panel.DraftField.text, Is.EqualTo(text + "很")); Assert.That(popup.gameObject.activeSelf, Is.False);
            }
            player.Handle(SkyControl.Cancel); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("AB😀CD"));
        }
    }
    [UnityTest]
    public IEnumerator FullDictionarySupportsRepeatedPartialChoicesAndUndo()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans" }))
        using (var target = Target("AB", 1))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule());
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            var panel = UguiSkyPanel.Create(PlayerRoot("One", 0, 1), player, _font);
            player.Open(target, runtime); player.Handle(SkyControl.Text, text: "nihaoshijie");
            foreach (string choice in new[] { "你", "好", "世界" })
            {
                float deadline = Time.realtimeSinceStartup + 5;
                while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                var snapshot = player.Session.Snapshot;
                Assert.That(snapshot.Querying, Is.False); Assert.That(snapshot.SelectedCandidate, Is.EqualTo(0));
                int index = snapshot.Candidates.Candidates.ToList().FindIndex(candidate => candidate.Text == choice);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                for (int step = 0; step < index; step++) player.Handle(SkyControl.NextCandidate);
                var button = panel.GetComponentsInChildren<SkyPanelButton>().Single(item => item.name == "Candidate" + index);
                Assert.That(button.interactable, Is.True); Assert.That(button.GetComponentInChildren<Text>().text, Is.EqualTo(choice));
                player.Handle(SkyControl.ConfirmCandidate); services.Context.Drain(); yield return null;
                if (choice == "世界") break;
                string raw = choice == "你" ? "haoshijie" : "shijie";
                Assert.That(player.Session.Snapshot.Composition.RawText, Is.EqualTo(raw));
                Assert.That(panel.DraftField.text, Is.EqualTo("AB"));
                var preview = panel.GetComponentsInChildren<Text>(true).Single(text => text.name == "CompositionPreview");
                Assert.That(preview.text, Is.EqualTo("A" + player.Session.Snapshot.Composition.DisplayText + "B"));
                if (choice == "你")
                {
                    player.Handle(SkyControl.Backspace); Assert.That(player.Session.Snapshot.Composition.RawText, Is.EqualTo("nihaoshijie"));
                    deadline = Time.realtimeSinceStartup + 5;
                    while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
                    index = player.Session.Snapshot.Candidates.Candidates.ToList().FindIndex(candidate => candidate.Text == "你");
                    for (int step = 0; step < index; step++) player.Handle(SkyControl.NextCandidate);
                    player.Handle(SkyControl.ConfirmCandidate); services.Context.Drain();
                }
            }
            Assert.That(panel.DraftField.text, Is.EqualTo("A你好世界B")); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("AB"));
            Assert.That(player.Session.Snapshot.Composition.IsEmpty, Is.True);
            Assert.That(panel.transform.Find("Frame/CandidatePopup").gameObject.activeSelf, Is.False);
            CapturePanels("ugui-continuous-selection");
            player.Handle(SkyControl.Finish); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("A你好世界B"));
        }
    }
    [UnityTest]
    public IEnumerator CandidatesFitTheirTextAndKeepGamepadSelectionVisible()
    {
        var words = new[] { "很", "狠", "痕", "很好", "很多", "很快", "很慢", "很高兴", "很有趣", "很漂亮", "很方便", "很久以前", "很久很久以前", "很长时间以后", "很高兴认识你", "很久没有见面了", "很想和你聊聊天", "很高兴今天能见到你", "很久没有收到你的来信", "很高兴能在这里和大家一起交流" };
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans" }))
        using (var target = Target("", 0))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule(words.Select((word, index) => new LexiconWord("candidate-" + index, word, "hen", 100 - index))));
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            var panel = UguiSkyPanel.Create(PlayerRoot("One", 0, 1), player, _font);
            player.Open(target, runtime); yield return null; yield return null;
            player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
            float deadline = Time.realtimeSinceStartup + 5;
            while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
            Assert.That(player.Session.Snapshot.Candidates.Candidates.Count, Is.EqualTo(20));
            var buttons = panel.GetComponentsInChildren<SkyPanelButton>(true).Where(button => button.transform.parent.name == "Candidates").ToArray();
            var first = buttons.Single(button => button.name == "Candidate0");
            var phrase = buttons.Single(button => button.name == "Candidate7");
            Assert.That(first.GetComponent<RectTransform>().rect.width, Is.LessThan(70));
            Assert.That(phrase.GetComponent<RectTransform>().rect.width, Is.GreaterThan(first.GetComponent<RectTransform>().rect.width));
            foreach (var button in buttons.Where(button => button.gameObject.activeSelf))
            {
                var rect = button.GetComponent<RectTransform>(); var label = button.GetComponentInChildren<Text>();
                Assert.That(label.rectTransform.rect.width, Is.GreaterThanOrEqualTo(label.preferredWidth));
                Assert.That(rect.anchoredPosition.x + rect.rect.width, Is.LessThanOrEqualTo(SkyKeyboardStyle.CandidateStripWidth));
            }
            CapturePanels("ugui-compact-candidates");
            for (int index = 0; index < 19; index++) player.Handle(SkyControl.NextCandidate);
            Assert.That(player.Session.Snapshot.SelectedCandidate, Is.EqualTo(19));
            var last = buttons.Single(button => button.name == "Candidate19");
            Assert.That(first.gameObject.activeSelf, Is.False); Assert.That(last.gameObject.activeSelf, Is.True);
            var lastRect = last.GetComponent<RectTransform>(); var lastText = last.GetComponentInChildren<Text>();
            Assert.That(lastText.rectTransform.rect.width, Is.GreaterThanOrEqualTo(lastText.preferredWidth));
            Assert.That(lastRect.anchoredPosition.x + lastRect.rect.width, Is.LessThanOrEqualTo(SkyKeyboardStyle.CandidateStripWidth));
            CapturePanels("ugui-long-candidate");
            player.Handle(SkyControl.Backspace); services.Context.Drain(); deadline = Time.realtimeSinceStartup + 5;
            while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
            Assert.That(first.gameObject.activeSelf, Is.True);
            for (int index = 0; index < 19; index++) player.Handle(SkyControl.NextCandidate);
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            last.OnPointerClick(pointer); services.Context.Drain();
            Assert.That(panel.DraftField.text, Is.EqualTo(lastText.text));
        }
    }
    [UnityTest]
    public IEnumerator PinyinCandidateAndRejectedDraftFillCanBeCorrected()
    {
        using (var runtime = new SkyRuntime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans", UserId = "ui-test-" + Guid.NewGuid().ToString("N") }))
        using (var target = Target("", 0, 1))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule(new[] { new LexiconWord("very", "很", "hen", 100), new LexiconWord("fierce", "狠", "hen", 10), new LexiconWord("mark", "痕", "hen", 5) }));
            var services = SkyUnityServices.Shared;
            runtime.Initialize(new RuntimeOptions { Resources = services.Resources, ExecutionContext = services.Context, ReplaceDefault = false, AutoSaveFrequencies = false });
            var panel = UguiSkyPanel.Create(PlayerRoot("One", 0, 1), player, _font);
            bool accepts = false; ((InputField)target.Identity).onValidateInput = (_, __, value) => accepts ? value : '\0';
            player.Open(target, runtime); yield return null; yield return null;
            player.Handle(SkyControl.Text, text: "hen"); services.Context.Drain();
            float deadline = Time.realtimeSinceStartup + 5;
            while (player.Session.Snapshot.Querying && Time.realtimeSinceStartup < deadline) { services.Context.Drain(); yield return null; }
            Assert.That(player.Session.Snapshot.Candidates.Candidates[0].Text, Is.EqualTo("很"));
            CapturePanels("ugui-pinyin-keyboard");
            player.Handle(SkyControl.ConfirmCandidate); services.Context.Drain();
            Assert.That(panel.DraftField.text, Is.EqualTo("很")); Assert.That(target.ReadSnapshot().Text, Is.Empty);
            player.Handle(SkyControl.Finish); services.Context.Drain();
            Assert.That(player.Session, Is.Not.Null); Assert.That(panel.gameObject.activeSelf, Is.True);
            accepts = true; player.Handle(SkyControl.Finish); services.Context.Drain();
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("很")); Assert.That(panel.gameObject.activeSelf, Is.False);
        }
    }
    private void CapturePanels(string name = "ugui-default-panels")
    {
        var cameraObject = new GameObject("UI Render", typeof(Camera)); var camera = cameraObject.GetComponent<Camera>();
        var texture = new RenderTexture(1920, 1080, 24); texture.Create(); camera.targetTexture = texture; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(9, 13, 22, 255);
        _canvas.renderMode = RenderMode.ScreenSpaceCamera; _canvas.worldCamera = camera; _canvas.planeDistance = 1; Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../artifacts/Reports")); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + "-" + Application.unityVersion + ".png"), pixels.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; texture.Release(); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(pixels); UnityEngine.Object.Destroy(cameraObject);
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.worldCamera = null;
    }
}
