using System;
using System.Collections.Generic;
using NUnit.Framework;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Languages.Chinese;
using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.Editor;
using SkyKeyYuyan.Unity.Legacy;
using UnityEditor;
using UnityEngine;

public class LegacyDriverTests
{
    private sealed class Source : ILegacyInputSource
    {
        public long Frame { get; set; }
        public string Text { get; set; } = "";
        public string[] Names = { "one", "two" };
        public readonly Dictionary<string, float> Axes = new Dictionary<string, float>();
        public readonly HashSet<KeyCode> Keys = new HashSet<KeyCode>();
        public string[] JoystickNames() => Names;
        public float Axis(string name) => Axes.TryGetValue(name, out var value) ? value : 0;
        public bool Key(KeyCode key) => Keys.Contains(key);
    }
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
    public void LegacySlotsNavigateSeparateDraftsAndReconnectWithoutLosingText()
    {
        var source = new Source();
        using (var runtime = Runtime())
        using (var first = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "one" }))
        using (var second = new SkyPlayerContext(new SkyPlayerOptions { PlayerId = "two" }))
        using (var router = new SkyInputRouter(SkyInputBackend.Legacy))
        using (var driver = new SkyLegacyInputDriver(router, source))
        {
            router.Register(first); router.Register(second); driver.BindGamepad(1, "one"); driver.BindGamepad(2, "two");
            var firstTarget = new Target(); var secondTarget = new Target(); first.Open(firstTarget, runtime); second.Open(secondTarget, runtime);
            source.Axes["SkyPad1.LY"] = -1; driver.Poll(0);
            source.Axes.Clear(); driver.Poll(0.01);
            source.Keys.Add(KeyCode.Joystick1Button0); source.Keys.Add(KeyCode.Joystick2Button0); driver.Poll(0.02); driver.Poll(0.03);
            Assert.That(first.Session.Snapshot.Text.Text, Is.EqualTo("q")); Assert.That(second.Session.Snapshot.Text.Text, Is.EqualTo("1"));
            source.Names[0] = ""; source.Keys.Clear(); source.Keys.Add(KeyCode.Joystick2Button2); driver.Poll(0.04);
            Assert.That(first.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Paused));
            Assert.That(secondTarget.ReadSnapshot().Text, Is.EqualTo("1"));
            source.Names[0] = "one"; source.Keys.Clear(); driver.Poll(0.05);
            Assert.That(first.Session.Snapshot.Lifecycle, Is.EqualTo(SessionLifecycle.Active));
            Assert.That(first.Session.Snapshot.Text.Text, Is.EqualTo("q")); Assert.That(firstTarget.ReadSnapshot().Text, Is.Empty);
        }
    }
    [Test]
    public void StickFocusSeparatesCaretMovementFromSelectionAndKeyboardNavigation()
    {
        var source = new Source();
        using (var runtime = Runtime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions()))
        using (var router = new SkyInputRouter(SkyInputBackend.Legacy))
        using (var driver = new SkyLegacyInputDriver(router, source))
        {
            router.Register(player); driver.BindGamepad(1, "player"); player.Open(new Target(), runtime); player.Handle(SkyControl.Text, text: "A😀BC");
            source.Keys.Add(KeyCode.Joystick1Button8); driver.Poll(0); driver.Poll(.01); Assert.That(player.Panel.State.TextFocused, Is.True);
            source.Keys.Add(KeyCode.Joystick1Button9); driver.Poll(.011); Assert.That(player.Panel.State.TextFocused, Is.False);
            source.Keys.Remove(KeyCode.Joystick1Button9); driver.Poll(.012); source.Keys.Add(KeyCode.Joystick1Button9); driver.Poll(.013); Assert.That(player.Panel.State.TextFocused, Is.True);
            source.Keys.Clear(); driver.Poll(.02); source.Axes["SkyPad1.LX"] = -1; driver.Poll(.03);
            Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(4));
            source.Axes.Clear(); driver.Poll(.04); source.Axes["SkyPad1.RX"] = -1; driver.Poll(.05); driver.Poll(.06);
            Assert.That(player.Session.Snapshot.Text.Anchor, Is.EqualTo(4)); Assert.That(player.Session.Snapshot.Text.Caret, Is.EqualTo(3));
            source.Axes.Clear(); source.Keys.Add(KeyCode.Joystick1Button9); driver.Poll(.07); Assert.That(player.Panel.State.TextFocused, Is.False);
            source.Keys.Clear(); driver.Poll(.08); source.Axes["SkyPad1.LX"] = .7f; source.Axes["SkyPad1.LY"] = -1; driver.Poll(.09);
            Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("key-1-0"));
            source.Axes.Clear(); source.Axes["SkyPad1.LY"] = 1; driver.Poll(.1);
            Assert.That(player.Panel.State.FocusedKey, Is.EqualTo("key-0-0"));
        }
    }
    [Test]
    public void RightStickBrowsesCandidatesAndYConfirmsBeforeSelectChangesLanguage()
    {
        var source = new Source();
        using (var runtime = Runtime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { InitialLanguage = "zh-Hans" }))
        using (var router = new SkyInputRouter(SkyInputBackend.Legacy))
        using (var driver = new SkyLegacyInputDriver(router, source))
        {
            runtime.RegisterLanguage(new ChinesePinyinModule(new[] { new LexiconWord("very", "很", "hen", 100), new LexiconWord("fierce", "狠", "hen", 10) }));
            runtime.InitializeLanguage("zh.full-pinyin");
            router.Register(player); driver.BindGamepad(1, "player"); player.Open(new Target(), runtime); player.Handle(SkyControl.Text, text: "hen");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (player.Session.Snapshot.Querying && timer.Elapsed.TotalSeconds < 5) System.Threading.Thread.Sleep(1);
            Assert.That(player.Session.Snapshot.Querying, Is.False);
            string expected = player.Session.Snapshot.Candidates.Candidates[1].Text;
            source.Axes["SkyPad1.RX"] = 1; driver.Poll(0); driver.Poll(.01); Assert.That(player.Session.Snapshot.SelectedCandidate, Is.EqualTo(1));
            source.Axes.Clear(); driver.Poll(.02); source.Keys.Add(KeyCode.Joystick1Button3); driver.Poll(.03);
            Assert.That(player.Session.Snapshot.Text.Text, Is.EqualTo(expected)); Assert.That(player.Session.Snapshot.LanguageId, Is.EqualTo("zh-Hans"));
            source.Keys.Clear(); driver.Poll(.04); source.Keys.Add(KeyCode.Joystick1Button6); driver.Poll(.05);
            Assert.That(player.Session.Snapshot.LanguageId, Is.EqualTo("en"));
        }
    }
    [Test]
    public void FocusedMultilineEnterAddsNewlineAndControlEnterSaves()
    {
        var source = new Source { Text = "a", Frame = 1 };
        using (var runtime = Runtime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions()) { KeyboardEnabled = true })
        using (var router = new SkyInputRouter(SkyInputBackend.Legacy))
        using (var driver = new SkyLegacyInputDriver(router, source))
        {
            var target = new Target(); router.Register(player); driver.BindKeyboard("player"); player.Open(target, runtime); player.Handle(SkyControl.ToggleTextFocus);
            source.Keys.Add(KeyCode.Return); driver.Poll(0); Assert.That(player.Session.Snapshot.Text.Text, Is.EqualTo("a\n"));
            source.Keys.Clear(); source.Text = ""; source.Frame++; driver.Poll(.01);
            source.Keys.Add(KeyCode.LeftControl); source.Keys.Add(KeyCode.Return); driver.Poll(.02);
            Assert.That(player.Session, Is.Null); Assert.That(target.ReadSnapshot().Text, Is.EqualTo("a\n"));
        }
    }
    [Test]
    public void SameLegacyKeyboardFrameIsConsumedOnceAndShiftExtendsUnicodeSelection()
    {
        var source = new Source { Text = "A😀B", Frame = 1 };
        using (var runtime = Runtime())
        using (var player = new SkyPlayerContext(new SkyPlayerOptions { EditingMode = EditingMode.Direct }) { KeyboardEnabled = true })
        using (var router = new SkyInputRouter(SkyInputBackend.Legacy))
        using (var driver = new SkyLegacyInputDriver(router, source))
        {
            var target = new Target(); router.Register(player); driver.BindKeyboard("player"); player.Open(target, runtime);
            driver.Poll(0); driver.Poll(0.01);
            Assert.That(target.ReadSnapshot().Text, Is.EqualTo("A😀B"));
            source.Text = ""; source.Frame++; source.Keys.Add(KeyCode.LeftShift); source.Keys.Add(KeyCode.LeftArrow); driver.Poll(0.02);
            source.Keys.Remove(KeyCode.LeftArrow); driver.Poll(0.03); source.Keys.Add(KeyCode.LeftArrow); driver.Poll(0.04);
            Assert.That(target.ReadSnapshot().Anchor, Is.EqualTo(4)); Assert.That(target.ReadSnapshot().Caret, Is.EqualTo(1));
        }
    }
    [Test]
    public void DefaultAxisSetupIsIdempotentAndKeepsExistingValues()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
        int before = settings.FindProperty("m_Axes").arraySize;
        var existing = new List<string>();
        for (int index = 0; index < before; index++) existing.Add(AxisIdentity(settings.FindProperty("m_Axes").GetArrayElementAtIndex(index)));
        SkyLegacyInputSetup.ConfigureDefaults(); settings.Update(); int configured = settings.FindProperty("m_Axes").arraySize;
        SkyLegacyInputSetup.ConfigureDefaults(); settings.Update();
        Assert.That(settings.FindProperty("m_Axes").arraySize, Is.EqualTo(configured));
        Assert.That(configured, Is.GreaterThanOrEqualTo(before));
        Assert.That(configured, Is.GreaterThanOrEqualTo(56));
        for (int index = 0; index < before; index++) Assert.That(AxisIdentity(settings.FindProperty("m_Axes").GetArrayElementAtIndex(index)), Is.EqualTo(existing[index]));
    }
    private static string AxisIdentity(SerializedProperty entry) => entry.FindPropertyRelative("m_Name").stringValue + ":" + entry.FindPropertyRelative("axis").intValue + ":" + entry.FindPropertyRelative("joyNum").intValue + ":" + entry.FindPropertyRelative("invert").boolValue;
}
