using System;
using UnityEngine;

namespace Neo.UI
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
            Timer = 8,
            /// <summary>
            /// A project-supplied trigger kind registered in <see cref="NeoTriggerKinds"/>. The actual
            /// kind is named by <see cref="FlowTrigger.customKind"/> — see the extensibility seam.
            /// </summary>
            Custom = 9
        }

        public TriggerType type = TriggerType.None;

        [Tooltip("Category of the button/toggle/view/stream this trigger matches")]
        public string category;
        [Tooltip("Name of the button/toggle/view/stream this trigger matches")]
        public string name;

        [Tooltip("Seconds before a Timer trigger fires")]
        [Min(0f)] public float timerDuration = 1f;

        [Tooltip("Id of the registered NeoTriggerKinds kind, when type == Custom")]
        public string customKind;

        public bool usesSignalStream =>
            type == TriggerType.ButtonClick || type == TriggerType.Signal ||
            type == TriggerType.ToggleOn || type == TriggerType.ToggleOff ||
            type == TriggerType.ViewShown || type == TriggerType.ViewHidden ||
            type == TriggerType.Back || type == TriggerType.Custom;

        public override string ToString()
        {
            switch (type)
            {
                case TriggerType.Timer: return $"Timer {timerDuration:0.##}s";
                case TriggerType.Back: return "Back";
                case TriggerType.None: return "None";
                case TriggerType.Custom: return $"Custom:{customKind} {category}/{name}";
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

        /// <summary> The trigger this listener watches — read by custom <see cref="INeoTriggerKind"/>s. </summary>
        public FlowTrigger Trigger => _trigger;

        /// <summary>
        /// Subscribes this listener to <paramref name="stream"/> (so its signals route to
        /// <see cref="OnSignal"/> → the kind's <c>Matches</c>). Used by a custom
        /// <see cref="INeoTriggerKind.Connect"/> implementation; <see cref="Disconnect"/> tears it down.
        /// No-op if already bound.
        /// </summary>
        public void BindStream(SignalStream stream)
        {
            if (_stream != null || stream == null) return;
            _stream = stream;
            _stream.ConnectReceiver(this);
        }

        public void Connect()
        {
            if (_stream != null || _trigger == null || !_trigger.usesSignalStream) return;
            if (_trigger.type == FlowTrigger.TriggerType.Custom)
            {
                if (NeoTriggerKinds.TryGet(_trigger.customKind, out INeoTriggerKind kind))
                    kind.Connect(this, () => _onFired?.Invoke());
                else
                    Debug.LogWarning($"FlowTrigger: no registered trigger kind '{_trigger.customKind}' — edge will never fire. Register it in NeoTriggerKinds from a [RuntimeInitializeOnLoadMethod].");
                return;
            }
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
            if (_trigger.type == FlowTrigger.TriggerType.Custom)
                return NeoTriggerKinds.TryGet(_trigger.customKind, out INeoTriggerKind kind)
                       && kind.Matches(this, signal);
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
