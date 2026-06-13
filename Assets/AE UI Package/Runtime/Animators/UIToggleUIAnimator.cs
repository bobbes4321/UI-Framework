using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Plays on/off UIAnimations with a toggle's value (instant changes skip the animation). </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("AlterEyes/UI/Animators/UI Toggle UI Animator")]
    public class UIToggleUIAnimator : MonoBehaviour, IToggleAnimator
    {
        [Tooltip("Toggle driving this animator; found on this GameObject or its parents when empty")]
        public UIToggle controller;

        public UIAnimation onAnimation = new UIAnimation { purpose = AnimationPurpose.State };
        public UIAnimation offAnimation = new UIAnimation { purpose = AnimationPurpose.State };

        protected virtual void Awake()
        {
            BindTarget();
        }

        protected virtual void OnEnable()
        {
            if (controller == null) controller = GetComponentInParent<UIToggle>();
            controller?.RegisterToggleAnimator(this);
        }

        protected virtual void OnDisable()
        {
            controller?.UnregisterToggleAnimator(this);
        }

        protected virtual void OnDestroy()
        {
            onAnimation.Stop(silent: true);
            offAnimation.Stop(silent: true);
            onAnimation.ReleaseTweens();
            offAnimation.ReleaseTweens();
        }

        public void BindTarget()
        {
            var rectTransform = GetComponent<RectTransform>();
            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null && (onAnimation.fade.enabled || offAnimation.fade.enabled))
                group = gameObject.AddComponent<CanvasGroup>();
            onAnimation.SetTarget(rectTransform, group);
            offAnimation.SetTarget(rectTransform, group);
        }

        public void OnToggleValueChanged(bool isOn, bool instant)
        {
            BindTarget();
            UIAnimation animation = isOn ? onAnimation : offAnimation;
            UIAnimation other = isOn ? offAnimation : onAnimation;
            if (!animation.hasEnabledChannels) return;

            other.Stop(silent: true);
            if (instant) animation.SetProgressAtOne();
            else animation.Play(PlayDirection.Forward);
        }
    }
}
