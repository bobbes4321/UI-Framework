using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Breathing glow/halo: pulses the host <see cref="NeoShape"/>'s edge <see cref="NeoShape.edgeSoftness"/>
    /// (the SDF blur that turns a crisp edge into a soft shadow/glow) and, optionally, the fill color's
    /// alpha between two values. Tier-1 — it only writes shape params that ride the shared material's
    /// vertex channels, so the glowing shape keeps batching with every other NeoShape; no new material,
    /// no fragment shader.
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Glow Pulse")]
    public class NeoGlowPulse : NeoShapeEffect
    {
        [Header("Softness (edge glow)")]
        [Tooltip("Edge softness in px at phase 0 — the resting/tight edge.")]
        [SerializeField] private float minSoftness = 2f;

        [Tooltip("Edge softness in px at phase 1 — the fully bloomed glow.")]
        [SerializeField] private float maxSoftness = 14f;

        [Header("Alpha (optional)")]
        [Tooltip("Also pulse the fill color's alpha for a fade-in/out halo.")]
        [SerializeField] private bool pulseAlpha;

        [Range(0f, 1f)]
        [Tooltip("Fill alpha at phase 0.")]
        [SerializeField] private float minAlpha = 0.6f;

        [Range(0f, 1f)]
        [Tooltip("Fill alpha at phase 1.")]
        [SerializeField] private float maxAlpha = 1f;

        /// <summary> Edge softness (px) at phase 0. </summary>
        public float SoftnessMin { get => minSoftness; set => minSoftness = Mathf.Max(0f, value); }
        /// <summary> Edge softness (px) at phase 1. </summary>
        public float SoftnessMax { get => maxSoftness; set => maxSoftness = Mathf.Max(0f, value); }
        /// <summary> Whether the fill color's alpha pulses too. </summary>
        public bool PulseAlpha { get => pulseAlpha; set => pulseAlpha = value; }
        /// <summary> Fill alpha at phase 0. </summary>
        public float AlphaMin { get => minAlpha; set => minAlpha = Mathf.Clamp01(value); }
        /// <summary> Fill alpha at phase 1. </summary>
        public float AlphaMax { get => maxAlpha; set => maxAlpha = Mathf.Clamp01(value); }

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            NeoShape shape = hostShape;
            if (shape == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoGlowPulse)} on '{name}' needs an NeoShape host " +
                                 "to pulse edge softness — effect is inert.", this);
                return;
            }

            shape.edgeSoftness = Mathf.LerpUnclamped(minSoftness, maxSoftness, easedPhase01);

            if (pulseAlpha)
            {
                Color c = shape.color;
                c.a = Mathf.LerpUnclamped(minAlpha, maxAlpha, easedPhase01);
                shape.color = c;
            }
        }
    }
}
