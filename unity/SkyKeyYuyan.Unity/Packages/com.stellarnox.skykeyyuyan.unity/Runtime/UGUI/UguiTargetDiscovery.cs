using System;
using System.Collections.Generic;
using System.Linq;
using SkyKeyYuyan.Core;
using UnityEngine;
using UnityEngine.UI;

namespace SkyKeyYuyan.Unity.UGUI
{
    /// <summary>
    /// 在指定玩家根内发现动态 InputField。
    /// </summary>
    public sealed class UguiTargetDiscovery : ISkyTargetDiscovery
    {
        private readonly Transform _root;
        private readonly Dictionary<InputField, SkyTargetBinding> _targets = new Dictionary<InputField, SkyTargetBinding>();
        /// <summary>
        /// 固定扫描边界。
        /// </summary>
        public UguiTargetDiscovery(Transform root) { _root = root != null ? root : throw new ArgumentNullException(nameof(root)); }
        /// <inheritdoc>
        /// </inheritdoc>
        public IReadOnlyList<SkyTargetBinding> Scan()
        {
            var controls = _root != null ? _root.GetComponentsInChildren<InputField>(true) : Array.Empty<InputField>();
            var seen = new HashSet<InputField>();
            foreach (var control in controls)
            {
                if (control.GetComponentInParent<SkyInputSurface>() != null) continue;
                if (!_targets.TryGetValue(control, out var binding))
                {
                    if (control.readOnly) continue;
                    _targets.Add(control, binding = new SkyTargetBinding(new UguiTextTarget(control)));
                }
                seen.Add(control); var settings = control.GetComponent<SkyTextTargetSettings>();
                binding.Mode = settings != null && !settings.UsePlayerEditingMode ? settings.EditingMode : (EditingMode?)null;
                binding.InitialLanguage = settings != null && !string.IsNullOrWhiteSpace(settings.InitialLanguage) ? settings.InitialLanguage : null;
            }
            foreach (var old in _targets.Keys.Where(control => !seen.Contains(control)).ToArray()) { _targets[old].Target.Dispose(); _targets.Remove(old); }
            return _targets.Values.ToArray();
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose() { foreach (var binding in _targets.Values) binding.Target.Dispose(); _targets.Clear(); }
    }
}
