using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary> What an animation is for — drives sensible channel defaults. </summary>
    public enum AnimationPurpose
    {
        Show = 0,
        Hide = 1,
        Loop = 2,
        Button = 3,
        State = 4,
        Custom = 5
    }

    [Serializable]
    public class MoveAnimation
    {
        public bool enabled;
        public TweenSettings settings = new TweenSettings { ease = Ease.OutCubic };
        public UIMoveDirection fromDirection = UIMoveDirection.CustomPosition;
        public UIMoveDirection toDirection = UIMoveDirection.CustomPosition;
        public ReferenceValue fromReference = ReferenceValue.StartValue;
        public ReferenceValue toReference = ReferenceValue.StartValue;
        public Vector3 fromCustomValue;
        public Vector3 toCustomValue;
        public Vector3 fromOffset;
        public Vector3 toOffset;
    }

    [Serializable]
    public class RotateAnimation
    {
        public bool enabled;
        public TweenSettings settings = new TweenSettings { ease = Ease.OutCubic };
        public ReferenceValue fromReference = ReferenceValue.StartValue;
        public ReferenceValue toReference = ReferenceValue.StartValue;
        public Vector3 fromCustomValue;
        public Vector3 toCustomValue;
        public Vector3 fromOffset;
        public Vector3 toOffset;
    }

    [Serializable]
    public class ScaleAnimation
    {
        public bool enabled;
        public TweenSettings settings = new TweenSettings { ease = Ease.OutCubic };
        public ReferenceValue fromReference = ReferenceValue.StartValue;
        public ReferenceValue toReference = ReferenceValue.StartValue;
        public Vector3 fromCustomValue = Vector3.one;
        public Vector3 toCustomValue = Vector3.one;
        public Vector3 fromOffset;
        public Vector3 toOffset;
    }

    [Serializable]
    public class FadeAnimation
    {
        public bool enabled;
        public TweenSettings settings = new TweenSettings { ease = Ease.OutCubic };
        public ReferenceValue fromReference = ReferenceValue.CustomValue;
        public ReferenceValue toReference = ReferenceValue.CustomValue;
        [Range(0f, 1f)] public float fromCustomValue;
        [Range(0f, 1f)] public float toCustomValue = 1f;
        public float fromOffset;
        public float toOffset;
    }

    /// <summary>
    /// Composite animation: Move + Rotate + Scale + Fade channels over a RectTransform/CanvasGroup,
    /// each channel independently enabled with its own settings. The animation is finished only when
    /// every enabled channel's tween has finished.
    /// </summary>
    [Serializable]
    public class UIAnimation
    {
        public AnimationPurpose purpose = AnimationPurpose.Custom;
        public MoveAnimation move = new MoveAnimation();
        public RotateAnimation rotate = new RotateAnimation();
        public ScaleAnimation scale = new ScaleAnimation();
        public FadeAnimation fade = new FadeAnimation();

        [NonSerialized] private RectTransform _rectTransform;
        [NonSerialized] private CanvasGroup _canvasGroup;
        [NonSerialized] private Vector3Tween _moveTween;
        [NonSerialized] private Vector3Tween _rotateTween;
        [NonSerialized] private Vector3Tween _scaleTween;
        [NonSerialized] private FloatTween _fadeTween;
        [NonSerialized] private bool _hasStartValues;

        public Vector3 startPosition { get; private set; }
        public Vector3 startRotation { get; private set; }
        public Vector3 startScale { get; private set; } = Vector3.one;
        public float startAlpha { get; private set; } = 1f;

        public RectTransform rectTransform => _rectTransform;
        public CanvasGroup canvasGroup => _canvasGroup;

        /// <summary> Invoked when a play starts. </summary>
        public Action onPlay;
        /// <summary> Invoked when every enabled channel has finished naturally. </summary>
        public Action onFinish;
        /// <summary> Invoked when the animation is stopped before finishing. </summary>
        public Action onStop;

        public bool hasEnabledChannels => move.enabled || rotate.enabled || scale.enabled || fade.enabled;

        public bool isActive =>
            (_moveTween?.isActive ?? false) || (_rotateTween?.isActive ?? false) ||
            (_scaleTween?.isActive ?? false) || (_fadeTween?.isActive ?? false);

        /// <summary> Worst-case duration of one play: max over enabled channels of delay + duration. </summary>
        public float totalDuration
        {
            get
            {
                float total = 0f;
                if (move.enabled) total = Mathf.Max(total, move.settings.startDelay + move.settings.duration);
                if (rotate.enabled) total = Mathf.Max(total, rotate.settings.startDelay + rotate.settings.duration);
                if (scale.enabled) total = Mathf.Max(total, scale.settings.startDelay + scale.settings.duration);
                if (fade.enabled) total = Mathf.Max(total, fade.settings.startDelay + fade.settings.duration);
                return total;
            }
        }

        // ------------------------------------------------------------------ target binding

        public void SetTarget(RectTransform target, CanvasGroup group = null)
        {
            _rectTransform = target;
            _canvasGroup = group != null ? group : (target != null ? target.GetComponent<CanvasGroup>() : null);
            if (!_hasStartValues) CaptureStartValues();
        }

        /// <summary> Snapshots the target's rest state — the values StartValue endpoints resolve to. </summary>
        public void CaptureStartValues()
        {
            if (_rectTransform == null) return;
            startPosition = _rectTransform.anchoredPosition3D;
            startRotation = _rectTransform.localEulerAngles;
            startScale = _rectTransform.localScale;
            startAlpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;
            _hasStartValues = true;
        }

        /// <summary>
        /// Restores the captured rest state — ONLY for channels this animation animates.
        /// Untouched channels must stay untouched: layout groups own positions and a cascade may
        /// own alpha; restoring a stale captured position teleports buttons to where the layout
        /// had them at capture time (e.g. another resolution).
        /// </summary>
        public void RestoreStartValues()
        {
            if (_rectTransform == null || !_hasStartValues) return;
            if (move.enabled) _rectTransform.anchoredPosition3D = startPosition;
            if (rotate.enabled) _rectTransform.localEulerAngles = startRotation;
            if (scale.enabled) _rectTransform.localScale = startScale;
            if (fade.enabled && _canvasGroup != null) _canvasGroup.alpha = startAlpha;
        }

        // ------------------------------------------------------------------ playback

        public void Play(bool inReverse) => Play(inReverse ? PlayDirection.Reverse : PlayDirection.Forward);

        public void Play(PlayDirection direction = PlayDirection.Forward)
        {
            if (_rectTransform == null)
            {
                Debug.LogWarning("[Neo.UI] UIAnimation has no target — call SetTarget first.");
                return;
            }

            Stop(silent: true);
            ConfigureTweens();
            onPlay?.Invoke();

            if (!hasEnabledChannels)
            {
                onFinish?.Invoke();
                return;
            }

            if (move.enabled) _moveTween.Play(direction);
            if (rotate.enabled) _rotateTween.Play(direction);
            if (scale.enabled) _scaleTween.Play(direction);
            if (fade.enabled) _fadeTween.Play(direction);
        }

        public void Reverse()
        {
            _moveTween?.Reverse();
            _rotateTween?.Reverse();
            _scaleTween?.Reverse();
            _fadeTween?.Reverse();
        }

        public void Stop(bool silent = false)
        {
            bool wasActive = isActive;
            _moveTween?.Stop(silent: true);
            _rotateTween?.Stop(silent: true);
            _scaleTween?.Stop(silent: true);
            _fadeTween?.Stop(silent: true);
            if (wasActive && !silent) onStop?.Invoke();
        }

        /// <summary> Jumps every enabled channel to its end value and completes. </summary>
        public void Finish()
        {
            if (!isActive)
            {
                SetProgressAtOne();
                onFinish?.Invoke();
                return;
            }
            // Finishing the tweens fires their onFinish, which funnels into CheckFinished → onFinish.
            _moveTween?.Finish(silent: !move.enabled);
            _rotateTween?.Finish(silent: !rotate.enabled);
            _scaleTween?.Finish(silent: !scale.enabled);
            _fadeTween?.Finish(silent: !fade.enabled);
        }

        public void Pause()
        {
            _moveTween?.Pause();
            _rotateTween?.Pause();
            _scaleTween?.Pause();
            _fadeTween?.Pause();
        }

        public void Resume()
        {
            _moveTween?.Resume();
            _rotateTween?.Resume();
            _scaleTween?.Resume();
            _fadeTween?.Resume();
        }

        /// <summary> Scrubs all enabled channels to the given progress without playing. </summary>
        public void SetProgressAt(float progress)
        {
            if (_rectTransform == null) return;
            ConfigureTweens();
            if (move.enabled) _moveTween.SetProgressAt(progress);
            if (rotate.enabled) _rotateTween.SetProgressAt(progress);
            if (scale.enabled) _scaleTween.SetProgressAt(progress);
            if (fade.enabled) _fadeTween.SetProgressAt(progress);
        }

        public void SetProgressAtZero() => SetProgressAt(0f);
        public void SetProgressAtOne() => SetProgressAt(1f);

        /// <summary> Releases pooled tweens (call from the owning component's OnDestroy). </summary>
        public void ReleaseTweens()
        {
            if (_moveTween != null) { TweenPool.Release(_moveTween); _moveTween = null; }
            if (_rotateTween != null) { TweenPool.Release(_rotateTween); _rotateTween = null; }
            if (_scaleTween != null) { TweenPool.Release(_scaleTween); _scaleTween = null; }
            if (_fadeTween != null) { TweenPool.Release(_fadeTween); _fadeTween = null; }
        }

        // ------------------------------------------------------------------ wiring

        private void ConfigureTweens()
        {
            if (!_hasStartValues) CaptureStartValues();

            if (move.enabled)
            {
                _moveTween = _moveTween ?? TweenPool.Get<Vector3Tween>();
                _moveTween.settings = move.settings;
                _moveTween.SetTarget(
                    () => _rectTransform != null ? _rectTransform.anchoredPosition3D : Vector3.zero,
                    v => { if (_rectTransform != null) _rectTransform.anchoredPosition3D = v; });
                _moveTween.SetStartValue(startPosition);
                ConfigureMoveEndpoint(_moveTween, isFrom: true);
                ConfigureMoveEndpoint(_moveTween, isFrom: false);
                _moveTween.onFinish = CheckFinished;
            }

            if (rotate.enabled)
            {
                _rotateTween = _rotateTween ?? TweenPool.Get<Vector3Tween>();
                _rotateTween.settings = rotate.settings;
                _rotateTween.SetTarget(
                    () => _rectTransform != null ? _rectTransform.localEulerAngles : Vector3.zero,
                    v => { if (_rectTransform != null) _rectTransform.localEulerAngles = v; });
                _rotateTween.SetStartValue(startRotation);
                _rotateTween.fromReferenceValue = rotate.fromReference;
                _rotateTween.toReferenceValue = rotate.toReference;
                _rotateTween.fromCustomValue = rotate.fromCustomValue;
                _rotateTween.toCustomValue = rotate.toCustomValue;
                _rotateTween.fromOffset = rotate.fromOffset;
                _rotateTween.toOffset = rotate.toOffset;
                _rotateTween.onFinish = CheckFinished;
            }

            if (scale.enabled)
            {
                _scaleTween = _scaleTween ?? TweenPool.Get<Vector3Tween>();
                _scaleTween.settings = scale.settings;
                _scaleTween.SetTarget(
                    () => _rectTransform != null ? _rectTransform.localScale : Vector3.one,
                    v => { if (_rectTransform != null) _rectTransform.localScale = v; });
                _scaleTween.SetStartValue(startScale);
                _scaleTween.fromReferenceValue = scale.fromReference;
                _scaleTween.toReferenceValue = scale.toReference;
                _scaleTween.fromCustomValue = scale.fromCustomValue;
                _scaleTween.toCustomValue = scale.toCustomValue;
                _scaleTween.fromOffset = scale.fromOffset;
                _scaleTween.toOffset = scale.toOffset;
                _scaleTween.onFinish = CheckFinished;
            }

            if (fade.enabled)
            {
                _fadeTween = _fadeTween ?? TweenPool.Get<FloatTween>();
                _fadeTween.settings = fade.settings;
                _fadeTween.SetTarget(
                    () => _canvasGroup != null ? _canvasGroup.alpha : 1f,
                    v => { if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Clamp01(v); });
                _fadeTween.SetStartValue(startAlpha);
                _fadeTween.fromReferenceValue = fade.fromReference;
                _fadeTween.toReferenceValue = fade.toReference;
                _fadeTween.fromCustomValue = fade.fromCustomValue;
                _fadeTween.toCustomValue = fade.toCustomValue;
                _fadeTween.fromOffset = fade.fromOffset;
                _fadeTween.toOffset = fade.toOffset;
                _fadeTween.onFinish = CheckFinished;
            }
        }

        private void ConfigureMoveEndpoint(Vector3Tween tween, bool isFrom)
        {
            UIMoveDirection direction = isFrom ? move.fromDirection : move.toDirection;
            if (direction != UIMoveDirection.CustomPosition)
            {
                Vector3 position = MoveMath.GetTargetPosition(_rectTransform, direction, startPosition);
                if (isFrom) tween.SetFrom(position);
                else tween.SetTo(position);
                return;
            }

            if (isFrom)
            {
                tween.fromReferenceValue = move.fromReference;
                tween.fromCustomValue = move.fromCustomValue;
                tween.fromOffset = move.fromOffset;
            }
            else
            {
                tween.toReferenceValue = move.toReference;
                tween.toCustomValue = move.toCustomValue;
                tween.toOffset = move.toOffset;
            }
        }

        private void CheckFinished()
        {
            if (isActive) return;
            onFinish?.Invoke();
        }

        // ------------------------------------------------------------------ purpose defaults

        /// <summary> Applies sensible defaults for the given purpose (Show slides+fades in, Button scale-pulses, …). </summary>
        public void ApplyPurposeDefaults(AnimationPurpose newPurpose)
        {
            purpose = newPurpose;
            switch (newPurpose)
            {
                case AnimationPurpose.Show:
                    move.enabled = true;
                    move.settings = new TweenSettings { duration = 0.6f, ease = Ease.OutExpo };
                    move.fromDirection = UIMoveDirection.Left;
                    move.toDirection = UIMoveDirection.CustomPosition;
                    move.toReference = ReferenceValue.StartValue;
                    fade.enabled = true;
                    fade.settings = new TweenSettings { duration = 0.6f, ease = Ease.OutExpo };
                    fade.fromCustomValue = 0f;
                    fade.toCustomValue = 1f;
                    break;
                case AnimationPurpose.Hide:
                    move.enabled = true;
                    move.settings = new TweenSettings { duration = 0.6f, ease = Ease.InExpo };
                    move.fromDirection = UIMoveDirection.CustomPosition;
                    move.fromReference = ReferenceValue.StartValue;
                    move.toDirection = UIMoveDirection.Left;
                    fade.enabled = true;
                    fade.settings = new TweenSettings { duration = 0.6f, ease = Ease.InExpo };
                    fade.fromCustomValue = 1f;
                    fade.toCustomValue = 0f;
                    break;
                case AnimationPurpose.Button:
                    scale.enabled = true;
                    scale.settings = new TweenSettings { duration = 0.2f, ease = Ease.InOutQuad };
                    scale.fromReference = ReferenceValue.CustomValue;
                    scale.fromCustomValue = new Vector3(0.9f, 0.9f, 1f);
                    scale.toReference = ReferenceValue.CustomValue;
                    scale.toCustomValue = Vector3.one;
                    break;
                case AnimationPurpose.Loop:
                    scale.enabled = true;
                    scale.settings = new TweenSettings
                    {
                        duration = 0.5f,
                        ease = Ease.InOutSine,
                        playMode = TweenPlayMode.PingPong,
                        loops = TweenSettings.InfiniteLoops
                    };
                    scale.fromReference = ReferenceValue.StartValue;
                    scale.toReference = ReferenceValue.StartValue;
                    scale.toOffset = new Vector3(0.05f, 0.05f, 0f);
                    break;
            }
        }
    }
}
