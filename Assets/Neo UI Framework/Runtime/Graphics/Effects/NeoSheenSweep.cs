using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Moving highlight band — the "shine sweeping across a button" look. NeoShape exposes no
    /// dedicated band parameter, but it does expose a linear-gradient fill whose <em>direction</em>
    /// we can animate: by sweeping <see cref="NeoShape.gradientAngleDegrees"/> (and the host already
    /// being in <see cref="ShapeFillMode.LinearGradient"/> with a bright <see cref="NeoShape.colorB"/>)
    /// the gradient's bright stop travels across the surface like a sheen. Tier-1 — only an existing
    /// shape param is written, so the shared material and batching are preserved; no new material.
    ///
    /// <para>If the host shape is not in a gradient fill mode the sweep has nothing to move, so the
    /// effect warns once and switches the host into <see cref="ShapeFillMode.LinearGradient"/> for
    /// the developer rather than failing silently.</para>
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

        [System.NonSerialized] private bool _warnedNoGradient;

        /// <summary> Gradient angle (degrees) at phase 0. </summary>
        public float FromAngle { get => fromAngle; set => fromAngle = value; }
        /// <summary> Gradient angle (degrees) at phase 1. </summary>
        public float ToAngle { get => toAngle; set => toAngle = value; }

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

            if (shape.fill == ShapeFillMode.Solid)
            {
                if (ensureLinearGradient)
                {
                    shape.fill = ShapeFillMode.LinearGradient;
                }
                else if (!_warnedNoGradient)
                {
                    _warnedNoGradient = true;
                    Debug.LogWarning($"[Neo.UI] {nameof(NeoSheenSweep)} on '{name}': host NeoShape is in " +
                                     "Solid fill, so there is no bright stop to sweep. Set fill mode to " +
                                     "LinearGradient (with a bright colorB) or enable 'ensureLinearGradient'.", this);
                }
            }

            shape.gradientAngleDegrees = Mathf.LerpUnclamped(fromAngle, toAngle, easedPhase01);
        }
    }
}
