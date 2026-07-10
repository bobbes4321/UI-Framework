using UnityEngine;
using UnityEngine.Events;

namespace Neo.UI
{
    /// <summary>
    /// Plus/minus stepper: a value stepped by buttons (or code) with step size, min/max and value events.
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/UI Stepper")]
    public class UIStepper : MonoBehaviour, INeoIdOwner
    {
        // A stepper has no id field of its own — its logical element id is derived from its minus
        // button's id ("{name}{ButtonSuffixMinus}"), the naming convention this component owns.
        public const string ButtonSuffixMinus = "_Minus";
        public const string ButtonSuffixPlus = "_Plus";

        CategoryNameId INeoIdOwner.OwnId
        {
            get
            {
                if (minusButton == null) return null;
                string buttonName = minusButton.id.Name;
                if (!buttonName.EndsWith(ButtonSuffixMinus, System.StringComparison.Ordinal)) return null;
                return new CategoryNameId(minusButton.id.Category,
                    buttonName.Substring(0, buttonName.Length - ButtonSuffixMinus.Length));
            }
        }

        [Header("Value")]
        [SerializeField] private float value;
        public float stepSize = 1f;
        public float minValue;
        public float maxValue = 10f;
        public bool wholeNumbers = true;

        [Header("Buttons (optional)")]
        public UIButton plusButton;
        public UIButton minusButton;

        [Header("Events")]
        public FloatEvent OnValueChanged = new FloatEvent();
        public FloatEvent OnValueIncremented = new FloatEvent();
        public FloatEvent OnValueDecremented = new FloatEvent();
        public UnityEvent OnValueReachedMin = new UnityEvent();
        public UnityEvent OnValueReachedMax = new UnityEvent();

        public float currentValue
        {
            get => value;
            set => SetValue(value);
        }

        private void OnEnable()
        {
            if (plusButton != null) plusButton.onClickEvent.AddListener(StepUp);
            if (minusButton != null) minusButton.onClickEvent.AddListener(StepDown);
        }

        private void OnDisable()
        {
            if (plusButton != null) plusButton.onClickEvent.RemoveListener(StepUp);
            if (minusButton != null) minusButton.onClickEvent.RemoveListener(StepDown);
        }

        public void StepUp() => SetValue(value + stepSize);
        public void StepDown() => SetValue(value - stepSize);

        public void SetValue(float newValue)
        {
            newValue = Mathf.Clamp(newValue, minValue, maxValue);
            if (wholeNumbers) newValue = Mathf.Round(newValue);
            if (Mathf.Approximately(newValue, value)) return;

            float previous = value;
            value = newValue;

            OnValueChanged?.Invoke(newValue);
            if (newValue > previous) OnValueIncremented?.Invoke(newValue - previous);
            else OnValueDecremented?.Invoke(previous - newValue);

            if (Mathf.Approximately(newValue, minValue)) OnValueReachedMin?.Invoke();
            if (Mathf.Approximately(newValue, maxValue)) OnValueReachedMax?.Invoke();
        }
    }
}
