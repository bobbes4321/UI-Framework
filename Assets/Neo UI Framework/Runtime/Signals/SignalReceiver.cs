using System;
using UnityEngine;
using UnityEngine.Events;

namespace Neo.UI
{
    /// <summary>
    /// Serializable signal receiver embeddable in any component: pick a stream in the inspector,
    /// hook the UnityEvent (or set <see cref="onSignal"/> from code), call Connect/Disconnect
    /// from the host's OnEnable/OnDisable.
    /// </summary>
    [Serializable]
    public class SignalReceiver : ISignalReceiver
    {
        public StreamId streamId = new StreamId();

        [Tooltip("Invoked whenever a signal arrives on the stream")]
        public UnityEvent onSignalEvent = new UnityEvent();

        /// <summary> Code-side callback, receives the full signal. </summary>
        public UnityAction<Signal> onSignal;

        public bool isConnected { get; private set; }

        private SignalStream _stream;

        public SignalStream stream => _stream;

        public SignalReceiver() { }

        public SignalReceiver(string category, string name)
        {
            streamId = new StreamId(category, name);
        }

        public SignalReceiver SetOnSignalCallback(UnityAction<Signal> callback)
        {
            onSignal = callback;
            return this;
        }

        public void Connect()
        {
            if (isConnected) return;
            _stream = Signals.Stream(streamId.Category, streamId.Name);
            _stream.ConnectReceiver(this);
            isConnected = true;
        }

        public void Disconnect()
        {
            if (!isConnected) return;
            _stream?.DisconnectReceiver(this);
            _stream = null;
            isConnected = false;
        }

        public void OnSignal(Signal signal)
        {
            onSignal?.Invoke(signal);
            onSignalEvent?.Invoke();
        }
    }
}
