using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Neo.UI
{
    /// <summary>
    /// Enforces exclusivity rules over a set of toggles. Modes mirror the classic set:
    /// passive, one-on (allow none), one-on enforced (always exactly one), any-on enforced (at least one).
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/UI Toggle Group")]
    public class UIToggleGroup : MonoBehaviour, INeoIdOwner
    {
        public enum ControlMode
        {
            /// <summary> No enforcement — toggles change freely. </summary>
            Passive = 0,
            /// <summary> At most one toggle on; all may be off. </summary>
            OneToggleOn = 1,
            /// <summary> Exactly one toggle on at all times. </summary>
            OneToggleOnEnforced = 2,
            /// <summary> Any number on, but at least one. </summary>
            AnyToggleOnEnforced = 3
        }

        public ToggleId id = new ToggleId();
        CategoryNameId INeoIdOwner.OwnId => id;

        public ControlMode controlMode = ControlMode.OneToggleOn;

        [Tooltip("Raised with the toggle that changed and its new value")]
        public BoolEvent onAnyToggleChanged = new BoolEvent();

        /// <summary> C# event: (changed toggle, new value). </summary>
        public event Action<UIToggle, bool> OnToggleChanged;

        private readonly List<UIToggle> _toggles = new List<UIToggle>();

        public IReadOnlyList<UIToggle> toggles => _toggles;

        public IEnumerable<UIToggle> activeToggles => _toggles.Where(t => t != null && t.isOn);

        public UIToggle firstActiveToggle => _toggles.FirstOrDefault(t => t != null && t.isOn);

        public void RegisterToggle(UIToggle toggle)
        {
            if (toggle == null || _toggles.Contains(toggle)) return;
            _toggles.Add(toggle);
            // Registration-time enforcement only trims EXTRA on-toggles — it must never force one
            // ON: members register one at a time and enable order is not hierarchy-guaranteed
            // (prefab reimports, domain reloads), so "none on yet" usually means the baked-on
            // member simply hasn't arrived. Forcing here hands the selection to whichever toggle
            // enables first and then deactivates the baked selection when it registers — a tab bar
            // ends up highlighting the wrong tab with every panel hidden. The at-least-one
            // guarantee is enforced where a selection actually changes: ToggleChangedValue.
            EnforceRules(preferred: firstActiveToggle, animateChange: false, allowForceOn: false);
        }

        public void UnregisterToggle(UIToggle toggle)
        {
            _toggles.Remove(toggle);
        }

        /// <summary> Called by a member toggle that wants to change value; applies group rules. </summary>
        internal void ToggleChangedValue(UIToggle toggle, bool requestedValue, bool animateChange)
        {
            switch (controlMode)
            {
                case ControlMode.Passive:
                    toggle.ApplyValue(requestedValue, animateChange, notifyGroup: true);
                    break;

                case ControlMode.OneToggleOn:
                    if (requestedValue)
                    {
                        TurnOffOthers(toggle, animateChange);
                        toggle.ApplyValue(true, animateChange, notifyGroup: true);
                    }
                    else
                    {
                        toggle.ApplyValue(false, animateChange, notifyGroup: true);
                    }
                    break;

                case ControlMode.OneToggleOnEnforced:
                    if (requestedValue)
                    {
                        TurnOffOthers(toggle, animateChange);
                        toggle.ApplyValue(true, animateChange, notifyGroup: true);
                    }
                    // turning the only active toggle off is rejected
                    break;

                case ControlMode.AnyToggleOnEnforced:
                    if (requestedValue || activeToggles.Count(t => t != toggle) > 0)
                        toggle.ApplyValue(requestedValue, animateChange, notifyGroup: true);
                    break;
            }
        }

        internal void NotifyToggleChanged(UIToggle toggle, bool value)
        {
            onAnyToggleChanged?.Invoke(value);
            OnToggleChanged?.Invoke(toggle, value);
        }

        private void TurnOffOthers(UIToggle except, bool animateChange)
        {
            foreach (UIToggle other in _toggles)
            {
                if (other == null || other == except || !other.isOn) continue;
                other.ApplyValue(false, animateChange, notifyGroup: true);
            }
        }

        private void EnforceRules(UIToggle preferred, bool animateChange, bool allowForceOn = true)
        {
            switch (controlMode)
            {
                case ControlMode.OneToggleOn:
                case ControlMode.OneToggleOnEnforced:
                {
                    UIToggle keep = preferred != null && preferred.isOn ? preferred : firstActiveToggle;
                    foreach (UIToggle toggle in _toggles)
                    {
                        if (toggle == null || toggle == keep || !toggle.isOn) continue;
                        toggle.ApplyValue(false, animateChange, notifyGroup: true);
                    }
                    if (allowForceOn && controlMode == ControlMode.OneToggleOnEnforced
                        && keep == null && _toggles.Count > 0)
                    {
                        UIToggle first = _toggles.FirstOrDefault(t => t != null);
                        first?.ApplyValue(true, animateChange, notifyGroup: true);
                    }
                    break;
                }
                case ControlMode.AnyToggleOnEnforced:
                {
                    if (allowForceOn && firstActiveToggle == null && _toggles.Count > 0)
                        _toggles.FirstOrDefault(t => t != null)?.ApplyValue(true, animateChange, notifyGroup: true);
                    break;
                }
            }
        }
    }
}
