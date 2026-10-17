using System;
using System.Collections.Generic;
using FairyGUI;
using SkyKeyYuyan.Core;
using SkyKeyYuyan.Unity;
using UnityEngine;

namespace SkyKeyYuyan.FairyGUI
{
    internal sealed class FairyKeyboardView : IDisposable
    {
        private sealed class Item { internal GComponent Button; internal GGraph Background; internal GTextField Label; internal float Width; }
        private readonly ISkySession _session;
        private readonly GTextInput _field;
        private readonly FairyCompositionView _composition;
        private readonly GComponent _popup = new GComponent { name = "SkyInlineCandidates", focusable = false };
        private readonly List<Item> _items = new List<Item>();
        private readonly FairyKeyboardPump _pump;
        internal FairyKeyboardView(ISkySession session, GTextInput field)
        {
            _session = session; _field = field;
            _composition = new FairyCompositionView(field, new GTextField { name = "SkyCompositionPreview" });
            GRoot.inst.AddChild(_popup); _popup.displayObject.gameObject.AddComponent<SkyInputSurface>();
            session.StateChanged += Render; Render(session.Snapshot);
            _pump = field.displayObject.gameObject.AddComponent<FairyKeyboardPump>(); _pump.Refresh = () => Render(session.Snapshot);
        }
        private void Render(SessionSnapshot snapshot)
        {
            if (_field.isDisposed) return;
            _composition.Render(new SkyCompositionText(snapshot));
            var page = snapshot.Candidates; int count = page?.Candidates.Count ?? 0;
            _popup.visible = count > 0 && !snapshot.Querying && !snapshot.Composition.IsEmpty;
            if (!_popup.visible) return;
            while (_items.Count < count)
            {
                var button = new GComponent { name = "Candidate" + _items.Count, focusable = false };
                var background = new GGraph { touchable = false }; var label = new GTextField { touchable = false, singleLine = true, align = AlignType.Center, verticalAlign = VertAlignType.Middle, autoSize = AutoSizeType.None };
                button.AddChild(background); button.AddChild(label); _popup.AddChild(button); _items.Add(new Item { Button = button, Background = background, Label = label });
            }
            float budget = Mathf.Max(40, Mathf.Min(860, GRoot.inst.width - 24)); int selected = Mathf.Clamp(snapshot.SelectedCandidate, 0, count - 1);
            for (int index = 0; index < count; index++)
            {
                var item = _items[index]; var candidate = page.Candidates[index];
                var format = new TextFormat(); format.CopyFrom(_field.textFormat); format.size = Mathf.Clamp(format.size, 14, 24); format.color = index == selected ? SkyKeyboardStyle.Accent : SkyKeyboardStyle.Text;
                item.Label.textFormat = format; item.Label.text = (index + 1) + " " + candidate.Text;
                item.Width = Mathf.Min(budget, Mathf.Max(52, item.Label.textWidth + 20));
                long query = page.Query.Id; string id = candidate.Id;
                item.Button.onClick.Clear(); item.Button.onClick.Add(() => _session.Process(InputOperation.ConfirmCandidate(query, id)));
            }
            int first = selected, end = selected + 1; float width = _items[selected].Width;
            while (first > 0 && width + _items[first - 1].Width + 6 <= budget) width += _items[--first].Width + 6;
            while (end < count && width + _items[end].Width + 6 <= budget) width += _items[end++].Width + 6;
            float x = 0;
            for (int index = 0; index < _items.Count; index++)
            {
                var item = _items[index]; item.Button.visible = index >= first && index < end; if (!item.Button.visible) continue;
                item.Button.SetXY(x, 0); item.Button.SetSize(item.Width, 48); item.Background.SetSize(item.Width, 48);
                item.Background.shape.DrawRect(1, SkyKeyboardStyle.Border, index == selected ? SkyKeyboardStyle.Active : SkyKeyboardStyle.Function);
                item.Label.SetXY(6, 2); item.Label.SetSize(item.Width - 12, 44); x += item.Width + 6;
            }
            _popup.SetSize(x, 48); var point = _popup.parent.GlobalToLocal(_composition.GlobalCaret);
            float y = point.y + 4; if (y + 48 > GRoot.inst.height - 8) y = point.y - _composition.LineHeight - 52;
            _popup.SetXY(Mathf.Clamp(point.x, 8, Mathf.Max(8, GRoot.inst.width - x - 8)), Mathf.Clamp(y, 8, Mathf.Max(8, GRoot.inst.height - 56)));
            _popup.parent.SetChildIndex(_popup, _popup.parent.numChildren - 1);
        }
        public void Dispose()
        {
            _session.StateChanged -= Render; _composition.Dispose(); if (!_popup.isDisposed) _popup.Dispose();
            if (_pump != null) { _pump.Refresh = null; if (Application.isPlaying) UnityEngine.Object.Destroy(_pump); else UnityEngine.Object.DestroyImmediate(_pump); }
        }
    }
    internal sealed class FairyKeyboardPump : MonoBehaviour
    {
        internal Action Refresh;
        private void LateUpdate() => Refresh?.Invoke();
    }
}
