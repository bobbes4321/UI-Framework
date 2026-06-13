using System;
using UnityEngine;
using UnityEngine.UI;

namespace AlterEyes.UI
{
    /// <summary> Abstraction over anything with an animatable color (UGUI Graphic, TMP text, SpriteRenderer…). </summary>
    public interface IColorTarget
    {
        Color GetColor();
        void SetColor(Color color);
        UnityEngine.Object targetObject { get; }
    }

    /// <summary> Color target for any UGUI Graphic — covers Image, RawImage and TMP text (TMP_Text is a Graphic). </summary>
    public class GraphicColorTarget : IColorTarget
    {
        private readonly Graphic _graphic;
        public GraphicColorTarget(Graphic graphic) => _graphic = graphic;
        public Color GetColor() => _graphic != null ? _graphic.color : Color.white;
        public void SetColor(Color color) { if (_graphic != null) _graphic.color = color; }
        public UnityEngine.Object targetObject => _graphic;
    }

    public class SpriteRendererColorTarget : IColorTarget
    {
        private readonly SpriteRenderer _renderer;
        public SpriteRendererColorTarget(SpriteRenderer renderer) => _renderer = renderer;
        public Color GetColor() => _renderer != null ? _renderer.color : Color.white;
        public void SetColor(Color color) { if (_renderer != null) _renderer.color = color; }
        public UnityEngine.Object targetObject => _renderer;
    }

    public static class ColorTargetUtils
    {
        /// <summary> Finds a color target on the GameObject (Graphic first, then SpriteRenderer). </summary>
        public static IColorTarget FindTarget(GameObject gameObject)
        {
            if (gameObject == null) return null;
            var graphic = gameObject.GetComponent<Graphic>();
            if (graphic != null) return new GraphicColorTarget(graphic);
            var spriteRenderer = gameObject.GetComponent<SpriteRenderer>();
            return spriteRenderer != null ? new SpriteRendererColorTarget(spriteRenderer) : null;
        }
    }

    /// <summary> How a color animation endpoint resolves. </summary>
    public enum ColorReference
    {
        /// <summary> The color captured when the animation first played. </summary>
        StartColor = 0,
        /// <summary> Whatever color the target has right now. </summary>
        CurrentColor = 1,
        /// <summary> A hardcoded color. </summary>
        CustomColor = 2,
        /// <summary> A theme token, resolved against the active theme at play time. </summary>
        ThemeToken = 3
    }

    [Serializable]
    public class ColorAnimationEndpoint
    {
        public ColorReference reference = ColorReference.CustomColor;
        public Color customColor = Color.white;
        public string themeToken;
    }

    /// <summary>
    /// Animates a color over time against an <see cref="IColorTarget"/>. Endpoints can reference
    /// the start/current color, a custom color, or a theme token ("animate to theme token X").
    /// </summary>
    [Serializable]
    public class ColorAnimation
    {
        public bool enabled = true;
        public TweenSettings settings = new TweenSettings { ease = Ease.OutQuad, duration = 0.2f };
        public ColorAnimationEndpoint from = new ColorAnimationEndpoint { reference = ColorReference.CurrentColor };
        public ColorAnimationEndpoint to = new ColorAnimationEndpoint();

        [NonSerialized] private IColorTarget _target;
        [NonSerialized] private ColorTween _tween;
        [NonSerialized] private Color _startColor = Color.white;
        [NonSerialized] private bool _hasStartColor;

        public Action onPlay;
        public Action onFinish;
        public Action onStop;

        public IColorTarget target => _target;
        public Color startColor => _startColor;
        public bool isActive => _tween?.isActive ?? false;
        public float totalDuration => settings.startDelay + settings.duration;

        public void SetTarget(IColorTarget colorTarget)
        {
            _target = colorTarget;
            if (!_hasStartColor && _target != null) CaptureStartColor();
        }

        public void SetTarget(GameObject gameObject) => SetTarget(ColorTargetUtils.FindTarget(gameObject));

        public void CaptureStartColor()
        {
            if (_target == null) return;
            _startColor = _target.GetColor();
            _hasStartColor = true;
        }

        public void RestoreStartColor()
        {
            if (_target == null || !_hasStartColor) return;
            _target.SetColor(_startColor);
        }

        public void Play(bool inReverse) => Play(inReverse ? PlayDirection.Reverse : PlayDirection.Forward);

        public void Play(PlayDirection direction = PlayDirection.Forward)
        {
            if (!enabled || _target == null)
            {
                onPlay?.Invoke();
                onFinish?.Invoke();
                return;
            }
            Configure();
            onPlay?.Invoke();
            _tween.Play(direction);
        }

        /// <summary> Animates from the current color to the given absolute color. </summary>
        public void PlayToColor(Color color)
        {
            if (_target == null) return;
            Configure();
            onPlay?.Invoke();
            _tween.PlayToValue(color);
        }

        public void Reverse() => _tween?.Reverse();

        public void Stop(bool silent = false)
        {
            bool wasActive = isActive;
            _tween?.Stop(silent: true);
            if (wasActive && !silent) onStop?.Invoke();
        }

        public void Finish() => _tween?.Finish();

        public void SetProgressAt(float progress)
        {
            if (_target == null) return;
            Configure();
            _tween.SetProgressAt(progress);
        }

        public void SetProgressAtZero() => SetProgressAt(0f);
        public void SetProgressAtOne() => SetProgressAt(1f);

        public void ReleaseTweens()
        {
            if (_tween == null) return;
            TweenPool.Release(_tween);
            _tween = null;
        }

        private void Configure()
        {
            if (!_hasStartColor) CaptureStartColor();
            _tween = _tween ?? TweenPool.Get<ColorTween>();
            _tween.settings = settings;
            _tween.SetTarget(() => _target.GetColor(), c => _target.SetColor(c));
            _tween.SetStartValue(_startColor);
            ApplyEndpoint(from, isFrom: true);
            ApplyEndpoint(to, isFrom: false);
            _tween.onFinish = () => onFinish?.Invoke();
        }

        private void ApplyEndpoint(ColorAnimationEndpoint endpoint, bool isFrom)
        {
            switch (endpoint.reference)
            {
                case ColorReference.StartColor:
                    Set(isFrom, ReferenceValue.StartValue, default);
                    break;
                case ColorReference.CurrentColor:
                    Set(isFrom, ReferenceValue.CurrentValue, default);
                    break;
                case ColorReference.CustomColor:
                    Set(isFrom, ReferenceValue.CustomValue, endpoint.customColor);
                    break;
                case ColorReference.ThemeToken:
                    Color resolved = endpoint.customColor;
                    if (ThemeService.TryGetColor(endpoint.themeToken, out Color themed)) resolved = themed;
                    Set(isFrom, ReferenceValue.CustomValue, resolved);
                    break;
            }
        }

        private void Set(bool isFrom, ReferenceValue reference, Color custom)
        {
            if (isFrom)
            {
                _tween.fromReferenceValue = reference;
                _tween.fromCustomValue = custom;
                _tween.fromOffset = default;
            }
            else
            {
                _tween.toReferenceValue = reference;
                _tween.toCustomValue = custom;
                _tween.toOffset = default;
            }
        }
    }
}
