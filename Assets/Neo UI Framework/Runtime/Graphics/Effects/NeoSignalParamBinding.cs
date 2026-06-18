using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Drives a sibling <see cref="NeoShapeEffect"/>'s parameters LIVE from domain signals — the seam
    /// that turns a baked, self-running effect into one a slider/toggle/dropdown can control at runtime.
    /// Each binding listens on a <c>Category/Name</c> signal stream (the same streams a spec
    /// <c>toggle</c>/<c>slider</c>/<c>dropdown</c> publishes its typed value to via its <c>signal</c>
    /// field) and routes the value into the effect:
    /// <list type="bullet">
    ///   <item>a <b>float</b> binding remaps the incoming value and calls
    ///   <see cref="NeoShapeEffect.TrySetLiveParam"/> with a named param (e.g. <c>softnessMax</c>);</item>
    ///   <item>the special param <see cref="EnabledParam"/> consumes a <b>bool</b> signal and toggles
    ///   the whole effect on/off.</item>
    /// </list>
    ///
    /// <para>Agent-first &amp; signals-over-UnityEvents by construction: nothing here is wired through a
    /// serialized callback — the link is a string-addressed signal stream and a string param name, both
    /// of which round-trip through the spec (<c>effect.params.bindings</c>). The set of drivable params
    /// is OPEN: each effect owns its own names by overriding <see cref="NeoShapeEffect.TrySetLiveParam"/>,
    /// so a consuming project's new effect is controllable the moment it implements that one method —
    /// this component needs no per-effect knowledge and no central switch.</para>
    ///
    /// <para>Signals are a <b>runtime</b> concern, so subscriptions only happen in play mode; in the
    /// editor the effect holds its baked resting frame (WYSIWYG) and this component is inert. The
    /// effect's own <see cref="NeoShapeEffect.Update"/> re-samples every frame, so a param change lands
    /// on the next frame with no explicit refresh.</para>
    /// </summary>
    [AddComponentMenu("Neo/UI/Effects/Signal Param Binding")]
    [RequireComponent(typeof(NeoShapeEffect))]
    public sealed class NeoSignalParamBinding : MonoBehaviour
    {
        /// <summary>
        /// Special <see cref="ParamBinding.param"/> value: instead of a float param, toggle the whole
        /// effect component on/off from a bool signal.
        /// </summary>
        public const string EnabledParam = "enabled";

        /// <summary> One signal→param link. </summary>
        [Serializable]
        public struct ParamBinding
        {
            [Tooltip("Signal category to listen on (e.g. \"Effects\").")]
            public string category;

            [Tooltip("Signal name to listen on (e.g. \"Softness\").")]
            public string signalName;

            [Tooltip("Effect param to drive. The special value \"enabled\" toggles the whole effect " +
                     "on/off from a bool signal; any other name routes a float through " +
                     "NeoShapeEffect.TrySetLiveParam.")]
            public string param;

            [Tooltip("Output value when the incoming normalized signal is 0. When min==max the raw " +
                     "incoming value is passed through unremapped.")]
            public float min;

            [Tooltip("Output value when the incoming normalized signal is 1.")]
            public float max;

            [Tooltip("Invert the bool for an \"enabled\" binding (true ⇒ a false signal enables).")]
            public bool invert;
        }

        [Tooltip("The signal→effect-param links applied at runtime.")]
        [SerializeField] private List<ParamBinding> bindings = new List<ParamBinding>();

        /// <summary> The serialized binding list (mutated by the generator descriptor at bake time). </summary>
        public List<ParamBinding> Bindings => bindings;

        [NonSerialized] private NeoShapeEffect _effect;
        [NonSerialized] private readonly List<Action> _unsubscribe = new List<Action>();

        private void OnEnable()
        {
            // Signals are a runtime concern; the editor keeps the baked WYSIWYG frame. (Mirrors the
            // play-mode gate in NeoShapeEffect.Update.)
            if (!Application.isPlaying) return;

            _effect = GetComponent<NeoShapeEffect>();
            if (_effect == null) return;

            foreach (ParamBinding b in bindings)
            {
                if (string.IsNullOrEmpty(b.category) || string.IsNullOrEmpty(b.signalName)) continue;

                if (b.param == EnabledParam)
                    SubscribeToggle(b);
                else
                    SubscribeFloat(b);
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _unsubscribe.Count; i++) _unsubscribe[i]();
            _unsubscribe.Clear();
        }

        private void SubscribeFloat(ParamBinding b)
        {
            string cat = b.category, name = b.signalName, param = b.param;
            float min = b.min, max = b.max;
            bool passthrough = Mathf.Approximately(min, max);

            Action<float> handler = v =>
            {
                if (_effect == null) return;
                float mapped = passthrough ? v : Mathf.LerpUnclamped(min, max, Mathf.Clamp01(v));
                _effect.TrySetLiveParam(param, mapped);
            };
            Signals.On(cat, name, handler);
            _unsubscribe.Add(() => Signals.Off(cat, name, handler));
        }

        private void SubscribeToggle(ParamBinding b)
        {
            string cat = b.category, name = b.signalName;
            bool invert = b.invert;

            Action<bool> handler = on =>
            {
                if (_effect != null) _effect.enabled = invert ? !on : on;
            };
            Signals.On(cat, name, handler);
            _unsubscribe.Add(() => Signals.Off(cat, name, handler));
        }
    }
}
