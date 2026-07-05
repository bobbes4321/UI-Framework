using System;
using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Option providers for spec-authoring dropdowns (the widget preset inspector, and any future
    /// authoring surface). Lists are built when a dropdown opens (never per frame) — every method
    /// here is a one-shot gather, sourced from the project theme / factory / registered attributes.
    /// </summary>
    public static class NeoWidgetOptions
    {
        // ---------------------------------------------------------------------------------------
        // Widget-attribute option sets (Pattern R — extensibility-seam-widget-attributes-plan.md).
        // Each set seeds the package built-ins, then a consuming project appends its own from an
        // [InitializeOnLoad] static ctor via RegisterVariant/RegisterSize/RegisterAlign/RegisterShape
        // — no package edit. The pickers read `.All` (snapshot array, so callers can't mutate the
        // seed). De-duped, case-insensitively (the shared keyed-registry base's `comparison` ctor
        // arg), preserving order; a re-register is a no-op (replaces the entry in place with itself).
        // NOTE: a registered shape NAME round-trips through the spec, but a brand-new shape
        // *primitive* (mesh + shader) needs the NeoShape graphics seam — out of scope here.
        // Wave 4 Task 4.2: migrated onto NeoKeyedRegistry<string> (Pattern R's shared base).
        // ---------------------------------------------------------------------------------------

        private static readonly NeoKeyedRegistry<string> _buttonVariants = new NeoKeyedRegistry<string>(
            s => s, StringComparison.OrdinalIgnoreCase,
            builtins: () => new[] { "primary", "secondary", "ghost", "danger" },
            registryName: "NeoWidgetOptions.ButtonVariants");
        private static readonly NeoKeyedRegistry<string> _buttonSizes = new NeoKeyedRegistry<string>(
            s => s, StringComparison.OrdinalIgnoreCase,
            builtins: () => new[] { "sm", "md", "lg" },
            registryName: "NeoWidgetOptions.ButtonSizes");
        private static readonly NeoKeyedRegistry<string> _aligns = new NeoKeyedRegistry<string>(
            s => s, StringComparison.OrdinalIgnoreCase,
            builtins: () => new[] { "left", "center", "right" },
            registryName: "NeoWidgetOptions.Aligns");
        private static readonly NeoKeyedRegistry<string> _shapeNames = new NeoKeyedRegistry<string>(
            s => s, StringComparison.OrdinalIgnoreCase,
            builtins: () => new[] { "roundedRect", "circle", "pill", "checkmark", "chevron", "cross", "ring", "arc" },
            registryName: "NeoWidgetOptions.ShapeNames");

        // Variants/sizes merge the code seed + code registrations + the project-authored attributes on
        // NeoUISettings — so a ButtonVariantAsset/ButtonSizeAsset shows up in the picker with NO separate
        // RegisterVariant/RegisterSize call (single source of truth: settings). De-duped case-insensitively,
        // seed order first. Fetched only when a dropdown opens, so the per-open merge is cheap. The
        // settings-authored names are folded into a throwaway copy (never persisted into the registry
        // itself) so removing a project's ButtonVariantAsset correctly drops it again on the next read.
        public static string[] ButtonVariants => WithSettingsNames(_buttonVariants.All, SettingsVariantNames());
        public static string[] ButtonSizes => WithSettingsNames(_buttonSizes.All, SettingsSizeNames());
        public static string[] Aligns => ToArray(_aligns.All);
        public static string[] ShapeNames => ToArray(_shapeNames.All);

        private static string[] WithSettingsNames(IReadOnlyList<string> seed, IEnumerable<string> extra)
        {
            var list = new List<string>(seed);
            foreach (string e in extra) RegisterInto(list, e);
            return list.ToArray();
        }

        private static string[] ToArray(IReadOnlyList<string> source)
        {
            var arr = new string[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }

        private static IEnumerable<string> SettingsVariantNames()
        {
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null || settings.buttonVariants == null) yield break;
            foreach (ButtonVariantAsset v in settings.buttonVariants)
                if (v != null && !string.IsNullOrEmpty(v.name)) yield return v.name;
        }

        private static IEnumerable<string> SettingsSizeNames()
        {
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null || settings.buttonSizes == null) yield break;
            foreach (ButtonSizeAsset s in settings.buttonSizes)
                if (s != null && !string.IsNullOrEmpty(s.name)) yield return s.name;
        }

        /// <summary> All widget-preset names (sorted), for the inspector preset picker. </summary>
        public static List<string> PresetNames()
        {
            var list = new List<string>(NeoWidgetPresets.Names);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        /// <summary> Preset names whose target kind matches (sorted), for a kind-scoped picker. </summary>
        public static List<string> PresetsForKind(string kind)
        {
            var list = new List<string>();
            foreach (NeoWidgetPreset p in NeoWidgetPresets.ForKind(kind))
                if (!string.IsNullOrEmpty(p.presetName)) list.Add(p.presetName);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        /// <summary> Registers a button variant id for the picker (append if new,
        /// case-insensitive no-op if already present). Author the variant's colors as a
        /// <see cref="ButtonVariantAsset"/> on <c>NeoUISettings.buttonVariants</c>. </summary>
        public static void RegisterVariant(string variant) => _buttonVariants.Register(variant);

        /// <summary> Registers a button size id for the picker. Author its
        /// dimensions as a <see cref="ButtonSizeAsset"/> on <c>NeoUISettings.buttonSizes</c>. </summary>
        public static void RegisterSize(string size) => _buttonSizes.Register(size);

        /// <summary> Registers a text-align id for the picker. </summary>
        public static void RegisterAlign(string align) => _aligns.Register(align);

        /// <summary> Registers a shape NAME for the picker. A new primitive also needs the
        /// NeoShape graphics seam (mesh + shader) — out of scope for this seam. </summary>
        public static void RegisterShape(string shape) => _shapeNames.Register(shape);

        // Transient, non-persisting dedupe for merging the settings-authored names into a throwaway
        // copy of a registry's snapshot (see WithSettingsNames) — never mutates the registry itself.
        private static void RegisterInto(List<string> set, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (string existing in set)
                if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)) return;
            set.Add(value);
        }

        /// <summary> Test-only: re-seed every option set back to the package built-ins so a test
        /// that calls Register() can't leak into the next test (static state survives the run). </summary>
        internal static void ResetAttributeRegistriesForTests()
        {
            _buttonVariants.ResetForTests();
            _buttonSizes.ResetForTests();
            _aligns.ResetForTests();
            _shapeNames.ResetForTests();
        }

        /// <summary> Alias matching the Wave-4 registry convention (<c>internal ResetForTests()</c>);
        /// forwards to <see cref="ResetAttributeRegistriesForTests"/> so existing test callers of the
        /// original name are unaffected. </summary>
        internal static void ResetForTests() => ResetAttributeRegistriesForTests();

        /// <summary> The on-scale spacing/padding snap values the design lint blesses. Single source
        /// of truth: reads <see cref="NeoUISettings.spacingScale"/> so every spacing-snap consumer and
        /// the design lint always agree (a project overrides the scale once, on settings). Falls back
        /// to the package default when there is no settings asset. </summary>
        public static float[] SpacingScale
        {
            get
            {
                NeoUISettings settings = NeoUISettings.instance;
                if (settings != null && settings.spacingScale != null && settings.spacingScale.Length > 0)
                    return settings.spacingScale;
                return DefaultSpacingScale;
            }
        }

        private static readonly float[] DefaultSpacingScale = { 0, 4, 8, 12, 16, 24, 32, 48, 64 };

        private static Theme ProjectTheme
        {
            get
            {
                NeoUISettings settings = NeoUISettings.instance;
                return settings != null ? settings.theme : null;
            }
        }

        public static List<string> Tokens(UISpec spec)
        {
            var set = new SortedSet<string>();
            if (spec?.theme?.tokens != null)
                foreach (string token in spec.theme.tokens.Keys) set.Add(token);
            Theme theme = ProjectTheme;
            if (theme != null)
                foreach (string token in theme.GetTokenNames()) set.Add(token);
            return new List<string>(set);
        }

        public static List<string> ShapeStyles()
        {
            var list = new List<string>();
            Theme theme = ProjectTheme;
            if (theme != null) list.AddRange(theme.GetShapeStyleNames());
            return list;
        }

        public static List<string> TextStyles()
        {
            var list = new List<string>();
            Theme theme = ProjectTheme;
            if (theme != null) list.AddRange(theme.GetTextStyleNames());
            return list;
        }

        public static List<string> Anchors()
        {
            var list = new List<string>();
            foreach (string name in UIWidgetFactory.AnchorPresetNames) list.Add(name);
            return list;
        }

        public static List<string> Icons()
        {
            var list = new List<string>();
            foreach (string name in IconMap.Names) list.Add(name);
            list.Sort();
            return list;
        }

        public static List<string> ViewIds(UISpec spec)
        {
            var list = new List<string>();
            if (spec?.views != null)
                foreach (ViewSpec view in spec.views) list.Add(view.id);
            return list;
        }

        public static List<string> PopupNames(UISpec spec)
        {
            var list = new List<string>();
            if (spec?.popups != null)
                foreach (PopupSpec popup in spec.popups)
                    if (!string.IsNullOrEmpty(popup.name)) list.Add(popup.name);
            return list;
        }

        /// <summary> Catalog ids across both settings and cheats sections (for the menu element's
        /// "catalog" picker). </summary>
        public static List<string> CatalogIds(UISpec spec)
        {
            var list = new List<string>();
            if (spec == null) return list;
            foreach (MenuCatalogSpec catalog in spec.settings) list.Add(catalog.id);
            foreach (MenuCatalogSpec catalog in spec.cheats) list.Add(catalog.id);
            return list;
        }

        /// <summary> Ids of sibling <c>panel</c> elements within <paramref name="view"/> — what a
        /// tab's <c>controls</c> can target. </summary>
        public static List<string> PanelIds(ViewSpec view)
        {
            var list = new List<string>();
            if (view != null) CollectPanels(view.elements, list);
            return list;
        }

        private static void CollectPanels(List<ElementSpec> elements, List<string> into)
        {
            if (elements == null) return;
            foreach (ElementSpec element in elements)
            {
                if (element.kind == "panel" && !string.IsNullOrEmpty(element.id)) into.Add(element.id);
                CollectPanels(element.children, into);
            }
        }
    }
}
