using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Drives an <see cref="NeoShape"/> Arc's sweep from a progressor — radial cooldowns and
    /// dials. The arc grows clockwise from its start angle; at zero the shape is hidden
    /// (a zero-sweep arc would still render its rounded cap).
    /// </summary>
    [AddComponentMenu("Neo/UI/Progress Targets/Shape Arc Progress Target")]
    public class ShapeProgressTarget : ProgressTarget
    {
        public NeoShape shape;

        [Tooltip("Sweep in degrees at progress 1")]
        [Range(0f, 360f)]
        public float maxSweep = 360f;

        private void Reset() => shape = GetComponent<NeoShape>();

        public override void UpdateTarget(Progressor progressor)
        {
            if (shape == null) return;
            float sweep = Mathf.Clamp01(Pick(progressor)) * maxSweep;
            shape.arcSweep = sweep;
            shape.enabled = sweep > 0.25f;
        }
    }
}
