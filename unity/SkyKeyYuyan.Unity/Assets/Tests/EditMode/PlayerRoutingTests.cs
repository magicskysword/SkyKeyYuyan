using System;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;

public class PlayerRoutingTests
{
    private sealed class Target : UnityTextTarget
    {
        private string _text;
        private int _anchor, _caret, _limit;
        public bool Owned;
        public Target(string text = "", int caret = 0, int limit = 0) : base(new object()) { _text = text; _anchor = _caret = caret; _limit = limit; }
        protected override bool NativeAlive => true;
        public override bool IsEditing => true;
        protected override int CharacterLimit => _limit;
        protected override bool SingleLine => false;
        protected override string ReadText() => _text;
        protected override void ReadSelection(out int anchor, out int caret) { anchor = _anchor; caret = _caret; }
        protected override void WriteText(string text, int anchor, int caret) { _text = text; WriteSelection(anchor, caret); }
        protected override void WriteSelection(int anchor, int caret) { _anchor = anchor; _caret = caret; }
        protected override void NotifyTextChanged(string text) { }
        public override void SetKeyboardOwned(bool owned) { Owned = owned; }
        public override void EndEditing() { }
    }
    private static SkyRuntime Runtime()
    {
        var runtime = new SkyRuntime(); runtime.RegisterLanguage(new BasicEnglishModule());
        Assert.That(runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, AutoSaveFrequencies = false }).Succeeded, Is.True);
        return runtime;
    }
    [Test]
    public void TwoPlayersReceiveOnlyTheirDeviceAndSelectedBackendOnce()
    {
        using (var runtime = Runtime())
        using (var first = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "one", EditingMode = EditingMode.Direct }))
        using (var second = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "two", EditingMode = EditingMode.Direct }))
        using (var router = new SkyInputRouter(SkyInputBackend.Legacy))
        using (var firstTarget = new Target())
        using (var secondTarget = new Target())
        {
            router.Register(first); router.Register(second);
            router.Bind("pad-one", SkyDeviceKind.Gamepad, "one"); router.Bind("pad-two", SkyDeviceKind.Gamepad, "two");
            Assert.That(first.Open(firstTarget, runtime).Succeeded, Is.True);
            Assert.That(second.Open(secondTarget, runtime).Succeeded, Is.True);
            Assert.That(router.Route(SkyInputBackend.InputSystem, "pad-one", SkyControl.Text, text: "wrong", sequence: 1), Is.False);
            Assert.That(router.Route(SkyInputBackend.Legacy, "pad-one", SkyControl.Text, text: "A", sequence: 1), Is.True);
            Assert.That(router.Route(SkyInputBackend.Legacy, "pad-one", SkyControl.Text, text: "A", sequence: 1), Is.False);
            router.Route(SkyInputBackend.Legacy, "pad-two", SkyControl.Text, text: "B", sequence: 1);
            Assert.That(firstTarget.ReadSnapshot().Text, Is.EqualTo("A"));
            Assert.That(secondTarget.ReadSnapshot().Text, Is.EqualTo("B"));
            Assert.Throws<InvalidOperationException>(() => router.Bind("pad-one", SkyDeviceKind.Gamepad, "two"));
        }
    }
    [Test]
    public void DeviceAndApplicationPauseConditionsMustBothBeCleared()
    {
        using (var runtime = Runtime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { EditingMode = EditingMode.Direct }))
        using (var router = new SkyInputRouter(SkyInputBackend.Custom))
        using (var target = new Target())
        {
            router.Register(player); router.Bind("pad", SkyDeviceKind.Gamepad, "player"); player.Open(target, runtime);
            router.Route(SkyInputBackend.Custom, "pad", SkyControl.Text, text: "before");
            router.SetConnected("pad", false); router.SetApplicationFocused(false); router.SetConnected("pad", true);
            Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Paused));
            Assert.That(router.Route(SkyInputBackend.Custom, "pad", SkyControl.Text, text: "lost"), Is.False);
            router.SetApplicationFocused(true); router.Route(SkyInputBackend.Custom, "pad", SkyControl.Text, text: "after");
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("beforeafter"));
        }
    }
    [Test]
    public void KeyboardPolicyControlsRoutingAndNativeOwnershipIndependently()
    {
        using (var runtime = Runtime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { EditingMode = EditingMode.Direct }))
        using (var router = new SkyInputRouter(SkyInputBackend.Custom))
        using (var target = new Target())
        {
            router.Register(player); router.Bind("keyboard", SkyDeviceKind.Keyboard, "player"); router.Bind("pad", SkyDeviceKind.Gamepad, "player"); player.Open(target, runtime);
            Assert.That(target.Owned, Is.False);
            Assert.That(router.Route(SkyInputBackend.Custom, "keyboard", SkyControl.Text, text: "disabled"), Is.False);
            player.KeyboardEnabled = true;
            Assert.That(target.Owned, Is.True);
            router.Route(SkyInputBackend.Custom, "keyboard", SkyControl.Text, text: "enabled");
            player.GamepadEnabled = false;
            Assert.That(router.Route(SkyInputBackend.Custom, "pad", SkyControl.Backspace), Is.False);
            player.KeyboardEnabled = false;
            Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Paused));
            Assert.That(target.Owned, Is.False);
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("enabled"));
        }
    }
    [Test]
    public void PanelShiftDraftFillAndCompletionShareTheSession()
    {
        using (var runtime = Runtime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions()))
        using (var target = new Target("ABCD", 2, 5))
        {
            MemoryTextTarget draft = null;
            player.DraftTargetFactory = initial => draft = new MemoryTextTarget(initial.Text, initial.Anchor, initial.Caret);
            int completions = 0; player.Completed += _ => completions++;
            Assert.That(player.Open(target, runtime).Succeeded, Is.True);
            player.Panel.ActivateKey("shift"); player.Panel.ActivateKey("key-1-0");
            player.Handle(SkyControl.Text, text: "XYZ");
            Assert.That(draft.ReadSnapshot().Text, Is.EqualTo("ABQXYZCD"));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("ABCD"));
            player.Handle(SkyControl.Finish);
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("ABQXY"));
            Assert.That(completions, Is.EqualTo(1)); Assert.That(player.Session, Is.Null);
        }
    }
}
