using UnityEngine;

namespace Neo.UI
{
    /// <summary> Receives value/progress updates from a <see cref="Progressor"/>. </summary>
    public abstract class ProgressTarget : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary> Feed the normalized progress [0,1]. </summary>
            Progress = 0,
            /// <summary> Feed the absolute current value. </summary>
            Value = 1
        }

        public Mode targetMode = Mode.Progress;

        public abstract void UpdateTarget(Progressor progressor);

        protected float Pick(Progressor progressor) =>
            targetMode == Mode.Progress ? progressor.progress : progressor.currentValue;
    }
}
