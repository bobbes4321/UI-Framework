using TMPro;
using UnityEngine;

namespace Neo.UI
{
    /// <summary> Writes a progressor's value into a TMP text ("{0:0}%" style format). </summary>
    [AddComponentMenu("Neo/UI/Progress Targets/Text Progress Target")]
    public class TextProgressTarget : ProgressTarget
    {
        public TMP_Text text;

        [Tooltip("string.Format pattern; {0} is the value (progress is multiplied by 100)")]
        public string format = "{0:0}%";

        [Tooltip("Multiply progress by 100 before formatting (ignored in Value mode)")]
        public bool progressAsPercentage = true;

        private void Reset() => text = GetComponent<TMP_Text>();

        public override void UpdateTarget(Progressor progressor)
        {
            if (text == null) return;
            float value = Pick(progressor);
            if (targetMode == Mode.Progress && progressAsPercentage) value *= 100f;
            text.text = string.Format(format, value);
        }
    }
}
