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
        private sealed class KeyView { internal KeyboardKey Key; internal SkyPanelButton Button; internal Text Text; }
        private readonly Dictionary<string, Font> _languageFonts = new Dictionary<string, Font>(StringComparer.Ordinal);
        private readonly List<KeyView> _keys = new List<KeyView>();
        private readonly List<SkyPanelButton> _candidateButtons = new List<SkyPanelButton>();
        private SkyPlayerContext _player;
        private SkyPanelController _controller;
        private UguiTextTarget _draftTarget;
        private Func<TextSnapshot, ITextTarget> _factory, _previousFactory;
        private RectTransform _frame, _keyboard, _candidateRoot;
        private Text _title, _composition, _selection, _status;
        private SkyDraftInputField _body;
        private SkyPanelButton _retry, _completion, _acceptCurrent, _retryWrite, _previousCandidate, _nextCandidate;
        private KeyboardLayout _layout;
        private bool _draftActive;
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
        /// 为组字、候选及键盘指定语言字体。
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
            _body.SetTextWithoutNotify(initial.Text); _body.selectionAnchorPosition = initial.Anchor; _body.selectionFocusPosition = initial.Caret;
            return _draftTarget = new UguiTextTarget(_body);
        }
        private void OnOpened(SkyPlayerContext player)
        {
            _controller = player.Panel; _controller.Changed += Render;
            gameObject.SetActive(true); Fit(); Render(_controller.State);
            if (_draftActive) _body.ActivateInputField();
        }
        private void OnClosed(SkyPlayerContext player)
        {
            if (_controller != null) _controller.Changed -= Render;
            _controller = null; _body.RetainEditing = false; _body.DeactivateInputField(); _draftActive = false;
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
            _frame = Rect("Frame", transform, 0, 0, 900, 810); _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f); _frame.pivot = new Vector2(0.5f, 0.5f); _frame.anchoredPosition = Vector2.zero;
            _frame.gameObject.AddComponent<Image>().color = new Color32(22, 29, 42, 250);
            _title = Label("Title", _frame, 24, 18, 852, 36, 24);
            var bodyRoot = Rect("Body", _frame, 24, 64, 852, 144); bodyRoot.gameObject.AddComponent<Image>().color = new Color32(12, 18, 29, 255);
            bodyRoot.gameObject.AddComponent<RectMask2D>();
            _body = bodyRoot.gameObject.AddComponent<SkyDraftInputField>(); _body.lineType = InputField.LineType.MultiLineNewline; _body.targetGraphic = bodyRoot.GetComponent<Image>();
            var bodyLabel = Label("Text", bodyRoot, 12, 8, 828, 128, 24); bodyLabel.verticalOverflow = VerticalWrapMode.Overflow; _body.textComponent = bodyLabel; _body.caretColor = new Color32(102, 222, 212, 255); _body.customCaretColor = true; _body.selectionColor = new Color(0.2f, 0.65f, 0.8f, 0.4f);
            _composition = Label("Composition", _frame, 24, 218, 852, 40, 26);
            _candidateRoot = Rect("Candidates", _frame, 24, 264, 852, 52);
            _selection = Label("SelectedCandidate", _frame, 24, 322, 592, 36, 18);
            _previousCandidate = Button("PreviousCandidate", _frame, 624, 322, 122, 32, "上一候选", () => _player?.Handle(SkyControl.PreviousCandidate));
            _nextCandidate = Button("NextCandidate", _frame, 754, 322, 122, 32, "下一候选", () => _player?.Handle(SkyControl.NextCandidate));
            _keyboard = Rect("Keyboard", _frame, 24, 364, 852, 350);
            _status = Label("Status", _frame, 24, 716, 852, 32, 17);
            Button("Cancel", _frame, 24, 758, 118, 36, "取消", () => _player?.Handle(SkyControl.Cancel));
            Button("Language", _frame, 150, 758, 118, 36, "切换语言", () => _player?.Handle(SkyControl.Language));
            _completion = Button("Completion", _frame, 276, 758, 118, 36, "补全", () => _player?.Handle(SkyControl.Completion));
            _retry = Button("Retry", _frame, 402, 758, 118, 36, "重试转换", () => _player?.Handle(SkyControl.Retry));
            _acceptCurrent = Button("AcceptCurrent", _frame, 528, 758, 118, 36, "使用当前正文", () => _player?.Handle(SkyControl.AcceptCurrentText));
            _retryWrite = Button("RetryWrite", _frame, 654, 758, 104, 36, "再次提交", () => _player?.Handle(SkyControl.RetryWrite));
            Button("Finish", _frame, 766, 758, 110, 36, "完成保存", () => _player?.Handle(SkyControl.Finish));
        }
        private void Render(SkyPanelState state)
        {
            var snapshot = state.Session;
            Font languageFont = _languageFonts.TryGetValue(snapshot.LanguageId, out var configured) && configured != null ? configured : BodyFont;
            var language = _player.Session?.Languages.FirstOrDefault(item => item.LanguageId == snapshot.LanguageId);
            _title.text = _player.PlayerId + "   ·   " + (language?.DisplayName ?? snapshot.LanguageId) + (snapshot.Lifecycle == SessionLifecycle.Paused ? "   已暂停" : snapshot.Literal ? "   字面输入" : "");
            _composition.font = languageFont; _composition.text = snapshot.Composition.IsEmpty ? "请输入" : snapshot.Composition.DisplayText;
            _selection.font = languageFont;
            if (!_draftActive)
            {
                _body.readOnly = true; _body.interactable = false; _body.SetTextWithoutNotify(snapshot.Text.Text);
                _body.selectionAnchorPosition = snapshot.Text.Anchor; _body.selectionFocusPosition = snapshot.Text.Caret;
            }
            else _body.interactable = true;
            if (!ReferenceEquals(_layout, state.Layout)) { _layout = state.Layout; BuildKeys(); }
            foreach (var view in _keys)
            {
                view.Text.font = languageFont;
                view.Text.text = view.Key.Action == "input" && view.Key.Output != " " ? state.Shift ? view.Key.Alternate : view.Key.Label : view.Key.Label;
                view.Button.GetComponent<Image>().color = view.Key.Id == state.FocusedKey ? new Color32(32, 136, 143, 255) : new Color32(48, 59, 77, 255);
                view.Button.interactable = _controller.IsKeyEnabled(view.Key);
            }
            RenderCandidates(snapshot, languageFont);
            _retry.gameObject.SetActive(snapshot.Literal);
            _completion.gameObject.SetActive(language != null && (language.Capabilities & LanguageCapabilities.CompletionSwitch) != 0);
            _completion.GetComponentInChildren<Text>().text = snapshot.CompletionEnabled ? "补全：开" : "补全：关";
            _acceptCurrent.gameObject.SetActive(snapshot.NeedsResolution); _retryWrite.gameObject.SetActive(snapshot.NeedsResolution);
            var result = state.LastResult;
            _status.text = snapshot.NeedsResolution ? "请确认正文是否已写入，再选择继续方式" : snapshot.Querying ? "正在查找候选…"
                : state.Problem != null ? "键盘暂不可用，可取消或完成编辑" : result?.Edit?.Status == TextEditStatus.Truncated ? "已按长度上限接受可容纳的文本"
                : result?.Status == OperationStatus.Rejected ? "操作未被接受，请修正内容后重试" : "A 输入   B 回退   LB / RB 候选   L3 / R3 确认   X 保存   Y 语言";
        }
        private void BuildKeys()
        {
            foreach (var key in _keys) DestroyObject(key.Button.gameObject); _keys.Clear();
            if (_layout == null) return;
            foreach (var key in _layout.Keys)
            {
                string id = key.Id;
                var button = Button(id, _keyboard, key.Column * 86, key.Row * 49, 78, 42, key.Label, () => _controller?.ActivateKey(id));
                _keys.Add(new KeyView { Key = key, Button = button, Text = button.GetComponentInChildren<Text>() });
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
            for (int index = 0; index < _candidateButtons.Count; index++)
            {
                var button = _candidateButtons[index]; button.gameObject.SetActive(index < count);
                if (index >= count) continue;
                float width = 852f / count;
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(index * width, 0); rect.sizeDelta = new Vector2(width - 7, 52);
                var textRect = (RectTransform)button.GetComponentInChildren<Text>().transform;
                textRect.sizeDelta = new Vector2(width - 13, 48);
                var label = button.GetComponentInChildren<Text>(); label.font = font; label.text = page.Candidates[index].Text;
                button.GetComponent<Image>().color = index == snapshot.SelectedCandidate ? new Color32(32, 136, 143, 255) : new Color32(48, 59, 77, 255);
                button.interactable = !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution;
            }
            _selection.text = count == 0 ? "" : page.Candidates[snapshot.SelectedCandidate].Text;
            _previousCandidate.gameObject.SetActive(count > 0); _nextCandidate.gameObject.SetActive(count > 0);
            _previousCandidate.interactable = _nextCandidate.interactable = !snapshot.Querying && snapshot.Lifecycle == SessionLifecycle.Active && !snapshot.NeedsResolution;
        }
        private Text Label(string name, Transform parent, float x, float y, float width, float height, int size)
        {
            var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>(); label.font = BodyFont; label.fontSize = size;
            label.color = new Color32(231, 239, 247, 255); label.supportRichText = false; label.raycastTarget = false; label.alignment = TextAnchor.UpperLeft; return label;
        }
        private SkyPanelButton Button(string name, Transform parent, float x, float y, float width, float height, string text, Action click)
        {
            var owner = Rect(name, parent, x, y, width, height); var image = owner.gameObject.AddComponent<Image>(); image.color = new Color32(48, 59, 77, 255);
            var button = owner.gameObject.AddComponent<SkyPanelButton>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(() => click());
            var label = Label("Label", owner, 3, 2, width - 6, height - 4, 18); label.alignment = TextAnchor.MiddleCenter; label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = 18;
            label.text = text; return button;
        }
        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private void Fit()
        {
            if (_frame == null) return;
            var rect = ((RectTransform)transform).rect; float scale = Mathf.Min(1, rect.width / 932, rect.height / 842); _frame.localScale = Vector3.one * Mathf.Max(0.01f, scale);
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
        private static void DestroyObject(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDestroy() => Unbind();
    }
}
