using UnityEngine;

namespace Neo.UI
{
    /// <summary> Standalone receiver component: connects its embedded receiver while enabled. </summary>
    [AddComponentMenu("Neo/UI/Signals/Signal Receiver")]
    public class SignalReceiverComponent : MonoBehaviour
    {
        public SignalReceiver receiver = new SignalReceiver();

        private void OnEnable() => receiver.Connect();
        private void OnDisable() => receiver.Disconnect();
    }
}
