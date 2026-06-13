using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Animates a color target between on/off colors with a toggle's value. </summary>
    [AddComponentMenu("AlterEyes/UI/Animators/UI Toggle Color Animator")]
    public class UIToggleColorAnimator : MonoBehaviour, IToggleAnimator
    {
        [Tooltip("Toggle driving this animator; found on this GameObject or its parents when empty")]
        public UIToggle controller;

        public ThemeColorRef onColor = new ThemeColorRef(Color.white);
        public ThemeColorRef offColor = new ThemeColorRef(new Color(0.6f, 0.6f, 0.6f));

        public TweenSettings transitionSettings = new TweenSettings { duration = 0.15f, ease = Ease.OutQuad };

        private ColorTween _tween;
        private IColorTarget _target;

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
            if (_tween == null) return;
            TweenPool.Release(_tween);
            _tween = null;
        }

        public void BindTarget()
        {
            if (_target == null) _target = ColorTargetUtils.FindTarget(gameObject);
        }

        public void OnToggleValueChanged(bool isOn, bool instant)
        {
            BindTarget();
            if (_target == null) return;

            Color targetColor = (isOn ? onColor : offColor).Resolve();

            if (instant)
            {
                _tween?.Stop(silent: true);
                _target.SetColor(targetColor);
                return;
            }

            if (_tween == null)
            {
                _tween = TweenPool.Get<ColorTween>();
                _tween.SetTarget(() => _target.GetColor(), c => _target.SetColor(c));
            }
            _tween.settings = transitionSettings;
            _tween.PlayToValue(targetColor);
        }
    }
}
