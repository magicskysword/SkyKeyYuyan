using System;
using System.Collections.Generic;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.UI.UGUI;
using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyKeyYuyan.Samples
{
    /// <summary>
    /// 创建独立玩家、文本目标和默认面板的完整示例。
    /// </summary>
    public abstract class SkyQuickStart : MonoBehaviour
    {
        [Range(1, 2)] public int PlayerCount = 1;
        public Font MixedFont;
        public bool KeyboardEnabled;
        public bool EnglishCompletion;
        public EditingMode Mode = EditingMode.Draft;
        private readonly List<UguiTextTarget> _targets = new List<UguiTextTarget>();
        private SkyRuntime _runtime;
        private SkyUnityHost _host;
        private GameObject _canvas, _events;
        private Font _generatedFont;
        private bool _destroyed;
        protected abstract SkyInputBackend Backend { get; }
        protected abstract void ConfigureInput(SkyUnityHost host, SkyPlayerContext[] players, GameObject events);
        protected async void Start()
        {
            try
            {
                _runtime = new SkyRuntime();
#if SKY_SAMPLE_ENGLISH
                if (EnglishCompletion) _runtime.RegisterLanguage(new SkyKeyYuyan.Languages.English.EnglishCompletionModule());
                else
#endif
                    _runtime.RegisterLanguage(new BasicEnglishModule());
#if SKY_SAMPLE_CHINESE
                _runtime.RegisterLanguage(new SkyKeyYuyan.Languages.Chinese.ChinesePinyinModule());
#endif
#if SKY_SAMPLE_KOREAN
                _runtime.RegisterLanguage(new SkyKeyYuyan.Languages.Korean.KoreanModule());
#endif
                var options = SkyUnityServices.Shared.CreateOptions();
                var initialized = await _runtime.InitializeAsync(options, System.Threading.CancellationToken.None);
                if (_destroyed) return;
                if (!initialized.Succeeded) throw new InvalidOperationException(initialized.Result.Reason);
                if (MixedFont == null) MixedFont = _generatedFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Malgun Gothic", "Arial" }, 24);
                _canvas = new GameObject("Input Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = _canvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = _canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
                if (EventSystem.current == null) _events = new GameObject("Event System", typeof(EventSystem));
                _host = gameObject.AddComponent<SkyInputHost>().Configure(Backend); _host.Runtime = _runtime;
                var players = new SkyPlayerContext[Mathf.Clamp(PlayerCount, 1, 2)];
                for (int index = 0; index < players.Length; index++)
                {
                    int number = index;
                    var parent = Rect("Player " + (index + 1), _canvas.transform, 0, 0, 0, 0);
                    parent.anchorMin = new Vector2((float)index / players.Length, 0); parent.anchorMax = new Vector2((float)(index + 1) / players.Length, 1); parent.offsetMin = parent.offsetMax = Vector2.zero;
                    var player = players[index] = _host.AddPlayer(new SkyPlayerOptions { PlayerId = "玩家" + (index + 1), UserId = "sample-" + (index + 1), InitialLanguage = "en", EditingMode = Mode }, new UguiTargetDiscovery(parent));
                    player.KeyboardEnabled = KeyboardEnabled && index == 0;
                    _host.Router.Bind("sample-pointer-" + index, SkyDeviceKind.Pointer, player.PlayerId);
                    Label("Title", parent, 24, 18, 450, 32, "玩家 " + (index + 1) + " · " + Backend);
                    var target = CreateTarget(parent, players.Length == 1 ? 1872 : 912); _targets.Add(target);
                    var open = Button(parent, 600 / players.Length, 18, 132, "打开输入", () => { if (player.Session == null) player.Open(target, _runtime); });
                    if (index == 0)
                    {
                        Button keyboard = null;
                        keyboard = Button(parent, 600 / players.Length + 140, 18, 132, "键盘：" + (player.KeyboardEnabled ? "开" : "关"), () => { player.KeyboardEnabled = !player.KeyboardEnabled; keyboard.GetComponentInChildren<Text>().text = "键盘：" + (player.KeyboardEnabled ? "开" : "关"); });
                    }
                    UguiSkyPanel.Create(parent, player, MixedFont);
                    player.Completed += result => Debug.Log("玩家 " + (number + 1) + " · " + result.Reason + " · " + target.ReadSnapshot().Text);
                }
                ConfigureInput(_host, players, _events);
                for (int index = 0; index < players.Length; index++) players[index].Open(_targets[index], _runtime);
            }
            catch (Exception ex) { if (!_destroyed) Debug.LogException(ex); }
        }
        private UguiTextTarget CreateTarget(Transform parent, float width)
        {
            var owner = Rect("Original", parent, 24, 62, width, 50); owner.gameObject.AddComponent<Image>().color = new Color32(36, 46, 62, 255);
            var label = Label("Text", owner, 10, 7, width - 20, 36, "");
            var field = owner.gameObject.AddComponent<InputField>(); field.textComponent = label; field.lineType = InputField.LineType.MultiLineNewline;
            field.text = "Hello，世界！"; field.selectionAnchorPosition = field.selectionFocusPosition = field.text.Length;
            return new UguiTextTarget(field);
        }
        private Text Label(string name, Transform parent, float x, float y, float width, float height, string value)
        {
            var label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>(); label.font = MixedFont; label.fontSize = 22; label.color = Color.white;
            label.supportRichText = false; label.raycastTarget = false; label.text = value; return label;
        }
        private Button Button(Transform parent, float x, float y, float width, string value, Action action)
        {
            var owner = Rect(value, parent, x, y, width, 32); var image = owner.gameObject.AddComponent<Image>(); image.color = new Color32(48, 69, 88, 255);
            var button = owner.gameObject.AddComponent<SkyPanelButton>(); button.targetGraphic = image; button.onClick.AddListener(() => action());
            var label = Label("Label", owner, 2, 0, width - 4, 32, value); label.fontSize = 18; label.alignment = TextAnchor.MiddleCenter; return button;
        }
        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        protected virtual void OnDestroy()
        {
            _destroyed = true; _host?.Dispose(); foreach (var target in _targets) target.Dispose(); _runtime?.Dispose();
            if (_canvas != null) Destroy(_canvas); if (_events != null) Destroy(_events); if (_generatedFont != null) Destroy(_generatedFont);
        }
    }
}
