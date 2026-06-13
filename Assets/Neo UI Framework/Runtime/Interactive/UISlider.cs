using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary> Payload published on "UISlider/Behaviour" for a committed slider value. </summary>
    [Serializable]
    public struct SliderSignalData
    {
        public string category;
        public string sliderName;
        public float value;
    }

    /// <summary>
    /// Slider with category/name id and richer value events (incremented/decremented/min/max).
    /// Extends Unity's Slider, so drag + keyboard/gamepad input come from UGUI.
    /// Distinguishes a continuous <see cref="Slider.onValueChanged"/> (preview, fired every frame of a
    /// drag) from <see cref="OnValueCommitted"/> (fired once on release / keyboard step / code set) —
    /// the settings "on drag vs on release" split. A committed value also publishes on "UISlider/Behaviour".
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/UI Slider")]
    public class UISlider : Slider, IPointerUpHandler, IEndDragHandler
    {
        public const string StreamCategory = "UISlider";
        public const string StreamName = "Behaviour";

        public SliderId id = new SliderId();

        public FloatEvent OnValueIncremented = new FloatEvent();
        public FloatEvent OnValueDecremented = new FloatEvent();
        public UnityEvent OnValueReachedMin = new UnityEvent();
        public UnityEvent OnValueReachedMax = new UnityEvent();

        /// <summary> Fired once when a value change is committed (pointer release, end drag, keyboard step
        /// or a code-driven <see cref="Slider.value"/> set) — as opposed to mid-drag preview frames. </summary>
        public FloatEvent OnValueCommitted = new FloatEvent();

        private static readonly HashSet<UISlider> Registry = new HashSet<UISlider>();

        private float _previousValue;
        private bool _initialized;
        private bool _dragging;

        public static IEnumerable<UISlider> allSliders => Registry;

        public static UISlider GetFirstSlider(string category, string name) =>
            Registry.FirstOrDefault(s => s.id.Matches(category, name));

        protected override void OnEnable()
        {
            base.OnEnable();
            Registry.Add(this);
            _previousValue = value;
            _initialized = true;
            onValueChanged.AddListener(HandleValueChanged);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Registry.Remove(this);
            onValueChanged.RemoveListener(HandleValueChanged);
            _dragging = false;
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            _dragging = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            Commit();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            _dragging = false;
            Commit();
        }

        private void HandleValueChanged(float newValue)
        {
            if (!_initialized) return;
            float previous = _previousValue;
            _previousValue = newValue;

            if (newValue > previous) OnValueIncremented?.Invoke(newValue - previous);
            else if (newValue < previous) OnValueDecremented?.Invoke(previous - newValue);

            if (Mathf.Approximately(newValue, minValue)) OnValueReachedMin?.Invoke();
            if (Mathf.Approximately(newValue, maxValue)) OnValueReachedMax?.Invoke();

            // A change outside of an active drag (keyboard, gamepad, code set) commits immediately.
            if (!_dragging) Commit();
        }

        private void Commit()
        {
            OnValueCommitted?.Invoke(value);
            Signals.Send(StreamCategory, StreamName,
                new SliderSignalData { category = id.Category, sliderName = id.Name, value = value }, this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
