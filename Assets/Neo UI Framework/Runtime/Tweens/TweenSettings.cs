using System;
using UnityEngine;

namespace Neo.UI
{
    public enum TweenState
    {
        Pooled,
        Idle,
        StartDelay,
        Playing,
        LoopDelay,
        Paused
    }

    public enum PlayDirection
    {
        Reverse = -1,
        Forward = 1
    }

    public enum TweenPlayMode
    {
        Normal = 0,
        PingPong = 1,
        Spring = 2,
        Shake = 3
    }

    /// <summary> How a tween's From/To endpoint resolves its value when Play is called. </summary>
    public enum ReferenceValue
    {
        /// <summary> The value captured when the tween first played (plus offset). </summary>
        StartValue = 0,
        /// <summary> Wherever the target is right now (plus offset) — enables "continue from here" transitions. </summary>
        CurrentValue = 1,
        /// <summary> An absolute custom value. </summary>
        CustomValue = 2
    }

    /// <summary>
    /// Serializable timing/easing settings for a tween. Every timing field has an optional
    /// random-range variant, re-rolled on every Play, for organic-feeling loop animations.
    /// </summary>
    [Serializable]
    public class TweenSettings
    {
        public const int InfiniteLoops = -1;

        public TweenPlayMode playMode = TweenPlayMode.Normal;

        public EaseMode easeMode = EaseMode.Ease;
        public Ease ease = Ease.OutQuad;
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public float duration = 0.3f;
        public bool useRandomDuration;
        public Vector2 randomDuration = new Vector2(0.2f, 0.5f);

        public float startDelay;
        public bool useRandomStartDelay;
        public Vector2 randomStartDelay = new Vector2(0f, 0.2f);

        /// <summary> Number of extra plays after the first. -1 = infinite. </summary>
        public int loops;
        public bool useRandomLoops;
        public Vector2Int randomLoops = new Vector2Int(0, 2);

        public float loopDelay;
        public bool useRandomLoopDelay;
        public Vector2 randomLoopDelay = new Vector2(0f, 0.2f);

        // Spring / Shake settings
        [Min(0f)] public float strength = 1f;
        [Min(1)] public int vibration = 8;
        [Range(0f, 1f)] public float elasticity = 1f;
        public bool fadeOutShake;

        public float GetDuration() =>
            Mathf.Max(0f, useRandomDuration ? UnityEngine.Random.Range(randomDuration.x, randomDuration.y) : duration);

        public float GetStartDelay() =>
            Mathf.Max(0f, useRandomStartDelay ? UnityEngine.Random.Range(randomStartDelay.x, randomStartDelay.y) : startDelay);

        public int GetLoops() =>
            useRandomLoops ? UnityEngine.Random.Range(randomLoops.x, randomLoops.y + 1) : loops;

        public float GetLoopDelay() =>
            Mathf.Max(0f, useRandomLoopDelay ? UnityEngine.Random.Range(randomLoopDelay.x, randomLoopDelay.y) : loopDelay);

        public float Evaluate(float t)
        {
            return easeMode == EaseMode.AnimationCurve && curve != null
                ? curve.Evaluate(Mathf.Clamp01(t))
                : Easing.Evaluate(ease, t);
        }

        public TweenSettings Clone()
        {
            var clone = (TweenSettings)MemberwiseClone();
            if (curve != null) clone.curve = new AnimationCurve(curve.keys);
            return clone;
        }

        public void CopyFrom(TweenSettings other)
        {
            if (other == null) return;
            playMode = other.playMode;
            easeMode = other.easeMode;
            ease = other.ease;
            curve = other.curve != null ? new AnimationCurve(other.curve.keys) : null;
            duration = other.duration;
            useRandomDuration = other.useRandomDuration;
            randomDuration = other.randomDuration;
            startDelay = other.startDelay;
            useRandomStartDelay = other.useRandomStartDelay;
            randomStartDelay = other.randomStartDelay;
            loops = other.loops;
            useRandomLoops = other.useRandomLoops;
            randomLoops = other.randomLoops;
            loopDelay = other.loopDelay;
            useRandomLoopDelay = other.useRandomLoopDelay;
            randomLoopDelay = other.randomLoopDelay;
            strength = other.strength;
            vibration = other.vibration;
            elasticity = other.elasticity;
            fadeOutShake = other.fadeOutShake;
        }
    }
}
