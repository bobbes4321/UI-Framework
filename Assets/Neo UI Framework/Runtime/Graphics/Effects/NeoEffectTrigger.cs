using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Gates sibling <see cref="NeoShapeEffect"/>(s) on POINTER state — the pointer-driven
    /// interactivity seam for effects. Instead of an effect running its timeline unconditionally,
    /// this trigger turns it on only on <b>hover</b> or only while <b>pressed</b>, so a host can wear
    /// the mobile-game "sheen on hover / juice on press" feel: a sheen sweep that runs while the
    /// cursor is over a card, a glow that breathes only while a button is held.
    ///
    /// <para>It drives EVERY <see cref="NeoShapeEffect"/> on the same GameObject (a host usually has
    /// one, but several are supported), so a single trigger can gate a glow + a sheen at once.</para>
    ///
    /// <para><b>Play-mode only.</b> Like <see cref="NeoShapeEffect"/> and
    /// <see cref="NeoSignalParamBinding"/>, everything here is guarded by
    /// <see cref="Application.isPlaying"/>: in the editor the trigger does nothing, so the baked
    /// resting frame the generator captured stays WYSIWYG and the inspector is never animated behind
    /// the developer's back.</para>
    ///
    /// <para><b>Hold vs PlayOnce</b> maps looping effects to one-shot effects:
    /// <see cref="TriggerMode.Hold"/> simply <c>enabled = true</c>s the effect while the pointer is
    /// over/held (and <see cref="NeoShapeEffect.Stop"/>s it on leave/release) — use it for looping
    /// effects (sheen sweep loop, glow pulse). <see cref="TriggerMode.PlayOnce"/> calls
    /// <see cref="NeoShapeEffect.Play"/> (restart from phase 0) on each enter/press — use it for
    /// one-shot sweeps (loop = false).</para>
    ///
    /// <para>It forces the host <see cref="Graphic.raycastTarget"/> on in play mode, otherwise the
    /// host would never receive the pointer events this trigger listens for.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Effect Trigger")]
    [RequireComponent(typeof(Graphic))]
    public class NeoEffectTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary> What pointer state gates the effects. </summary>
        public enum TriggerOn
        {
            /// <summary> No gating — the effect runs its own timeline as authored (default effect behavior). </summary>
            Always,

            /// <summary> Active while the pointer is over the host. </summary>
            Hover,

            /// <summary> Active while the pointer is held down on the host. </summary>
            Press,
        }

        /// <summary> How the gated effect is started when the trigger activates. </summary>
        public enum TriggerMode
        {
            /// <summary> Enable the effect while active, Stop() it when it ends — for looping effects. </summary>
            Hold,

            /// <summary> Play() (restart from phase 0) on each activate, Stop() on end — for one-shot sweeps. </summary>
            PlayOnce,
        }

        [Tooltip("What pointer state gates the effects: Always (no gating), Hover, or Press.")]
        [SerializeField] private TriggerOn triggerOn = TriggerOn.Hover;

        [Tooltip("Hold = enable the effect while active (looping effects); " +
                 "PlayOnce = restart the effect from 0 on each activate (one-shot sweeps).")]
        [SerializeField] private TriggerMode mode = TriggerMode.Hold;

        [System.NonSerialized] private NeoShapeEffect[] _effects;
        [System.NonSerialized] private Graphic _graphic;

        // ------------------------------------------------------------------ properties

        /// <summary> What pointer state gates the effects (Always / Hover / Press). </summary>
        public TriggerOn Trigger
        {
            get => triggerOn;
            set => triggerOn = value;
        }

        /// <summary> How the gated effect is started when the trigger activates (Hold / PlayOnce). </summary>
        public TriggerMode Mode
        {
            get => mode;
            set => mode = value;
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            _graphic = GetComponent<Graphic>();
            _effects = GetComponents<NeoShapeEffect>();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;

            if (_graphic == null) _graphic = GetComponent<Graphic>();
            if (_effects == null) _effects = GetComponents<NeoShapeEffect>();

            if (_effects == null || _effects.Length == 0)
            {
                Debug.LogWarning($"[Neo.UI] {nameof(NeoEffectTrigger)} on '{name}' has no " +
                                 $"{nameof(NeoShapeEffect)} to gate — trigger is inert.", this);
                return;
            }

            // Without a raycast target the host never receives the pointer events we listen for.
            if (_graphic != null) _graphic.raycastTarget = true;

            // Always = no gating: leave the effects running their own timeline. Otherwise rest them
            // until a pointer enter/press triggers them.
            if (triggerOn != TriggerOn.Always)
                Deactivate();
        }

        // ------------------------------------------------------------------ pointer events

        /// <inheritdoc/>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!Application.isPlaying) return;
            if (triggerOn == TriggerOn.Hover) Activate();
        }

        /// <inheritdoc/>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (!Application.isPlaying) return;
            if (triggerOn == TriggerOn.Hover) Deactivate();
        }

        /// <inheritdoc/>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Application.isPlaying) return;
            if (triggerOn == TriggerOn.Press) Activate();
        }

        /// <inheritdoc/>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!Application.isPlaying) return;
            if (triggerOn == TriggerOn.Press) Deactivate();
        }

        // ------------------------------------------------------------------ drive

        /// <summary>
        /// Starts every gated effect: <see cref="NeoShapeEffect.Play"/> (restart from 0) in PlayOnce
        /// mode, plain <c>enabled = true</c> in Hold mode.
        /// </summary>
        private void Activate()
        {
            if (_effects == null) return;
            for (int i = 0; i < _effects.Length; i++)
            {
                var fx = _effects[i];
                if (fx == null) continue;
                if (mode == TriggerMode.PlayOnce) fx.Play();
                else fx.enabled = true;
            }
        }

        /// <summary> Stops every gated effect, restoring its baked resting frame. </summary>
        private void Deactivate()
        {
            if (_effects == null) return;
            for (int i = 0; i < _effects.Length; i++)
            {
                var fx = _effects[i];
                if (fx == null) continue;
                fx.Stop();
            }
        }
    }
}
