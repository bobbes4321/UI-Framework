using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One field a <see cref="NeoWidgetPreset"/> can govern on an <see cref="ElementSpec"/>. Every
    /// preset-aware call site — the generator's merge, the exporter's delta, and native authoring's
    /// Apply/Create/Update/Reset — loops over <see cref="PresetFields.All"/> instead of hand-listing the
    /// field set, which is what killed audit finding D1 (five independently maintained copies of this
    /// list, two of which had shipped bugs).
    /// </summary>
    public sealed class PresetField
    {
        public readonly string name;
        public readonly Func<ElementSpec, object> getElement;
        public readonly Action<ElementSpec, object> setElement;
        public readonly Func<NeoWidgetPreset, object> getPreset;
        public readonly Action<NeoWidgetPreset, object> setPreset;
        public readonly Func<object, object, bool> equal;
        public readonly Action<ElementSpec> clearElement;

        public PresetField(string name,
            Func<ElementSpec, object> getElement, Action<ElementSpec, object> setElement,
            Func<NeoWidgetPreset, object> getPreset, Action<NeoWidgetPreset, object> setPreset,
            Action<ElementSpec> clearElement, Func<object, object, bool> equal = null)
        {
            this.name = name;
            this.getElement = getElement;
            this.setElement = setElement;
            this.getPreset = getPreset;
            this.setPreset = setPreset;
            this.clearElement = clearElement;
            this.equal = equal ?? PresetFields.DefaultEquals;
        }

        /// <summary> True when <paramref name="element"/> leaves this field unset (null / empty string /
        /// null array) — the generate-time merge only copies the preset's value into fields the element
        /// doesn't already set. </summary>
        public bool IsUnsetOnElement(ElementSpec element) => PresetFields.IsUnset(getElement(element));
    }

    /// <summary>
    /// The ordered table of every field a <see cref="NeoWidgetPreset"/> governs on an <see cref="ElementSpec"/>
    /// (audit finding D1). Consumers:
    /// <list type="bullet">
    /// <item><see cref="UISpecGenerator"/>'s <c>ResolvePresetAndOverrides</c> — merge: an element field the
    /// author left unset is filled from the preset.</item>
    /// <item><see cref="UISpecExporter"/>'s <c>ApplyPresetDelta</c> — delta: an element field that still
    /// equals the preset's value is cleared, so the export keeps only the override delta.</item>
    /// <item><c>NeoSceneAuthoring</c>'s Apply/Create/Update/Reset preset workflow (native authoring) — the
    /// same clear/capture semantics, so e.g. Apply-Preset correctly lets the preset's <c>icon</c> win
    /// instead of being clobbered by the widget's old one (the audit's shipped bug).</item>
    /// </list>
    /// <para>
    /// Extension seam: a project that wants its own <see cref="NeoWidgetPreset"/> field to participate in
    /// merge/delta/apply/reset registers a descriptor via <see cref="Register"/> — no fork of any of the
    /// call sites above.
    /// </para>
    /// </summary>
    public static class PresetFields
    {
        private static readonly List<PresetField> _fields = BuildBuiltins();
        private static readonly int _builtinCount = _fields.Count;

        /// <summary> Every registered preset-governed field: built-ins first, then any project-registered
        /// additions, in registration order. </summary>
        public static IReadOnlyList<PresetField> All => _fields;

        /// <summary>
        /// Registers an additional preset-governed field — the project extension seam. Appended after the
        /// built-ins. A null/unnamed field, or a name that collides with an already-registered one, is
        /// warned-and-ignored (never silently shadows an existing field).
        /// </summary>
        public static void Register(PresetField field)
        {
            if (field == null || string.IsNullOrEmpty(field.name))
            {
                Debug.LogWarning("[Neo.UI] PresetFields.Register: ignored a null/unnamed field.");
                return;
            }
            foreach (PresetField existing in _fields)
            {
                if (string.Equals(existing.name, field.name, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[Neo.UI] PresetFields.Register: '{field.name}' is already registered — ignored.");
                    return;
                }
            }
            _fields.Add(field);
        }

        /// <summary> Test-only: removes a project-registered field by name so a suite that registers a
        /// custom field leaves the static table clean for sibling suites. Built-ins can't be removed. </summary>
        internal static bool Remove(string name)
        {
            for (int i = _builtinCount; i < _fields.Count; i++)
            {
                if (string.Equals(_fields[i].name, name, StringComparison.Ordinal))
                {
                    _fields.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary> A field's value is "unset" when it's a null reference, or a string that is null/empty
        /// — the same "unset" test <c>ResolvePresetAndOverrides</c> used per-field before this table
        /// existed (nullable floats/arrays box to a null reference when unset, so a single check covers
        /// every field kind). </summary>
        internal static bool IsUnset(object value) => value == null || (value is string s && string.IsNullOrEmpty(s));

        /// <summary> Value equality used to decide whether an exported field still matches its preset (so
        /// it can be dropped from the delta): float fields compare with <see cref="Mathf.Approximately"/>,
        /// float arrays element-wise, everything else via <see cref="object.Equals(object)"/>. </summary>
        internal static bool DefaultEquals(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is float fa && b is float fb) return Mathf.Approximately(fa, fb);
            if (a is float[] arrA && b is float[] arrB)
            {
                if (arrA.Length != arrB.Length) return false;
                for (int i = 0; i < arrA.Length; i++)
                    if (!Mathf.Approximately(arrA[i], arrB[i])) return false;
                return true;
            }
            return a.Equals(b);
        }

        private static List<PresetField> BuildBuiltins() => new List<PresetField>
        {
            StringField("variant", e => e.variant, (e, v) => e.variant = v, p => p.variant, (p, v) => p.variant = v),
            StringField("sizeVariant", e => e.sizeVariant, (e, v) => e.sizeVariant = v, p => p.sizeVariant, (p, v) => p.sizeVariant = v),
            StringField("textStyle", e => e.textStyle, (e, v) => e.textStyle = v, p => p.textStyle, (p, v) => p.textStyle = v),
            // Spec field is "style"; the preset's is "shapeStyle" (its own name for the same theme shape style).
            StringField("style", e => e.style, (e, v) => e.style = v, p => p.shapeStyle, (p, v) => p.shapeStyle = v),
            StringField("background", e => e.background, (e, v) => e.background = v, p => p.background, (p, v) => p.background = v),
            StringField("labelColor", e => e.labelColor, (e, v) => e.labelColor = v, p => p.labelColor, (p, v) => p.labelColor = v),
            StringField("icon", e => e.icon, (e, v) => e.icon = v, p => p.icon, (p, v) => p.icon = v),
            NullableFloatField("radius", e => e.radius, (e, v) => e.radius = v, p => p.RadiusOrNull, (p, v) => p.radius = v ?? -1f),
            NullableFloatField("padding", e => e.padding, (e, v) => e.padding = v, p => p.PaddingOrNull, (p, v) => p.padding = v ?? -1f),
            FloatArrayField("padding4", e => e.padding4, (e, v) => e.padding4 = v, p => p.Padding4OrNull, (p, v) => p.padding4 = v),
            NullableFloatField("spacing", e => e.spacing, (e, v) => e.spacing = v, p => p.SpacingOrNull, (p, v) => p.spacing = v ?? -1f),
            MotionField(),
        };

        // ---------------------------------------------------------------- typed constructor helpers
        // (keep BuildBuiltins above readable; each wraps a typed get/set pair into the object-boxed
        // delegates PresetField stores, plus the shared "clear = set to null" clearElement.)

        private static PresetField StringField(string name,
            Func<ElementSpec, string> get, Action<ElementSpec, string> set,
            Func<NeoWidgetPreset, string> getPreset, Action<NeoWidgetPreset, string> setPreset) =>
            new PresetField(name,
                e => get(e),
                (e, v) => set(e, (string)v),
                p => getPreset(p),
                (p, v) => setPreset(p, (string)v),
                e => set(e, null));

        private static PresetField NullableFloatField(string name,
            Func<ElementSpec, float?> get, Action<ElementSpec, float?> set,
            Func<NeoWidgetPreset, float?> getPreset, Action<NeoWidgetPreset, float?> setPreset) =>
            new PresetField(name,
                e => get(e),
                (e, v) => set(e, (float?)v),
                p => getPreset(p),
                (p, v) => setPreset(p, (float?)v),
                e => set(e, null));

        private static PresetField FloatArrayField(string name,
            Func<ElementSpec, float[]> get, Action<ElementSpec, float[]> set,
            Func<NeoWidgetPreset, float[]> getPreset, Action<NeoWidgetPreset, float[]> setPreset) =>
            new PresetField(name,
                e => get(e),
                (e, v) => set(e, (float[])v),
                p => getPreset(p),
                (p, v) => setPreset(p, (float[])v),
                e => set(e, null));

        /// <summary>
        /// Motion is a custom field, not a plain get/set pair: the preset's <c>motion</c> maps onto the
        /// element's <see cref="ElementAnimationsSpec.loop"/> channel (a play-on-start animation), so
        /// setting it must seed/clone the element's <c>animations</c> object rather than assign a scalar,
        /// and clearing it must drop the whole <c>animations</c> object once empty. Mirrors the logic that
        /// used to live inline in <c>UISpecGenerator.ResolvePresetAndOverrides</c> /
        /// <c>UISpecExporter.ApplyPresetDelta</c>.
        /// </summary>
        private static PresetField MotionField() => new PresetField(
            name: "motion",
            getElement: e => e.animations?.loop,
            setElement: (e, v) =>
            {
                string motion = (string)v;
                if (string.IsNullOrEmpty(motion)) return;
                // ShallowClone (the caller's clone-before-mutate) shares the animations reference with the
                // original spec, so clone it here too before mutating — never stomp the caller's spec.
                e.animations = e.animations == null
                    ? new ElementAnimationsSpec()
                    : new ElementAnimationsSpec
                    {
                        hover = e.animations.hover, press = e.animations.press,
                        selected = e.animations.selected, disabled = e.animations.disabled,
                        loop = e.animations.loop
                    };
                e.animations.loop = motion;
            },
            getPreset: p => p.motion,
            setPreset: (p, v) => p.motion = (string)v,
            clearElement: e =>
            {
                if (e.animations == null) return;
                e.animations.loop = null;
                if (e.animations.IsEmpty) e.animations = null;
            });
    }
}
