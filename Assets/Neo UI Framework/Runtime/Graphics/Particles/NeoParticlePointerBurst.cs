using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Bursts the sibling <see cref="NeoParticleEmitter"/> at the exact point the user clicks inside
    /// the element — "a particle is triggered where I click" (tap-to-sparkle, click confetti, hit
    /// feedback) with no per-scene UnityEvents. The sibling pattern to
    /// <see cref="NeoParticleBurstOnSignal"/>: same emitter, different trigger — a pointer-down at a
    /// screen position instead of a named signal.
    /// </summary>
    /// <remarks>
    /// <para><b>Play-mode only.</b> Like <see cref="NeoEffectTrigger"/>, every pointer handler guards on
    /// <see cref="Application.isPlaying"/> so editor selection-clicks never spawn particles, and the
    /// host <see cref="Graphic.raycastTarget"/> is forced on in <see cref="OnEnable"/> — without it the
    /// host never receives the pointer-down this listens for.</para>
    ///
    /// <para><b>Spawn-origin approach.</b> The click is converted to a local point in the emitter's own
    /// <see cref="RectTransform"/> (centre-relative) via
    /// <see cref="RectTransformUtility.ScreenPointToLocalPointInRectangle"/> and passed to
    /// <see cref="NeoParticleEmitter.Burst(int,Vector2)"/>, which spawns THIS burst from that point and
    /// restores the centre afterwards — so the emitter and its host element never move. (An earlier
    /// version translated the host rect to the click point, which dragged the whole visible element
    /// every click; the per-burst origin overload replaces that.)</para>
    /// </remarks>
    [AddComponentMenu("Neo/UI/Particles/Pointer Burst")]
    [RequireComponent(typeof(NeoParticleEmitter))]
    public class NeoParticlePointerBurst : MonoBehaviour, IPointerDownHandler
    {
        [Tooltip("How many particles to emit at the click point. <= 0 uses the emitter's configured burst count.")]
        [SerializeField] private int count;

        private NeoParticleEmitter _emitter;
        private RectTransform _rectTransform;
        private Graphic _graphic;

        /// <summary> How many particles a click emits; &lt;= 0 falls back to the emitter's burst count. </summary>
        public int Count { get => count; set => count = value; }

        private void Awake()
        {
            _emitter = GetComponent<NeoParticleEmitter>();
            _rectTransform = (RectTransform)transform;
            _graphic = GetComponent<Graphic>();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            if (_emitter == null) _emitter = GetComponent<NeoParticleEmitter>();
            if (_rectTransform == null) _rectTransform = (RectTransform)transform;
            if (_graphic == null) _graphic = GetComponent<Graphic>();

            if (_emitter == null)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoParticlePointerBurst)} on '{name}': no " +
                                 $"{nameof(NeoParticleEmitter)} found — pointer burst inactive.", this);
                return;
            }

            // Without a raycast target the host never receives the pointer-down we listen for.
            if (_graphic != null) _graphic.raycastTarget = true;
        }

        /// <inheritdoc/>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Application.isPlaying) return;
            if (_emitter == null || _rectTransform == null) return;

            // Convert the screen-space click into a local point in the emitter's own rect.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                return; // click could not be resolved into this rect — nothing sensible to do
            }

            // Emit FROM the click point without moving the emitter or its host: the emitter's
            // per-burst spawn-origin overload places this burst's particles at `local` (emitter-centre
            // relative) and restores the centre afterwards. (Previously this translated the host rect,
            // which dragged the whole visible element with every click.)
            int n = count > 0 ? count : _emitter.BurstCount;
            _emitter.Burst(n, local);
        }
    }
}
