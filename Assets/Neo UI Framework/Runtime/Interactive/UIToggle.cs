using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Neo.UI
{
    [Serializable] public class BoolEvent : UnityEvent<bool> { }

    /// <summary> An animator reacting to toggle on/off changes. </summary>
    public interface IToggleAnimator
    {
        void OnToggleValueChanged(bool isOn, bool instant);
    }

    /// <summary> Payload published on "UIToggle/Behaviour" whenever a toggle's value changes. </summary>
    [Serializable]
    public struct ToggleSignalData
    {
        public string category;
        public string toggleName;
        public bool isOn;
    }

    /// <summary>
    /// On/off selectable with group support: value-changed events (plus instant variants that skip
    /// animation), category/name lookup, optional <see cref="UIToggleGroup"/> exclusivity.
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/UI Toggle")]
    public class UIToggle : UISelectable, IPointerClickHandler, ISubmitHandler, INeoIdOwner
    {
        public const string StreamCategory = "UIToggle";
        public const string StreamName = "Behaviour";

        public ToggleId id = new ToggleId();
        CategoryNameId INeoIdOwner.OwnId => id;

        [Tooltip("Optional domain stream this toggle publishes its bool value to, in addition to the " +
                 "standard \"UIToggle/Behaviour\" stream — lets game code do Signals.On<bool>(category, name, …) " +
                 "without branching the firehose. Default (None/None) = no extra stream.")]
        public StreamId domainSignal = new StreamId();

        [SerializeField] private bool isOnValue;

        [Tooltip("Group enforcing exclusivity; optional")]
        [SerializeField] private UIToggleGroup groupReference;

        /// <summary> The group enforcing exclusivity; assigning re-registers with the new group. </summary>
        public UIToggleGroup toggleGroup
        {
            get => groupReference;
            set
            {
                if (groupReference == value) return;
                if (isActiveAndEnabled) groupReference?.UnregisterToggle(this);
                groupReference = value;
                if (isActiveAndEnabled) groupReference?.RegisterToggle(this);
            }
        }

        [Header("Events")]
        public BoolEvent onValueChanged = new BoolEvent();
        public UnityEvent onToggleOn = new UnityEvent();
        public UnityEvent onToggleOff = new UnityEvent();

        /// <summary> C# event: (isOn, animateChange). </summary>
        public event Action<bool, bool> OnValueChanged;

        private readonly List<IToggleAnimator> _toggleAnimators = new List<IToggleAnimator>();
        private static readonly HashSet<UIToggle> Registry = new HashSet<UIToggle>();

        public static IEnumerable<UIToggle> allToggles => Registry;

        public static UIToggle GetFirstToggle(string category, string name) =>
            Registry.FirstOrDefault(t => t.id.Matches(category, name));

        public bool isOn
        {
            get => isOnValue;
            set => SetIsOn(value, animateChange: true);
        }

        public bool inGroup => toggleGroup != null;

        // ------------------------------------------------------------------ lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            Registry.Add(this);
            if (toggleGroup != null) toggleGroup.RegisterToggle(this);
            NotifyAnimators(instant: true);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Registry.Remove(this);
            if (toggleGroup != null) toggleGroup.UnregisterToggle(this);
        }

        public void RegisterToggleAnimator(IToggleAnimator animator)
        {
            if (animator == null || _toggleAnimators.Contains(animator)) return;
            _toggleAnimators.Add(animator);
            animator.OnToggleValueChanged(isOnValue, instant: true);
        }

        public void UnregisterToggleAnimator(IToggleAnimator animator) => _toggleAnimators.Remove(animator);

        // ------------------------------------------------------------------ value

        /// <summary> Sets the value, optionally without animating (instant variant). </summary>
        public void SetIsOn(bool value, bool animateChange)
        {
            if (isOnValue == value) return;

            if (toggleGroup != null)
            {
                toggleGroup.ToggleChangedValue(this, value, animateChange);
                return;
            }

            ApplyValue(value, animateChange);
        }

        public void InstantToggleOn() => SetIsOn(true, animateChange: false);
        public void InstantToggleOff() => SetIsOn(false, animateChange: false);

        /// <summary> Internal value application — used directly by the group after rule enforcement. </summary>
        internal void ApplyValue(bool value, bool animateChange, bool notifyGroup = false)
        {
            if (isOnValue == value) return;
            isOnValue = value;

            NotifyAnimators(instant: !animateChange);
            onValueChanged?.Invoke(value);
            if (value) onToggleOn?.Invoke();
            else onToggleOff?.Invoke();
            OnValueChanged?.Invoke(value, animateChange);

            Signals.Send(StreamCategory, StreamName,
                new ToggleSignalData { category = id.Category, toggleName = id.Name, isOn = value }, this);

            // additive first-class domain signal: Signals.On<bool>("Audio","Muted", …) works directly
            if (!domainSignal.isDefault)
                Signals.Send(domainSignal.Category, domainSignal.Name, value, this);

            if (notifyGroup && toggleGroup != null)
                toggleGroup.NotifyToggleChanged(this, value);
        }

        private void NotifyAnimators(bool instant)
        {
            for (int i = 0; i < _toggleAnimators.Count; i++)
                _toggleAnimators[i].OnToggleValueChanged(isOnValue, instant);
        }

        public void Toggle() => SetIsOn(!isOnValue, animateChange: true);

        // ------------------------------------------------------------------ input

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (!interactable || eventData.button != PointerEventData.InputButton.Left) return;
            Toggle();
            DeselectAfterPointerClick();
        }

        public virtual void OnSubmit(BaseEventData eventData)
        {
            if (!interactable) return;
            Toggle();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
