using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Idle "alive" motion for any UI element — a gentle bob / sway / breathe, or an attention
    /// wobble / jelly squash. Tier-1 and inherently <b>batch-safe</b>: unlike the shape-param
    /// effects (<see cref="NeoGlowPulse"/> et al.) it never touches <see cref="Graphic.material"/>
    /// or the SDF mesh at all — it only drives the host's <see cref="RectTransform"/>
    /// (anchoredPosition / local Z rotation / localScale), so the shared NeoShape batch is fully
    /// preserved no matter how many elements are wiggling.
    ///
    /// <para>The motion is applied as signed OFFSETS over a cached <b>base pose</b> snapshot, so it
    /// is WYSIWYG and reversible: at <see cref="NeoShapeEffect.restingPhase"/> 0 the wave is
    /// <c>sin(0)=0</c>, so the baked rest frame equals the base pose exactly, and disabling the
    /// effect restores the element to precisely where it was baked. The base pose is captured BEFORE
    /// the first sample overwrites it (see <see cref="OnEnable"/>), mirroring how the hue-capturing
    /// effects snapshot their starting state ahead of <see cref="NeoShapeEffect.EvaluateRest"/>.</para>
    ///
    /// <para>Each channel is additive and a channel with amplitude 0 contributes nothing, so unused
    /// channels are free. The oscillation uses a sine wave (<c>sin(phase·2π)</c>) so the motion is
    /// smooth and centered on the base pose regardless of loop / ping-pong mode.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Transform Juice")]
    public class NeoTransformJuice : NeoShapeEffect
    {
        [Header("Position (offset over base pose, px)")]
        [Tooltip("Vertical bob amplitude in px: anchoredPosition.y = baseY + bob * wave.")]
        [SerializeField] private float bob = 6f;

        [Tooltip("Horizontal sway amplitude in px: anchoredPosition.x = baseX + sway * wave.")]
        [SerializeField] private float sway;

        [Header("Rotation (offset over base pose, deg)")]
        [Tooltip("Z rotation amplitude in degrees: localEulerAngles.z = baseRot + rotate * wave " +
                 "(UI only spins around Z).")]
        [SerializeField] private float rotate;

        [Header("Scale (fraction of base scale)")]
        [Tooltip("Uniform scale pulse as a fraction, e.g. 0.06 → ±6%: localScale = baseScale * (1 + scale * wave).")]
        [SerializeField] private float scale;

        [Tooltip("Non-uniform jelly squash as a fraction: scale.x grows while scale.y shrinks " +
                 "(and vice versa) for a squash-and-stretch feel.")]
        [SerializeField] private float squash;

        [System.NonSerialized] private RectTransform _rt;
        [System.NonSerialized] private Vector2 _basePos;
        [System.NonSerialized] private float _baseRot;
        [System.NonSerialized] private Vector3 _baseScale = Vector3.one;
        [System.NonSerialized] private bool _captured;
        [System.NonSerialized] private bool _warnedNoRect;

        /// <summary> Vertical bob amplitude in px. </summary>
        public float Bob { get => bob; set => bob = value; }
        /// <summary> Horizontal sway amplitude in px. </summary>
        public float Sway { get => sway; set => sway = value; }
        /// <summary> Z rotation amplitude in degrees. </summary>
        public float Rotate { get => rotate; set => rotate = value; }
        /// <summary> Uniform scale pulse as a fraction of the base scale (e.g. 0.06 = ±6%). </summary>
        public float Scale { get => scale; set => scale = value; }
        /// <summary> Non-uniform jelly squash as a fraction of the base scale. </summary>
        public float Squash { get => squash; set => squash = value; }

        private RectTransform Rect => _rt != null
            ? _rt
            : (_rt = hostGraphic != null ? hostGraphic.rectTransform : GetComponent<RectTransform>());

        /// <inheritdoc/>
        protected override void OnEnable()
        {
            // Snapshot the base pose BEFORE base.OnEnable() runs — its EvaluateRest→Sample→ApplyAt
            // would otherwise read an already-offset RectTransform back as the "base". Mirrors the
            // hue-capture ordering of the color effects.
            RectTransform rt = Rect;
            if (rt != null && !_captured)
            {
                _basePos = rt.anchoredPosition;
                _baseRot = rt.localEulerAngles.z;
                _baseScale = rt.localScale;
                _captured = true;
            }

            base.OnEnable();
        }

        /// <inheritdoc/>
        protected override void OnDisable()
        {
            // Leave the element exactly where it was baked: restore the cached base pose, then let the
            // base unsubscribe / rest.
            RectTransform rt = Rect;
            if (rt != null && _captured)
            {
                rt.anchoredPosition = _basePos;
                Vector3 euler = rt.localEulerAngles;
                euler.z = _baseRot;
                rt.localEulerAngles = euler;
                rt.localScale = _baseScale;
            }
            _captured = false;

            base.OnDisable();
        }

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            RectTransform rt = Rect;
            if (rt == null)
            {
                if (!_warnedNoRect)
                {
                    Debug.LogWarning($"[Neo.UI] {nameof(NeoTransformJuice)} on '{name}' has no RectTransform " +
                                     "to drive — effect is inert.", this);
                    _warnedNoRect = true;
                }
                return;
            }

            // Lazily capture the base pose if OnEnable didn't (e.g. ApplyAt scrubbed directly by tooling
            // before enable) so offsets are always relative to a stable origin.
            if (!_captured)
            {
                _basePos = rt.anchoredPosition;
                _baseRot = rt.localEulerAngles.z;
                _baseScale = rt.localScale;
                _captured = true;
            }

            // Signed -1..1 oscillation centered on the base pose; sine stays smooth in any loop mode.
            float wave = Mathf.Sin(easedPhase01 * Mathf.PI * 2f);

            // Position — additive over the base anchoredPosition (zero amplitude ⇒ no contribution).
            if (bob != 0f || sway != 0f)
                rt.anchoredPosition = new Vector2(_basePos.x + sway * wave, _basePos.y + bob * wave);

            // Rotation — Z only.
            if (rotate != 0f)
            {
                Vector3 euler = rt.localEulerAngles;
                euler.z = _baseRot + rotate * wave;
                rt.localEulerAngles = euler;
            }

            // Scale — uniform pulse and/or non-uniform jelly squash, both as fractions of base scale.
            if (scale != 0f || squash != 0f)
            {
                float uniform = 1f + scale * wave;
                rt.localScale = new Vector3(
                    _baseScale.x * uniform * (1f + squash * wave),
                    _baseScale.y * uniform * (1f - squash * wave),
                    _baseScale.z);
            }
        }

        /// <inheritdoc/>
        public override bool TrySetLiveParam(string param, float value)
        {
            switch (param)
            {
                case "bob": Bob = value; return true;
                case "sway": Sway = value; return true;
                case "rotate": Rotate = value; return true;
                case "scale": Scale = value; return true;
                case "squash": Squash = value; return true;
            }
            return base.TrySetLiveParam(param, value);
        }
    }
}
