using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Binds Show/Hide UIAnimations to a container's visibility lifecycle. Registers itself with the
    /// container; the container's transition completes only when all registered animators finish.
    /// Interruption: a Show while the hide animation runs reverses it mid-flight (and vice versa).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Neo/UI/Animators/UI Container UI Animator")]
    public class UIContainerUIAnimator : MonoBehaviour, IContainerAnimator
    {
        [Tooltip("Container driving this animator; found on this GameObject or its parents when empty")]
        public UIContainer controller;

        public UIAnimation showAnimation = new UIAnimation { purpose = AnimationPurpose.Show };
        public UIAnimation hideAnimation = new UIAnimation { purpose = AnimationPurpose.Hide };

        // Per-call scratch for a view transition's override channels — created lazily on first use.
        // NEVER the shared ViewTransitionAsset's own UIAnimation instance: two containers showing
        // under the same transition would fight over one tween's live playhead (see
        // UIAnimationChannels.Copy). Re-copied and re-captured on every override call so StartValue
        // endpoints always resolve against THIS view's current rest pose.
        [NonSerialized] private UIAnimation _showOverride;
        [NonSerialized] private UIAnimation _hideOverride;

        private bool _showWasInterrupted;
        private bool _hideWasInterrupted;

        public bool isAnimating =>
            showAnimation.isActive || hideAnimation.isActive ||
            (_showOverride != null && _showOverride.isActive) || (_hideOverride != null && _hideOverride.isActive);

        public float showDuration => showAnimation.totalDuration;
        public float hideDuration => hideAnimation.totalDuration;

        // Editor-only: seeds show/hide from the project's chosen defaults when the component is added.
        protected virtual void Reset()
        {
            NeoUISettings.ApplyDefaultAnimation(NeoAnimatorRoles.ViewShow, showAnimation);
            NeoUISettings.ApplyDefaultAnimation(NeoAnimatorRoles.ViewHide, hideAnimation);
        }

        protected virtual void Awake()
        {
            BindTarget();
        }

        protected virtual void OnEnable()
        {
            FindController();
            controller?.RegisterAnimator(this);
        }

        protected virtual void OnDisable()
        {
            controller?.UnregisterAnimator(this);
        }

        protected virtual void OnDestroy()
        {
            showAnimation.Stop(silent: true);
            hideAnimation.Stop(silent: true);
            showAnimation.ReleaseTweens();
            hideAnimation.ReleaseTweens();
            _showOverride?.Stop(silent: true);
            _hideOverride?.Stop(silent: true);
            _showOverride?.ReleaseTweens();
            _hideOverride?.ReleaseTweens();
        }

        private void FindController()
        {
            if (controller == null) controller = GetComponentInParent<UIContainer>();
        }

        public void BindTarget()
        {
            var rectTransform = GetComponent<RectTransform>();
            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null && (showAnimation.fade.enabled || hideAnimation.fade.enabled
                                   || (_showOverride?.fade.enabled ?? false) || (_hideOverride?.fade.enabled ?? false)))
                group = gameObject.AddComponent<CanvasGroup>();
            showAnimation.SetTarget(rectTransform, group);
            hideAnimation.SetTarget(rectTransform, group);
            _showOverride?.SetTarget(rectTransform, group);
            _hideOverride?.SetTarget(rectTransform, group);
        }

        /// <summary>
        /// Re-snapshots the rest pose. Awake binds the target and captures start values, but when the
        /// container uses a custom start position that capture can race the container's Awake snap
        /// (component Awake order is undefined) and bake the editor layout offset into the StartValue
        /// endpoints — sending shown views off-screen. The container calls this once, AFTER it has
        /// placed the rect at customStartPosition, so move/scale/fade StartValue endpoints resolve to
        /// the real runtime pose instead of the offset.
        /// </summary>
        public void RecaptureStartValues()
        {
            BindTarget();
            showAnimation.CaptureStartValues();
            hideAnimation.CaptureStartValues();
        }

        private static bool HasChannels(UIAnimation animation) =>
            animation.move.enabled || animation.rotate.enabled
            || animation.scale.enabled || animation.fade.enabled;

        /// <summary>
        /// Both animations empty (a view generated without show/hide animations): the animator still
        /// owns the minimal visual contract — hidden means alpha 0, visible means alpha 1. Without
        /// this, an animation-less view reports Hidden while rendering at full alpha, and stacked
        /// "hidden" views cover the one the flow actually showed.
        /// </summary>
        private bool ApplyDefaultAlphaIfEmpty(float alpha)
        {
            if (HasChannels(showAnimation) || HasChannels(hideAnimation)) return false;
            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = alpha;
            return true;
        }

        /// <summary>
        /// Undoes what a completed hide OVERRIDE left behind, right before a show plays: a
        /// transition's slide-out parks the rect in its hidden end pose, and the view's own show
        /// (a fade for every generated view) never touches position — the container would report
        /// Visible while the view sits off-screen forever (the "slide away, come Back to a black
        /// screen" bug). Restoring here also keeps an override show's CaptureStartValues honest:
        /// capturing the displaced pose would bake it into the StartValue endpoints and a slide-in
        /// would land off-screen. Restores from the override's OWN capture, which OnHide re-takes
        /// at rest on every cut — always current-layout-correct. The serialized hideAnimation is
        /// deliberately NOT restored: its capture can predate layout (the stale-teleport hazard on
        /// RestoreStartValues), and serialized show/hide pairs own their own symmetry. Alpha is
        /// left alone when the show has a fade channel — that fade owns alpha, and snapping it up
        /// front would flash the view if the fade starts delayed.
        /// </summary>
        private void RestoreHiddenPose(UIAnimation upcomingShow)
        {
            UIAnimation hide = _hideOverride;
            if (hide == null || !hide.hasStartValues) return;
            var rect = GetComponent<RectTransform>();
            if (rect == null) return;
            if (hide.move.enabled) rect.anchoredPosition3D = hide.startPosition;
            if (hide.rotate.enabled) rect.localEulerAngles = hide.startRotation;
            if (hide.scale.enabled) rect.localScale = hide.startScale;
            if (hide.fade.enabled && !upcomingShow.fade.enabled)
            {
                CanvasGroup group = GetComponent<CanvasGroup>();
                if (group != null) group.alpha = hide.startAlpha;
            }
        }

        public void OnShow(bool instant)
        {
            BindTarget();
            if (ApplyDefaultAlphaIfEmpty(1f))
            {
                // an animation-less view can still have been displaced by a transition's override
                RestoreHiddenPose(showAnimation);
                return;
            }
            if (instant)
            {
                hideAnimation.Stop(silent: true);
                _hideOverride?.Stop(silent: true);
                showAnimation.Stop(silent: true);
                RestoreHiddenPose(showAnimation);
                showAnimation.SetProgressAtOne();
                return;
            }

            if (hideAnimation.isActive)
            {
                // interrupt the running hide by reversing it back toward the visible state
                hideAnimation.Reverse();
                return;
            }
            if (_hideOverride != null && _hideOverride.isActive)
            {
                // a plain Show during a transition's hide must reverse it, not fight it
                _hideOverride.Reverse();
                return;
            }

            RestoreHiddenPose(showAnimation);
            showAnimation.Play(PlayDirection.Forward);
        }

        public void OnHide(bool instant)
        {
            BindTarget();
            if (ApplyDefaultAlphaIfEmpty(0f)) return;
            if (instant)
            {
                showAnimation.Stop(silent: true);
                _showOverride?.Stop(silent: true);
                hideAnimation.Stop(silent: true);
                hideAnimation.SetProgressAtOne();
                return;
            }

            if (showAnimation.isActive)
            {
                // interrupt the running show by reversing it back toward the hidden state
                showAnimation.Reverse();
                return;
            }
            if (_showOverride != null && _showOverride.isActive)
            {
                // a plain Hide during a transition's show must reverse it, not fight it
                _showOverride.Reverse();
                return;
            }

            hideAnimation.Play(PlayDirection.Forward);
        }

        /// <summary>
        /// Show driven by a view transition's incoming channel instead of <see cref="showAnimation"/>.
        /// The shared asset's animation is copied into <see cref="_showOverride"/> (never played
        /// directly) and rebound/recaptured on every call so its StartValue endpoints resolve against
        /// this view's CURRENT rest pose, not whatever the asset last animated on some other view.
        /// Interrupt semantics match the legacy path: a running hide (serialized or override) reverses
        /// back toward visible instead of restarting, and plain Show()/Hide() calls reverse a running
        /// override the same way. Every fresh show first restores the pose the last hide displaced
        /// (see <see cref="RestoreHiddenPose(UIAnimation)"/>) and only THEN captures start values, so
        /// the override's StartValue endpoints resolve against the true rest pose.
        /// </summary>
        public void OnShow(bool instant, UIAnimation overrideAnimation)
        {
            if (overrideAnimation == null) { OnShow(instant); return; }

            _showOverride = _showOverride ?? new UIAnimation { purpose = AnimationPurpose.Show };
            UIAnimationChannels.Copy(overrideAnimation, _showOverride);
            BindTarget();

            if (instant)
            {
                hideAnimation.Stop(silent: true);
                showAnimation.Stop(silent: true);
                _hideOverride?.Stop(silent: true);
                _showOverride.Stop(silent: true);
                RestoreHiddenPose(_showOverride);
                _showOverride.CaptureStartValues();
                _showOverride.SetProgressAtOne();
                return;
            }

            if (hideAnimation.isActive) { hideAnimation.Reverse(); return; }
            if (_hideOverride != null && _hideOverride.isActive) { _hideOverride.Reverse(); return; }

            RestoreHiddenPose(_showOverride);
            _showOverride.CaptureStartValues();
            _showOverride.Play(PlayDirection.Forward);
        }

        /// <summary> Hide counterpart of <see cref="OnShow(bool, UIAnimation)"/>. </summary>
        public void OnHide(bool instant, UIAnimation overrideAnimation)
        {
            if (overrideAnimation == null) { OnHide(instant); return; }

            _hideOverride = _hideOverride ?? new UIAnimation { purpose = AnimationPurpose.Hide };
            UIAnimationChannels.Copy(overrideAnimation, _hideOverride);
            BindTarget();
            _hideOverride.CaptureStartValues();

            if (instant)
            {
                showAnimation.Stop(silent: true);
                hideAnimation.Stop(silent: true);
                _showOverride?.Stop(silent: true);
                _hideOverride.Stop(silent: true);
                _hideOverride.SetProgressAtOne();
                return;
            }

            if (showAnimation.isActive) { showAnimation.Reverse(); return; }
            if (_showOverride != null && _showOverride.isActive) { _showOverride.Reverse(); return; }

            _hideOverride.Play(PlayDirection.Forward);
        }
    }
}
