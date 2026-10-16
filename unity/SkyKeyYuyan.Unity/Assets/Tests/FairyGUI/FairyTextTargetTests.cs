using System;
using FairyGUI;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.FairyGUI;
using UnityEngine;

public class FairyTextTargetTests
{
    private GTextInput _field;
    private GComponent _parent;
    [SetUp]
    public void Setup()
    {
        _parent = new GComponent(); GRoot.inst.AddChild(_parent);
        _field = new GTextInput { text = "ABCD", maxLength = 5 }; _parent.AddChild(_field);
        _field.SetSize(240, 150);
        _field.SetSelection(2, 0);
    }
    [TearDown]
    public void Cleanup() { _parent.Dispose(); }
    [Test]
    public void InsertionKeepsSuffixAndDuplicateRequestIsCached()
    {
        using (var target = new FairyTextTarget(_field))
        {
            var request = new TextEditRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, 2, 0, "XYZ");
            var result = target.ApplyEdit(request);
            Assert.That(result.Status, Is.EqualTo(TextEditStatus.Truncated));
            Assert.That(result.Snapshot.Text, Is.EqualTo("ABXCD"));
            Assert.That(target.ApplyEdit(request), Is.SameAs(result));
        }
    }
    [Test]
    public void NativeCharacterLimitCountsSurrogatePairOnce()
    {
        _field.text = "A"; _field.maxLength = 2; _field.SetSelection(1, 0);
        using (var target = new FairyTextTarget(_field))
        {
            var result = target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, 1, 0, "😀B"));
            Assert.That(result.Snapshot.Text, Is.EqualTo("A😀"));
            Assert.That(result.Snapshot.Caret, Is.EqualTo(3));
            Assert.That(_field.caretPosition, Is.EqualTo(2));
            var left = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 2), result.Snapshot.Version, TextNavigation.Left));
            Assert.That(left.Snapshot.Caret, Is.EqualTo(1));
        }
    }
    [Test]
    public void ReverseSelectionPreservesBothEndpoints()
    {
        _field.SetSelection(3, 0);
        using (var target = new FairyTextTarget(_field))
        {
            var first = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, TextNavigation.Left, true));
            var second = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 2), first.Snapshot.Version, TextNavigation.Left, true));
            Assert.That(second.Snapshot.Anchor, Is.EqualTo(3));
            Assert.That(second.Snapshot.Caret, Is.EqualTo(1));
            Assert.That(_field.inputTextField.GetSelection(), Is.EqualTo("BC"));
        }
    }
    [Test]
    public void NavigationDoesNotTakeOtherTargetsFocus()
    {
        using (var target = new FairyTextTarget(_field))
        {
            var other = new GTextInput { text = "other" }; _parent.AddChild(other); other.SetSize(200, 50); other.SetSelection(2, 0);
            try
            {
                var focus = Stage.inst.focus;
                Assert.That(focus, Is.SameAs(other.inputTextField));
                target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, TextNavigation.Left));
                Assert.That(Stage.inst.focus, Is.SameAs(focus));
            }
            finally { other.Dispose(); }
        }
    }
    [Test]
    public void VerticalNavigationUsesNativeLinePositions()
    {
        _field.singleLine = false; _field.text = "abcd\nabcd\nabcd"; _field.SetSelection(3, 0);
        using (var target = new FairyTextTarget(_field))
        {
            var first = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, TextNavigation.Down, true));
            var second = target.Navigate(new TextNavigationRequest(new OperationId(Guid.NewGuid(), 2), first.Snapshot.Version, TextNavigation.Down, true));
            Assert.That(first.Snapshot.Caret, Is.EqualTo(8));
            Assert.That(second.Snapshot.Caret, Is.EqualTo(13));
            Assert.That(second.Snapshot.Anchor, Is.EqualTo(3));
        }
    }
    [Test]
    public void NativeRestrictionRejectsInvalidCommitAndAllowsCorrection()
    {
        _field.text = "12"; _field.restrict = "[0-9]";
        using (var target = new FairyTextTarget(_field))
        {
            var before = target.ReadSnapshot();
            Assert.That(target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 1), before.Version, 2, 0, "a")).Status, Is.EqualTo(TextEditStatus.Rejected));
            Assert.That(target.ApplyEdit(new TextEditRequest(new OperationId(Guid.NewGuid(), 2), before.Version, 2, 0, "3")).Snapshot.Text, Is.EqualTo("123"));
        }
    }
    [Test]
    public void HostBodyChangesAndDisposalAreReportedOnce()
    {
        using (var target = new FairyTextTarget(_field))
        {
            target.ReadSnapshot(); int changes = 0, invalid = 0;
            target.Changed += change => { if (change.Invalidated) invalid++; else changes++; };
            _field.text = "host"; target.Poll(); target.Poll();
            Assert.That(changes, Is.EqualTo(1));
            _field.Dispose(); target.Poll(); target.Poll();
            Assert.That(invalid, Is.EqualTo(1));
        }
    }
    [Test]
    public void NotificationFailureCannotRepeatAcceptedWrite()
    {
        using (var target = new FairyTextTarget(_field))
        {
            int notifications = 0, diagnostics = 0;
            _field.onChanged.Add(() => { notifications++; throw new InvalidOperationException("listener"); });
            target.Diagnostic += _ => diagnostics++;
            var request = new TextEditRequest(new OperationId(Guid.NewGuid(), 1), target.ReadSnapshot().Version, 2, 0, "X");
            var result = target.ApplyEdit(request);
            Assert.That(result.Status, Is.EqualTo(TextEditStatus.Accepted));
            Assert.That(target.ApplyEdit(request), Is.SameAs(result));
            Assert.That(notifications, Is.EqualTo(1)); Assert.That(diagnostics, Is.EqualTo(1));
        }
    }
    [Test]
    public void KeyboardOwnershipStopsNativeNavigationAndRestoresOptions()
    {
        using (var target = new FairyTextTarget(_field))
        {
            bool editable = _field.editable, keyboard = _field.keyboardInput, ime = _field.disableIME;
            int nativeCalls = 0; _field.inputTextField.onKeyDown.Add(() => nativeCalls++);
            target.SetKeyboardOwned(true);
            _field.inputTextField.BubbleEvent("onKeyDown", new InputEvent());
            Assert.That(nativeCalls, Is.Zero);
            target.SetKeyboardOwned(false);
            Assert.That(_field.editable, Is.EqualTo(editable)); Assert.That(_field.keyboardInput, Is.EqualTo(keyboard)); Assert.That(_field.disableIME, Is.EqualTo(ime));
        }
    }
}
