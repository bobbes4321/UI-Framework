using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary> An animator reacting to selection-state changes (per-state animations/colors). </summary>
    public interface ISelectionStateAnimator
    {
        void OnSelectionStateChanged(UISelectionState state, bool instant);
    }

    /// <summary>
    /// Base interactive component (extends Unity's Selectable): exposes the five selection states
    /// as a public event + registered animator model, so per-state animations and colors hook in
    /// without polling.
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/UI Selectable")]
    public class UISelectable : Selectable, ISelectionStateHost
    {
        [Tooltip("Clear the EventSystem selection after a pointer click, so the widget returns to " +
                 "Normal/Highlighted instead of parking in the Selected state (whose colors read as a " +
                 "stuck hover tint). Keyboard/gamepad Submit never deselects, so navigation is unaffected.")]
        public bool deselectAfterClick = true;

        private readonly SelectionStateRelay _relay = new SelectionStateRelay();

        public UISelectionState selectionState => _relay.selectionState;

        /// <summary> Raised on every selection-state change (instant flag = no animation requested). </summary>
        public event Action<UISelectionState, bool> OnSelectionStateChanged
        {
            add => _relay.OnChanged += value;
            remove => _relay.OnChanged -= value;
        }

        public void RegisterStateAnimator(ISelectionStateAnimator animator) => _relay.Register(animator);

        public void UnregisterStateAnimator(ISelectionStateAnimator animator) => _relay.Unregister(animator);

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            _relay.Relay((int)state, instant);
        }

        /// <summary>
        /// Clears the EventSystem selection this widget grabbed on pointer-down, honoring
        /// <see cref="deselectAfterClick"/>. Pointer-click handlers call this after their click work:
        /// without it, Unity's Selected state (which outranks Highlighted and survives pointer exit)
        /// keeps the widget hover-tinted until the user clicks somewhere else.
        /// </summary>
        protected void DeselectAfterPointerClick()
        {
            if (!deselectAfterClick) return;
            EventSystem system = EventSystem.current;
            if (system == null || system.alreadySelecting) return;
            if (system.currentSelectedGameObject != gameObject) return;
            system.SetSelectedGameObject(null);
        }
    }
}
