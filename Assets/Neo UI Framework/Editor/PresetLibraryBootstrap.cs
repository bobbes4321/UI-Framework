using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using F = Neo.UI.Editor.UIWidgetFactory;

namespace Neo.UI.Editor
{
    /// <summary>
    /// "Tools → Neo UI → Setup → Create or Repair Widget Presets": seeds the package's built-in
    /// <see cref="NeoWidgetPreset"/> library under <see cref="NeoWidgetPresets.PresetsRoot"/> — a generous,
    /// categorized set ("Primary Button", "Section Header", "Card", …) every project gets out of the box.
    /// Each preset references the EXISTING design-system layers (variants/sizes/text styles/shape styles/
    /// tokens) by name, so it adapts when the theme or a bundle changes. Idempotent like the other
    /// bootstraps: a preset asset already at one of our paths is updated in place (never duplicated); the
    /// helper only ever manages assets under <see cref="NeoWidgetPresets.PresetsRoot"/>, so a hand-authored
    /// preset elsewhere is untouched. A consuming project adds its own preset by dropping one asset — the
    /// registry discovers it with no fork.
    /// </summary>
    public static class PresetLibraryBootstrap
    {
        [MenuItem("Tools/Neo UI/Setup/Create or Repair Widget Presets", priority = 102)]
        public static void CreateOrRepairMenu()
        {
            GenerateReport report = CreateOrRepair();
            Debug.Log($"[Neo.UI] Widget presets:\n{report}");
        }

        /// <summary>
        /// Creates or repairs every built-in preset under <see cref="NeoWidgetPresets.PresetsRoot"/>.
        /// Returns a report of created/updated assets. Re-running it only updates in place (idempotent).
        /// </summary>
        public static GenerateReport CreateOrRepair()
        {
            var report = new GenerateReport();
            EnsureFolder(NeoWidgetPresets.PresetsRoot);

            foreach (PresetDef def in Library)
            {
                string folder = $"{NeoWidgetPresets.PresetsRoot}/{def.category}";
                EnsureFolder(folder);
                string path = $"{folder}/{def.presetName.Replace(" ", string.Empty)}.asset";

                NeoWidgetPreset asset = AssetDatabase.LoadAssetAtPath<NeoWidgetPreset>(path);
                bool created = asset == null;
                if (created)
                {
                    // Populate BEFORE CreateAsset so the asset is serialized to disk complete in one shot.
                    // If we created first and ApplyTo'd after, CreateAsset would persist an EMPTY preset and
                    // the in-memory presetName set afterward gets reset when a later asset's import reloads
                    // this instance from its (empty) disk state — only the last-written preset survives, so
                    // discovery sees a library of name-less presets (flaky, batch-import-order dependent).
                    asset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
                    def.ApplyTo(asset);
                    AssetDatabase.CreateAsset(asset, path);
                }
                else
                {
                    def.ApplyTo(asset);
                    EditorUtility.SetDirty(asset);
                }
                // Register the in-memory instance directly so the seeded preset is addressable RIGHT NOW,
                // without waiting on FindAssets discovery. AssetDatabase.FindAssets("t:NeoWidgetPreset")
                // races the deferred import of just-created assets in a single batch/test session (it can
                // miss them until the next import tick), which would make a seed-then-use caller — e.g.
                // ThemeBundles.Apply's SetRadius, or PresetLibraryBootstrapTests — see an incomplete set.
                // Discovery still folds in any OTHER project presets lazily (replace-by-name dedupes).
                NeoWidgetPresets.Register(asset);
                (created ? report.created : report.updated).Add($"preset '{def.presetName}' ({path})");
            }

            AssetDatabase.SaveAssets();
            NeoWidgetPresets.InvalidateDiscovery();
            return report;
        }

        // ------------------------------------------------------------------ the built-in library

        /// <summary>
        /// The shipped presets. Only fields that make sense for a kind are set; the rest stay unset
        /// (null strings / negative floats) so the element's own values or the factory defaults apply.
        /// </summary>
        private static readonly PresetDef[] Library =
        {
            // --- Buttons -------------------------------------------------------------------------
            new PresetDef("Primary Button",   "Button", "button", variant: F.VariantPrimary,   size: F.SizeMedium, textStyle: F.TextStyleButtonLabel),
            new PresetDef("Secondary Button", "Button", "button", variant: F.VariantSecondary, size: F.SizeMedium, textStyle: F.TextStyleButtonLabel),
            new PresetDef("Ghost Button",     "Button", "button", variant: F.VariantGhost,     size: F.SizeMedium, textStyle: F.TextStyleButtonLabel),
            new PresetDef("Danger Button",    "Button", "button", variant: F.VariantDanger,    size: F.SizeMedium, textStyle: F.TextStyleButtonLabel),
            new PresetDef("Primary Button Large", "Button", "button", variant: F.VariantPrimary, size: F.SizeLarge,  textStyle: F.TextStyleButtonLabelLarge),
            new PresetDef("Primary Button Small", "Button", "button", variant: F.VariantPrimary, size: F.SizeSmall,  textStyle: F.TextStyleButtonLabelSmall),
            new PresetDef("Icon Button",      "Button", "button", variant: F.VariantGhost,     size: F.SizeMedium, icon: "settings"),
            new PresetDef("Floating Action",  "Button", "button", variant: F.VariantPrimary,   size: F.SizeLarge,  icon: "plus", radius: 28f),
            new PresetDef("Link Button",      "Button", "button", variant: F.VariantGhost,     size: F.SizeMedium, textStyle: F.TextStyleButtonLabel),

            // --- Text / headers ------------------------------------------------------------------
            new PresetDef("Display Text",   "Text", "text", textStyle: F.TextStyleDisplay),
            new PresetDef("Title Text",     "Text", "text", textStyle: F.TextStyleTitle),
            new PresetDef("Section Header", "Text", "text", textStyle: F.TextStyleHeading, labelColor: F.TokenTextStrong),
            new PresetDef("Body Text",      "Text", "text", textStyle: F.TextStyleBody),
            new PresetDef("Caption",        "Text", "text", textStyle: F.TextStyleCaption, labelColor: F.TokenTextMuted),

            // --- Inputs --------------------------------------------------------------------------
            new PresetDef("Default Dropdown", "Dropdown", "dropdown", variant: F.VariantSecondary, size: F.SizeMedium, textStyle: F.TextStyleBody),
            new PresetDef("Text Input",       "Input",    "input",    shapeStyle: F.StyleControl,  textStyle: F.TextStyleBody),
            new PresetDef("Default Toggle",   "Toggle",   "toggle",   textStyle: F.TextStyleBody),
            new PresetDef("Default Switch",   "Switch",   "switch"),
            new PresetDef("Default Slider",   "Slider",   "slider",   shapeStyle: F.StyleControlPill),

            // --- Progress ------------------------------------------------------------------------
            new PresetDef("Linear Progress", "Progress", "progress", shapeStyle: F.StyleControlPill),
            new PresetDef("Radial Progress", "Progress", "progress", shapeStyle: "radial"),

            // --- Navigation ----------------------------------------------------------------------
            new PresetDef("Default Tab", "Tab", "tab", variant: F.VariantGhost,   textStyle: F.TextStyleButtonLabel),
            new PresetDef("Filled Tab",  "Tab", "tab", variant: F.VariantPrimary, textStyle: F.TextStyleButtonLabel),

            // --- Surfaces & rows -----------------------------------------------------------------
            new PresetDef("Card",        "Surface", "panel",  shapeStyle: F.StyleCard,  padding: 16f, spacing: 12f),
            new PresetDef("Panel",       "Surface", "panel",  shapeStyle: F.StylePanel, padding: 12f, spacing: 8f),
            new PresetDef("List Row",    "Row",     "hstack", spacing: 12f, padding4: new[] { 16f, 12f, 16f, 12f }),
            new PresetDef("Compact Row", "Row",     "hstack", spacing: 8f,  padding4: new[] { 8f, 8f, 8f, 8f }),
            new PresetDef("Large Row",   "Row",     "hstack", spacing: 16f, padding4: new[] { 20f, 16f, 20f, 16f }),
        };

        /// <summary> A built-in preset definition; <see cref="ApplyTo"/> writes it onto an asset. </summary>
        private readonly struct PresetDef
        {
            public readonly string presetName, category, targetKind;
            private readonly string variant, size, textStyle, shapeStyle, background, labelColor, icon;
            private readonly float radius, padding, spacing;
            private readonly float[] padding4;

            public PresetDef(string presetName, string category, string targetKind,
                string variant = null, string size = null, string textStyle = null, string shapeStyle = null,
                string background = null, string labelColor = null, string icon = null,
                float radius = -1f, float padding = -1f, float spacing = -1f, float[] padding4 = null)
            {
                this.presetName = presetName; this.category = category; this.targetKind = targetKind;
                this.variant = variant; this.size = size; this.textStyle = textStyle; this.shapeStyle = shapeStyle;
                this.background = background; this.labelColor = labelColor; this.icon = icon;
                this.radius = radius; this.padding = padding; this.spacing = spacing; this.padding4 = padding4;
            }

            public void ApplyTo(NeoWidgetPreset p)
            {
                p.presetName = presetName;
                p.category = category;
                p.targetKind = targetKind;
                p.variant = variant;
                p.sizeVariant = size;
                p.textStyle = textStyle;
                p.shapeStyle = shapeStyle;
                p.background = background;
                p.labelColor = labelColor;
                p.icon = icon;
                p.radius = radius;
                p.padding = padding;
                p.spacing = spacing;
                p.padding4 = padding4;
                p.motion = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary> Creates a folder (and any missing parents) under Assets if it doesn't already exist. </summary>
        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
