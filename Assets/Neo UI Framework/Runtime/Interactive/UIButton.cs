using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Neo.UI
{
    /// <summary> Payload published on the "UIButton/Behaviour" stream for every executed button behaviour. </summary>
    [Serializable]
    public struct ButtonSignalData
    {
        public string category;
        public string buttonName;
        public BehaviourTrigger trigger;

        public override string ToString() => $"{category}/{buttonName} {trigger}";
    }

    /// <summary>
    /// The workhorse: a selectable button with category/name id, a static registry, and per-trigger
    /// behaviours (PointerEnter/Exit/Down/Up, Click, DoubleClick, LongClick, RightClick,
    /// Selected/Deselected, Submit) each carrying a UnityEvent, signal emission and cooldown.
    /// Every executed behaviour publishes <see cref="ButtonSignalData"/> on "UIButton/Behaviour" —
    /// the channel flow-graph nodes listen to.
    /// </summary>
    [AddComponentMenu("Neo/UI/Interactive/UI Button")]
    public class UIButton : UISelectable, IPointerClickHandler, ISubmitHandler, ITickable
    {
        public const string StreamCategory = "UIButton";
        public const string StreamName = "Behaviour";

        public const float DoubleClickWindow = 0.3f;
        public const float LongClickThreshold = 0.5f;

        public ButtonId id = new ButtonId();

        [Tooltip("Configured reactions; add one per trigger you care about")]
        public List<UIActionBehaviour> behaviours = new List<UIActionBehaviour>
        {
            new UIActionBehaviour(BehaviourTrigger.Click)
        };

        private static readonly HashSet<UIButton> Registry = new HashSet<UIButton>();

        private float _lastClickTime = float.MinValue;
        private float _pressTime;
        private bool _pressed;
        private bool _longClickFired;

        public static IEnumerable<UIButton> allButtons => Registry;

        public static UIButton GetFirstButton(string category, string name) =>
            Registry.FirstOrDefault(b => b.id.Matches(category, name));

        /// <summary> Shortcut to the Click behaviour's UnityEvent (creating the behaviour if needed). </summary>
        public UnityEvent onClickEvent => GetOrAddBehaviour(BehaviourTrigger.Click).Event;

        public UIActionBehaviour GetBehaviour(BehaviourTrigger trigger) =>
            behaviours.FirstOrDefault(b => b.trigger == trigger);

        public UIActionBehaviour GetOrAddBehaviour(BehaviourTrigger trigger)
        {
            UIActionBehaviour behaviour = GetBehaviour(trigger);
            if (behaviour == null)
            {
                behaviour = new UIActionBehaviour(trigger);
                behaviours.Add(behaviour);
            }
            return behaviour;
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            Registry.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Registry.Remove(this);
            UITick.Unregister(this);
            _pressed = false;
        }

        // ------------------------------------------------------------------ pointer handlers

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            if (!interactable) return;
            ExecuteTrigger(BehaviourTrigger.PointerEnter);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            base.OnPointerExit(eventData);
            if (!interactable) return;
            ExecuteTrigger(BehaviourTrigger.PointerExit);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (!interactable || eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = true;
            _longClickFired = false;
            _pressTime = Time.realtimeSinceStartup;
            if (GetBehaviour(BehaviourTrigger.LongClick) != null) UITick.Register(this);
            ExecuteTrigger(BehaviourTrigger.PointerDown);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = false;
            UITick.Unregister(this);
            if (!interactable) return;
            ExecuteTrigger(BehaviourTrigger.PointerUp);
        }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (!interactable) return;

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                ExecuteTrigger(BehaviourTrigger.RightClick);
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (_longClickFired) return; // a long click consumed this press

            Click();
        }

        public virtual void OnSubmit(BaseEventData eventData)
        {
            if (!interactable) return;
            ExecuteTrigger(BehaviourTrigger.Submit);
            Click();
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            ExecuteTrigger(BehaviourTrigger.Selected);
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            ExecuteTrigger(BehaviourTrigger.Deselected);
        }

        // ------------------------------------------------------------------ click logic

        /// <summary> Executes the Click behaviour (and DoubleClick when within the window). Callable from code. </summary>
        public void Click()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastClickTime <= DoubleClickWindow && GetBehaviour(BehaviourTrigger.DoubleClick) != null)
            {
                _lastClickTime = float.MinValue;
                ExecuteTrigger(BehaviourTrigger.DoubleClick);
                return;
            }

            _lastClickTime = now;
            ExecuteTrigger(BehaviourTrigger.Click);
        }

        /// <summary> Long-click detection while pressed. </summary>
        public void Tick(float deltaTime)
        {
            if (!_pressed || _longClickFired) return;
            if (Time.realtimeSinceStartup - _pressTime < LongClickThreshold) return;
            _longClickFired = true;
            UITick.Unregister(this);
            ExecuteTrigger(BehaviourTrigger.LongClick);
        }

        /// <summary> Executes a trigger's behaviour (if configured) and publishes the button signal. </summary>
        public bool ExecuteTrigger(BehaviourTrigger trigger)
        {
            UIActionBehaviour behaviour = GetBehaviour(trigger);
            bool executed = behaviour != null && behaviour.Execute(this);
            if (behaviour == null || executed)
                PublishSignal(trigger);
            return executed;
        }

        private void PublishSignal(BehaviourTrigger trigger)
        {
            // Only publish triggers anyone could reasonably navigate on; hover spam stays local.
            switch (trigger)
            {
                case BehaviourTrigger.Click:
                case BehaviourTrigger.DoubleClick:
                case BehaviourTrigger.LongClick:
                case BehaviourTrigger.RightClick:
                case BehaviourTrigger.Submit:
                    Signals.Send(StreamCategory, StreamName,
                        new ButtonSignalData { category = id.Category, buttonName = id.Name, trigger = trigger }, this);
                    break;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();
    }
}
