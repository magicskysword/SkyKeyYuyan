using System;
using System.Collections.Generic;
using System.Linq;
using FairyGUI;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.FairyGUI;
using SkyKeyYuyan.Unity;
using UnityEngine;

namespace SkyKeyYuyan.UI.FairyGUI
{
    /// <summary>
    /// 从原生 FairyGUI 工程资源创建候选、键盘和独立草稿面板。
    /// </summary>
    public sealed class FairySkyPanel : IDisposable
    {
        private sealed class KeyView { internal KeyboardKey Key; internal GButton Button; }
        private sealed class ButtonSkin
        {
            internal GGraph Background;
            internal GObject Outline, Badge, Glyph;
            internal bool Initialized, Focused;
            internal Color Fill, Edge;
        }
        private const string PackageId = "7b0bbb69", PackageName = "SkyKeyYuyan";
        private readonly SkyPlayerContext _player;
        private readonly GComponent _parent, _keyboard, _candidates, _candidatePopup;
        private readonly GGraph _candidateBackground;
        private readonly GButton _previousCandidate, _nextCandidate;
        private readonly GTextField _title, _status;
        private readonly GTextInput _body;
        private readonly FairyDraftSelection _draftSelection;
        private readonly FairyCompositionView _inline;
        private readonly Dictionary<string, string> _fonts = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<KeyView> _keys = new List<KeyView>();
        private readonly List<GButton> _candidateButtons = new List<GButton>();
        private readonly List<float> _candidateWidths = new List<float>();
        private CandidatePage _candidatePage;
        private string _candidateFont;
        private readonly Func<TextSnapshot, ITextTarget> _factory, _previousFactory;
        private FairyTextTarget _draft;
        private SkyPanelController _controller;
        private KeyboardLayout _layout;
        private bool _disposed, _draftActive;
        private bool _bodyFocused;
        private float _bodyHeight;
        private string _measuredBodyText;
        private SkyTextInputOptions _bodyOptions;
        private readonly GGraph _bodyBackground;
        /// <summary>
        /// 完整面板的原生根组件。
        /// </summary>
        public GComponent View { get; }
        /// <summary>
        /// 当前原生草稿控件。
        /// </summary>
        public GTextInput DraftField => _body;
        /// <summary>
        /// 在玩家没有活动输入时创建并绑定面板。
        /// </summary>
        public static FairySkyPanel Create(GComponent parent, SkyPlayerContext player, string mixedFont = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (player.Session != null) throw new InvalidOperationException("绑定面板前需要结束活动输入");
            if (UIPackage.GetById(PackageId) == null) UIPackage.AddPackage("SkyKeyYuyan/UI/FairyGUI/SkyKeyYuyan");
            return new FairySkyPanel(parent, player, mixedFont);
        }
        private FairySkyPanel(GComponent parent, SkyPlayerContext player, string mixedFont)
        {
            _parent = parent; _player = player;
            View = UIPackage.CreateObject(PackageName, "Panel").asCom; parent.AddChild(View);
            View.displayObject.gameObject.AddComponent<SkyInputSurface>();
            _title = View.GetChild("title").asTextField; _status = View.GetChild("status").asTextField;
            _body = (GTextInput)View.GetChild("body"); _body.maxLength = 0;
            _bodyBackground = View.GetChild("bodyBackground").asGraph; _bodyHeight = _bodyBackground.height;
            _body.onTouchBegin.AddCapture(context => { if (!PrepareBodyPointer()) context.StopPropagation(); else _controller?.SetTextFocused(true); });
            _keyboard = View.GetChild("keyboard").asCom; _candidates = View.GetChild("candidates").asCom;
            _keyboard.RemoveChildren(0, -1, true); _candidates.RemoveChildren(0, -1, true);
            if (!string.IsNullOrEmpty(mixedFont)) SetFont(View, mixedFont);
            _draftSelection = new FairyDraftSelection(_body);
            _inline = new FairyCompositionView(_body, View.GetChild("compositionPreview").asTextField);
            _candidatePopup = new GComponent { name = "candidatePopup" }; View.AddChild(_candidatePopup);
            _candidateBackground = View.GetChild("candidateBackground").asGraph;
            _previousCandidate = View.GetChild("previousCandidate").asButton; _nextCandidate = View.GetChild("nextCandidate").asButton;
            _candidatePopup.AddChild(_candidateBackground); _candidatePopup.AddChild(_candidates); _candidatePopup.AddChild(_previousCandidate); _candidatePopup.AddChild(_nextCandidate);
            _candidateBackground.SetXY(0, 0); _candidates.SetXY(8, 8); _previousCandidate.SetSize(36, 52); _nextCandidate.SetSize(36, 52);
            _candidatePopup.visible = false;
            Wire("cancel", SkyControl.Cancel); Wire("finish", SkyControl.Finish); Wire("language", SkyControl.Language); Wire("confirm", SkyControl.ConfirmCandidate);
            Wire("previousCandidate", SkyControl.PreviousCandidate); Wire("nextCandidate", SkyControl.NextCandidate);
            _factory = CreateDraft; _previousFactory = player.DraftTargetFactory; player.DraftTargetFactory = _factory;
            player.Opened += OnOpened; player.Closed += OnClosed; parent.onSizeChanged.Add(Fit);
            var pump = View.displayObject.gameObject.AddComponent<FairyPanelPump>(); pump.Panel = this;
            View.visible = false; Fit();
        }
        /// <summary>
        /// 配置候选和虚拟按键的语言字体。
        /// </summary>
        public void SetLanguageFont(string language, string font)
        { _fonts[language] = font; if (_controller != null) Render(_controller.State); }
        private ITextTarget CreateDraft(TextSnapshot initial)
        {
            _draft?.Dispose(); _draftActive = true; _body.editable = true; _body.text = initial.Text;
            _body.singleLine = _player.TextInput.Type == SkyTextInputType.SingleLine;
            _draft = new FairyTextTarget(_body, initial.Anchor, initial.Caret);
            return _draft;
        }
        private void OnOpened(SkyPlayerContext player)
        {
            _controller = player.Panel; _controller.Changed += Render; View.visible = true; Fit(); Render(_controller.State);
        }
        private void OnClosed(SkyPlayerContext player)
        {
            if (_controller != null) _controller.Changed -= Render; _controller = null;
            if (_body.inputTextField.focused) Stage.inst.SetFocus(null);
            _draftActive = false; View.visible = false;
            _bodyFocused = false;
        }
        private void Wire(string name, SkyControl control)
        {
            var button = (View.GetChild(name) ?? _candidatePopup.GetChild(name)).asButton; button.focusable = false;
            button.onClick.Add(() => _player.Handle(control));
        }
        private bool PrepareBodyPointer()
        {
            var session = _player.Session;
            if (session == null) return false;
            if (!session.Snapshot.Composition.IsEmpty) session.Process(InputOperation.CommitComposition());
            return session.Snapshot.Composition.IsEmpty && !session.Snapshot.NeedsResolution;
        }
        private void Render(SkyPanelState state)
        {
            if (_disposed) return;
            var snapshot = state.Session; var language = _player.Session?.Languages.FirstOrDefault(item => item.LanguageId == snapshot.LanguageId);
            string font = _fonts.TryGetValue(snapshot.LanguageId, out var configured) && !string.IsNullOrEmpty(configured) ? configured : _body.textFormat.font;
            _title.text = _player.PlayerId + "   ·   " + (language?.DisplayName ?? snapshot.LanguageId) + (snapshot.Lifecycle == SessionLifecycle.Paused ? "   已暂停" : snapshot.Literal ? "   字面输入" : "");
            var composition = new SkyCompositionText(snapshot);
            if (!_draftActive) { _body.editable = false; _body.touchable = false; _body.text = snapshot.Text.Text; }
            else _body.touchable = true;
            RenderBody(state, composition); _inline.Render(composition);
            if (!ReferenceEquals(_layout, state.Layout)) { _layout = state.Layout; BuildKeys(); }
            foreach (var key in _keys)
            {
                bool enabled = _controller.IsKeyEnabled(key.Key);
                bool active = enabled && (key.Key.Action == "shift" && state.Shift || key.Key.Action == "completion" && snapshot.CompletionEnabled);
                key.Button.title = SkyKeyboardStyle.KeyLabel(key.Key, state.Shift, snapshot.CompletionEnabled);
                if (key.Key.Action == "completion" && (language == null || (language.Capabilities & LanguageCapabilities.CompletionSwitch) == 0)) key.Button.title = key.Key.Label;
                Style(key.Button, font, !state.TextFocused && key.Key.Id == state.FocusedKey, enabled, key.Key, active);
            }
            RenderCandidates(snapshot, font);
            var result = state.LastResult;
            _status.text = snapshot.NeedsResolution ? "请确认正文是否已写入，再选择继续方式" : snapshot.Querying ? "正在查找候选…"
                : state.Problem != null ? "键盘暂不可用，可取消或完成编辑" : result?.Edit?.Status == TextEditStatus.Truncated ? "已按长度上限接受可容纳的文本"
                : result?.Status == OperationStatus.Rejected ? "操作未被接受，请修正内容后重试" : state.TextFocused ? "左摇杆移动光标 · 右摇杆框选 · 按摇杆返回键盘" : "右摇杆选择候选 · X 选词 · 按摇杆编辑正文";
            View.GetChild("hintLabel0").asTextField.text = state.TextFocused ? "光标" : "选键";
            View.GetChild("hintLabel3").asTextField.text = state.TextFocused ? "框选" : "候选";
            View.GetChild("hintLabel4").asTextField.text = state.TextFocused ? "键盘" : "正文";
            foreach (string name in new[] { "hint1", "hintGlyph1", "hintLabel1" }) View.GetChild(name).alpha = state.TextFocused ? .35f : 1;
            _draftSelection.Render(snapshot.Text, state.TextFocused && !composition.Active);
        }
        private void RenderBody(SkyPanelState state, SkyCompositionText composition)
        {
            if (_bodyFocused != state.TextFocused)
            {
                _bodyFocused = state.TextFocused;
                if (_draftActive) { if (_bodyFocused) _body.RequestFocus(); else if (_body.inputTextField.focused) Stage.inst.SetFocus(null); }
                _bodyBackground.shape.DrawRoundRect(state.TextFocused ? 3 : 1, state.TextFocused ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Border, SkyKeyboardStyle.Inset, 10, 10, 10, 10);
            }
            var options = _player.TextInput; string text = composition.Text;
            if (_measuredBodyText == text && ReferenceEquals(_bodyOptions, options)) return;
            _measuredBodyText = text; _bodyOptions = options; _body.singleLine = options.Type == SkyTextInputType.SingleLine;
            float height = SkyKeyboardStyle.BodyHeight(options, _inline.Measure(text)), delta = height - _bodyHeight;
            if (delta == 0) return;
            _bodyHeight = height; View.SetSize(View.width, View.height + delta);
            View.GetChild("background").SetSize(View.width, View.height);
            _bodyBackground.SetSize(_bodyBackground.width, height); _body.SetSize(_body.width, height - 16);
            foreach (var child in View.GetChildren())
                if (child.name != "background" && child.name != "title" && child.name != "cancel" && child.name != "bodyBackground" && child.name != "body" && child.name != "compositionViewport" && child != _candidatePopup) child.y += delta;
            Fit();
        }
        private void BuildKeys()
        {
            _keyboard.RemoveChildren(0, -1, true); _keys.Clear();
            if (_layout == null) return;
            foreach (var key in _layout.Keys)
            {
                string id = key.Id; var area = SkyKeyboardStyle.KeyRect(_layout, key);
                var button = NewButton(_keyboard, id, area.x, area.y, area.width, area.height);
                button.onClick.Add(() => _controller?.ActivateKey(id)); _keys.Add(new KeyView { Key = key, Button = button });
            }
        }
        private void RenderCandidates(SessionSnapshot snapshot, string font)
        {
            var page = snapshot.Candidates; int count = page?.Candidates.Count ?? 0;
            while (_candidateButtons.Count < count)
            {
                int index = _candidateButtons.Count; var button = NewButton(_candidates, "candidate" + index, 0, 0, 100, 52);
                button.onClick.Add(() =>
                {
                    var current = _player.Session?.Snapshot;
                    if (current == null || current.Querying || current.Candidates == null || index >= current.Candidates.Candidates.Count) return;
                    _player.Session.Process(InputOperation.ConfirmCandidate(current.Candidates.Query.Id, current.Candidates.Candidates[index].Id));
                });
                _candidateButtons.Add(button);
            }
            if (!ReferenceEquals(_candidatePage, page) || _candidateFont != font)
            {
                _candidatePage = page; _candidateFont = font; _candidateWidths.Clear();
                for (int index = 0; index < count; index++)
                {
                    var label = _candidateButtons[index].GetTextField(); SetFont(label, font);
                    label.autoSize = AutoSizeType.None; label.singleLine = true; label.text = page.Candidates[index].Text;
                    _candidateWidths.Add(SkyKeyboardStyle.CandidateWidth(label.textWidth));
                    label.autoSize = AutoSizeType.Shrink;
                }
            }
            SkyKeyboardStyle.CandidateRange(_candidateWidths, snapshot.SelectedCandidate, out int first, out int end);
            float x = 0;
            for (int index = 0; index < _candidateButtons.Count; index++)
            {
                var button = _candidateButtons[index]; button.visible = index >= first && index < end;
                if (!button.visible) continue;
                float width = _candidateWidths[index]; button.SetXY(x, 0); button.SetSize(width, 52); button.title = page.Candidates[index].Text;
                Style(button, font, index == snapshot.SelectedCandidate, !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution, candidate: true);
                x += width + SkyKeyboardStyle.CandidateGap;
            }
            _candidatePopup.visible = count > 0 && !snapshot.Composition.IsEmpty;
            _previousCandidate.touchable = _nextCandidate.touchable = !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution;
            if (!_candidatePopup.visible) return;
            _previousCandidate.SetXY(x + 8, 8); _nextCandidate.SetXY(x + 52, 8);
            var popup = SkyKeyboardStyle.CandidatePopupRect(_inline.Caret, _inline.LineHeight, x + 96, _keyboard.y);
            _candidatePopup.SetXY(popup.x, popup.y); _candidatePopup.SetSize(popup.width, popup.height);
            _candidateBackground.SetSize(popup.width, popup.height); _candidateBackground.shape.DrawRoundRect(1, SkyKeyboardStyle.Border, SkyKeyboardStyle.Inset, 10, 10, 10, 10);
        }
        private static GButton NewButton(GComponent parent, string name, float x, float y, float width, float height)
        {
            var button = UIPackage.CreateObject(PackageName, "Key").asButton; button.name = name; button.focusable = false;
            parent.AddChild(button); button.SetXY(x, y); button.SetSize(width, height); return button;
        }
        private static void Style(GButton button, string font, bool focused, bool enabled, KeyboardKey key = null, bool active = false, bool candidate = false)
        {
            var text = button.GetTextField(); SetFont(text, font); button.touchable = enabled; button.alpha = enabled ? 1 : 0.32f;
            var skin = button.data as ButtonSkin;
            if (skin == null)
            {
                skin = new ButtonSkin { Background = button.GetChild("background").asGraph, Outline = button.GetChild("focusOutline"), Badge = button.GetChild("focusBadge"), Glyph = button.GetChild("focusGlyph") };
                button.data = skin;
            }
            bool frame = focused && key != null;
            Color fill = key != null ? SkyKeyboardStyle.KeyColor(key, focused, active) : focused ? candidate ? SkyKeyboardStyle.Active : SkyKeyboardStyle.Accent : SkyKeyboardStyle.Function;
            Color edge = frame ? SkyKeyboardStyle.DarkText : focused ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Border;
            if (!skin.Initialized || skin.Fill != fill || skin.Edge != edge || skin.Focused != frame)
            {
                skin.Background.shape.DrawRoundRect(frame ? 5 : candidate && focused ? 2 : 1, edge, fill, 8, 8, 8, 8);
                skin.Fill = fill; skin.Edge = edge; skin.Focused = frame; skin.Initialized = true;
            }
            if (skin.Outline != null) skin.Outline.visible = frame;
            if (skin.Badge != null) skin.Badge.visible = frame && enabled;
            if (skin.Glyph != null) skin.Glyph.visible = frame && enabled;
            var format = text.textFormat; Color foreground = candidate && focused ? SkyKeyboardStyle.Accent : focused || key?.Action == "finish" ? SkyKeyboardStyle.DarkText : SkyKeyboardStyle.Text;
            if (format.color != foreground) { format.color = foreground; text.textFormat = format; }
        }
        private static void SetFont(GObject value, string font)
        {
            if (value is GTextField field) { var format = field.textFormat; if (format.font != font) { format.font = font; field.textFormat = format; } }
            else if (value is GComponent component) foreach (var child in component.GetChildren()) SetFont(child, font);
        }
        private void Fit()
        {
            if (_disposed || View.isDisposed) return;
            float scale = Mathf.Max(0.01f, Mathf.Min(1, _parent.width / (SkyKeyboardStyle.Width + 32), _parent.height / (View.height + 32))); View.SetScale(scale, scale);
            View.SetXY((_parent.width - SkyKeyboardStyle.Width * scale) / 2, (_parent.height - View.height * scale) / 2);
        }
        internal void Tick()
        {
            if (_disposed) return;
            if (View.isDisposed) { Dispose(); return; }
            if (_player.Session != null && View.visible) _draftSelection.Render(_player.Session.Snapshot.Text, _player.Panel.State.TextFocused && _player.Session.Snapshot.Composition.IsEmpty);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose()
        {
            if (_disposed) return;
            if (_player.Session != null) _player.Handle(SkyControl.Cancel);
            _disposed = true; if (_controller != null) _controller.Changed -= Render;
            _player.Opened -= OnOpened; _player.Closed -= OnClosed; _parent.onSizeChanged.Remove(Fit);
            if (ReferenceEquals(_player.DraftTargetFactory, _factory)) _player.DraftTargetFactory = _previousFactory;
            _draft?.Dispose(); _draftSelection.Dispose(); _inline.Dispose(); if (!View.isDisposed) View.Dispose();
        }
    }
}
