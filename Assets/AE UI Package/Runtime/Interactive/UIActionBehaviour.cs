using System;
using UnityEngine;
using UnityEngine.Events;

namespace AlterEyes.UI
{
    /// <summary> Interaction triggers a UIButton (and friends) can react to. </summary>
    public enum BehaviourTrigger
    {
        PointerEnter = 0,
        PointerExit = 1,
        PointerDown = 2,
        PointerUp = 3,
        Click = 4,
        DoubleClick = 5,
        LongClick = 6,
        RightClick = 7,
        Selected = 8,
        Deselected = 9,
        Submit = 10
    }

    /// <summary>
    /// One configured reaction to a trigger: a UnityEvent, optional signal emission and an optional
    /// per-behaviour cooldown.
    /// </summary>
    [Serializable]
    public class UIActionBehaviour
    {
        public BehaviourTrigger trigger = BehaviourTrigger.Click;

        public UnityEvent Event = new UnityEvent();

        [Tooltip("Also send a signal on a custom stream when this behaviour executes")]
        public bool sendSignal;
        public StreamId signalStream = new StreamId();

        [Tooltip("Seconds this behaviour ignores re-triggers after executing; 0 = none")]
        [Min(0f)] public float cooldown;

        [NonSerialized] private float _lastExecuteTime = float.MinValue;

        /// <summary> Code-side callback, invoked alongside the UnityEvent. </summary>
        public Action onExecute;

        public UIActionBehaviour() { }

        public UIActionBehaviour(BehaviourTrigger behaviourTrigger)
        {
            trigger = behaviourTrigger;
        }

        public bool isOnCooldown =>
            cooldown > 0f && CurrentTime() - _lastExecuteTime < cooldown;

        /// <summary> Executes the behaviour (UnityEvent + callbacks + signal). False when on cooldown. </summary>
        public bool Execute(object sender = null)
        {
            if (isOnCooldown) return false;
            _lastExecuteTime = CurrentTime();
            Event?.Invoke();
            onExecute?.Invoke();
            if (sendSignal && signalStream != null && !signalStream.isDefault)
                Signals.Send(signalStream.Category, signalStream.Name, sender: sender);
            return true;
        }

        private static float CurrentTime() => Time.realtimeSinceStartup;
    }
}
