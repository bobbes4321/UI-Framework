using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI
{
    /// <summary>
    /// Scrollbar with the same selection-state animator integration as the other selectables:
    /// per-state animators register and react to Normal/Highlighted/Pressed/Selected/Disabled.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Interactive/UI Scrollbar")]
    public class UIScrollbar : Scrollbar, ISelectionStateHost
    {
        private readonly SelectionStateRelay _relay = new SelectionStateRelay();

        public UISelectionState selectionState => _relay.selectionState;

        public void RegisterStateAnimator(ISelectionStateAnimator animator) => _relay.Register(animator);
        public void UnregisterStateAnimator(ISelectionStateAnimator animator) => _relay.Unregister(animator);

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            _relay.Relay((int)state, instant);
        }
    }
}
