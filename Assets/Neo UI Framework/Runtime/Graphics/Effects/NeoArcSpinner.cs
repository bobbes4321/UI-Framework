using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Loading spinner / radial sweep — continuously rotates an arc or ring around its center by
    /// driving the host <see cref="NeoShape"/>'s <see cref="NeoShape.arcStart"/> from
    /// <see cref="SpinFrom"/> to <see cref="SpinTo"/> (0→360 by default) so the arc chases its own
    /// tail. Tier-1 — it only writes shape params (<see cref="NeoShape.arcStart"/> and, optionally,
    /// <see cref="NeoShape.arcSweep"/> / <see cref="NeoShape.ringThickness"/>) that ride the shared
    /// SDF material's vertex channels, so the spinning shape keeps batching with every other NeoShape;
    /// no new material, no fragment shader.
    ///
    /// <para>The natural usage is a Ring or Arc host with <c>loop=true</c>, <c>pingPong=false</c> and
    /// <c>ease=Linear</c> for a steady, continuous spin — but none of that is forced; the rotation
    /// simply lerps by the eased phase, so a different timeline gives a different (e.g. easing,
    /// breathing) feel. Enable <see cref="AnimateSweep"/> for a Material-style indeterminate spinner
    /// whose arc length grows and shrinks while it turns, and/or <see cref="AnimateThickness"/> to
    /// pulse the ring's stroke width.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Arc Spinner")]
    public class NeoArcSpinner : NeoShapeEffect
    {
        [Header("Spin (arc rotation)")]
        [Tooltip("Arc start angle (degrees) at phase 0 — where the sweep begins.")]
        [SerializeField] private float spinFrom = 0f;

        [Tooltip("Arc start angle (degrees) at phase 1 — a full turn is 360 past spinFrom.")]
        [SerializeField] private float spinTo = 360f;

        [Header("Sweep (arc length, optional)")]
        [Tooltip("Also animate the arc length (arcSweep) so the visible arc breathes while it spins — " +
                 "the Material-style indeterminate spinner.")]
        [SerializeField] private bool animateSweep;

        [Tooltip("Arc sweep (degrees) at phase 0 — used only when Animate Sweep is on.")]
        [SerializeField] private float sweepFrom = 30f;

        [Tooltip("Arc sweep (degrees) at phase 1 — used only when Animate Sweep is on.")]
        [SerializeField] private float sweepTo = 300f;

        [Header("Thickness (ring stroke, optional)")]
        [Tooltip("Also animate the ring stroke width (ringThickness) while it spins.")]
        [SerializeField] private bool animateThickness;

        [Tooltip("Ring thickness (px) at phase 0 — used only when Animate Thickness is on.")]
        [SerializeField] private float thicknessMin = 4f;

        [Tooltip("Ring thickness (px) at phase 1 — used only when Animate Thickness is on.")]
        [SerializeField] private float thicknessMax = 12f;

        /// <summary> Arc start angle (degrees) at phase 0 — where the sweep begins. </summary>
        public float SpinFrom { get => spinFrom; set => spinFrom = value; }
        /// <summary> Arc start angle (degrees) at phase 1 — a full turn is 360 past <see cref="SpinFrom"/>. </summary>
        public float SpinTo { get => spinTo; set => spinTo = value; }
        /// <summary> Whether the arc length (<see cref="NeoShape.arcSweep"/>) breathes while spinning. </summary>
        public bool AnimateSweep { get => animateSweep; set => animateSweep = value; }
        /// <summary> Arc sweep (degrees) at phase 0; clamped 0..360. </summary>
        public float SweepFrom { get => sweepFrom; set => sweepFrom = Mathf.Clamp(value, 0f, 360f); }
        /// <summary> Arc sweep (degrees) at phase 1; clamped 0..360. </summary>
        public float SweepTo { get => sweepTo; set => sweepTo = Mathf.Clamp(value, 0f, 360f); }
        /// <summary> Whether the ring stroke width (<see cref="NeoShape.ringThickness"/>) pulses while spinning. </summary>
        public bool AnimateThickness { get => animateThickness; set => animateThickness = value; }
        /// <summary> Ring thickness (px) at phase 0; clamped to the NeoShape minimum. </summary>
        public float ThicknessMin { get => thicknessMin; set => thicknessMin = Mathf.Max(0.5f, value); }
        /// <summary> Ring thickness (px) at phase 1; clamped to the NeoShape minimum. </summary>
        public float ThicknessMax { get => thicknessMax; set => thicknessMax = Mathf.Max(0.5f, value); }

        /// <inheritdoc/>
        protected override void ApplyAt(float easedPhase01)
        {
            NeoShape shape = hostShape;
            if (shape == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoArcSpinner)} on '{name}' needs an NeoShape host " +
                                 "to rotate an arc — effect is inert.", this);
                return;
            }

            shape.arcStart = Mathf.LerpUnclamped(spinFrom, spinTo, easedPhase01);

            if (animateSweep)
                shape.arcSweep = Mathf.LerpUnclamped(sweepFrom, sweepTo, easedPhase01);

            if (animateThickness)
                shape.ringThickness = Mathf.LerpUnclamped(thicknessMin, thicknessMax, easedPhase01);
        }

        /// <inheritdoc/>
        public override bool TrySetLiveParam(string param, float value)
        {
            switch (param)
            {
                case "spinFrom": SpinFrom = value; return true;
                case "spinTo": SpinTo = value; return true;
                // Convenience: pin both ends so a slider sets the arc length directly.
                case "sweep": SweepFrom = value; SweepTo = value; AnimateSweep = true; return true;
                case "sweepFrom": SweepFrom = value; AnimateSweep = true; return true;
                case "sweepTo": SweepTo = value; AnimateSweep = true; return true;
                // Convenience: pin both ends so a slider sets the ring stroke directly.
                case "thickness": ThicknessMin = value; ThicknessMax = value; AnimateThickness = true; return true;
                case "thicknessMin": ThicknessMin = value; AnimateThickness = true; return true;
                case "thicknessMax": ThicknessMax = value; AnimateThickness = true; return true;
            }
            return base.TrySetLiveParam(param, value);
        }
    }
}
