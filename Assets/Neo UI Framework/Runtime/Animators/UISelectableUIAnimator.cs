using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Plays a UIAnimation per selection state (Normal/Highlighted/Pressed/Selected/Disabled)
    /// on buttons and other selectables. The press feel lives here (Spring/Shake play modes).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Neo/UI/Animators/UI Selectable UI Animator")]
    public class UISelectableUIAnimator : MonoBehaviour, ISelectionStateAnimator
    {
        [Tooltip("Selectable driving this animator; found on this GameObject or its parents when empty")]
        public Component controller;

        public UIAnimation normalAnimation = new UIAnimation { purpose = AnimationPurpose.State };
        public UIAnimation highlightedAnimation = new UIAnimation { purpose = AnimationPurpose.State };
        public UIAnimation pressedAnimation = new UIAnimation { purpose = AnimationPurpose.State };
        public UIAnimation selectedAnimation = new UIAnimation { purpose = AnimationPurpose.State };
        public UIAnimation disabledAnimation = new UIAnimation { purpose = AnimationPurpose.State };

        private ISelectionStateHost _host;
        private UIAnimation _current;
        // True once _current has been sent back to rest by a no-channel transition. Guards against a
        // SECOND consecutive no-channel state re-reversing it: Tween.Reverse() on an idle (already
        // settled) tween replays it FORWARD, which would park the widget at the animated end value
        // (e.g. the pressed 0.96 scale) permanently. Click selects a button → Pressed → Selected →
        // Normal is exactly two no-channel states in a row, so this case is the common one.
        private bool _currentSettledToRest;

        public UIAnimation GetAnimation(UISelectionState state)
        {
            switch (state)
            {
                case UISelectionState.Highlighted: return highlightedAnimation;
                case UISelectionState.Pressed: return pressedAnimation;
                case UISelectionState.Selected: return selectedAnimation;
                case UISelectionState.Disabled: return disabledAnimation;
                default: return normalAnimation;
            }
        }

        protected virtual void Awake()
        {
            BindTarget();
        }

        protected virtual void OnEnable()
        {
            FindHost();
            _host?.RegisterStateAnimator(this);
        }

        protected virtual void OnDisable()
        {
            _host?.UnregisterStateAnimator(this);
        }

        protected virtual void OnDestroy()
        {
            foreach (UIAnimation animation in AllAnimations())
            {
                animation.Stop(silent: true);
                animation.ReleaseTweens();
            }
        }

        private void FindHost()
        {
            if (_host != null) return;
            if (controller is ISelectionStateHost configured) _host = configured;
            else _host = GetComponentInParent<ISelectionStateHost>();
        }

        public void BindTarget()
        {
            var rectTransform = GetComponent<RectTransform>();
            CanvasGroup group = GetComponent<CanvasGroup>();
            bool needsGroup = false;
            foreach (UIAnimation animation in AllAnimations()) needsGroup |= animation.fade.enabled;
            if (group == null && needsGroup) group = gameObject.AddComponent<CanvasGroup>();
            foreach (UIAnimation animation in AllAnimations()) animation.SetTarget(rectTransform, group);
        }

        public void OnSelectionStateChanged(UISelectionState state, bool instant)
        {
            UIAnimation animation = GetAnimation(state);
            if (!animation.hasEnabledChannels)
            {
                // No animation for this state (e.g. Normal / un-hover): return the previous state's
                // animation to its rest (from) value so it doesn't stick. This MUST run even while
                // that animation is still playing — un-hovering mid scale-up is the common case, and
                // the old "!isActive" guard skipped it, leaving the button stuck at the hover scale.
                if (_current != null)
                {
                    if (instant)
                    {
                        _current.Stop(silent: true);
                        _current.RestoreStartValues();
                        _currentSettledToRest = true;
                    }
                    else if (!_currentSettledToRest)
                    {
                        // Reverse smoothly back to rest: flips the in-flight playhead when the
                        // forward play is still running, or replays in reverse from the end when it
                        // already finished. Guarded by _currentSettledToRest so a SECOND consecutive
                        // no-channel state (Selected → Normal after a click) can't reverse an
                        // already-settled tween a second time — that would replay it forward and
                        // strand the widget at the animated end scale (visible gaps / shrunk buttons).
                        _current.Reverse();
                        _currentSettledToRest = true;
                    }
                }
                return;
            }

            BindTarget();
            _current?.Stop(silent: true);
            _current = animation;
            _currentSettledToRest = false;

            if (instant) animation.SetProgressAtOne();
            else animation.Play(PlayDirection.Forward);
        }

        private UIAnimation[] AllAnimations() =>
            new[] { normalAnimation, highlightedAnimation, pressedAnimation, selectedAnimation, disabledAnimation };
    }
}
