using System;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// A condition that advances the flow: a button click, a signal, a toggle, a view event,
    /// the back button or a timer. Lives on a flow edge (UINode) or a PortalNode.
    /// </summary>
    [Serializable]
    public class FlowTrigger
    {
        public enum TriggerType
        {
            None = 0,
            ButtonClick = 1,
            Signal = 2,
            ToggleOn = 3,
            ToggleOff = 4,
            ViewShown = 5,
            ViewHidden = 6,
            Back = 7,
            Timer = 8
        }

        public TriggerType type = TriggerType.None;

        [Tooltip("Category of the button/toggle/view/stream this trigger matches")]
        public string category;
        [Tooltip("Name of the button/toggle/view/stream this trigger matches")]
        public string name;

        [Tooltip("Seconds before a Timer trigger fires")]
        [Min(0f)] public float timerDuration = 1f;

        public bool usesSignalStream =>
            type == TriggerType.ButtonClick || type == TriggerType.Signal ||
            type == TriggerType.ToggleOn || type == TriggerType.ToggleOff ||
            type == TriggerType.ViewShown || type == TriggerType.ViewHidden ||
            type == TriggerType.Back;

        public override string ToString()
        {
            switch (type)
            {
                case TriggerType.Timer: return $"Timer {timerDuration:0.##}s";
                case TriggerType.Back: return "Back";
                case TriggerType.None: return "None";
                default: return $"{type} {category}/{name}";
            }
        }
    }

    /// <summary>
    /// Connects a FlowTrigger to its signal stream and invokes a callback when it matches.
    /// Timer triggers are not handled here — nodes count them down in Tick.
    /// </summary>
    public class FlowTriggerListener : ISignalReceiver
    {
        private readonly FlowTrigger _trigger;
        private readonly Action _onFired;
        private SignalStream _stream;

        public FlowTriggerListener(FlowTrigger trigger, Action onFired)
        {
            _trigger = trigger;
            _onFired = onFired;
        }

        public void Connect()
        {
            if (_stream != null || _trigger == null || !_trigger.usesSignalStream) return;
            switch (_trigger.type)
            {
                case FlowTrigger.TriggerType.ButtonClick:
                    _stream = Signals.Stream(UIButton.StreamCategory, UIButton.StreamName);
                    break;
                case FlowTrigger.TriggerType.ToggleOn:
                case FlowTrigger.TriggerType.ToggleOff:
                    _stream = Signals.Stream(UIToggle.StreamCategory, UIToggle.StreamName);
                    break;
                case FlowTrigger.TriggerType.ViewShown:
                case FlowTrigger.TriggerType.ViewHidden:
                    _stream = Signals.Stream(UIView.StreamCategory, UIView.VisibilityStreamName);
                    break;
                case FlowTrigger.TriggerType.Back:
                    _stream = Signals.Stream(BackButton.StreamCategory, BackButton.StreamName);
                    break;
                case FlowTrigger.TriggerType.Signal:
                    _stream = Signals.Stream(_trigger.category, _trigger.name);
                    break;
            }
            _stream?.ConnectReceiver(this);
        }

        public void Disconnect()
        {
            _stream?.DisconnectReceiver(this);
            _stream = null;
        }

        public void OnSignal(Signal signal)
        {
            if (Matches(signal)) _onFired?.Invoke();
        }

        private bool Matches(Signal signal)
        {
            switch (_trigger.type)
            {
                case FlowTrigger.TriggerType.ButtonClick:
                    return signal.TryGetValue(out ButtonSignalData button)
                           && button.category == _trigger.category
                           && button.buttonName == _trigger.name
                           && (button.trigger == BehaviourTrigger.Click || button.trigger == BehaviourTrigger.Submit);

                case FlowTrigger.TriggerType.ToggleOn:
                case FlowTrigger.TriggerType.ToggleOff:
                    return signal.TryGetValue(out ToggleSignalData toggle)
                           && toggle.category == _trigger.category
                           && toggle.toggleName == _trigger.name
                           && toggle.isOn == (_trigger.type == FlowTrigger.TriggerType.ToggleOn);

                case FlowTrigger.TriggerType.ViewShown:
                    return signal.TryGetValue(out ViewVisibilityData shown)
                           && shown.category == _trigger.category
                           && shown.viewName == _trigger.name
                           && shown.state == VisibilityState.Visible;

                case FlowTrigger.TriggerType.ViewHidden:
                    return signal.TryGetValue(out ViewVisibilityData hidden)
                           && hidden.category == _trigger.category
                           && hidden.viewName == _trigger.name
                           && hidden.state == VisibilityState.Hidden;

                case FlowTrigger.TriggerType.Back:
                case FlowTrigger.TriggerType.Signal:
                    return true;

                default:
                    return false;
            }
        }
    }
}
