using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Moving highlight band — the "shine sweeping across a button" look. NeoShape exposes no
    /// dedicated band parameter, but it does expose a linear-gradient fill whose <em>direction</em>
    /// we can animate: by sweeping <see cref="NeoShape.gradientAngleDegrees"/> (with the host in
    /// <see cref="ShapeFillMode.LinearGradient"/> and a bright <see cref="NeoShape.colorB"/>) the
    /// gradient's bright stop travels across the surface like a sheen. Tier-1 — only existing shape
    /// params are written, so the shared material and batching are preserved; no new material.
    ///
    /// <para>For the sheen to be <em>visible</em> the two fill stops must contrast: the base
    /// (<see cref="NeoShape.color"/>) and the sheen stop (<see cref="NeoShape.colorB"/>). A host with
    /// only a solid <c>background</c> token has <c>colorB = white</c> by default, which already
    /// contrasts; but a host whose base is itself white (or whose stops were left equal) shows
    /// nothing to sweep. So this effect is <b>self-sufficient</b>: when it forces the gradient on it
    /// also seeds a bright <see cref="sheenColor"/> into <see cref="NeoShape.colorB"/> (once, only if
    /// the two stops are currently equal) so the sweep reads regardless of how the base was colored —
    /// the common "solid card + sheen" case just works without the author wiring a second stop.</para>
    ///
    /// <para>Note this drives NeoShape's <em>built-in</em> two-stop gradient, NOT a sibling
    /// <see cref="NeoGradient"/> mesh effect. Stacking a <c>gradient</c> (NeoGradient) UNDER a sheen
    /// is counter-productive: NeoGradient <em>multiplies</em> onto the vertices, so it can only darken
    /// the bright sheen stop. Use a solid <c>background</c> for the base and let the sheen own the
    /// bright stop (see the effects showcase).</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Sheen Sweep")]
    public class NeoSheenSweep : NeoShapeEffect
    {
        [Tooltip("Gradient angle (degrees) the sheen sweeps FROM at phase 0.")]
        [Range(0f, 360f)]
        [SerializeField] private float fromAngle = 0f;

        [Tooltip("Gradient angle (degrees) the sheen sweeps TO at phase 1.")]
        [Range(0f, 360f)]
        [SerializeField] private float toAngle = 360f;

        [Tooltip("Force the host shape into LinearGradient fill so the sweep has a bright stop to " +
                 "move. Turn off if you manage the fill mode yourself.")]
        [SerializeField] private bool ensureLinearGradient = true;

        [Tooltip("Bright stop seeded into NeoShape.colorB when forcing the gradient on, so the sheen " +
                 "has visible contrast against the base. Only seeded when the two stops are equal " +
                 "(i.e. the author hasn't already wired a colorB), so it never stomps a custom stop.")]
        [SerializeField] private Color sheenColor = Color.white;

        [System.NonSerialized] private bool _seeded;

        /// <summary> Gradient angle (degrees) at phase 0. </summary>
        public float FromAngle { get => fromAngle; set => fromAngle = value; }
        /// <summary> Gradient angle (degrees) at phase 1. </summary>
        public float ToAngle { get => toAngle; set => toAngle = value; }
        /// <summary> Bright stop seeded into <see cref="NeoShape.colorB"/> so the sweep reads. </summary>
        public Color SheenColor { get => sheenColor; set => sheenColor = value; }

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            NeoShape shape = hostShape;
            if (shape == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoSheenSweep)} on '{name}' needs an NeoShape host " +
                                 "to sweep its gradient — effect is inert.", this);
                return;
            }

            if (ensureLinearGradient && shape.fill != ShapeFillMode.LinearGradient)
                shape.fill = ShapeFillMode.LinearGradient;

            // Self-sufficiency: if the two stops are equal there's nothing to sweep, so seed a bright
            // sheen stop once. Equal stops are the tell-tale of "solid base, no second stop wired".
            if (!_seeded && shape.fill == ShapeFillMode.LinearGradient && shape.color == shape.colorB)
            {
                shape.colorB = sheenColor;
                _seeded = true;
            }

            shape.gradientAngleDegrees = Mathf.LerpUnclamped(fromAngle, toAngle, easedPhase01);
        }
    }
}
