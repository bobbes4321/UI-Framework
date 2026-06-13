using TMPro;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Rolling number label: <see cref="SetValue"/> tweens the displayed value to the target
    /// (score counters, coin totals). The baked text always shows <see cref="value"/> so prefabs
    /// and screenshots match the runtime start state (WYSIWYG).
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    [AddComponentMenu("Neo/UI/Interactive/Counter")]
    public class UICounter : MonoBehaviour
    {
        [Tooltip("Current target value; SetValue rolls toward new targets")]
        public float value;
        [Tooltip("Numeric format string for the label, e.g. 0 / 0.0 / #,0")]
        public string format = "0";
        [Tooltip("Roll duration in seconds")]
        public float duration = 0.4f;
        public Ease ease = Ease.OutCubic;

        private TMP_Text _text;
        private FloatTween _tween;
        private float _displayed;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
            _displayed = value;
        }

        private void OnEnable() => Write(_displayed);

        private void OnDisable() => _tween?.Stop(silent: true);

        /// <summary> Rolls the displayed number to the new value (instant outside play mode). </summary>
        public void SetValue(float newValue, bool instant = false)
        {
            value = newValue;
            if (instant || !Application.isPlaying || duration <= 0f)
            {
                _tween?.Stop(silent: true);
                Write(newValue);
                return;
            }

            if (_tween == null)
            {
                _tween = new FloatTween();
                _tween.SetTarget(() => _displayed, Write);
            }
            _tween.Stop(silent: true);
            _tween.SetFrom(_displayed);
            _tween.SetTo(newValue);
            _tween.settings.duration = duration;
            _tween.settings.ease = ease;
            _tween.Play();
        }

        private void Write(float displayValue)
        {
            _displayed = displayValue;
            if (_text == null) _text = GetComponent<TMP_Text>();
            if (_text != null)
                _text.text = displayValue.ToString(string.IsNullOrEmpty(format) ? "0" : format,
                    System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
