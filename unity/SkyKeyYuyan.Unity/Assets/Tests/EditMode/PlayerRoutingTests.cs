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
        public bool Editing = true;
        public Target(string text = "", int caret = 0, int limit = 0) : base(new object()) { _text = text; _anchor = _caret = caret; _limit = limit; }
        protected override bool NativeAlive => true;
        public override bool IsEditing => Editing;
        protected override int CharacterLimit => _limit;
        protected override bool SingleLine => false;
        protected override string ReadText() => _text;
        protected override void ReadSelection(out int anchor, out int caret) { anchor = _anchor; caret = _caret; }
        protected override void WriteText(string text, int anchor, int caret) { _text = text; WriteSelection(anchor, caret); }
        protected override void WriteSelection(int anchor, int caret) { _anchor = anchor; _caret = caret; }
        protected override void NotifyTextChanged(string text) { }
        public override void SetKeyboardOwned(bool owned) { Owned = owned; }
        public override void EndEditing() { Editing = false; }
        public void ExternalText(string text) { _text = text; NativeChanged(); }
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
    [Test]
    public void HostFollowsEditingEdgesAndKeepsExternalHostChanges()
    {
        using (var runtime = Runtime())
        using (var host = new SkyUnityHost(SkyInputBackend.Custom) { Runtime = runtime })
        using (var target = new Target())
        {
            var player = host.AddPlayer(new SkyPlayerOptions());
            host.RegisterTarget("player", new SkyTargetBinding(target)); host.Router.Bind("pad", SkyDeviceKind.Gamepad, "player");
            int opened = 0; player.Opened += _ => opened++;
            host.Tick(0); Assert.That(player.Session, Is.Not.Null);
            player.Handle(SkyControl.Text, text: "draft"); target.ExternalText("host"); host.Tick(0.1);
            Assert.That(player.Session, Is.Null); Assert.That(opened, Is.EqualTo(1));
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("host"));
            target.Editing = false; host.Tick(0.2); target.Editing = true; host.Tick(0.3);
            Assert.That(opened, Is.EqualTo(2));
        }
    }
    [Test]
    public void HostTargetDefaultsOverridePlayerModeAndNewFocusSwitchesTargets()
    {
        using (var runtime = Runtime())
        using (var host = new SkyUnityHost(SkyInputBackend.Custom) { Runtime = runtime })
        using (var first = new Target())
        using (var second = new Target { Editing = false })
        {
            var player = host.AddPlayer(new SkyPlayerOptions()); host.Router.Bind("pad", SkyDeviceKind.Gamepad, "player");
            host.RegisterTarget("player", new SkyTargetBinding(first, EditingMode.Direct)); host.RegisterTarget("player", new SkyTargetBinding(second));
            host.Tick(0); player.Handle(SkyControl.Text, text: "first");
            Assert.That(first.ReadSnapshot().Text, Is.EqualTo("first"));
            first.Editing = false; second.Editing = true; host.Tick(0.1); player.Handle(SkyControl.Text, text: "second");
            Assert.That(player.Target, Is.SameAs(second)); Assert.That(second.ReadSnapshot().Text, Is.Empty);
            player.Handle(SkyControl.Finish); Assert.That(second.ReadSnapshot().Text, Is.EqualTo("second"));
        }
    }
    [Test]
    public void FocusAndApplicationPauseMustBothRestoreBeforeInputResumes()
    {
        using (var runtime = Runtime())
        using (var host = new SkyUnityHost(SkyInputBackend.Custom) { Runtime = runtime })
        using (var target = new Target())
        {
            var player = host.AddPlayer(new SkyPlayerOptions()); host.Router.Bind("pad", SkyDeviceKind.Gamepad, "player"); host.RegisterTarget("player", new SkyTargetBinding(target));
            host.Tick(0); host.SetApplicationFocused(false); host.SetApplicationPaused(true); host.SetApplicationFocused(true);
            Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Paused));
            host.SetApplicationPaused(false); Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Active));
            host.SetSuspended(true); Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Paused));
            host.SetSuspended(false); Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Active));
        }
    }
    [Test]
    public void ReleasingHostClosesPlayerViewAndRetainsBorrowedTarget()
    {
        using (var runtime = Runtime())
        using (var target = new Target())
        {
            var host = new SkyUnityHost(SkyInputBackend.Custom) { Runtime = runtime };
            var player = host.AddPlayer(new SkyPlayerOptions()); host.RegisterTarget("player", new SkyTargetBinding(target)); host.Router.Bind("pad", SkyDeviceKind.Gamepad, "player");
            int closed = 0; player.Closed += _ => closed++;
            host.Tick(0); player.Handle(SkyControl.Text, text: "draft"); host.Dispose(); host.Dispose();
            Assert.That(closed, Is.EqualTo(1)); Assert.That(target.IsAlive, Is.True);
            Assert.That(target.ReadSnapshot().Text, Is.Empty); Assert.That(target.Editing, Is.False);
        }
    }
}
