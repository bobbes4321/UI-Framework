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
        /// <summary> Built-in Tier-1 loading-spinner / radial-sweep effect id (drives arcStart/arcSweep/ringThickness). </summary>
        public const string ArcSpinner = "arcSpinner";
        /// <summary> Built-in Tier-1 corner-breathing / blob-morph effect id (animates cornerRadius/cornerRadii). </summary>
        public const string CornerMorph = "cornerMorph";
        /// <summary> Built-in Tier-1 focus/selection-ring effect id (pulses border width + outline alpha). </summary>
        public const string BorderPulse = "borderPulse";
        /// <summary> Built-in Tier-1 rainbow hue-cycling effect id (shifts the fill color's hue). </summary>
        public const string HueShift = "hueShift";
        /// <summary> Built-in Tier-1 idle/attention motion effect id (bob/sway/rotate/scale/squash on the RectTransform). </summary>
        public const string TransformJuice = "transformJuice";

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
            // Tier-1 library expansion — each descriptor lives in its own file under Effects/ (the
            // per-descriptor extensibility seam), registered here so the built-in order stays fixed.
            Register(new ArcSpinnerDescriptor());
            Register(new CornerMorphDescriptor());
            Register(new BorderPulseDescriptor());
            Register(new HueShiftDescriptor());
            Register(new TransformJuiceDescriptor());
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

        /// <summary>
        /// Tier-1 shifting gradient: drives a sibling NeoGradient's angle (and/or colors). The cycle's
        /// four endpoint colors (<c>fromColorA</c>/<c>fromColorB</c>/<c>toColorA</c>/<c>toColorB</c>)
        /// are OPTIONAL theme-token-or-hex refs — the same color-ref model as <c>colorOverLife</c>/
        /// variant (<see cref="ParticleEffectRegistry.ParseColorRef"/>). Omitted endpoints keep the
        /// component's vivid defaults, so a bare <c>cycleColors:true</c> bakes a colorful rest frame
        /// rather than a gray wash.
        /// </summary>
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

                // Cycle endpoint colors are OPTIONAL theme-token-or-hex refs (same model as
                // colorOverLife / variant). Omitted → keep the component's (vivid) defaults so a bare
                // `cycleColors:true` never bakes a gray wash. Reuses the shared ParseColorRef.
                string fromA = GetString(parameters, "fromColorA", null);
                string fromB = GetString(parameters, "fromColorB", null);
                string toA = GetString(parameters, "toColorA", null);
                string toB = GetString(parameters, "toColorB", null);
                if (!string.IsNullOrEmpty(fromA)) e.FromColorA = ParticleEffectRegistry.ParseColorRef(fromA);
                if (!string.IsNullOrEmpty(fromB)) e.FromColorB = ParticleEffectRegistry.ParseColorRef(fromB);
                if (!string.IsNullOrEmpty(toA)) e.ToColorA = ParticleEffectRegistry.ParseColorRef(toA);
                if (!string.IsNullOrEmpty(toB)) e.ToColorB = ParticleEffectRegistry.ParseColorRef(toB);

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
                // Deterministic key order; always emitted so the round-trip is a fixed point regardless
                // of whether the colors came from the spec or the component defaults.
                p["fromColorA"] = ParticleEffectRegistry.ColorRefToString(e.FromColorA);
                p["fromColorB"] = ParticleEffectRegistry.ColorRefToString(e.FromColorB);
                p["toColorA"] = ParticleEffectRegistry.ColorRefToString(e.ToColorA);
                p["toColorB"] = ParticleEffectRegistry.ColorRefToString(e.ToColorB);
                parameters = p;
                return true;
            }
        }

        /// <summary>
        /// Tier-2 material variant: attaches an <see cref="NeoShapeVariant"/> resolving a
        /// <see cref="ShapeEffectDefinition"/> by id. BatchSafe=false — the variant is a deliberate,
        /// named batch split (shared per variant, not per instance).
        ///
        /// <para>Optionally animates a NAMED material float over the shared timeline: when the bag
        /// carries <c>animate</c> (the property name, e.g. <c>_DissolveAmount</c>) plus
        /// <c>from</c>/<c>to</c> and the usual <c>duration</c>/<c>loop</c>/<c>pingPong</c>/<c>ease</c>/
        /// <c>restingPhase</c> keys, it also adds a <see cref="NeoMaterialFloatCycle"/> so the variant
        /// comes alive at runtime (the static material default stays the baked rest frame). Absent
        /// <c>animate</c> ⇒ behaves exactly as before (a static variant) — fully backward compatible.</para>
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

                // Optional material-float animation: only when an `animate` property name is supplied.
                // Reuses the shared NeoShapeEffect timeline parse (ApplyBase) so it stays consistent
                // with the Tier-1 descriptors.
                string animate = GetString(parameters, "animate", null);
                if (!string.IsNullOrEmpty(animate))
                {
                    var cycle = host.GetComponent<NeoMaterialFloatCycle>();
                    if (cycle == null) cycle = host.AddComponent<NeoMaterialFloatCycle>();
                    ApplyBase(cycle, parameters);
                    cycle.PropertyName = animate;
                    cycle.FromValue = GetFloat(parameters, "from", cycle.FromValue);
                    cycle.ToValue = GetFloat(parameters, "to", cycle.ToValue);
                    cycle.EvaluateRest();
                }
            }

            public bool TryExport(GameObject host, out IDictionary<string, object> parameters)
            {
                parameters = null;
                var variant = host.GetComponent<NeoShapeVariant>();
                if (variant == null) return false;
                var p = new Dictionary<string, object> { ["definition"] = variant.EffectId ?? "" };

                // Emit the animation keys (deterministic order) ONLY when a cycle is present, so a
                // static variant round-trips to exactly the bare { definition } it generated from.
                var cycle = host.GetComponent<NeoMaterialFloatCycle>();
                if (cycle != null)
                {
                    p["animate"] = cycle.PropertyName ?? "";
                    p["from"] = (double)cycle.FromValue;
                    p["to"] = (double)cycle.ToValue;
                    WriteBase(cycle, p); // shared timeline keys (duration/loop/pingPong/restingPhase/ease)
                }

                parameters = p;
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

        /// <summary>
        /// Applies the shared <see cref="NeoShapeEffect"/> timeline params (+ live signal bindings) from
        /// the bag. <c>internal</c> so a descriptor defined in its OWN file (the extensibility seam) can
        /// reuse the exact shared parse the built-ins use.
        /// </summary>
        internal static void ApplyBase(NeoShapeEffect e, IDictionary<string, object> p)
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
            ApplyBindings(e.gameObject, p);
            ApplyTrigger(e.gameObject, p);
        }

        /// <summary>
        /// Writes the shared timeline params (+ live signal bindings) deterministically (stable key
        /// order). <c>internal</c> so an out-of-file descriptor (the seam) reuses the exact shared export.
        /// </summary>
        internal static void WriteBase(NeoShapeEffect e, IDictionary<string, object> p)
        {
            p["duration"] = (double)e.duration;
            p["loop"] = e.loop;
            p["pingPong"] = e.pingPongLoop;
            p["restingPhase"] = (double)e.restingPhase;
            if (e.easingMode == EaseMode.Ease) p["ease"] = e.easing.ToString();
            WriteBindings(e.gameObject, p);
            WriteTrigger(e.gameObject, p);
        }

        // ----------------------------------------------------------------- pointer trigger

        /// <summary>
        /// Parses the optional <c>trigger</c> (<c>"hover"</c>/<c>"press"</c>/<c>"always"</c>) and
        /// <c>triggerMode</c> (<c>"hold"</c>/<c>"playOnce"</c>) shared by every effect, attaching a
        /// <see cref="NeoEffectTrigger"/> so the effect runs only on hover or while pressed. Absent ⇒
        /// the effect runs on its own timeline as before.
        /// </summary>
        private static void ApplyTrigger(GameObject host, IDictionary<string, object> p)
        {
            string trig = GetString(p, "trigger", null);
            if (string.IsNullOrEmpty(trig)) return;

            var t = host.GetComponent<NeoEffectTrigger>();
            if (t == null) t = host.AddComponent<NeoEffectTrigger>();

            switch (trig.ToLowerInvariant())
            {
                case "press": t.Trigger = NeoEffectTrigger.TriggerOn.Press; break;
                case "always": t.Trigger = NeoEffectTrigger.TriggerOn.Always; break;
                default: t.Trigger = NeoEffectTrigger.TriggerOn.Hover; break;
            }

            string mode = GetString(p, "triggerMode", null)?.ToLowerInvariant();
            t.Mode = (mode == "playonce" || mode == "play")
                ? NeoEffectTrigger.TriggerMode.PlayOnce
                : NeoEffectTrigger.TriggerMode.Hold;
        }

        /// <summary> Exports a host's <see cref="NeoEffectTrigger"/> (omitted when Always = no gating). </summary>
        private static void WriteTrigger(GameObject host, IDictionary<string, object> p)
        {
            var t = host.GetComponent<NeoEffectTrigger>();
            if (t == null || t.Trigger == NeoEffectTrigger.TriggerOn.Always) return;
            p["trigger"] = t.Trigger == NeoEffectTrigger.TriggerOn.Press ? "press" : "hover";
            p["triggerMode"] = t.Mode == NeoEffectTrigger.TriggerMode.PlayOnce ? "playOnce" : "hold";
        }

        // ----------------------------------------------------------------- live signal bindings

        /// <summary>
        /// Parses the optional <c>bindings</c> array (live signal→param links) shared by every effect
        /// and configures a <see cref="NeoSignalParamBinding"/> on the host. Each entry is
        /// <c>{ "signal":"Category/Name", "param":"softnessMax", "min":1, "max":20 }</c>; the special
        /// param <c>"enabled"</c> toggles the whole effect from a bool signal (optional <c>"invert"</c>).
        /// Absent/empty ⇒ no component added, so effects without live control are untouched.
        /// </summary>
        private static void ApplyBindings(GameObject host, IDictionary<string, object> p)
        {
            if (p == null || !p.TryGetValue("bindings", out object raw) || !(raw is List<object> list) || list.Count == 0)
                return;

            var comp = host.GetComponent<NeoSignalParamBinding>();
            if (comp == null) comp = host.AddComponent<NeoSignalParamBinding>();
            comp.Bindings.Clear();

            foreach (object o in list)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                string signal = GetString(d, "signal", null);
                if (string.IsNullOrEmpty(signal)) continue;
                SplitSignal(signal, out string category, out string name);
                comp.Bindings.Add(new NeoSignalParamBinding.ParamBinding
                {
                    category = category,
                    signalName = name,
                    param = GetString(d, "param", null),
                    min = GetFloat(d, "min", 0f),
                    max = GetFloat(d, "max", 0f),
                    invert = GetBool(d, "invert", false),
                });
            }
        }

        /// <summary>
        /// Exports a host's <see cref="NeoSignalParamBinding"/> back to the deterministic <c>bindings</c>
        /// array (stable key order) so the live-control links round-trip. No component ⇒ nothing written
        /// (specs without bindings stay byte-identical).
        /// </summary>
        private static void WriteBindings(GameObject host, IDictionary<string, object> p)
        {
            var comp = host.GetComponent<NeoSignalParamBinding>();
            if (comp == null || comp.Bindings == null || comp.Bindings.Count == 0) return;

            var arr = new List<object>(comp.Bindings.Count);
            foreach (NeoSignalParamBinding.ParamBinding b in comp.Bindings)
            {
                var d = new Dictionary<string, object> { ["signal"] = $"{b.category}/{b.signalName}" };
                if (!string.IsNullOrEmpty(b.param)) d["param"] = b.param;
                d["min"] = (double)b.min;
                d["max"] = (double)b.max;
                if (b.invert) d["invert"] = true;
                arr.Add(d);
            }
            p["bindings"] = arr;
        }

        /// <summary> Splits a "Category/Name" signal address on the first slash (name may be empty). </summary>
        private static void SplitSignal(string signal, out string category, out string name)
        {
            int slash = signal.IndexOf('/');
            if (slash < 0) { category = signal; name = string.Empty; return; }
            category = signal.Substring(0, slash);
            name = signal.Substring(slash + 1);
        }
    }
}
