using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A named, reusable bundle of widget styling — the design-system "component" layer (think a Figma
    /// component/variant). A spec element references one BY NAME (<c>ElementSpec.preset</c>); at generate
    /// time the preset's fields resolve as the base and any element-level field overrides them. Change
    /// the preset, regenerate, and every instance updates — the link survives export as a
    /// <see cref="WidgetPresetTag"/> + an override delta.
    /// <para>
    /// Flat, force-text and addressed by <see cref="presetName"/> (agent-first — never a GUID). Every
    /// styling field is OPTIONAL: a null string / null nullable means "this preset does not set it", so a
    /// preset only governs the fields it cares about. The package ships a generous built-in library
    /// (see <c>PresetLibraryBootstrap</c>); a consuming project adds its own by dropping one asset — the
    /// editor registry discovers it with no fork and no C#.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Neo/UI/Widget Preset", fileName = "WidgetPreset", order = 120)]
    public class NeoWidgetPreset : ScriptableObject
    {
        [Tooltip("The id used in specs, e.g. \"Primary Button\". Unique across all presets; matched ordinally.")]
        public string presetName;

        [Tooltip("Grouping for the Composer palette/picker, e.g. Button / Text / Card.")]
        public string category = "Button";

        [Tooltip("Which element kind this preset styles (button, text, toggle, …). Drives which fields apply " +
                 "and the visual thumbnail.")]
        public string targetKind = "button";

        [TextArea(2, 4)]
        [Tooltip("Optional human-readable note shown in the preset inspector/picker.")]
        public string description;

        // ---- References INTO the lower design-system layers (resolved at generate; null = not set) ----

        [Header("Style references (blank = not set by this preset)")]
        [Tooltip("Button variant id (primary / secondary / ghost / danger or a project-authored one).")]
        public string variant;

        [Tooltip("Button size id (sm / md / lg or a project-authored one).")]
        public string sizeVariant;

        [Tooltip("Theme text style name (owns the label's font/size/spacing).")]
        public string textStyle;

        [Tooltip("Theme shape style name (NeoShape surface personality).")]
        public string shapeStyle;

        [Tooltip("Animation preset name for the widget's default motion — seeds the element's on-start/loop " +
                 "animation channel (a play-on-start UIAnimator). The element's own loop animation overrides it.")]
        public string motion;

        [Tooltip("Theme token for the widget background fill.")]
        public string background;

        [Tooltip("Theme token for the label/content color.")]
        public string labelColor;

        [Tooltip("Default icon (Lucide glyph name) for the widget's icon slot.")]
        public string icon;

        // ---- Direct property defaults (the -1 sentinels keep the asset force-text & Unity-serializable) ----

        [Header("Direct defaults (negative = not set)")]
        [Tooltip("Corner radius in px. Negative = not set by this preset.")]
        public float radius = -1f;

        [Tooltip("Uniform container padding in px. Negative = not set.")]
        public float padding = -1f;

        [Tooltip("Per-side container padding [left, top, right, bottom]. Empty = not set; wins over uniform padding.")]
        public float[] padding4;

        [Tooltip("Container child spacing in px. Negative = not set.")]
        public float spacing = -1f;

        /// <summary> Radius as an optional value (null when the <c>-1</c> sentinel means "not set"). </summary>
        public float? RadiusOrNull => radius >= 0f ? (float?)radius : null;

        /// <summary> Uniform padding as an optional value (null when unset). </summary>
        public float? PaddingOrNull => padding >= 0f ? (float?)padding : null;

        /// <summary> Per-side padding, or null when none is authored. </summary>
        public float[] Padding4OrNull => padding4 != null && padding4.Length == 4 ? padding4 : null;

        /// <summary> Child spacing as an optional value (null when unset). </summary>
        public float? SpacingOrNull => spacing >= 0f ? (float?)spacing : null;
    }
}
