using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Fires a configured signal on demand and/or on lifecycle events.
    /// </summary>
    [AddComponentMenu("Neo/UI/Signals/Signal Sender")]
    public class SignalSender : MonoBehaviour
    {
        public StreamId streamId = new StreamId();

        [Tooltip("Optional message attached to the signal")]
        public string signalMessage;

        public bool sendOnStart;
        public bool sendOnEnable;
        public bool sendOnDisable;
        public bool sendOnDestroy;

        private void Start()
        {
            if (sendOnStart) SendSignal();
        }

        private void OnEnable()
        {
            if (sendOnEnable) SendSignal();
        }

        private void OnDisable()
        {
            if (sendOnDisable) SendSignal();
        }

        private void OnDestroy()
        {
            if (sendOnDestroy) SendSignal();
        }

        public virtual void SendSignal() =>
            Signals.Send(streamId.Category, streamId.Name, sender: this,
                message: string.IsNullOrEmpty(signalMessage) ? null : signalMessage);
    }
}
