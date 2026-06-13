using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Drives an <see cref="AEShape"/> Arc's sweep from a progressor — radial cooldowns and
    /// dials. The arc grows clockwise from its start angle; at zero the shape is hidden
    /// (a zero-sweep arc would still render its rounded cap).
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Progress Targets/Shape Arc Progress Target")]
    public class ShapeProgressTarget : ProgressTarget
    {
        public AEShape shape;

        [Tooltip("Sweep in degrees at progress 1")]
        [Range(0f, 360f)]
        public float maxSweep = 360f;

        private void Reset() => shape = GetComponent<AEShape>();

        public override void UpdateTarget(Progressor progressor)
        {
            if (shape == null) return;
            float sweep = Mathf.Clamp01(Pick(progressor)) * maxSweep;
            shape.arcSweep = sweep;
            shape.enabled = sweep > 0.25f;
        }
    }
}
