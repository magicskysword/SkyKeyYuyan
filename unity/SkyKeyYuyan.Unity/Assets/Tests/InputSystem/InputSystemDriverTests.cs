using System;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.InputSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class InputSystemDriverTests
{
    private sealed class Target : IUnityTextTarget
    {
        private readonly MemoryTextTarget _memory = new MemoryTextTarget();
        public object Identity => _memory.Identity;
        public bool IsAlive => true;
        public bool IsEditing => true;
        public event Action<TextTargetChange> Changed { add { _memory.Changed += value; } remove { _memory.Changed -= value; } }
        public TextSnapshot ReadSnapshot() => _memory.ReadSnapshot();
        public TextEditResult ApplyEdit(TextEditRequest request) => _memory.ApplyEdit(request);
        public TextNavigationResult Navigate(TextNavigationRequest request) => _memory.Navigate(request);
        public void ReleaseSession(Guid id) => _memory.ReleaseSession(id);
        public void SetKeyboardOwned(bool owned) { }
        public void EndEditing() { }
        public void Poll() { }
        public void Dispose() { }
    }
    private static SkyRuntime Runtime()
    {
        var runtime = new SkyRuntime(); runtime.RegisterLanguage(new BasicEnglishModule());
        runtime.Initialize(new RuntimeOptions { ReplaceDefault = false, AutoSaveFrequencies = false }); return runtime;
    }
    [Test]
    public void VirtualGamepadsNavigateAndSaveIndependentDrafts()
    {
        var firstPad = InputSystem.AddDevice<Gamepad>(); var secondPad = InputSystem.AddDevice<Gamepad>();
        try
        {
            using (var runtime = Runtime())
            using (var first = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "one" }))
            using (var second = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "two" }))
            using (var router = new SkyInputRouter(SkyInputBackend.InputSystem))
            using (var driver = new SkyInputSystemDriver(router))
            {
                var firstTarget = new Target(); var secondTarget = new Target();
                router.Register(first); router.Register(second); driver.Bind(firstPad, "one"); driver.Bind(secondPad, "two");
                first.Open(firstTarget, runtime); second.Open(secondTarget, runtime);
                InputSystem.QueueStateEvent(firstPad, new GamepadState { leftStick = Vector2.down }); InputSystem.Update(); driver.Poll(0);
                Assert.That(first.Panel.State.FocusedKey, Is.EqualTo("key-1-0"));
                InputSystem.QueueStateEvent(firstPad, new GamepadState()); InputSystem.Update(); driver.Poll(0.01);
                InputSystem.QueueStateEvent(firstPad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.QueueStateEvent(secondPad, new GamepadState().WithButton(GamepadButton.South)); InputSystem.Update(); driver.Poll(0.02); driver.Poll(0.03);
                Assert.That(first.Session.Snapshot.Text.Text, Is.EqualTo("q"));
                Assert.That(second.Session.Snapshot.Text.Text, Is.EqualTo("1"));
                InputSystem.QueueStateEvent(firstPad, new GamepadState().WithButton(GamepadButton.West)); InputSystem.Update(); driver.Poll(0.04);
                Assert.That(firstTarget.ReadSnapshot().Text, Is.EqualTo("q"));
                Assert.That(first.Session, Is.Null); Assert.That(second.Session, Is.Not.Null);
                Assert.That(secondTarget.ReadSnapshot().Text, Is.Empty);
            }
        }
        finally { InputSystem.RemoveDevice(firstPad); InputSystem.RemoveDevice(secondPad); }
    }
    [Test]
    public void StickButtonsToggleTextFocusAndSticksMoveCaretAndSelection()
    {
        var pad = InputSystem.AddDevice<Gamepad>();
        try
        {
            using (var runtime = Runtime())
            using (var player = new SkyPlayerContext(new SkyPlayerOptions()))
            using (var router = new SkyInputRouter(SkyInputBackend.InputSystem))
            using (var driver = new SkyInputSystemDriver(router))
            {
                router.Register(player); driver.Bind(pad, "player"); player.Open(new Target(), runtime);
                player.Handle(SkyControl.Text, text: "A😀BC");
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.LeftStick)); InputSystem.Update(); driver.Poll(0); driver.Poll(.01);
                Assert.That(player.Panel.State.TextFocused, Is.True);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.LeftStick).WithButton(GamepadButton.RightStick)); InputSystem.Update(); driver.Poll(.011);
                Assert.That(player.Panel.State.TextFocused, Is.False);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.LeftStick)); InputSystem.Update(); driver.Poll(.012);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.LeftStick).WithButton(GamepadButton.RightStick)); InputSystem.Update(); driver.Poll(.013);
                Assert.That(player.Panel.State.TextFocused, Is.True);
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); driver.Poll(.02);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.left }); InputSystem.Update(); driver.Poll(.03);
                Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(4));
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); driver.Poll(.04);
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Vector2.left }); InputSystem.Update(); driver.Poll(.05); driver.Poll(.06);
                Assert.That(player.Session.Snapshot.Text.Anchor, Is.EqualTo(4)); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(3));
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.RightStick)); InputSystem.Update(); driver.Poll(.07);
                Assert.That(player.Panel.State.TextFocused, Is.False);
                Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(3));
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); driver.Poll(.08);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(.7f, -1) }); InputSystem.Update(); driver.Poll(.09);
                Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("key-1-0"));
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.up }); InputSystem.Update(); driver.Poll(.1);
                Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("key-0-0"));
            }
        }
        finally { InputSystem.RemoveDevice(pad); }
    }
    [Test]
    public void RightStickBrowsesCandidatesAndYConfirmsBeforeSelectChangesLanguage()
    {
        var pad = InputSystem.AddDevice<Gamepad>();
        try
        {
            using (var runtime = Runtime())
            using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans" }))
            using (var router = new SkyInputRouter(SkyInputBackend.InputSystem))
            using (var driver = new SkyInputSystemDriver(router))
            {
                runtime.RegisterLanguage(new ChinesePinyinModule(new[] { new LexiconWord("very", "很", "hen", 100), new LexiconWord("fierce", "狠", "hen", 10) }));
                runtime.InitializeLanguage("zh.full-pinyin");
                router.Register(player); driver.Bind(pad, "player"); player.Open(new Target(), runtime); player.Handle(SkyControl.Text, text: "hen");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (player.Session.Snapshot.Querying && timer.Elapsed.TotalSeconds < 5) System.Threading.Thread.Sleep(1);
                Assert.That(player.Session.Snapshot.Querying, Is.False);
                string expected = player.Session.Snapshot.Candidates.Candidates[1].Text;
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = Vector2.right }); InputSystem.Update(); driver.Poll(0); driver.Poll(.01);
                Assert.That(player.Session.Snapshot.SelectedCandidate, Is.EqualTo(1));
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); driver.Poll(.02);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.North)); InputSystem.Update(); driver.Poll(.03);
                Assert.That(player.Session.Snapshot.Text.Text, Is.EqualTo(expected)); Assert.That(player.Session.Snapshot.LanguageId, Is.EqualTo("zh-Hans"));
                InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update(); driver.Poll(.04);
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Select)); InputSystem.Update(); driver.Poll(.05);
                Assert.That(player.Session.Snapshot.LanguageId, Is.EqualTo("en"));
            }
        }
        finally { InputSystem.RemoveDevice(pad); }
    }
    [Test]
    public void FocusedMultilineEnterAddsNewlineAndControlEnterSaves()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            using (var runtime = Runtime())
            using (var player = new SkyPlayerContext(new SkyPlayerOptions()) { KeyboardEnabled = true })
            using (var router = new SkyInputRouter(SkyInputBackend.InputSystem))
            using (var driver = new SkyInputSystemDriver(router))
            {
                var target = new Target(); router.Register(player); driver.Bind(keyboard, "player"); player.Open(target, runtime); player.Handle(SkyControl.ToggleTextFocus);
                InputSystem.QueueTextEvent(keyboard, 'a'); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter)); InputSystem.Update(); driver.Poll(0);
                Assert.That(player.Session.Snapshot.Text.Text, Is.EqualTo("a\n"));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); driver.Poll(.01);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl, Key.Enter)); InputSystem.Update(); driver.Poll(.02);
                Assert.That(player.Session, Is.Null); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("a\n"));
            }
        }
        finally { InputSystem.RemoveDevice(keyboard); }
    }
    [Test]
    public void KeyboardTextWaitsForEnableAndKeepsCompleteUnicodeCharacters()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            using (var runtime = Runtime())
            using (var player = new SkyPlayerContext(new SkyPlayerOptions { EditingMode = EditingMode.Direct }))
            using (var router = new SkyInputRouter(SkyInputBackend.InputSystem))
            using (var driver = new SkyInputSystemDriver(router))
            {
                var target = new Target(); router.Register(player); driver.Bind(keyboard, "player"); player.Open(target, runtime);
                InputSystem.QueueTextEvent(keyboard, 'x'); InputSystem.Update(); driver.Poll(0);
                Assert.That(target.ReadSnapshot().Text, Is.Empty);
                player.KeyboardEnabled = true;
                InputSystem.QueueTextEvent(keyboard, 'A'); InputSystem.QueueTextEvent(keyboard, '\ud83d'); InputSystem.QueueTextEvent(keyboard, '\ude00'); InputSystem.Update(); driver.Poll(0.1);
                Assert.That(target.ReadSnapshot().Text, Is.EqualTo("A😀"));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftArrow)); InputSystem.Update(); driver.Poll(0.2);
                Assert.That(target.ReadSnapshot().Caret, Is.EqualTo(1));
            }
        }
        finally { InputSystem.RemoveDevice(keyboard); }
    }
    [Test]
    public void DisconnectingVirtualDevicePausesAndRebindingResumes()
    {
        var pad = InputSystem.AddDevice<Gamepad>();
        try
        {
            using (var runtime = Runtime())
            using (var player = new SkyPlayerContext(new SkyPlayerOptions()))
            using (var router = new SkyInputRouter(SkyInputBackend.InputSystem))
            using (var driver = new SkyInputSystemDriver(router))
            {
                router.Register(player); driver.Bind(pad, "player"); player.Open(new Target(), runtime);
                InputSystem.RemoveDevice(pad); driver.Poll(0);
                Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Paused));
                InputSystem.AddDevice(pad); driver.Poll(0.1);
                Assert.That(player.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Active));
            }
        }
        finally { if (pad.added) InputSystem.RemoveDevice(pad); }
    }
}
