using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Receives signals from a stream. </summary>
    public interface ISignalReceiver
    {
        void OnSignal(Signal signal);
    }

    /// <summary>
    /// A named event channel addressed by category + name. Obtained (get-or-create) through
    /// <see cref="Signals.Stream(string,string)"/>. Receivers connect/disconnect; sending iterates
    /// a snapshot so receivers may disconnect (or connect others) from inside their handler.
    /// </summary>
    public class SignalStream
    {
        public string category { get; }
        public string name { get; }
        public string key => $"{category}/{name}";

        private readonly List<ISignalReceiver> _receivers = new List<ISignalReceiver>();
        private readonly List<ISignalReceiver> _snapshot = new List<ISignalReceiver>();

        public int receiverCount => _receivers.Count;

        /// <summary> The last signal sent through this stream (null until the first send). </summary>
        public Signal lastSignal { get; private set; }

        public event Action<ISignalReceiver> OnReceiverConnected;
        public event Action<ISignalReceiver> OnReceiverDisconnected;

        internal SignalStream(string streamCategory, string streamName)
        {
            category = streamCategory;
            name = streamName;
        }

        public void ConnectReceiver(ISignalReceiver receiver)
        {
            if (receiver == null || _receivers.Contains(receiver)) return;
            _receivers.Add(receiver);
            OnReceiverConnected?.Invoke(receiver);
        }

        public void DisconnectReceiver(ISignalReceiver receiver)
        {
            if (_receivers.Remove(receiver))
                OnReceiverDisconnected?.Invoke(receiver);
        }

        public bool IsConnected(ISignalReceiver receiver) => _receivers.Contains(receiver);

        public Signal SendSignal(object sender = null, string message = null)
        {
            var signal = new Signal();
            Stamp(signal, sender, message);
            Deliver(signal);
            return signal;
        }

        public MetaSignal<T> SendSignal<T>(T payload, object sender = null, string message = null)
        {
            var signal = new MetaSignal<T> { value = payload };
            Stamp(signal, sender, message);
            Deliver(signal);
            return signal;
        }

        private void Stamp(Signal signal, object sender, string message)
        {
            signal.stream = this;
            signal.senderObject = sender;
            signal.sourceGameObject =
                sender as GameObject ?? (sender is Component component && component != null ? component.gameObject : null);
            signal.timestamp = Application.isPlaying ? Time.realtimeSinceStartup : 0f;
            signal.message = message;
        }

        private void Deliver(Signal signal)
        {
            lastSignal = signal;
            if (_receivers.Count == 0) return;
            _snapshot.Clear();
            _snapshot.AddRange(_receivers);
            for (int i = 0; i < _snapshot.Count; i++)
            {
                ISignalReceiver receiver = _snapshot[i];
                if (!_receivers.Contains(receiver)) continue; // disconnected mid-delivery
                try
                {
                    receiver.OnSignal(signal);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public override string ToString() => key;
    }
}
