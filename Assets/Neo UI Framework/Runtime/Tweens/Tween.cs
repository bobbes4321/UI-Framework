using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Base tween: a pooled, allocation-free-at-playback animation object driven by <see cref="UITick"/>.
    /// Works identically in play mode and edit mode (the tick source differs, the tween doesn't).
    ///
    /// Playhead model: <c>elapsed</c> is a position in [0, duration]. Forward ticks increase it,
    /// Reverse ticks decrease it; <see cref="Reverse"/> mid-flight just flips the direction, keeping
    /// the playhead — which is what makes interruptible Show/Hide transitions possible.
    /// </summary>
    public abstract class Tween : ITickable
    {
        public TweenSettings settings = new TweenSettings();

        public TweenState state { get; private set; } = TweenState.Idle;
        public PlayDirection direction { get; private set; } = PlayDirection.Forward;

        /// <summary> Playhead position in seconds, within [playStart, playEnd]. </summary>
        public float elapsed { get; private set; }

        /// <summary> Resolved duration for the current play (random ranges already rolled). </summary>
        public float currentDuration { get; private set; }
        public float currentStartDelay { get; private set; }
        public int currentLoops { get; private set; }
        public float currentLoopDelay { get; private set; }
        public int completedLoops { get; private set; }

        /// <summary> Linear progress of the playhead in [0,1]. </summary>
        public float progress => currentDuration > 0f ? Mathf.Clamp01(elapsed / currentDuration) : (direction == PlayDirection.Forward ? 1f : 0f);

        public float easedProgress => settings.Evaluate(progress);

        public bool isActive => state == TweenState.StartDelay || state == TweenState.Playing || state == TweenState.LoopDelay || state == TweenState.Paused;
        public bool isPlaying => state == TweenState.Playing;
        public bool isPaused => state == TweenState.Paused;
        public bool isPooled => state == TweenState.Pooled;

        // Playhead boundaries (seconds) — narrowed by PlayToProgress / PlayFromToProgress.
        private float _playStart;
        private float _playEnd;
        private float _delayElapsed;
        private TweenState _stateBeforePause;
        private bool _pingPongFlipped;

        public Action onPlay;
        public Action onStop;
        public Action onFinish;
        public Action onLoop;
        public Action onPause;
        public Action onResume;
        /// <summary> Invoked every tick while playing, with the eased progress. </summary>
        public Action<float> onUpdate;

        // ------------------------------------------------------------------ play API

        public void Play(bool inReverse) => Play(inReverse ? PlayDirection.Reverse : PlayDirection.Forward);

        public void Play(PlayDirection playDirection = PlayDirection.Forward)
        {
            if (state == TweenState.Pooled)
            {
                Debug.LogWarning("[Neo.UI] Cannot play a pooled tween. Get a fresh one from TweenPool.");
                return;
            }

            if (isActive) Stop(silent: true);

            RefreshSettings();
            direction = playDirection;
            _playStart = 0f;
            _playEnd = currentDuration;
            elapsed = direction == PlayDirection.Forward ? _playStart : _playEnd;
            completedLoops = 0;
            _pingPongFlipped = false;
            _delayElapsed = 0f;

            ResolveValues();

            onPlay?.Invoke();

            if (currentStartDelay > 0f)
            {
                SetState(TweenState.StartDelay);
            }
            else
            {
                SetState(TweenState.Playing);
                ApplyProgress();
            }

            UITick.Register(this);

            if (currentDuration <= 0f && state == TweenState.Playing)
                CompleteCycle();
        }

        /// <summary> Plays from the current progress to the given progress (direction inferred). </summary>
        public void PlayToProgress(float toProgress)
        {
            float current = isActive ? progress : ResolveIdleProgress();
            PlayFromToProgress(current, Mathf.Clamp01(toProgress));
        }

        /// <summary> Plays from the given progress to the end (forward). </summary>
        public void PlayFromProgress(float fromProgress) => PlayFromToProgress(Mathf.Clamp01(fromProgress), 1f);

        public void PlayFromToProgress(float fromProgress, float toProgress)
        {
            if (state == TweenState.Pooled) return;
            if (isActive) Stop(silent: true);

            RefreshSettings();
            fromProgress = Mathf.Clamp01(fromProgress);
            toProgress = Mathf.Clamp01(toProgress);
            direction = toProgress >= fromProgress ? PlayDirection.Forward : PlayDirection.Reverse;
            _playStart = Mathf.Min(fromProgress, toProgress) * currentDuration;
            _playEnd = Mathf.Max(fromProgress, toProgress) * currentDuration;
            elapsed = fromProgress * currentDuration;
            completedLoops = 0;
            _pingPongFlipped = false;
            _delayElapsed = 0f;
            currentLoops = 0; // partial plays don't loop

            ResolveValues();
            onPlay?.Invoke();
            SetState(TweenState.Playing);
            ApplyProgress();
            UITick.Register(this);

            if (Mathf.Approximately(_playStart, _playEnd)) CompleteCycle();
        }

        public void Stop(bool silent = false)
        {
            if (!isActive)
            {
                UITick.Unregister(this);
                return;
            }
            SetState(TweenState.Idle);
            UITick.Unregister(this);
            if (!silent) onStop?.Invoke();
        }

        /// <summary> Jumps to the end value, then fires onStop and onFinish. </summary>
        public void Finish(bool silent = false)
        {
            if (state == TweenState.Pooled) return;
            bool wasActive = isActive;
            if (wasActive)
            {
                elapsed = direction == PlayDirection.Forward ? _playEnd : _playStart;
                ApplyProgress();
            }
            SetState(TweenState.Idle);
            UITick.Unregister(this);
            if (!silent)
            {
                onStop?.Invoke();
                onFinish?.Invoke();
            }
        }

        public void Pause(bool silent = false)
        {
            if (!isActive || state == TweenState.Paused) return;
            _stateBeforePause = state;
            SetState(TweenState.Paused);
            if (!silent) onPause?.Invoke();
        }

        public void Resume(bool silent = false)
        {
            if (state != TweenState.Paused) return;
            SetState(_stateBeforePause);
            if (!silent) onResume?.Invoke();
        }

        /// <summary> Flips direction. Mid-flight the playhead is kept — the motion reverses smoothly. </summary>
        public void Reverse()
        {
            if (isActive)
            {
                if (state == TweenState.StartDelay)
                {
                    _delayElapsed = 0f;
                    SetState(TweenState.Playing);
                }
                direction = direction == PlayDirection.Forward ? PlayDirection.Reverse : PlayDirection.Forward;
            }
            else
            {
                Play(direction == PlayDirection.Forward ? PlayDirection.Reverse : PlayDirection.Forward);
            }
        }

        /// <summary> Moves the playhead back to the start of the current direction without stopping. </summary>
        public void Rewind()
        {
            elapsed = direction == PlayDirection.Forward ? _playStart : _playEnd;
            if (isActive && state != TweenState.StartDelay) ApplyProgress();
        }

        /// <summary> Evaluates and applies the value at the given progress without starting playback. </summary>
        public void SetProgressAt(float targetProgress)
        {
            if (state == TweenState.Pooled) return;
            if (isActive) Stop(silent: true);
            RefreshSettings();
            _playStart = 0f;
            _playEnd = currentDuration;
            ResolveValues();
            elapsed = Mathf.Clamp01(targetProgress) * currentDuration;
            ApplyProgress();
        }

        public void SetProgressAtZero() => SetProgressAt(0f);
        public void SetProgressAtOne() => SetProgressAt(1f);

        // ------------------------------------------------------------------ ticking

        public void Tick(float deltaTime)
        {
            switch (state)
            {
                case TweenState.StartDelay:
                    _delayElapsed += deltaTime;
                    if (_delayElapsed >= currentStartDelay)
                    {
                        float overflow = _delayElapsed - currentStartDelay;
                        _delayElapsed = 0f;
                        SetState(TweenState.Playing);
                        ApplyProgress();
                        if (overflow > 0f) Tick(overflow);
                    }
                    break;

                case TweenState.LoopDelay:
                    _delayElapsed += deltaTime;
                    if (_delayElapsed >= currentLoopDelay)
                    {
                        float overflow = _delayElapsed - currentLoopDelay;
                        _delayElapsed = 0f;
                        StartNextLoop();
                        if (overflow > 0f) Tick(overflow);
                    }
                    break;

                case TweenState.Playing:
                    elapsed += deltaTime * (int)direction;
                    bool hitEnd = direction == PlayDirection.Forward ? elapsed >= _playEnd : elapsed <= _playStart;
                    if (hitEnd)
                    {
                        elapsed = direction == PlayDirection.Forward ? _playEnd : _playStart;
                        ApplyProgress();
                        CompleteCycle();
                    }
                    else
                    {
                        ApplyProgress();
                    }
                    break;
            }
        }

        private void CompleteCycle()
        {
            bool hasMoreLoops = currentLoops == TweenSettings.InfiniteLoops || completedLoops < currentLoops;
            if (hasMoreLoops)
            {
                completedLoops++;
                onLoop?.Invoke();
                if (currentLoopDelay > 0f)
                {
                    _delayElapsed = 0f;
                    SetState(TweenState.LoopDelay);
                }
                else
                {
                    StartNextLoop();
                }
                return;
            }

            SetState(TweenState.Idle);
            UITick.Unregister(this);
            onStop?.Invoke();
            onFinish?.Invoke();
        }

        private void StartNextLoop()
        {
            SetState(TweenState.Playing);
            if (settings.playMode == TweenPlayMode.PingPong)
            {
                direction = direction == PlayDirection.Forward ? PlayDirection.Reverse : PlayDirection.Forward;
                _pingPongFlipped = !_pingPongFlipped;
            }
            else
            {
                elapsed = direction == PlayDirection.Forward ? _playStart : _playEnd;
            }
            ApplyProgress();
        }

        private void ApplyProgress()
        {
            float eased = settings.Evaluate(progress);
            ApplyValue(eased, progress);
            onUpdate?.Invoke(eased);
        }

        private void SetState(TweenState newState) => state = newState;

        // ------------------------------------------------------------------ subclass hooks

        /// <summary> Re-rolls timing settings (called on every Play). </summary>
        protected void RefreshSettings()
        {
            currentDuration = settings.GetDuration();
            currentStartDelay = settings.GetStartDelay();
            currentLoops = settings.GetLoops();
            currentLoopDelay = settings.GetLoopDelay();
        }

        private float ResolveIdleProgress() => currentDuration > 0f ? Mathf.Clamp01(elapsed / currentDuration) : 0f;

        /// <summary> Resolves From/To endpoint values (and spring/shake cycles) at play time. </summary>
        protected abstract void ResolveValues();

        /// <summary> Applies the value for the given eased progress to the target. </summary>
        protected abstract void ApplyValue(float easedT, float linearT);

        /// <summary> Clears callbacks and resets runtime state (used when recycling into the pool). </summary>
        public virtual void Reset()
        {
            Stop(silent: true);
            onPlay = onStop = onFinish = onLoop = onPause = onResume = null;
            onUpdate = null;
            settings = new TweenSettings();
            elapsed = 0f;
            completedLoops = 0;
            direction = PlayDirection.Forward;
        }

        internal void MarkPooled() => SetState(TweenState.Pooled);
        internal void MarkIdle() => SetState(TweenState.Idle);
    }
}
