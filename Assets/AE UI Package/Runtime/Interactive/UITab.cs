using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// A toggle whose isOn syncs bidirectionally with a target container's visibility:
    /// toggling the tab shows/hides the container; showing/hiding the container updates the tab.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Interactive/UI Tab")]
    public class UITab : UIToggle
    {
        [Tooltip("Container this tab controls and mirrors")]
        [SerializeField] private UIContainer containerReference;

        private bool _syncing;
        private bool _subscribed;

        /// <summary> The container this tab mirrors; assigning re-subscribes and syncs immediately. </summary>
        public UIContainer targetContainer
        {
            get => containerReference;
            set
            {
                if (containerReference == value) return;
                UnsubscribeFromContainer();
                containerReference = value;
                if (isActiveAndEnabled) SubscribeToContainer();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            OnValueChanged += HandleToggleChanged;
            SubscribeToContainer();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            OnValueChanged -= HandleToggleChanged;
            UnsubscribeFromContainer();
        }

        private void SubscribeToContainer()
        {
            if (_subscribed || containerReference == null) return;
            containerReference.OnVisibilityChanged += HandleContainerVisibility;
            _subscribed = true;
            // The tab is the authority at init: push the (baked) selection to the container instead
            // of reading the container's not-yet-initialized state. A direct reference means there's
            // no registry/enable-order race, and it pre-empts the container's own start behaviour so
            // a tab bar deterministically shows its selected panel and hides the rest. (Live external
            // show/hide still flows back through HandleContainerVisibility.)
            PushToContainer();
        }

        private void PushToContainer()
        {
            if (_syncing || containerReference == null) return;
            _syncing = true;
            if (isOn) containerReference.InstantShow();
            else containerReference.InstantHide();
            _syncing = false;
        }

        private void UnsubscribeFromContainer()
        {
            if (!_subscribed || containerReference == null)
            {
                _subscribed = false;
                return;
            }
            containerReference.OnVisibilityChanged -= HandleContainerVisibility;
            _subscribed = false;
        }

        private void HandleToggleChanged(bool value, bool animateChange)
        {
            if (_syncing || containerReference == null) return;
            _syncing = true;
            if (value)
            {
                if (animateChange) containerReference.Show();
                else containerReference.InstantShow();
            }
            else
            {
                if (animateChange) containerReference.Hide();
                else containerReference.InstantHide();
            }
            _syncing = false;
        }

        private void HandleContainerVisibility(VisibilityState state) => SyncFromContainer(state);

        private void SyncFromContainer(VisibilityState state)
        {
            if (_syncing) return;
            _syncing = true;
            bool shouldBeOn = state == VisibilityState.Visible || state == VisibilityState.IsShowing;
            if (isOn != shouldBeOn) SetIsOn(shouldBeOn, animateChange: true);
            _syncing = false;
        }
    }
}
