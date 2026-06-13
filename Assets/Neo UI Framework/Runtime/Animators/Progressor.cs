using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Neo.UI
{
    [Serializable] public class FloatEvent : UnityEvent<float> { }

    /// <summary>
    /// Drives a float from FromValue to ToValue with its own tween, raising value/progress events
    /// and updating registered <see cref="ProgressTarget"/>s (Image fill, TMP text, UnityEvent).
    /// </summary>
    [AddComponentMenu("Neo/UI/Progressor")]
    public class Progressor : MonoBehaviour
    {
        public enum StartBehaviour
        {
            Disabled = 0,
            SetFromValue = 1,
            SetToValue = 2,
            PlayForward = 3,
            PlayReverse = 4,
            /// <summary> Start at <see cref="startValue"/> — keeps the authored/baked visual state. </summary>
            SetCustomValue = 5
        }

        [Header("Values")]
        public float fromValue;
        public float toValue = 1f;

        [Header("Behaviour")]
        public StartBehaviour onStartBehaviour = StartBehaviour.SetFromValue;
        [Tooltip("Value applied on Start when behaviour is SetCustomValue")]
        public float startValue;

        [Header("Tween")]
        public TweenSettings settings = new TweenSettings { duration = 0.5f, ease = Ease.OutQuad };

        [Header("Targets")]
        public List<ProgressTarget> progressTargets = new List<ProgressTarget>();

        [Header("Events")]
        public FloatEvent OnValueChanged = new FloatEvent();
        public FloatEvent OnProgressChanged = new FloatEvent();
        public FloatEvent OnValueIncremented = new FloatEvent();
        public FloatEvent OnValueDecremented = new FloatEvent();
        public UnityEvent OnValueReachedFrom = new UnityEvent();
        public UnityEvent OnValueReachedTo = new UnityEvent();

        private FloatTween _tween;
        private float _currentValue;
        private bool _initialized;

        public float currentValue => _currentValue;

        /// <summary> Normalized progress of the current value between From and To. </summary>
        public float progress => Mathf.Approximately(toValue, fromValue)
            ? 1f
            : Mathf.InverseLerp(fromValue, toValue, _currentValue);

        public bool isActive => _tween?.isActive ?? false;
        public float duration => settings.startDelay + settings.duration;
        public FloatTween tween => _tween;

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            switch (onStartBehaviour)
            {
                case StartBehaviour.SetFromValue: SetValueAt(fromValue); break;
                case StartBehaviour.SetToValue: SetValueAt(toValue); break;
                case StartBehaviour.PlayForward: Play(PlayDirection.Forward); break;
                case StartBehaviour.PlayReverse: Play(PlayDirection.Reverse); break;
                case StartBehaviour.SetCustomValue: SetValueAt(startValue); break;
            }
        }

        private void OnDestroy()
        {
            if (_tween == null) return;
            TweenPool.Release(_tween);
            _tween = null;
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _currentValue = fromValue;
            _tween = TweenPool.Get<FloatTween>();
            _tween.SetTarget(() => _currentValue, ApplyValue);
        }

        private void Configure()
        {
            Initialize();
            _tween.settings = settings;
            _tween.SetFrom(fromValue);
            _tween.SetTo(toValue);
        }

        // ------------------------------------------------------------------ API

        public void Play(PlayDirection direction = PlayDirection.Forward)
        {
            Configure();
            _tween.Play(direction);
        }

        public void Play(bool inReverse) => Play(inReverse ? PlayDirection.Reverse : PlayDirection.Forward);

        public void Reverse()
        {
            Initialize();
            if (_tween.isActive) _tween.Reverse();
            else Play(PlayDirection.Reverse);
        }

        public void Stop() => _tween?.Stop();

        public void Finish() => _tween?.Finish();

        /// <summary> Tweens from the current value to the given absolute value. </summary>
        public void PlayToValue(float targetValue)
        {
            Configure();
            float clampedProgress = Mathf.Approximately(toValue, fromValue)
                ? 1f
                : Mathf.InverseLerp(fromValue, toValue, targetValue);
            _tween.PlayFromToProgress(progress, clampedProgress);
        }

        public void PlayToProgress(float targetProgress)
        {
            Configure();
            _tween.PlayFromToProgress(progress, Mathf.Clamp01(targetProgress));
        }

        /// <summary> Jumps to the given absolute value without animating. </summary>
        public void SetValueAt(float value)
        {
            Initialize();
            _tween.Stop(silent: true);
            ApplyValue(value);
        }

        /// <summary> Jumps to the given normalized progress without animating. </summary>
        public void SetProgressAt(float targetProgress)
        {
            SetValueAt(Mathf.Lerp(fromValue, toValue, Mathf.Clamp01(targetProgress)));
        }

        public void SetProgressAtZero() => SetProgressAt(0f);
        public void SetProgressAtOne() => SetProgressAt(1f);

        // ------------------------------------------------------------------ value application

        private void ApplyValue(float value)
        {
            float previous = _currentValue;
            _currentValue = value;

            if (!Mathf.Approximately(previous, value))
            {
                OnValueChanged?.Invoke(value);
                OnProgressChanged?.Invoke(progress);
                if (value > previous) OnValueIncremented?.Invoke(value - previous);
                else OnValueDecremented?.Invoke(previous - value);

                if (Mathf.Approximately(value, fromValue)) OnValueReachedFrom?.Invoke();
                if (Mathf.Approximately(value, toValue)) OnValueReachedTo?.Invoke();
            }

            for (int i = 0; i < progressTargets.Count; i++)
            {
                ProgressTarget target = progressTargets[i];
                if (target != null) target.UpdateTarget(this);
            }
        }
    }
}
