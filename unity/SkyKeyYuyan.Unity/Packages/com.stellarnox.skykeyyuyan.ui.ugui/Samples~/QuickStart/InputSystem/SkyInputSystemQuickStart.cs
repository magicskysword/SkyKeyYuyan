using SkyKeyYuyan.Unity;
using SkyKeyYuyan.Unity.InputSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace SkyKeyYuyan.Samples
{
    /// <summary>
    /// 绑定前两个 Input System 手柄，保留每个设备的玩家归属。
    /// </summary>
    public sealed class SkyInputSystemQuickStart : SkyQuickStart
    {
        private SkyInputSystemDriver _driver;
        private SkyPlayerContext[] _players;
        private Gamepad[] _pads;
        private Keyboard _keyboard;
        protected override SkyInputBackend Backend => SkyInputBackend.InputSystem;
        protected override void ConfigureInput(SkyUnityHost host, SkyPlayerContext[] players, GameObject events)
        {
            if (events != null) events.AddComponent<InputSystemUIInputModule>();
            _players = players; _pads = new Gamepad[players.Length]; _driver = new SkyInputSystemDriver(host.Router); host.SetDriver(_driver); BindNewDevices();
        }
        private void Update() { if (_driver != null) BindNewDevices(); }
        private void BindNewDevices()
        {
            if (_keyboard == null && Keyboard.current != null) { _keyboard = Keyboard.current; _driver.Bind(_keyboard, _players[0].PlayerId); }
            foreach (var pad in Gamepad.all)
            {
                if (System.Array.IndexOf(_pads, pad) >= 0) continue;
                int slot = System.Array.IndexOf(_pads, null); if (slot < 0) break;
                _pads[slot] = pad; _driver.Bind(pad, _players[slot].PlayerId);
            }
        }
    }
}
