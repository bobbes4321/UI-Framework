using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary> Debug helper: logs every signal on a stream to the console while enabled. </summary>
    [AddComponentMenu("AlterEyes/UI/Signals/Signal Logger")]
    public class SignalLogger : MonoBehaviour
    {
        public StreamId streamId = new StreamId();

        private SignalReceiver _receiver;

        private void OnEnable()
        {
            _receiver = new SignalReceiver(streamId.Category, streamId.Name)
                .SetOnSignalCallback(signal => Debug.Log($"[SignalLogger] {signal}", this));
            _receiver.Connect();
        }

        private void OnDisable()
        {
            _receiver?.Disconnect();
        }
    }
}
