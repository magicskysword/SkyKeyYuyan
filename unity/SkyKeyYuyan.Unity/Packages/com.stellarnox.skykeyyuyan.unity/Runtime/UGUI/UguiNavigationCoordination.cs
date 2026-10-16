using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SkyKeyYuyan.Unity.UGUI
{
    /// <summary>
    /// 保存并还原 EventSystem 的原生导航，防止一次手柄确认再次触发原生按钮。
    /// </summary>
    public static class UguiNavigationCoordination
    {
        private static readonly Dictionary<EventSystem, bool> Previous = new Dictionary<EventSystem, bool>();
        private static bool _registered;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize() => EnsureRegistered();
        internal static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true; SkyNativeInputCoordination.NavigationRequested += Apply;
        }
        private static void Apply(bool owned)
        {
            if (owned)
            {
                foreach (var system in Object.FindObjectsOfType<EventSystem>())
                {
                    if (!Previous.ContainsKey(system)) Previous.Add(system, system.sendNavigationEvents);
                    system.sendNavigationEvents = false;
                }
            }
            else
            {
                foreach (var entry in Previous) if (entry.Key != null) entry.Key.sendNavigationEvents = entry.Value;
                Previous.Clear();
            }
        }
    }
}
