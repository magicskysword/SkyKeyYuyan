using UnityEngine;

namespace SkyKeyYuyan.UI.FairyGUI
{
    internal sealed class FairyPanelPump : MonoBehaviour
    {
        internal FairySkyPanel Panel;
        private void LateUpdate() => Panel?.Tick();
        private void OnDestroy() { Panel?.Dispose(); Panel = null; }
    }
}
