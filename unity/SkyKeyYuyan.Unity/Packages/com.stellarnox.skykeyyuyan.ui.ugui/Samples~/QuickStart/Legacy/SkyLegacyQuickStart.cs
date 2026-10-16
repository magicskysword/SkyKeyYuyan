using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.Legacy;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SkyKeyYuyan.Samples
{
    /// <summary>
    /// 通过 Legacy Input 绑定前两个手柄槽位和首位玩家的键盘。
    /// </summary>
    public sealed class SkyLegacyQuickStart : SkyQuickStart
    {
        protected override SkyInputBackend Backend => SkyInputBackend.Legacy;
        protected override void ConfigureInput(SkyUnityHost host, SkyPlayerContext[] players, GameObject events)
        {
            if (events != null) events.AddComponent<StandaloneInputModule>();
            var driver = new SkyLegacyInputDriver(host.Router); host.SetDriver(driver);
            for (int index = 0; index < players.Length; index++) driver.BindGamepad(index + 1, players[index].PlayerId);
            driver.BindKeyboard(players[0].PlayerId);
        }
    }
}
