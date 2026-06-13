using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Animates a color (Graphic/TMP/SpriteRenderer on this GameObject) with the container's
    /// Show/Hide lifecycle. Endpoints can reference theme tokens.
    /// </summary>
    [AddComponentMenu("Neo/UI/Animators/UI Container Color Animator")]
    public class UIContainerColorAnimator : MonoBehaviour, IContainerAnimator
    {
        [Tooltip("Container driving this animator; found on this GameObject or its parents when empty")]
        public UIContainer controller;

        public ColorAnimation showAnimation = new ColorAnimation
        {
            from = new ColorAnimationEndpoint { reference = ColorReference.CurrentColor },
            to = new ColorAnimationEndpoint { reference = ColorReference.StartColor }
        };

        public ColorAnimation hideAnimation = new ColorAnimation
        {
            from = new ColorAnimationEndpoint { reference = ColorReference.CurrentColor },
            to = new ColorAnimationEndpoint { reference = ColorReference.CustomColor, customColor = Color.clear }
        };

        public bool isAnimating => showAnimation.isActive || hideAnimation.isActive;
        public float showDuration => showAnimation.totalDuration;
        public float hideDuration => hideAnimation.totalDuration;

        protected virtual void Awake()
        {
            BindTarget();
        }

        protected virtual void OnEnable()
        {
            if (controller == null) controller = GetComponentInParent<UIContainer>();
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

        public void BindTarget()
        {
            IColorTarget target = ColorTargetUtils.FindTarget(gameObject);
            showAnimation.SetTarget(target);
            hideAnimation.SetTarget(target);
        }

        public void OnShow(bool instant)
        {
            if (instant)
            {
                hideAnimation.Stop(silent: true);
                showAnimation.Stop(silent: true);
                showAnimation.SetProgressAtOne();
                return;
            }

            if (hideAnimation.isActive)
            {
                hideAnimation.Reverse();
                return;
            }

            showAnimation.Play(PlayDirection.Forward);
        }

        public void OnHide(bool instant)
        {
            if (instant)
            {
                showAnimation.Stop(silent: true);
                hideAnimation.Stop(silent: true);
                hideAnimation.SetProgressAtOne();
                return;
            }

            if (showAnimation.isActive)
            {
                showAnimation.Reverse();
                return;
            }

            hideAnimation.Play(PlayDirection.Forward);
        }
    }
}
