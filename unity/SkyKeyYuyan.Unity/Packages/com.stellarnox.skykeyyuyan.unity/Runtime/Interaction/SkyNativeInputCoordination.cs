using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SkyKeyYuyan.Unity
{
    /// <summary>
    /// 协调多个玩家对 Unity 原生输入路径的共同接管。
    /// </summary>
    public static class SkyNativeInputCoordination
    {
        private static readonly Dictionary<object, Tuple<bool, bool>> Owners = new Dictionary<object, Tuple<bool, bool>>();
        private static SkyNativeInputPump _pump;
        private static Event _event;
#if ENABLE_LEGACY_INPUT_MANAGER
        private static bool _imeOwned;
        private static IMECompositionMode _imeBefore;
#endif
        /// <summary>
        /// UGUI 等可选适配协调原生导航的入口。
        /// </summary>
        public static event Action<bool> NavigationRequested;
        /// <summary>
        /// 当前是否有玩家接管键盘。
        /// </summary>
        public static bool KeyboardOwned => Owners.Values.Any(value => value.Item1);
        /// <summary>
        /// 当前是否有玩家接管手柄导航。
        /// </summary>
        public static bool NavigationOwned => Owners.Values.Any(value => value.Item2);
        internal static void Set(object owner, bool keyboard, bool navigation)
        {
            if (!keyboard && !navigation) Owners.Remove(owner); else Owners[owner] = Tuple.Create(keyboard, navigation);
            if (Application.isPlaying && _pump == null && Owners.Count != 0)
            {
                var go = new GameObject("SkyKeyYuyan Native Input") { hideFlags = HideFlags.HideAndDontSave };
                _pump = go.AddComponent<SkyNativeInputPump>(); UnityEngine.Object.DontDestroyOnLoad(go);
            }
            ApplyNativePolicy();
        }
        /// <summary>
        /// 在 EventSystem 之前排空已由 SKY 路由的原生键盘事件。
        /// </summary>
        public static void Pump()
        {
            ApplyNativePolicy();
            if (!KeyboardOwned) return;
            if (_event == null) _event = new Event();
            // InputField 即使只读仍处理方向键，因此需要阻止其再次消费同一键盘事件。
            while (Event.PopEvent(_event)) { }
        }
        private static void ApplyNativePolicy()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (KeyboardOwned)
            {
                if (!_imeOwned) { _imeBefore = Input.imeCompositionMode; _imeOwned = true; }
                Input.imeCompositionMode = IMECompositionMode.Off;
            }
            else if (_imeOwned) { Input.imeCompositionMode = _imeBefore; _imeOwned = false; }
#endif
            if (NavigationRequested == null) return;
            foreach (Action<bool> handler in NavigationRequested.GetInvocationList())
                try { handler(NavigationOwned); } catch (Exception ex) { Debug.LogWarning("SKY native input: " + ex.Message); }
        }
    }
}
