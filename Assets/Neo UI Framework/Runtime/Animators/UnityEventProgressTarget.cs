using UnityEngine;

namespace Neo.UI
{
    /// <summary> Forwards a progressor's value/progress to a UnityEvent&lt;float&gt;. </summary>
    [AddComponentMenu("Neo/UI/Progress Targets/UnityEvent Progress Target")]
    public class UnityEventProgressTarget : ProgressTarget
    {
        public FloatEvent onUpdate = new FloatEvent();

        public override void UpdateTarget(Progressor progressor) => onUpdate?.Invoke(Pick(progressor));
    }
}
