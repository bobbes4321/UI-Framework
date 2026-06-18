using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Pulsing focus/selection ring: lerps the host <see cref="NeoShape"/>'s outline
    /// <see cref="NeoShape.border"/> width between two values and, optionally, fades the
    /// <see cref="NeoShape.outlineColor"/>'s alpha in step so the ring breathes as it grows — the
    /// "this control is active" highlight. Tier-1 — it only writes shape params that ride the shared
    /// SDF material's vertex channels (border width, outline color), so the pulsing shape keeps
    /// batching with every other NeoShape; no new material, no fragment shader.
    ///
    /// <para>Theme-aware only when an outline color <see cref="ThemeColorRef"/> token is supplied: in
    /// that case the ring's RGB rides the theme (this effect re-bakes its rest frame on
    /// <see cref="ThemeService.OnThemeChanged"/>) while the pulse always drives the alpha. With no
    /// token the host's already-baked outline color is left untouched (only its alpha pulses), so a
    /// bare borderPulse simply breathes the existing ring.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Border Pulse")]
    public class NeoBorderPulse : NeoShapeEffect
    {
        [Header("Border (focus ring width)")]
        [Tooltip("Outline width in px at phase 0 — the resting/thin ring.")]
        [SerializeField] private float minBorder = 1f;

        [Tooltip("Outline width in px at phase 1 — the fully grown ring.")]
        [SerializeField] private float maxBorder = 5f;

        [Header("Alpha (optional)")]
        [Tooltip("Also pulse the outline color's alpha so the ring fades in/out as it grows.")]
        [SerializeField] private bool pulseAlpha = true;

        [Range(0f, 1f)]
        [Tooltip("Outline alpha at phase 0.")]
        [SerializeField] private float minAlpha = 0.3f;

        [Range(0f, 1f)]
        [Tooltip("Outline alpha at phase 1.")]
        [SerializeField] private float maxAlpha = 1f;

        [Header("Color (theme-aware, optional)")]
        [Tooltip("When a token/color is supplied, the ring's RGB is driven from this ref (alpha stays " +
                 "driven by the pulse). When left unset the host's existing outline color RGB is kept.")]
        [SerializeField] private bool useColorRef;

        [Tooltip("Outline color (theme token or raw color) — only applied when 'useColorRef' is on.")]
        [SerializeField] private ThemeColorRef outlineColorRef = new ThemeColorRef(new Color(0.36f, 0.42f, 0.96f)); // accent indigo

        /// <summary> Outline width (px) at phase 0. </summary>
        public float BorderMin { get => minBorder; set => minBorder = Mathf.Max(0f, value); }
        /// <summary> Outline width (px) at phase 1. </summary>
        public float BorderMax { get => maxBorder; set => maxBorder = Mathf.Max(0f, value); }
        /// <summary> Whether the outline color's alpha pulses too. </summary>
        public bool PulseAlpha { get => pulseAlpha; set => pulseAlpha = value; }
        /// <summary> Outline alpha at phase 0. </summary>
        public float AlphaMin { get => minAlpha; set => minAlpha = Mathf.Clamp01(value); }
        /// <summary> Outline alpha at phase 1. </summary>
        public float AlphaMax { get => maxAlpha; set => maxAlpha = Mathf.Clamp01(value); }

        /// <summary>
        /// Outline color (theme token or raw color). Setting a non-null ref activates it (the ring's
        /// RGB is then driven from it); setting null leaves the host's baked outline color untouched.
        /// </summary>
        public ThemeColorRef OutlineColorRef
        {
            get => outlineColorRef;
            set
            {
                outlineColorRef = value;
                useColorRef = value != null;
            }
        }

        /// <summary>
        /// True when an outline color ref is active (was explicitly supplied) so the ring's RGB is
        /// driven from <see cref="OutlineColorRef"/>. False ⇒ the host's baked outline RGB is kept and
        /// only the alpha pulses — the bare default. The exporter emits the color key only in this case
        /// so a default borderPulse round-trips minimally (whether the ref came from a token OR a hex).
        /// </summary>
        public bool HasOutlineColor => useColorRef && outlineColorRef != null;

        // Only theme-aware when a token-based outline color is actually in play; a raw-color or
        // unset ref needs no theme subscription.
        /// <inheritdoc/>
        protected override bool UsesTheme => useColorRef && outlineColorRef != null && outlineColorRef.useToken;

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            NeoShape shape = hostShape;
            if (shape == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoBorderPulse)} on '{name}' needs an NeoShape host " +
                                 "to pulse the outline border — effect is inert.", this);
                return;
            }

            shape.border = Mathf.LerpUnclamped(minBorder, maxBorder, easedPhase01);

            // Resolve the ring color: an active ref (theme-live) overrides the RGB; otherwise keep the
            // host's existing baked outline color so a bare pulse only breathes the alpha.
            Color outline = useColorRef && outlineColorRef != null ? outlineColorRef.Resolve() : shape.outlineColor;

            if (pulseAlpha)
                outline.a = Mathf.LerpUnclamped(minAlpha, maxAlpha, easedPhase01);
            else if (!(useColorRef && outlineColorRef != null))
                return; // nothing to write — leave the host's outline color exactly as baked.

            shape.outlineColor = outline;
        }

        /// <inheritdoc/>
        public override bool TrySetLiveParam(string param, float value)
        {
            switch (param)
            {
                case "borderMin": BorderMin = value; return true;
                case "borderMax": BorderMax = value; return true;
                // Convenience: set both ends so a slider drives the ring thickness directly (non-pulsing).
                case "border": BorderMin = value; BorderMax = value; return true;
                case "alphaMin": AlphaMin = value; PulseAlpha = true; return true;
                case "alphaMax": AlphaMax = value; PulseAlpha = true; return true;
                case "alpha": AlphaMin = value; AlphaMax = value; PulseAlpha = true; return true;
            }
            return base.TrySetLiveParam(param, value);
        }
    }
}
