using System;
using System.Collections.Generic;
using System.Linq;
using FairyGUI;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;

namespace SkyKeyYuyan.FairyGUI
{
    /// <summary>
    /// 沿指定玩家根的逻辑层级发现动态 GTextInput。
    /// </summary>
    public sealed class FairyTargetDiscovery : ISkyTargetDiscovery
    {
        private readonly GComponent _root;
        private readonly Dictionary<GTextInput, SkyTargetBinding> _targets = new Dictionary<GTextInput, SkyTargetBinding>();
        /// <summary>
        /// 固定玩家 UI 根。
        /// </summary>
        public FairyTargetDiscovery(GComponent root) { _root = root ?? throw new ArgumentNullException(nameof(root)); }
        /// <inheritdoc>
        /// </inheritdoc>
        public IReadOnlyList<SkyTargetBinding> Scan()
        {
            var seen = new HashSet<GTextInput>();
            if (!_root.isDisposed) Visit(_root, seen);
            foreach (var old in _targets.Keys.Where(control => !seen.Contains(control)).ToArray()) { _targets[old].Target.Dispose(); _targets.Remove(old); }
            return _targets.Values.ToArray();
        }
        private void Visit(GObject node, HashSet<GTextInput> seen)
        {
            if (node.isDisposed || node.displayObject.gameObject.GetComponent<SkyInputSurface>() != null) return;
            if (node is GTextInput control)
            {
                if (!_targets.TryGetValue(control, out var binding))
                {
                    if (!control.editable) return;
                    _targets.Add(control, binding = new SkyTargetBinding(new FairyTextTarget(control)));
                }
                seen.Add(control); var settings = control.displayObject.gameObject.GetComponent<SkyTextTargetSettings>();
                binding.Mode = settings != null && !settings.UsePlayerEditingMode ? settings.EditingMode : (EditingMode?)null;
                binding.InitialLanguage = settings != null && !string.IsNullOrWhiteSpace(settings.InitialLanguage) ? settings.InitialLanguage : null;
                binding.TextInput = settings?.TextInput;
            }
            if (node is GComponent container) foreach (var child in container.GetChildren()) Visit(child, seen);
        }
        /// <inheritdoc>
        /// </inheritdoc>
        public void Dispose() { foreach (var binding in _targets.Values) binding.Target.Dispose(); _targets.Clear(); }
    }
}
