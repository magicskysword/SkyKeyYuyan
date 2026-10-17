using System;
using System.Collections.Generic;
using System.Linq;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.UGUI;
using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.UI.UGUI
{
    /// <summary>
    /// 提供完整候选、键盘、草稿与恢复操作的 UGUI 默认面板。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UguiSkyPanel : MonoBehaviour
    {
        private sealed class KeyView { internal KeyboardKey Key; internal SkyPanelButton Button; internal Text Text; internal GameObject Badge; }
        private readonly Dictionary<string, Font> _languageFonts = new Dictionary<string, Font>(StringComparer.Ordinal);
        private readonly List<KeyView> _keys = new List<KeyView>();
        private readonly List<SkyPanelButton> _candidateButtons = new List<SkyPanelButton>();
        private readonly List<float> _candidateWidths = new List<float>();
        private CandidatePage _candidatePage;
        private Font _candidateFont;
        private SkyPlayerContext _player;
        private SkyPanelController _controller;
        private UguiTextTarget _draftTarget;
        private Func<TextSnapshot, ITextTarget> _factory, _previousFactory;
        private RectTransform _frame, _keyboard, _candidateRoot, _candidatePopup;
        private Text _title, _status;
        private SkyDraftInputField _body;
        private UguiCompositionView _inline;
        private SkyPanelButton _previousCandidate, _nextCandidate;
        private KeyboardLayout _layout;
        private bool _draftActive;
        private bool _bodyFocused;
        private float _bodyHeight = 72;
        private string _measuredBodyText;
        private SkyTextInputOptions _bodyOptions;
        private Font _measuredBodyFont;
        private readonly TextGenerator _bodyMeasure = new TextGenerator();
        private readonly Text[] _hintLabels = new Text[8];
        private CanvasGroup _activationHint;
        /// <summary>
        /// 宿主正文和草稿使用的混合语言字体。
        /// </summary>
        public Font BodyFont { get; set; }
        /// <summary>
        /// 当前草稿控件，可由宿主调整视觉样式。
        /// </summary>
        public SkyDraftInputField DraftField => _body;
        /// <summary>
        /// 为一个玩家创建默认面板。
        /// </summary>
        public static UguiSkyPanel Create(Transform parent, SkyPlayerContext player, Font mixedFont = null)
        {
            var owner = new GameObject("SkyKeyYuyan Panel", typeof(RectTransform), typeof(SkyInputSurface)); owner.transform.SetParent(parent, false);
            var panel = owner.AddComponent<UguiSkyPanel>(); panel.BodyFont = mixedFont; panel.Bind(player); return panel;
        }
        /// <summary>
        /// 为候选及键盘指定语言字体。
        /// </summary>
        public void SetLanguageFont(string language, Font font) { _languageFonts[language] = font; if (_controller != null) Render(_controller.State); }
        /// <summary>
        /// 在玩家没有活动输入时绑定面板。
        /// </summary>
        public void Bind(SkyPlayerContext player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (_player?.Session != null || player.Session != null) throw new InvalidOperationException("绑定面板前需要结束活动输入");
            Unbind(); _player = player;
            Build(); _factory = CreateDraft; _previousFactory = player.DraftTargetFactory; player.DraftTargetFactory = _factory;
            player.Opened += OnOpened; player.Closed += OnClosed; gameObject.SetActive(false);
        }
        private ITextTarget CreateDraft(TextSnapshot initial)
        {
            _draftTarget?.Dispose(); _draftActive = true;
            _body.RetainEditing = true; _body.readOnly = false; _body.characterLimit = 0;
            _body.lineType = _player.TextInput.Type == SkyTextInputType.SingleLine ? InputField.LineType.SingleLine : InputField.LineType.MultiLineNewline;
            _body.SetTextWithoutNotify(initial.Text); _body.selectionAnchorPosition = initial.Anchor; _body.selectionFocusPosition = initial.Caret;
            return _draftTarget = new UguiTextTarget(_body);
        }
        private void OnOpened(SkyPlayerContext player)
        {
            _controller = player.Panel; _controller.Changed += Render;
            gameObject.SetActive(true); Fit(); Render(_controller.State);
        }
        private void OnClosed(SkyPlayerContext player)
        {
            if (_controller != null) _controller.Changed -= Render;
            _controller = null; _body.RetainEditing = false; _body.DeactivateInputField(); _draftActive = false;
            _bodyFocused = false;
            gameObject.SetActive(false);
        }
        private void Build()
        {
            if (_frame != null) return;
            if (GetComponent<SkyInputSurface>() == null) gameObject.AddComponent<SkyInputSurface>();
            var owner = (RectTransform)transform; owner.anchorMin = Vector2.zero; owner.anchorMax = Vector2.one; owner.offsetMin = owner.offsetMax = Vector2.zero;
            if (BodyFont == null)
            {
#if UNITY_2022_2_OR_NEWER
                BodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                BodyFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }
            _frame = Rect("Frame", transform, 0, 0, SkyKeyboardStyle.Width, SkyKeyboardStyle.Height); _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f); _frame.pivot = new Vector2(0.5f, 0.5f); _frame.anchoredPosition = Vector2.zero;
            _frame.gameObject.AddComponent<SkyPanelGraphic>().Configure(SkyKeyboardStyle.Panel, SkyKeyboardStyle.Border, 1, 16);
            _title = Label("Title", _frame, 24, 16, 1000, 32, 23);
            Button("Close", _frame, 1052, 12, 44, 36, "×", () => _player?.Handle(SkyControl.Cancel));
            var bodyRoot = Rect("Body", _frame, 24, 56, 1072, 72); bodyRoot.gameObject.AddComponent<SkyPanelGraphic>().Configure(SkyKeyboardStyle.Inset, SkyKeyboardStyle.Accent, 1, 10);
            bodyRoot.gameObject.AddComponent<RectMask2D>();
            _body = bodyRoot.gameObject.AddComponent<SkyDraftInputField>(); _body.lineType = InputField.LineType.MultiLineNewline; _body.targetGraphic = bodyRoot.GetComponent<Image>();
            _body.FocusRequested += () => _controller?.SetTextFocused(true);
            _body.PreparePointerFocus = PrepareBodyPointer;
            var bodyLabel = Label("Text", bodyRoot, 12, 8, 1048, 56, 26); bodyLabel.verticalOverflow = VerticalWrapMode.Overflow; _body.textComponent = bodyLabel; _body.caretColor = SkyKeyboardStyle.Accent; _body.customCaretColor = true; _body.selectionColor = new Color(0.25f, 0.75f, 0.65f, 0.4f);
            _inline = new UguiCompositionView(_body);
            _keyboard = Rect("Keyboard", _frame, 24, 208, 1072, 336);
            _status = Label("Status", _frame, 24, 552, 1072, 24, 17); _status.color = SkyKeyboardStyle.Muted;
            BuildHints();
            _candidatePopup = Rect("CandidatePopup", _frame, 24, 100, 964, SkyKeyboardStyle.CandidatePopupHeight);
            _candidatePopup.gameObject.AddComponent<SkyPanelGraphic>().Configure(SkyKeyboardStyle.Inset, SkyKeyboardStyle.Border, 1, 10);
            _candidateRoot = Rect("Candidates", _candidatePopup, 8, 8, 860, 52);
            _previousCandidate = Button("PreviousCandidate", _candidatePopup, 876, 8, 36, 52, "‹", () => _player?.Handle(SkyControl.PreviousCandidate));
            _nextCandidate = Button("NextCandidate", _candidatePopup, 920, 8, 36, 52, "›", () => _player?.Handle(SkyControl.NextCandidate));
            _candidatePopup.gameObject.SetActive(false);
        }
        private bool PrepareBodyPointer()
        {
            var session = _player?.Session;
            if (session == null) return false;
            if (!session.Snapshot.Composition.IsEmpty) session.Process(InputOperation.CommitComposition());
            return session.Snapshot.Composition.IsEmpty && !session.Snapshot.NeedsResolution;
        }
        private void Render(SkyPanelState state)
        {
            var snapshot = state.Session;
            Font languageFont = _languageFonts.TryGetValue(snapshot.LanguageId, out var configured) && configured != null ? configured : BodyFont;
            var language = _player.Session?.Languages.FirstOrDefault(item => item.LanguageId == snapshot.LanguageId);
            _title.text = _player.PlayerId + "   ·   " + (language?.DisplayName ?? snapshot.LanguageId) + (snapshot.Lifecycle == SessionLifecycle.Paused ? "   已暂停" : snapshot.Literal ? "   字面输入" : "");
            var composition = new SkyCompositionText(snapshot);
            if (!_draftActive)
            {
                _body.readOnly = true; _body.interactable = false; _body.SetTextWithoutNotify(snapshot.Text.Text);
                _body.selectionAnchorPosition = snapshot.Text.Anchor; _body.selectionFocusPosition = snapshot.Text.Caret;
            }
            else _body.interactable = true;
            RenderBody(state, composition);
            _inline.Render(composition);
            if (!ReferenceEquals(_layout, state.Layout)) { _layout = state.Layout; BuildKeys(); }
            foreach (var view in _keys)
            {
                view.Text.font = languageFont;
                bool focused = !state.TextFocused && view.Key.Id == state.FocusedKey;
                bool enabled = _controller.IsKeyEnabled(view.Key);
                bool active = enabled && (view.Key.Action == "shift" && state.Shift || view.Key.Action == "completion" && snapshot.CompletionEnabled);
                view.Text.text = SkyKeyboardStyle.KeyLabel(view.Key, state.Shift, snapshot.CompletionEnabled);
                if (view.Key.Action == "completion" && (language == null || (language.Capabilities & LanguageCapabilities.CompletionSwitch) == 0)) view.Text.text = view.Key.Label;
                ((SkyPanelGraphic)view.Button.targetGraphic).Configure(SkyKeyboardStyle.KeyColor(view.Key, focused, active), focused ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Border, focused ? 3 : 1, 8, focused);
                view.Text.color = !enabled ? new Color(.6f, .66f, .72f, .45f) : (Color)(focused || view.Key.Action == "finish" ? SkyKeyboardStyle.DarkText : SkyKeyboardStyle.Text);
                view.Badge.SetActive(focused && enabled);
                view.Button.interactable = enabled;
            }
            RenderCandidates(snapshot, languageFont);
            var result = state.LastResult;
            _status.text = snapshot.NeedsResolution ? "请确认正文是否已写入，再选择继续方式" : snapshot.Querying ? "正在查找候选…"
                : state.Problem != null ? "键盘暂不可用，可取消或完成编辑" : result?.Edit?.Status == TextEditStatus.Truncated ? "已按长度上限接受可容纳的文本"
                : result?.Status == OperationStatus.Rejected ? "操作未被接受，请修正内容后重试" : state.TextFocused ? "左摇杆移动光标 · 右摇杆框选 · 按摇杆返回键盘" : "右摇杆选择候选 · X 选词 · 按摇杆编辑正文";
            _hintLabels[0].text = state.TextFocused ? "光标" : "选键"; _hintLabels[3].text = state.TextFocused ? "框选" : "候选"; _hintLabels[4].text = state.TextFocused ? "键盘" : "正文";
            _activationHint.alpha = state.TextFocused ? .35f : 1;
        }
        private void RenderBody(SkyPanelState state, SkyCompositionText composition)
        {
            _body.RetainEditing = _draftActive && state.TextFocused;
            if (_bodyFocused != state.TextFocused)
            {
                _bodyFocused = state.TextFocused;
                if (_draftActive) { if (_bodyFocused) _body.ActivateInputField(); else _body.DeactivateInputField(); }
            }
            ((SkyPanelGraphic)_body.targetGraphic).Configure(SkyKeyboardStyle.Inset, state.TextFocused ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Border, state.TextFocused ? 3 : 1, 10);
            var options = _player.TextInput; var label = _body.textComponent; string text = composition.Text;
            if (_measuredBodyText == text && ReferenceEquals(_bodyOptions, options) && _measuredBodyFont == label.font) return;
            _measuredBodyText = text; _bodyOptions = options; _measuredBodyFont = label.font;
            _body.lineType = options.Type == SkyTextInputType.SingleLine ? InputField.LineType.SingleLine : InputField.LineType.MultiLineNewline;
            var settings = label.GetGenerationSettings(new Vector2(1048, 0)); settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            float preferred = _bodyMeasure.GetPreferredHeight(text, settings) / label.pixelsPerUnit;
            float height = SkyKeyboardStyle.BodyHeight(options, preferred), delta = height - _bodyHeight;
            if (delta == 0) return;
            _bodyHeight = height; _frame.sizeDelta += new Vector2(0, delta);
            ((RectTransform)_body.transform).sizeDelta = new Vector2(1072, height);
            label.rectTransform.sizeDelta = new Vector2(1048, height - 16);
            foreach (RectTransform child in _frame)
                if (child.name != "Title" && child.name != "Close" && child.name != "Body" && child != _candidatePopup) child.anchoredPosition += new Vector2(0, -delta);
            _body.ForceLabelUpdate(); Fit();
        }
        private void BuildKeys()
        {
            foreach (var key in _keys) DestroyView(key.Button.gameObject); _keys.Clear();
            if (_layout == null) return;
            foreach (var key in _layout.Keys)
            {
                string id = key.Id;
                var area = SkyKeyboardStyle.KeyRect(_layout, key);
                var button = Button(id, _keyboard, area.x, area.y, area.width, area.height, key.Label, () => _controller?.ActivateKey(id));
                var badge = Rect("FocusBadge", button.transform, area.width - 17, -6, 24, 24);
                badge.gameObject.AddComponent<SkyPanelGraphic>().Configure(SkyKeyboardStyle.DarkText, SkyKeyboardStyle.Accent, 2, 12); badge.GetComponent<SkyPanelGraphic>().raycastTarget = false;
                var caption = Label("A", badge, 0, 0, 24, 24, 17); caption.alignment = TextAnchor.MiddleCenter; caption.color = SkyKeyboardStyle.Accent; caption.text = "A";
                _keys.Add(new KeyView { Key = key, Button = button, Text = button.GetComponentInChildren<Text>(), Badge = badge.gameObject });
            }
        }
        private void RenderCandidates(SessionSnapshot snapshot, Font font)
        {
            var page = snapshot.Candidates; int count = page?.Candidates.Count ?? 0;
            while (_candidateButtons.Count < count)
            {
                int index = _candidateButtons.Count;
                _candidateButtons.Add(Button("Candidate" + index, _candidateRoot, index * 107, 0, 100, 52, "", () =>
                {
                    var current = _player?.Session?.Snapshot;
                    if (current == null || current.Querying || current.Candidates == null || index >= current.Candidates.Candidates.Count) return;
                    _player.Session.Process(InputOperation.ConfirmCandidate(current.Candidates.Query.Id, current.Candidates.Candidates[index].Id));
                }));
            }
            if (!ReferenceEquals(_candidatePage, page) || _candidateFont != font)
            {
                _candidatePage = page; _candidateFont = font; _candidateWidths.Clear();
                for (int index = 0; index < count; index++)
                {
                    var label = _candidateButtons[index].GetComponentInChildren<Text>(true);
                    label.font = font; label.text = page.Candidates[index].Text; label.resizeTextForBestFit = false;
                    _candidateWidths.Add(SkyKeyboardStyle.CandidateWidth(label.preferredWidth));
                    label.resizeTextForBestFit = true;
                }
            }
            SkyKeyboardStyle.CandidateRange(_candidateWidths, snapshot.SelectedCandidate, out int first, out int end);
            float x = 0;
            for (int index = 0; index < _candidateButtons.Count; index++)
            {
                var button = _candidateButtons[index]; bool visible = index >= first && index < end;
                button.gameObject.SetActive(visible);
                if (!visible) continue;
                float width = _candidateWidths[index];
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(x, 0); rect.sizeDelta = new Vector2(width, 52);
                var textRect = (RectTransform)button.GetComponentInChildren<Text>().transform;
                textRect.sizeDelta = new Vector2(width - 12, 44);
                var label = button.GetComponentInChildren<Text>(); label.font = font; label.text = page.Candidates[index].Text;
                bool selected = index == snapshot.SelectedCandidate;
                ((SkyPanelGraphic)button.targetGraphic).Configure(selected ? SkyKeyboardStyle.Active : SkyKeyboardStyle.Function, selected ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Border, selected ? 2 : 1);
                label.color = selected ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Text;
                button.interactable = !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution;
                x += width + SkyKeyboardStyle.CandidateGap;
            }
            _candidatePopup.gameObject.SetActive(count > 0 && !snapshot.Composition.IsEmpty);
            _previousCandidate.interactable = _nextCandidate.interactable = !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution;
            if (!_candidatePopup.gameObject.activeSelf) return;
            ((RectTransform)_previousCandidate.transform).anchoredPosition = new Vector2(x + 8, -8);
            ((RectTransform)_nextCandidate.transform).anchoredPosition = new Vector2(x + 52, -8);
            var popup = SkyKeyboardStyle.CandidatePopupRect(_inline.Caret, _inline.LineHeight, x + 96, -_keyboard.anchoredPosition.y);
            _candidatePopup.anchoredPosition = new Vector2(popup.x, -popup.y); _candidatePopup.sizeDelta = popup.size;
        }
        private Text Label(string name, Transform parent, float x, float y, float width, float height, int size)
        {
            var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>(); label.font = BodyFont; label.fontSize = size;
            label.color = SkyKeyboardStyle.Text; label.supportRichText = false; label.raycastTarget = false; label.alignment = TextAnchor.UpperLeft; return label;
        }
        private SkyPanelButton Button(string name, Transform parent, float x, float y, float width, float height, string text, Action click)
        {
            var owner = Rect(name, parent, x, y, width, height); var image = owner.gameObject.AddComponent<SkyPanelGraphic>(); image.Configure(SkyKeyboardStyle.Function, SkyKeyboardStyle.Border);
            var button = owner.gameObject.AddComponent<SkyPanelButton>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(() => click());
            var colors = button.colors; colors.normalColor = colors.highlightedColor = colors.selectedColor = Color.white; colors.pressedColor = new Color(.8f, .8f, .8f, 1); colors.disabledColor = new Color(1, 1, 1, .32f); button.colors = colors;
            var label = Label("Label", owner, 6, 4, width - 12, height - 8, 24); label.alignment = TextAnchor.MiddleCenter; label.resizeTextForBestFit = true; label.resizeTextMinSize = 14; label.resizeTextMaxSize = 24;
            label.text = text; return button;
        }
        private void BuildHints()
        {
            var glyphs = new[] { "L", "A", "B", "R", "L3/R3", "X", "Y", "Start" };
            var labels = new[] { "选键", "输入", "回退", "候选", "正文", "选词", "语言", "保存" };
            var widths = new[] { 120, 100, 110, 142, 190, 110, 110, 190 };
            float x = 24;
            for (int index = 0; index < glyphs.Length; index++)
            {
                float glyphWidth = glyphs[index].Length > 1 ? 64 : 30;
                var group = Rect("HintGroup" + index, _frame, x, 584, widths[index], 30);
                if (index == 1) _activationHint = group.gameObject.AddComponent<CanvasGroup>();
                if (index >= 5)
                {
                    var graphic = group.gameObject.AddComponent<Image>(); graphic.color = Color.clear;
                    var button = group.gameObject.AddComponent<SkyPanelButton>(); button.targetGraphic = graphic; button.navigation = new Navigation { mode = Navigation.Mode.None };
                    var control = index == 5 ? SkyControl.ConfirmCandidate : index == 6 ? SkyControl.Language : SkyControl.Finish;
                    button.onClick.AddListener(() => _player?.Handle(control));
                }
                var glyph = Rect("Hint" + index, group, 0, 0, glyphWidth, 30);
                glyph.gameObject.AddComponent<SkyPanelGraphic>().Configure(SkyKeyboardStyle.Inset, SkyKeyboardStyle.Muted, 1, glyphWidth == 30 ? 15 : 6);
                var caption = Label("Glyph", glyph, 0, 0, glyphWidth, 30, 16); caption.alignment = TextAnchor.MiddleCenter; caption.alignByGeometry = true; caption.text = glyphs[index];
                var hint = _hintLabels[index] = Label("HintLabel" + index, group, glyphWidth + 8, 0, widths[index] - glyphWidth - 8, 30, 18);
                hint.alignment = TextAnchor.MiddleLeft; hint.alignByGeometry = true; hint.text = labels[index];
                x += widths[index];
            }
        }
        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private void Fit()
        {
            if (_frame == null) return;
            var rect = ((RectTransform)transform).rect; float scale = Mathf.Min(1, rect.width / (SkyKeyboardStyle.Width + 32), rect.height / (_frame.rect.height + 32)); _frame.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }
        private void OnRectTransformDimensionsChange() => Fit();
        private void Unbind()
        {
            if (_player == null) return;
            if (_player.Session != null) _player.Handle(SkyControl.Cancel);
            if (_controller != null) _controller.Changed -= Render;
            _player.Opened -= OnOpened; _player.Closed -= OnClosed;
            if (ReferenceEquals(_player.DraftTargetFactory, _factory)) _player.DraftTargetFactory = _previousFactory;
            _draftTarget?.Dispose(); _draftTarget = null; _player = null; _controller = null;
        }
        private static void DestroyView(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDestroy() { Unbind(); _inline?.Dispose(); ((IDisposable)_bodyMeasure).Dispose(); }
    }
}
