using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Shifting gradient: drives a sibling <see cref="NeoGradient"/>'s <see cref="NeoGradient.angle"/>
    /// and/or interpolates between two <see cref="ThemeColorRef"/> stop pairs over time for an animated
    /// color wash. Tier-1 — <see cref="NeoGradient"/> is a <see cref="UnityEngine.UI.BaseMeshEffect"/>
    /// that multiplies into vertex colors, so the gradient still rides the host's shared material and
    /// batching is preserved; no new material, no fragment shader.
    ///
    /// <para>Theme-aware: the four stop endpoints are <see cref="ThemeColorRef"/>s, so they recolor
    /// live when tokens change (this effect re-bakes its rest frame on
    /// <see cref="ThemeService.OnThemeChanged"/>). Requires a sibling <see cref="NeoGradient"/>;
    /// warns rather than failing silently when one is missing.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Gradient Cycle")]
    [RequireComponent(typeof(NeoGradient))]
    public class NeoGradientCycle : NeoShapeEffect
    {
        [Header("Angle")]
        [Tooltip("Animate the gradient's angle as well as / instead of its colors.")]
        [SerializeField] private bool cycleAngle = true;

        [Tooltip("Gradient angle (degrees) at phase 0.")]
        [Range(0f, 360f)]
        [SerializeField] private float fromAngle = 0f;

        [Tooltip("Gradient angle (degrees) at phase 1.")]
        [Range(0f, 360f)]
        [SerializeField] private float toAngle = 360f;

        [Header("Colors (theme-aware)")]
        [Tooltip("Interpolate the gradient stops between two color pairs over the timeline.")]
        [SerializeField] private bool cycleColors;

        // Vivid, theme-friendly endpoint defaults so a `cycleColors:true` with no authored colors bakes
        // a colorful resting frame (phase 0 = fromColorA/fromColorB) rather than the old white→gray wash.
        [Tooltip("Stop A at phase 0.")]
        [SerializeField] private ThemeColorRef fromColorA = new ThemeColorRef(new Color(0.36f, 0.42f, 0.96f)); // indigo
        [Tooltip("Stop B at phase 0.")]
        [SerializeField] private ThemeColorRef fromColorB = new ThemeColorRef(new Color(0.92f, 0.28f, 0.60f)); // magenta
        [Tooltip("Stop A at phase 1.")]
        [SerializeField] private ThemeColorRef toColorA = new ThemeColorRef(new Color(0.20f, 0.78f, 0.85f)); // cyan
        [Tooltip("Stop B at phase 1.")]
        [SerializeField] private ThemeColorRef toColorB = new ThemeColorRef(new Color(0.99f, 0.70f, 0.24f)); // amber

        [System.NonSerialized] private NeoGradient _gradient;

        /// <summary> Whether the gradient angle animates. </summary>
        public bool CycleAngle { get => cycleAngle; set => cycleAngle = value; }
        /// <summary> Whether the gradient stop colors animate. </summary>
        public bool CycleColors { get => cycleColors; set => cycleColors = value; }

        /// <summary> Stop A at phase 0 (theme token or raw color). </summary>
        public ThemeColorRef FromColorA { get => fromColorA; set => fromColorA = value; }
        /// <summary> Stop B at phase 0 (theme token or raw color). </summary>
        public ThemeColorRef FromColorB { get => fromColorB; set => fromColorB = value; }
        /// <summary> Stop A at phase 1 (theme token or raw color). </summary>
        public ThemeColorRef ToColorA { get => toColorA; set => toColorA = value; }
        /// <summary> Stop B at phase 1 (theme token or raw color). </summary>
        public ThemeColorRef ToColorB { get => toColorB; set => toColorB = value; }

        /// <summary> The sibling gradient this effect drives (cached). </summary>
        public NeoGradient Gradient => _gradient != null ? _gradient : (_gradient = GetComponent<NeoGradient>());

        // Color stops are theme tokens, so we must refresh on theme changes like NeoGradient does.
        /// <inheritdoc/>
        protected override bool UsesTheme => cycleColors;

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            NeoGradient gradient = Gradient;
            if (gradient == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoGradientCycle)} on '{name}' has no sibling " +
                                 "NeoGradient to drive — effect is inert.", this);
                return;
            }

            if (cycleAngle)
                gradient.angle = Mathf.Repeat(Mathf.LerpUnclamped(fromAngle, toAngle, easedPhase01), 360f);

            if (cycleColors)
            {
                // Interpolate the resolved (theme-live) endpoints, then write hardcoded colors onto the
                // gradient's stops — the gradient still re-resolves via this effect on theme change.
                Color a = Color.LerpUnclamped(fromColorA.Resolve(), toColorA.Resolve(), easedPhase01);
                Color b = Color.LerpUnclamped(fromColorB.Resolve(), toColorB.Resolve(), easedPhase01);
                gradient.colorA = new ThemeColorRef(a);
                gradient.colorB = new ThemeColorRef(b);
            }

            // NeoGradient is a mesh effect; force the host mesh to rebuild so the new stops/angle land.
            if (hostGraphic != null)
                hostGraphic.SetVerticesDirty();
        }

        /// <inheritdoc/>
        public override bool TrySetLiveParam(string param, float value)
        {
            switch (param)
            {
                case "fromAngle": fromAngle = value; CycleAngle = true; return true;
                case "toAngle": toAngle = value; CycleAngle = true; return true;
                // Convenience: pin both ends so a slider sets the wash angle directly.
                case "angle": fromAngle = toAngle = value; CycleAngle = true; return true;
            }
            return base.TrySetLiveParam(param, value);
        }
    }
}
