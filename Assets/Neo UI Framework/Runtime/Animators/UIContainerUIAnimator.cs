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

        private bool _showWasInterrupted;
        private bool _hideWasInterrupted;

        public bool isAnimating => showAnimation.isActive || hideAnimation.isActive;
        public float showDuration => showAnimation.totalDuration;
        public float hideDuration => hideAnimation.totalDuration;

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
        }

        private void FindController()
        {
            if (controller == null) controller = GetComponentInParent<UIContainer>();
        }

        public void BindTarget()
        {
            var rectTransform = GetComponent<RectTransform>();
            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null && (showAnimation.fade.enabled || hideAnimation.fade.enabled))
                group = gameObject.AddComponent<CanvasGroup>();
            showAnimation.SetTarget(rectTransform, group);
            hideAnimation.SetTarget(rectTransform, group);
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

        public void OnShow(bool instant)
        {
            BindTarget();
            if (ApplyDefaultAlphaIfEmpty(1f)) return;
            if (instant)
            {
                hideAnimation.Stop(silent: true);
                showAnimation.Stop(silent: true);
                showAnimation.SetProgressAtOne();
                return;
            }

            if (hideAnimation.isActive)
            {
                // interrupt the running hide by reversing it back toward the visible state
                hideAnimation.Reverse();
                return;
            }

            showAnimation.Play(PlayDirection.Forward);
        }

        public void OnHide(bool instant)
        {
            BindTarget();
            if (ApplyDefaultAlphaIfEmpty(0f)) return;
            if (instant)
            {
                showAnimation.Stop(silent: true);
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

            hideAnimation.Play(PlayDirection.Forward);
        }
    }
}
