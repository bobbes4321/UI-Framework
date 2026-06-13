using System;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Typed tween: From/To endpoints with reference-value modes (StartValue / CurrentValue / CustomValue
    /// plus offsets), a getter/setter pair binding it to its target, and Spring/Shake cycle evaluation.
    /// </summary>
    public abstract class Tween<T> : Tween where T : struct
    {
        /// <summary> Reads the current value from the target (also used to resolve CurrentValue endpoints). </summary>
        public Func<T> getter;
        /// <summary> Writes the animated value to the target. </summary>
        public Action<T> setter;

        public ReferenceValue fromReferenceValue = ReferenceValue.CustomValue;
        public ReferenceValue toReferenceValue = ReferenceValue.CustomValue;
        public T fromCustomValue;
        public T toCustomValue;
        public T fromOffset;
        public T toOffset;

        /// <summary> Resolved endpoints for the current play. </summary>
        public T fromValue { get; private set; }
        public T toValue { get; private set; }
        public T currentValue { get; private set; }

        /// <summary> The captured "initial" value used by StartValue endpoints. </summary>
        public T startValue { get; private set; }
        public bool hasStartValue { get; private set; }

        public Action<T> onValueChanged;

        // Spring/Shake cycle data, computed at play time.
        private T[] _cycleValues = Array.Empty<T>();
        private float[] _cycleEnds = Array.Empty<float>(); // cumulative end progress (0..1] per cycle
        private int _cycleCount;

        protected abstract T Lerp(T a, T b, float t);
        protected abstract T Add(T a, T b);
        /// <summary> Component-wise scale (used by spring/shake oscillation). </summary>
        protected abstract T Scale(T a, float factor);

        // ------------------------------------------------------------------ configuration

        public void SetTarget(Func<T> getValue, Action<T> setValue)
        {
            getter = getValue;
            setter = setValue;
        }

        /// <summary> Captures the StartValue snapshot from the target (or sets it explicitly). </summary>
        public void CaptureStartValue()
        {
            if (getter == null) return;
            startValue = getter();
            hasStartValue = true;
        }

        public void SetStartValue(T value)
        {
            startValue = value;
            hasStartValue = true;
        }

        public void ClearStartValue() => hasStartValue = false;

        public void SetFrom(T value)
        {
            fromReferenceValue = ReferenceValue.CustomValue;
            fromCustomValue = value;
        }

        public void SetTo(T value)
        {
            toReferenceValue = ReferenceValue.CustomValue;
            toCustomValue = value;
        }

        /// <summary> Convenience: animate from the current value to the given value, then play. </summary>
        public void PlayToValue(T value)
        {
            fromReferenceValue = ReferenceValue.CurrentValue;
            SetTo(value);
            Play();
        }

        // ------------------------------------------------------------------ resolution

        protected override void ResolveValues()
        {
            T current = getter != null ? getter() : default;
            if (!hasStartValue && getter != null)
            {
                startValue = current;
                hasStartValue = true;
            }

            fromValue = Resolve(fromReferenceValue, fromOffset, fromCustomValue, current);
            toValue = Resolve(toReferenceValue, toOffset, toCustomValue, current);

            if (settings.playMode == TweenPlayMode.Spring) ComputeSpringCycles();
            else if (settings.playMode == TweenPlayMode.Shake) ComputeShakeCycles();
        }

        private T Resolve(ReferenceValue reference, T offset, T custom, T current)
        {
            switch (reference)
            {
                case ReferenceValue.StartValue: return Add(startValue, offset);
                case ReferenceValue.CurrentValue: return Add(current, offset);
                case ReferenceValue.CustomValue: return custom;
                default: return custom;
            }
        }

        // ------------------------------------------------------------------ evaluation

        protected override void ApplyValue(float easedT, float linearT)
        {
            T value;
            switch (settings.playMode)
            {
                case TweenPlayMode.Spring:
                case TweenPlayMode.Shake:
                    value = EvaluateCycles(linearT);
                    break;
                default:
                    value = Lerp(fromValue, toValue, easedT);
                    break;
            }
            currentValue = value;
            setter?.Invoke(value);
            onValueChanged?.Invoke(value);
        }

        /// <summary>
        /// Spring: oscillates around From with decaying amplitude (To is the oscillation extent), ends at From.
        /// Doozy-compatible: amplitude alternates +force / -force*elasticity, force decays linearly to 0.
        /// </summary>
        private void ComputeSpringCycles()
        {
            int cycles = Mathf.Max(1, settings.vibration + (int)(settings.vibration * currentDuration));
            if (cycles % 2 != 0) cycles++;
            EnsureCycleCapacity(cycles + 1);
            _cycleCount = cycles;

            float force = settings.strength;
            float reduction = cycles > 1 ? force / (cycles - 1) : force;
            for (int i = 0; i < cycles; i++)
            {
                float amplitude = i % 2 == 0 ? force : -force * settings.elasticity;
                _cycleValues[i] = Add(fromValue, Scale(toValue, amplitude));
                force -= reduction;
            }
            _cycleValues[0] = fromValue;
            _cycleValues[cycles - 1] = fromValue;
            for (int i = 0; i < cycles; i++) _cycleEnds[i] = (i + 1f) / cycles;
        }

        /// <summary>
        /// Shake: jumps between From and random points scaled by To*strength, ends at From.
        /// With fadeOutShake, cycle lengths stretch over time (OutExpo) so the shake settles.
        /// </summary>
        private void ComputeShakeCycles()
        {
            int cycles = Mathf.Max(1, settings.vibration + (int)(settings.vibration * currentDuration));
            if (cycles % 2 == 0) cycles++;
            EnsureCycleCapacity(cycles + 1);
            _cycleCount = cycles;

            for (int i = 0; i < cycles; i++)
            {
                _cycleValues[i] = i % 2 == 0
                    ? fromValue
                    : Add(fromValue, Scale(toValue, UnityEngine.Random.value * settings.strength));
            }
            _cycleValues[cycles - 1] = fromValue;

            if (settings.fadeOutShake)
            {
                for (int i = 0; i < cycles; i++)
                    _cycleEnds[i] = Easing.Evaluate(Ease.OutExpo, (i + 1f) / cycles);
                _cycleEnds[cycles - 1] = 1f;
            }
            else
            {
                for (int i = 0; i < cycles; i++) _cycleEnds[i] = (i + 1f) / cycles;
            }
        }

        private void EnsureCycleCapacity(int size)
        {
            if (_cycleValues.Length < size)
            {
                _cycleValues = new T[size];
                _cycleEnds = new float[size];
            }
        }

        private T EvaluateCycles(float linearT)
        {
            if (_cycleCount == 0) return fromValue;
            if (linearT >= 1f) return _cycleValues[_cycleCount - 1];

            float segStart = 0f;
            for (int i = 0; i < _cycleCount; i++)
            {
                float segEnd = _cycleEnds[i];
                if (linearT <= segEnd || i == _cycleCount - 1)
                {
                    float segLength = segEnd - segStart;
                    float segT = segLength > 0f ? (linearT - segStart) / segLength : 1f;
                    T a = i == 0 ? fromValue : _cycleValues[i - 1];
                    return Lerp(a, _cycleValues[i], settings.Evaluate(segT));
                }
                segStart = segEnd;
            }
            return _cycleValues[_cycleCount - 1];
        }

        public override void Reset()
        {
            base.Reset();
            getter = null;
            setter = null;
            onValueChanged = null;
            fromReferenceValue = ReferenceValue.CustomValue;
            toReferenceValue = ReferenceValue.CustomValue;
            fromCustomValue = default;
            toCustomValue = default;
            fromOffset = default;
            toOffset = default;
            hasStartValue = false;
            _cycleCount = 0;
        }
    }

    public class FloatTween : Tween<float>
    {
        protected override float Lerp(float a, float b, float t) => Mathf.LerpUnclamped(a, b, t);
        protected override float Add(float a, float b) => a + b;
        protected override float Scale(float a, float factor) => a * factor;
    }

    public class Vector2Tween : Tween<Vector2>
    {
        protected override Vector2 Lerp(Vector2 a, Vector2 b, float t) => Vector2.LerpUnclamped(a, b, t);
        protected override Vector2 Add(Vector2 a, Vector2 b) => a + b;
        protected override Vector2 Scale(Vector2 a, float factor) => a * factor;
    }

    public class Vector3Tween : Tween<Vector3>
    {
        protected override Vector3 Lerp(Vector3 a, Vector3 b, float t) => Vector3.LerpUnclamped(a, b, t);
        protected override Vector3 Add(Vector3 a, Vector3 b) => a + b;
        protected override Vector3 Scale(Vector3 a, float factor) => a * factor;
    }

    public class ColorTween : Tween<Color>
    {
        protected override Color Lerp(Color a, Color b, float t) => Color.LerpUnclamped(a, b, t);
        protected override Color Add(Color a, Color b) => a + b;
        protected override Color Scale(Color a, float factor) => a * factor;
    }
}
