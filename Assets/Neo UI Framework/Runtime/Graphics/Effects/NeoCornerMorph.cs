using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Breathing corners: morphs the host <see cref="NeoShape"/>'s rounding over the timeline so a
    /// RoundedRect breathes between sharp and pill-round. In the default uniform mode it lerps
    /// <see cref="NeoShape.cornerRadius"/> between <see cref="RadiusMin"/> and <see cref="RadiusMax"/>;
    /// with <see cref="PerCorner"/> on it lerps the per-corner <see cref="NeoShape.cornerRadii"/>
    /// (Vector4, component order x=TR y=BR z=TL w=BL) between <see cref="RadiiFrom"/> and
    /// <see cref="RadiiTo"/> so each corner morphs independently — a squircle/blob wobble.
    ///
    /// <para>Tier-1 — it only writes shape params that already ride the shared SDF material's vertex
    /// channels, so the morphing shape keeps batching with every other NeoShape; no new material,
    /// no fragment shader.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Corner Morph")]
    public class NeoCornerMorph : NeoShapeEffect
    {
        [Header("Uniform corner radius")]
        [Tooltip("Uniform corner radius (px) at phase 0 — the sharper edge.")]
        [SerializeField] private float radiusMin = 8f;

        [Tooltip("Uniform corner radius (px) at phase 1 — the rounder/pill edge.")]
        [SerializeField] private float radiusMax = 48f;

        [Header("Per-corner (blob) mode")]
        [Tooltip("Animate the per-corner radii (Vector4) independently for a blob wobble instead of " +
                 "the single uniform radius.")]
        [SerializeField] private bool perCorner;

        [Tooltip("Per-corner radii (px) at phase 0 — component order x=TR y=BR z=TL w=BL.")]
        [SerializeField] private Vector4 radiiFrom = new Vector4(8f, 8f, 8f, 8f);

        [Tooltip("Per-corner radii (px) at phase 1 — component order x=TR y=BR z=TL w=BL.")]
        [SerializeField] private Vector4 radiiTo = new Vector4(48f, 48f, 48f, 48f);

        /// <summary> Uniform corner radius (px) at phase 0. </summary>
        public float RadiusMin { get => radiusMin; set => radiusMin = Mathf.Max(0f, value); }
        /// <summary> Uniform corner radius (px) at phase 1. </summary>
        public float RadiusMax { get => radiusMax; set => radiusMax = Mathf.Max(0f, value); }
        /// <summary> Whether the per-corner radii animate independently (blob) instead of the uniform radius. </summary>
        public bool PerCorner { get => perCorner; set => perCorner = value; }
        /// <summary> Per-corner radii (px) at phase 0 (x=TR y=BR z=TL w=BL). </summary>
        public Vector4 RadiiFrom { get => radiiFrom; set => radiiFrom = value; }
        /// <summary> Per-corner radii (px) at phase 1 (x=TR y=BR z=TL w=BL). </summary>
        public Vector4 RadiiTo { get => radiiTo; set => radiiTo = value; }

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            NeoShape shape = hostShape;
            if (shape == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoCornerMorph)} on '{name}' needs an NeoShape host " +
                                 "to morph corner rounding — effect is inert.", this);
                return;
            }

            if (perCorner)
                shape.cornerRadii = Vector4.LerpUnclamped(radiiFrom, radiiTo, easedPhase01);
            else
                shape.cornerRadius = Mathf.LerpUnclamped(radiusMin, radiusMax, easedPhase01);
        }

        /// <inheritdoc/>
        public override bool TrySetLiveParam(string param, float value)
        {
            switch (param)
            {
                case "radiusMin": RadiusMin = value; return true;
                case "radiusMax": RadiusMax = value; return true;
                // "morph" is an alias for the bloomed end.
                case "morph": RadiusMax = value; return true;
                // Convenience: pin both ends so a slider drives corner roundness as a direct value.
                case "radius": RadiusMin = value; RadiusMax = value; return true;
            }
            return base.TrySetLiveParam(param, value);
        }
    }
}
