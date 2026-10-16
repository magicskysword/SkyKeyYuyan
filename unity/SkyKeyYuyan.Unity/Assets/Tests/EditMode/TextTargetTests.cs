using System;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.UGUI;
using SkyKeyYuyan.Unity.TMP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UnityTextTargetTests
{
    private GameObject _root;
    private Font _font;
    private TMP_FontAsset _tmpFont;
    [SetUp]
    public void Setup()
    {
        _root = new GameObject("Target Tests", typeof(Canvas));
        _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
    }
    [TearDown]
    public void Cleanup()
    {
        UnityEngine.Object.DestroyImmediate(_root);
        if (_tmpFont != null) UnityEngine.Object.DestroyImmediate(_tmpFont);
        UnityEngine.Object.DestroyImmediate(_font);
    }
    private UnityTextTarget Target(bool tmp, string text = "ABCD", int anchor = 2, int caret = 2, int limit = 5, bool multiLine = false)
    {
        var owner = new GameObject("Input", typeof(RectTransform)); owner.transform.SetParent(_root.transform, false);
        var label = new GameObject("Text", typeof(RectTransform)); label.transform.SetParent(owner.transform, false);
        if (tmp)
        {
            _tmpFont = TMP_FontAsset.CreateFontAsset(_font);
            var component = label.AddComponent<TextMeshProUGUI>(); component.font = _tmpFont;
            var input = owner.AddComponent<TMP_InputField>(); input.textComponent = component; input.textViewport = owner.GetComponent<RectTransform>();
            if (multiLine) input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.text = text; input.characterLimit = limit; input.selectionStringAnchorPosition = anchor; input.selectionStringFocusPosition = caret;
            return new TmpTextTarget(input);
        }
        else
        {
            var component = label.AddComponent<Text>(); component.font = _font; component.supportRichText = false;
            var input = owner.AddComponent<InputField>(); input.textComponent = component;
            if (multiLine) input.lineType = InputField.LineType.MultiLineNewline;
            input.text = text; input.characterLimit = limit; input.selectionAnchorPosition = anchor; input.selectionFocusPosition = caret;
            return new UguiTextTarget(input);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void InsertionKeepsUnselectedSuffixAndReplaysOnce(bool tmp)
    {
        using (var target = Target(tmp))
        {
            var snapshot = target.ReadSnapshot(); var operation = new OperationId(Guid.NewGuid(), 1);
            var request = new TextEditRequest(operation, snapshot.Version, snapshot.SelectionStart, snapshot.SelectionLength, "XYZ");
            var result = target.ApplyEdit(request);
            Assert.That(result.Status, Is.EqualTo(TextEditStatus.Truncated));
            Assert.That(result.Snapshot.Text, Is.EqualTo("ABXCD"));
            Assert.That(target.ApplyEdit(request), Is.SameAs(result));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void WholeDraftFillUsesWholeTextLimit(bool tmp)
    {
        using (var target = Target(tmp))
        {
            var result = target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, 0, 4, "ABXYZCD"));
            Assert.That(result.Snapshot.Text, Is.EqualTo("ABXYZ"));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void NativeValidationRejectionRetainsBodyAndConfiguration(bool tmp)
    {
        using (var target = Target(tmp, "123", 3, 3, 0))
        {
            if (tmp) _root.GetComponentInChildren<TMP_InputField>().contentType = TMP_InputField.ContentType.IntegerNumber;
            else _root.GetComponentInChildren<InputField>().contentType = InputField.ContentType.IntegerNumber;
            var before = target.ReadSnapshot();
            var result = target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 1), before.Version, 3, 0, "a"));
            Assert.That(result.Status, Is.EqualTo(TextEditStatus.Rejected));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("123"));
            var accepted = target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 2), before.Version, 3, 0, "4"));
            Assert.That(accepted.Snapshot.Text, Is.EqualTo("1234"));
            Assert.That(tmp ? (int)_root.GetComponentInChildren<TMP_InputField>().contentType : (int)_root.GetComponentInChildren<InputField>().contentType, Is.EqualTo(2));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ExternalChangesAndDestroyedControlAreReported(bool tmp)
    {
        using (var target = Target(tmp))
        {
            target.ReadSnapshot(); int external = 0, invalid = 0;
            target.Changed += change => { if (change.Invalidated) invalid++; else external++; };
            if (tmp) _root.GetComponentInChildren<TMP_InputField>().text = "host";
            else _root.GetComponentInChildren<InputField>().text = "host";
            target.Poll(); Assert.That(external, Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(_root.transform.GetChild(0).gameObject);
            target.Poll(); target.Poll(); Assert.That(invalid, Is.EqualTo(1));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void Utf16SelectionDoesNotSplitSurrogatePairs(bool tmp)
    {
        using (var target = Target(tmp, "A😀B", 3, 3, 0))
        {
            var navigation = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, TextNavigation.Left));
            Assert.That(navigation.Snapshot.Caret, Is.EqualTo(1));
            var result = target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 2), navigation.Snapshot.Version, 1, 2, ""));
            Assert.That(result.Snapshot.Text, Is.EqualTo("AB"));
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void NotificationFailureKeepsOneWriteAndOneNotification(bool tmp)
    {
        using (var target = Target(tmp, "AB", 1, 1, 0))
        {
            int notifications = 0, external = 0, diagnostics = 0;
            target.Changed += _ => external++;
            target.Diagnostic += _ => diagnostics++;
            UnityEngine.Events.UnityAction<string> listener = _ => { notifications++; throw new InvalidOperationException("listener"); };
            if (tmp) _root.GetComponentInChildren<TMP_InputField>().onValueChanged.AddListener(listener);
            else _root.GetComponentInChildren<InputField>().onValueChanged.AddListener(listener);
            var request = new TextEditRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, 1, 0, "x");
            var result = target.ApplyEdit(request);
            Assert.That(result.Status, Is.EqualTo(TextEditStatus.Accepted));
            Assert.That(target.ApplyEdit(request), Is.SameAs(result));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("AxB"));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(diagnostics, Is.EqualTo(1));
            Assert.That(external, Is.Zero);
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void SessionUsesCurrentNativeSelection(bool tmp)
    {
        using (var target = Target(tmp, "ABCD", 2, 2, 0))
        using (var runtime = new SkyRuntime())
        {
            runtime.RegisterLanguage(new BasicEnglishModule());
            runtime.Initialize(new RuntimeOptions { ReplaceDefault = false });
            using (var session = runtime.OpenSession(new SessionOptions { EditingMode = EditingMode.Direct }, target).Session)
            {
                if (tmp) { var input = _root.GetComponentInChildren<TMP_InputField>(); input.selectionStringAnchorPosition = 4; input.selectionStringFocusPosition = 4; }
                else { var input = _root.GetComponentInChildren<InputField>(); input.selectionAnchorPosition = 4; input.selectionFocusPosition = 4; }
                Assert.That(session.Process(InputOperation.Insert("X")).Status, Is.EqualTo(OperationStatus.Applied));
                Assert.That(target.ReadSnapshot().Text, Is.EqualTo("ABCDX"));
            }
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void VerticalNavigationPreservesAnchorAndDesiredColumn(bool tmp)
    {
        using (var target = Target(tmp, "abcd\nabcd\nabcd", 3, 3, 0, true))
        {
            if (tmp)
            {
                var input = _root.GetComponentInChildren<TMP_InputField>(); input.lineType = TMP_InputField.LineType.MultiLineNewline;
                input.textComponent.rectTransform.sizeDelta = new Vector2(200, 150); input.textComponent.fontSize = 16;
            }
            else
            {
                var input = _root.GetComponentInChildren<InputField>(); input.lineType = InputField.LineType.MultiLineNewline;
                input.textComponent.rectTransform.sizeDelta = new Vector2(200, 150);
            }
            var first = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, TextNavigation.Down, true));
            var second = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 2), first.Snapshot.Version, TextNavigation.Down, true));
            Assert.That(first.Snapshot.Caret, Is.EqualTo(8));
            Assert.That(second.Snapshot.Caret, Is.EqualTo(13));
            Assert.That(second.Snapshot.Anchor, Is.EqualTo(3));
        }
    }
    [Test]
    public void TmpRegexUsesSerializedNativePattern()
    {
        using (var target = Target(true, "12", 2, 2, 0))
        {
            var input = _root.GetComponentInChildren<TMP_InputField>();
            JsonUtility.FromJsonOverwrite("{\"m_RegexValue\":\"[0-9]\"}", input);
            input.characterValidation = TMP_InputField.CharacterValidation.Regex;
            var before = target.ReadSnapshot();
            Assert.That(target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 1), before.Version, 2, 0, "x")).Status, Is.EqualTo(TextEditStatus.Rejected));
            Assert.That(target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 2), before.Version, 2, 0, "3")).Snapshot.Text, Is.EqualTo("123"));
        }
    }
}
