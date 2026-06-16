using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Bursts the sibling <see cref="NeoParticleEmitter"/> when a named signal fires — the "coin burst
    /// on purchase" / "sparkle on toggle" wiring with no per-scene UnityEvents. The trigger is
    /// addressed by category + name strings (agent-first), and the subscription is established in
    /// <see cref="OnEnable"/> / torn down in <see cref="OnDisable"/> so it never leaks.
    /// </summary>
    [AddComponentMenu("Neo/UI/Particles/Burst On Signal")]
    [RequireComponent(typeof(NeoParticleEmitter))]
    public class NeoParticleBurstOnSignal : MonoBehaviour
    {
        [Tooltip("Signal category to listen on (e.g. \"Shop\").")]
        [SerializeField] private string category = "UI";
        [Tooltip("Signal name to listen on (e.g. \"ItemBought\").")]
        [SerializeField] private string signalName = "Burst";
        [Tooltip("How many particles to emit when the signal fires. <= 0 uses the emitter's configured burst count.")]
        [SerializeField] private int count;

        private NeoParticleEmitter _emitter;

        /// <summary> Signal category this listens on. </summary>
        public string Category { get => category; set => category = value; }

        /// <summary> Signal name this listens on. </summary>
        public string SignalName { get => signalName; set => signalName = value; }

        private void Awake() => _emitter = GetComponent<NeoParticleEmitter>();

        private void OnEnable()
        {
            if (_emitter == null) _emitter = GetComponent<NeoParticleEmitter>();
            if (_emitter == null)
            {
                Debug.LogWarning($"[Neo.UI] NeoParticleBurstOnSignal '{name}': no NeoParticleEmitter found — burst wiring inactive.");
                return;
            }
            Signals.On(category, signalName, HandleSignal);
        }

        private void OnDisable()
        {
            Signals.Off(category, signalName, HandleSignal);
        }

        private void HandleSignal()
        {
            if (_emitter == null) return;
            if (count > 0) _emitter.Burst(count);
            else _emitter.Burst();
        }
    }
}
