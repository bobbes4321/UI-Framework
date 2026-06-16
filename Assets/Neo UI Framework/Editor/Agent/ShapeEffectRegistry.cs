using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One shape-effect descriptor: it knows how to ATTACH its effect component to a host
    /// GameObject from a flat param bag (<see cref="Apply"/>) and how to READ that component back
    /// into a deterministic param bag (<see cref="TryExport"/>). The param bag is the project's
    /// existing JSON object model — a <see cref="Dictionary{String,Object}"/> with numbers stored as
    /// <see cref="double"/> and nested objects as the same dictionary type — exactly like
    /// <see cref="GradientSpec"/>; descriptors never invent a parallel JSON layer.
    ///
    /// <para>This is the keystone seam for "fancy shader effects on UI": the spec carries an effect
    /// as an OPEN bag (<c>"effect": { "id": "...", "params": { ... } }</c>) and this descriptor owns
    /// the parse/bake/export, so <see cref="UISpec"/>/<see cref="UISpecGenerator"/>/
    /// <see cref="UISpecExporter"/> never grow a per-effect switch. A consuming project adds an effect
    /// by registering one descriptor — zero core edits.</para>
    /// </summary>
    public interface IShapeEffectDescriptor
    {
        /// <summary> Stable, agent-addressable effect id (never a GUID), e.g. "glowPulse". </summary>
        string Id { get; }

        /// <summary>
        /// False when attaching this effect breaks the project-wide single NeoShape material batch
        /// (Tier-2 variants). True for Tier-1 drivers that only write vertex-channel shape params.
        /// Surfaced so tooling/lint can flag the batch split honestly.
        /// </summary>
        bool BatchSafe { get; }

        /// <summary>
        /// Adds the effect component to <paramref name="host"/> and sets its fields from
        /// <paramref name="parameters"/> (may be null → defaults). Tier-1 descriptors call
        /// <see cref="NeoShapeEffect.EvaluateRest"/> so the baked prefab matches the resting frame
        /// (WYSIWYG).
        /// </summary>
        void Apply(GameObject host, IDictionary<string, object> parameters);

        /// <summary>
        /// Detects this descriptor's effect component on <paramref name="host"/> and, when present,
        /// writes its params deterministically (stable key order) into <paramref name="parameters"/>.
        /// Returns false when the component is absent (so the registry tries the next descriptor).
        /// </summary>
        bool TryExport(GameObject host, out IDictionary<string, object> parameters);
    }

    /// <summary>
    /// Pattern-R registry of shape-effect descriptors (mirrors <see cref="LayoutConstraints"/>): the
    /// documented extensibility seam for spec-driven shape effects. Built-ins register in the static
    /// ctor in a FIXED order; the exporter walks <see cref="All"/> and the first descriptor whose
    /// <see cref="IShapeEffectDescriptor.TryExport"/> succeeds wins, so detection stays deterministic.
    /// <see cref="All"/> / <see cref="Get"/> / <see cref="Register"/> (replace-by-Id) /
    /// <see cref="ResetForTests"/>.
    /// </summary>
    public static class ShapeEffectRegistry
    {
        /// <summary> Built-in Tier-1 glow/halo effect id. </summary>
        public const string GlowPulse = "glowPulse";
        /// <summary> Built-in Tier-1 moving-highlight effect id. </summary>
        public const string SheenSweep = "sheenSweep";
        /// <summary> Built-in Tier-1 shifting-gradient effect id. </summary>
        public const string GradientCycle = "gradientCycle";
        /// <summary> Built-in Tier-2 material-variant effect id (resolves a ShapeEffectDefinition). </summary>
        public const string Variant = "variant";

        private static readonly List<IShapeEffectDescriptor> _all = new List<IShapeEffectDescriptor>();

        static ShapeEffectRegistry()
        {
            RegisterBuiltins();
        }

        private static void RegisterBuiltins()
        {
            // Order matters for deterministic exporter detection: the Tier-2 variant is detected by a
            // distinct component (NeoShapeVariant) so order vs the Tier-1 drivers is incidental, but we
            // keep a fixed, documented order regardless.
            Register(new GlowPulseDescriptor());
            Register(new SheenSweepDescriptor());
            Register(new GradientCycleDescriptor());
            Register(new VariantDescriptor());
        }

        /// <summary> Every registered descriptor (built-ins first, in registration order). </summary>
        public static IReadOnlyList<IShapeEffectDescriptor> All => _all;

        /// <summary> Finds the descriptor with the given id; null + warning when missing (no silent failure). </summary>
        public static IShapeEffectDescriptor Get(string id)
        {
            if (!string.IsNullOrEmpty(id))
                foreach (IShapeEffectDescriptor d in _all)
                    if (d != null && d.Id == id) return d;
            Debug.LogWarning($"ShapeEffectRegistry.Get: no shape effect '{id}' registered — the effect will be skipped. Register one in ShapeEffectRegistry, or check the spec.");
            return null;
        }

        /// <summary> Registers a descriptor, replacing any existing one with the same Id. </summary>
        public static void Register(IShapeEffectDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.Id))
            {
                Debug.LogWarning("ShapeEffectRegistry.Register ignored a descriptor with a null/empty Id.");
                return;
            }
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == descriptor.Id) { _all[i] = descriptor; return; }
            _all.Add(descriptor);
        }

        /// <summary> Test/seam hook: clear and re-seed the built-ins (static state survives a test run). </summary>
        internal static void ResetForTests()
        {
            _all.Clear();
            RegisterBuiltins();
        }

        // ----------------------------------------------------------------- param-bag helpers

        internal static float GetFloat(IDictionary<string, object> p, string key, float fallback) =>
            p != null && p.TryGetValue(key, out object v) && v is double d ? (float)d : fallback;

        internal static bool GetBool(IDictionary<string, object> p, string key, bool fallback) =>
            p != null && p.TryGetValue(key, out object v) && v is bool b ? b : fallback;

        internal static string GetString(IDictionary<string, object> p, string key, string fallback) =>
            p != null && p.TryGetValue(key, out object v) && v != null ? v.ToString() : fallback;

        // ----------------------------------------------------------------- built-in descriptors

        /// <summary> Tier-1 breathing glow: pulses edge softness (+ optional fill alpha). </summary>
        private sealed class GlowPulseDescriptor : IShapeEffectDescriptor
        {
            public string Id => GlowPulse;
            public bool BatchSafe => true;

            public void Apply(GameObject host, IDictionary<string, object> parameters)
            {
                var e = host.GetComponent<NeoGlowPulse>();
                if (e == null) e = host.AddComponent<NeoGlowPulse>();
                ApplyBase(e, parameters);
                e.SoftnessMin = GetFloat(parameters, "softnessMin", e.SoftnessMin);
                e.SoftnessMax = GetFloat(parameters, "softnessMax", e.SoftnessMax);
                e.PulseAlpha = GetBool(parameters, "pulseAlpha", e.PulseAlpha);
                e.AlphaMin = GetFloat(parameters, "alphaMin", e.AlphaMin);
                e.AlphaMax = GetFloat(parameters, "alphaMax", e.AlphaMax);
                e.EvaluateRest();
            }

            public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
            {
                parameters = null;
                var e = host.GetComponent<NeoGlowPulse>();
                if (e == null) return false;
                var p = new Dictionary<string, object>();
                WriteBase(e, p);
                p["softnessMin"] = (double)e.SoftnessMin;
                p["softnessMax"] = (double)e.SoftnessMax;
                if (e.PulseAlpha) p["pulseAlpha"] = true;
                p["alphaMin"] = (double)e.AlphaMin;
                p["alphaMax"] = (double)e.AlphaMax;
                parameters = p;
                return true;
            }
        }

        /// <summary> Tier-1 sheen: sweeps the gradient angle from→to. </summary>
        private sealed class SheenSweepDescriptor : IShapeEffectDescriptor
        {
            public string Id => SheenSweep;
            public bool BatchSafe => true;

            public void Apply(GameObject host, IDictionary<string, object> parameters)
            {
                var e = host.GetComponent<NeoSheenSweep>();
                if (e == null) e = host.AddComponent<NeoSheenSweep>();
                ApplyBase(e, parameters);
                e.FromAngle = GetFloat(parameters, "fromAngle", e.FromAngle);
                e.ToAngle = GetFloat(parameters, "toAngle", e.ToAngle);
                e.EvaluateRest();
            }

            public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
            {
                parameters = null;
                var e = host.GetComponent<NeoSheenSweep>();
                if (e == null) return false;
                var p = new Dictionary<string, object>();
                WriteBase(e, p);
                p["fromAngle"] = (double)e.FromAngle;
                p["toAngle"] = (double)e.ToAngle;
                parameters = p;
                return true;
            }
        }

        /// <summary> Tier-1 shifting gradient: drives a sibling NeoGradient's angle (and/or colors). </summary>
        private sealed class GradientCycleDescriptor : IShapeEffectDescriptor
        {
            public string Id => GradientCycle;
            public bool BatchSafe => true;

            public void Apply(GameObject host, IDictionary<string, object> parameters)
            {
                // NeoGradientCycle [RequireComponent(NeoGradient)] — ensure the sibling exists first so
                // AddComponent doesn't fail and the cycle has something to drive.
                if (host.GetComponent<NeoGradient>() == null) host.AddComponent<NeoGradient>();
                var e = host.GetComponent<NeoGradientCycle>();
                if (e == null) e = host.AddComponent<NeoGradientCycle>();
                ApplyBase(e, parameters);
                e.CycleAngle = GetBool(parameters, "cycleAngle", e.CycleAngle);
                e.CycleColors = GetBool(parameters, "cycleColors", e.CycleColors);
                e.EvaluateRest();
            }

            public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
            {
                parameters = null;
                var e = host.GetComponent<NeoGradientCycle>();
                if (e == null) return false;
                var p = new Dictionary<string, object>();
                WriteBase(e, p);
                p["cycleAngle"] = e.CycleAngle;
                p["cycleColors"] = e.CycleColors;
                parameters = p;
                return true;
            }
        }

        /// <summary>
        /// Tier-2 material variant: attaches an <see cref="NeoShapeVariant"/> resolving a
        /// <see cref="ShapeEffectDefinition"/> by id. BatchSafe=false — the variant is a deliberate,
        /// named batch split (shared per variant, not per instance).
        /// </summary>
        private sealed class VariantDescriptor : IShapeEffectDescriptor
        {
            public string Id => Variant;
            public bool BatchSafe => false;

            public void Apply(GameObject host, IDictionary<string, object> parameters)
            {
                string defId = GetString(parameters, "definition", null) ?? GetString(parameters, "id", null);
                ShapeEffectDefinition definition = ResolveDefinition(defId);
                if (definition == null)
                {
                    Debug.LogWarning($"ShapeEffectRegistry: variant effect could not resolve a ShapeEffectDefinition with id '{defId}' — skipped. Check the spec or create the definition asset.");
                    return;
                }
                var variant = host.GetComponent<NeoShapeVariant>();
                if (variant == null) variant = host.AddComponent<NeoShapeVariant>();
                variant.Definition = definition; // setter applies the shared material + defaults
            }

            public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
            {
                parameters = null;
                var variant = host.GetComponent<NeoShapeVariant>();
                if (variant == null) return false;
                parameters = new Dictionary<string, object> { ["definition"] = variant.EffectId ?? "" };
                return true;
            }

            private static ShapeEffectDefinition ResolveDefinition(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                // Agent-first resolve by stable id (never a GUID). Scoped to the asset TYPE — this is a
                // ScriptableObject lookup, NOT the forbidden "scan all of Assets" prefab fallback.
                foreach (string guid in AssetDatabase.FindAssets("t:ShapeEffectDefinition"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var def = AssetDatabase.LoadAssetAtPath<ShapeEffectDefinition>(path);
                    if (def != null && def.Id == id) return def;
                }
                return null;
            }
        }

        // ----------------------------------------------------------------- Tier-1 base round-trip

        /// <summary> Applies the shared <see cref="NeoShapeEffect"/> timeline params from the bag. </summary>
        private static void ApplyBase(NeoShapeEffect e, IDictionary<string, object> p)
        {
            e.duration = GetFloat(p, "duration", e.duration);
            e.loop = GetBool(p, "loop", e.loop);
            e.pingPongLoop = GetBool(p, "pingPong", e.pingPongLoop);
            e.restingPhase = GetFloat(p, "restingPhase", e.restingPhase);
            string ease = GetString(p, "ease", null);
            if (!string.IsNullOrEmpty(ease) && Enum.TryParse(ease, out Ease parsed))
            {
                e.easing = parsed;
                e.easingMode = EaseMode.Ease;
            }
        }

        /// <summary> Writes the shared timeline params deterministically (stable key order). </summary>
        private static void WriteBase(NeoShapeEffect e, IDictionary<string, object> p)
        {
            p["duration"] = (double)e.duration;
            p["loop"] = e.loop;
            p["pingPong"] = e.pingPongLoop;
            p["restingPhase"] = (double)e.restingPhase;
            if (e.easingMode == EaseMode.Ease) p["ease"] = e.easing.ToString();
        }
    }
}
