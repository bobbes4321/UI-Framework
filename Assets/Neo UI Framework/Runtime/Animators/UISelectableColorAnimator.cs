using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Animates a color target to a per-selection-state color (entries can reference theme tokens).
    /// </summary>
    [AddComponentMenu("Neo/UI/Animators/UI Selectable Color Animator")]
    public class UISelectableColorAnimator : MonoBehaviour, ISelectionStateAnimator
    {
        [Tooltip("Selectable driving this animator; found on this GameObject or its parents when empty")]
        public Component controller;

        public SelectableColorSet colors = new SelectableColorSet();

        public TweenSettings transitionSettings = new TweenSettings { duration = 0.15f, ease = Ease.OutQuad };

        private ISelectionStateHost _host;
        private ColorTween _tween;
        private IColorTarget _target;

        protected virtual void Awake()
        {
            BindTarget();
        }

        protected virtual void OnEnable()
        {
            if (_host == null)
            {
                if (controller is ISelectionStateHost configured) _host = configured;
                else _host = GetComponentInParent<ISelectionStateHost>();
            }
            _host?.RegisterStateAnimator(this);
        }

        protected virtual void OnDisable()
        {
            _host?.UnregisterStateAnimator(this);
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

        public void OnSelectionStateChanged(UISelectionState state, bool instant)
        {
            BindTarget();
            if (_target == null) return;

            Color targetColor = colors.GetColor(state);

            if (instant)
            {
                _tween?.Stop(silent: true);
                _target.SetColor(targetColor);
                return;
            }

            if (_tween == null)
            {
                _tween = TweenPool.Get<ColorTween>();
                _tween.SetTarget(this, () => _target.GetColor(), c => _target.SetColor(c));
            }
            _tween.settings = transitionSettings;
            _tween.PlayToValue(targetColor);
        }
    }
}
