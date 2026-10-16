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
        private readonly GComponent _parent, _keyboard, _candidates;
        private readonly GTextField _title, _composition, _selection, _status;
        private readonly GTextInput _body;
        private readonly FairyDraftSelection _draftSelection;
        private readonly Dictionary<string, string> _fonts = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<KeyView> _keys = new List<KeyView>();
        private readonly List<GButton> _candidateButtons = new List<GButton>();
        private readonly Func<TextSnapshot, ITextTarget> _factory, _previousFactory;
        private FairyTextTarget _draft;
        private SkyPanelController _controller;
        private KeyboardLayout _layout;
        private bool _disposed, _draftActive;
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
            _title = View.GetChild("title").asTextField; _composition = View.GetChild("composition").asTextField;
            _selection = View.GetChild("selection").asTextField; _status = View.GetChild("status").asTextField;
            _body = (GTextInput)View.GetChild("body"); _body.maxLength = 0;
            _keyboard = View.GetChild("keyboard").asCom; _candidates = View.GetChild("candidates").asCom;
            _keyboard.RemoveChildren(0, -1, true); _candidates.RemoveChildren(0, -1, true);
            if (!string.IsNullOrEmpty(mixedFont)) SetFont(View, mixedFont);
            _draftSelection = new FairyDraftSelection(_body);
            Wire("cancel", SkyControl.Cancel); Wire("finish", SkyControl.Finish); Wire("language", SkyControl.Language);
            Wire("previousCandidate", SkyControl.PreviousCandidate); Wire("nextCandidate", SkyControl.NextCandidate);
            _factory = CreateDraft; _previousFactory = player.DraftTargetFactory; player.DraftTargetFactory = _factory;
            player.Opened += OnOpened; player.Closed += OnClosed; parent.onSizeChanged.Add(Fit);
            var pump = View.displayObject.gameObject.AddComponent<FairyPanelPump>(); pump.Panel = this;
            View.visible = false; Fit();
        }
        /// <summary>
        /// 配置组字、候选和虚拟按键的语言字体。
        /// </summary>
        public void SetLanguageFont(string language, string font)
        { _fonts[language] = font; if (_controller != null) Render(_controller.State); }
        private ITextTarget CreateDraft(TextSnapshot initial)
        {
            _draft?.Dispose(); _draftActive = true; _body.editable = true; _body.text = initial.Text;
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
        }
        private void Wire(string name, SkyControl control)
        {
            var button = View.GetChild(name).asButton; button.focusable = false;
            button.onClick.Add(() => _player.Handle(control));
        }
        private void Render(SkyPanelState state)
        {
            if (_disposed) return;
            var snapshot = state.Session; var language = _player.Session?.Languages.FirstOrDefault(item => item.LanguageId == snapshot.LanguageId);
            string font = _fonts.TryGetValue(snapshot.LanguageId, out var configured) && !string.IsNullOrEmpty(configured) ? configured : _body.textFormat.font;
            _title.text = _player.PlayerId + "   ·   " + (language?.DisplayName ?? snapshot.LanguageId) + (snapshot.Lifecycle == SessionLifecycle.Paused ? "   已暂停" : snapshot.Literal ? "   字面输入" : "");
            SetFont(_composition, font); _composition.text = snapshot.Composition.IsEmpty ? "请输入" : snapshot.Composition.DisplayText;
            if (!_draftActive) { _body.editable = false; _body.touchable = false; _body.text = snapshot.Text.Text; }
            else _body.touchable = true;
            if (!ReferenceEquals(_layout, state.Layout)) { _layout = state.Layout; BuildKeys(); }
            foreach (var key in _keys)
            {
                bool enabled = _controller.IsKeyEnabled(key.Key);
                bool active = enabled && (key.Key.Action == "shift" && state.Shift || key.Key.Action == "completion" && snapshot.CompletionEnabled);
                key.Button.title = SkyKeyboardStyle.KeyLabel(key.Key, state.Shift, snapshot.CompletionEnabled);
                if (key.Key.Action == "completion" && (language == null || (language.Capabilities & LanguageCapabilities.CompletionSwitch) == 0)) key.Button.title = key.Key.Label;
                Style(key.Button, font, key.Key.Id == state.FocusedKey, enabled, key.Key, active);
            }
            RenderCandidates(snapshot, font);
            var result = state.LastResult;
            _status.text = snapshot.NeedsResolution ? "请确认正文是否已写入，再选择继续方式" : snapshot.Querying ? "正在查找候选…"
                : state.Problem != null ? "键盘暂不可用，可取消或完成编辑" : result?.Edit?.Status == TextEditStatus.Truncated ? "已按长度上限接受可容纳的文本"
                : result?.Status == OperationStatus.Rejected ? "操作未被接受，请修正内容后重试" : "右摇杆移动正文光标  ·  LT 扩展选区";
            _draftSelection.Render(snapshot.Text);
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
            for (int index = 0; index < _candidateButtons.Count; index++)
            {
                var button = _candidateButtons[index]; button.visible = index < count;
                if (index >= count) continue;
                float width = 860f / count; button.SetXY(index * width, 0); button.SetSize(width - 7, 52); button.title = page.Candidates[index].Text;
                Style(button, font, index == snapshot.SelectedCandidate, !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution);
            }
            SetFont(_selection, font); _selection.text = count == 0 ? "" : page.Candidates[snapshot.SelectedCandidate].Text;
            foreach (string name in new[] { "previousCandidate", "nextCandidate" })
            { var button = View.GetChild(name); button.visible = count > 0; button.touchable = !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution; }
        }
        private static GButton NewButton(GComponent parent, string name, float x, float y, float width, float height)
        {
            var button = UIPackage.CreateObject(PackageName, "Key").asButton; button.name = name; button.focusable = false;
            parent.AddChild(button); button.SetXY(x, y); button.SetSize(width, height); return button;
        }
        private static void Style(GButton button, string font, bool focused, bool enabled, KeyboardKey key = null, bool active = false)
        {
            var text = button.GetTextField(); SetFont(text, font); button.touchable = enabled; button.alpha = enabled ? 1 : 0.32f;
            var skin = button.data as ButtonSkin;
            if (skin == null)
            {
                skin = new ButtonSkin { Background = button.GetChild("background").asGraph, Outline = button.GetChild("focusOutline"), Badge = button.GetChild("focusBadge"), Glyph = button.GetChild("focusGlyph") };
                button.data = skin;
            }
            bool frame = focused && key != null;
            Color fill = key != null ? SkyKeyboardStyle.KeyColor(key, focused, active) : focused ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Function;
            Color edge = frame ? SkyKeyboardStyle.DarkText : focused ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Border;
            if (!skin.Initialized || skin.Fill != fill || skin.Edge != edge || skin.Focused != frame)
            {
                skin.Background.shape.DrawRoundRect(frame ? 5 : 1, edge, fill, 8, 8, 8, 8);
                skin.Fill = fill; skin.Edge = edge; skin.Focused = frame; skin.Initialized = true;
            }
            if (skin.Outline != null) skin.Outline.visible = frame;
            if (skin.Badge != null) skin.Badge.visible = frame && enabled;
            if (skin.Glyph != null) skin.Glyph.visible = frame && enabled;
            var format = text.textFormat; Color foreground = focused || key?.Action == "finish" ? SkyKeyboardStyle.DarkText : SkyKeyboardStyle.Text;
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
            float scale = Mathf.Max(0.01f, Mathf.Min(1, _parent.width / (SkyKeyboardStyle.Width + 32), _parent.height / (SkyKeyboardStyle.Height + 32))); View.SetScale(scale, scale);
            View.SetXY((_parent.width - SkyKeyboardStyle.Width * scale) / 2, (_parent.height - SkyKeyboardStyle.Height * scale) / 2);
        }
        internal void Tick()
        {
            if (_disposed) return;
            if (View.isDisposed) { Dispose(); return; }
            if (_player.Session != null && View.visible) _draftSelection.Render(_player.Session.Snapshot.Text);
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
            _draft?.Dispose(); _draftSelection.Dispose(); if (!View.isDisposed) View.Dispose();
        }
    }
}
