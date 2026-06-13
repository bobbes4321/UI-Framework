using TMPro;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Binds a TMP label to a UIStepper's value — the missing piece between "the stepper steps"
    /// and "the user can see it stepped". Auto-finds the stepper in parents and the label on this
    /// GameObject when left empty.
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/UI Stepper Value Label")]
    public class UIStepperValueLabel : MonoBehaviour
    {
        [Tooltip("Stepper to mirror; found in parents when empty")]
        public UIStepper stepper;
        [Tooltip("Label to write into; found on this GameObject when empty")]
        public TMP_Text label;
        [Tooltip("Numeric display format")]
        public string format = "0.##";

        private void OnEnable()
        {
            if (stepper == null) stepper = GetComponentInParent<UIStepper>();
            if (label == null) label = GetComponent<TMP_Text>();
            if (stepper == null) return;
            stepper.OnValueChanged.AddListener(OnValueChanged);
            Refresh();
        }

        private void OnDisable()
        {
            if (stepper != null) stepper.OnValueChanged.RemoveListener(OnValueChanged);
        }

        private void OnValueChanged(float _) => Refresh();

        public void Refresh()
        {
            if (label == null || stepper == null) return;
            label.text = stepper.currentValue.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
