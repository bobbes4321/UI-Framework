using System;
using UnityEngine;
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
    }
}
