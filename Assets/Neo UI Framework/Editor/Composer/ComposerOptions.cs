using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Option providers for the Composer's dropdowns. Lists are built when a dropdown opens (never
    /// per frame) — every method here is a one-shot gather. Theme tokens come from BOTH the live
    /// document (the Composer can add tokens that aren't on the project theme yet) and the project
    /// theme; everything else (styles, anchors) comes from the project theme / factory since the
    /// document doesn't redefine those.
    /// </summary>
    public static class ComposerOptions
    {
        // ---------------------------------------------------------------------------------------
        // Widget-attribute option sets (Pattern R — extensibility-seam-widget-attributes-plan.md).
        // Each set seeds the package built-ins, then a consuming project appends its own from an
        // [InitializeOnLoad] static ctor via RegisterVariant/RegisterSize/RegisterAlign/RegisterShape
        // — no package edit. The pickers read `.All` (snapshot array, so callers can't mutate the
        // seed). De-duped, case-insensitively, preserving order; a re-register is a no-op.
        // NOTE: a registered shape NAME round-trips through the spec, but a brand-new shape
        // *primitive* (mesh + shader) needs the NeoShape graphics seam — out of scope here.
        // ---------------------------------------------------------------------------------------

        private static readonly List<string> _buttonVariants = new List<string> { "primary", "secondary", "ghost", "danger" };
        private static readonly List<string> _buttonSizes = new List<string> { "sm", "md", "lg" };
        private static readonly List<string> _aligns = new List<string> { "left", "center", "right" };
        private static readonly List<string> _shapeNames =
            new List<string> { "roundedRect", "circle", "pill", "checkmark", "chevron", "cross", "ring", "arc" };

        public static string[] ButtonVariants => _buttonVariants.ToArray();
        public static string[] ButtonSizes => _buttonSizes.ToArray();
        public static string[] Aligns => _aligns.ToArray();
        public static string[] ShapeNames => _shapeNames.ToArray();

        /// <summary> Registers a button variant id for the Composer picker (append if new,
        /// case-insensitive no-op if already present). Author the variant's colors as a
        /// <see cref="ButtonVariantAsset"/> on <c>NeoUISettings.buttonVariants</c>. </summary>
        public static void RegisterVariant(string variant) => RegisterInto(_buttonVariants, variant);

        /// <summary> Registers a button size id for the Composer picker. Author its
        /// dimensions as a <see cref="ButtonSizeAsset"/> on <c>NeoUISettings.buttonSizes</c>. </summary>
        public static void RegisterSize(string size) => RegisterInto(_buttonSizes, size);

        /// <summary> Registers a text-align id for the Composer picker. </summary>
        public static void RegisterAlign(string align) => RegisterInto(_aligns, align);

        /// <summary> Registers a shape NAME for the Composer picker. A new primitive also needs the
        /// NeoShape graphics seam (mesh + shader) — out of scope for this seam. </summary>
        public static void RegisterShape(string shape) => RegisterInto(_shapeNames, shape);

        private static void RegisterInto(List<string> set, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (string existing in set)
                if (string.Equals(existing, value, System.StringComparison.OrdinalIgnoreCase)) return;
            set.Add(value);
        }

        /// <summary> Test-only: re-seed every option set back to the package built-ins so a test
        /// that calls Register() can't leak into the next test (static state survives the run). </summary>
        internal static void ResetAttributeRegistriesForTests()
        {
            _buttonVariants.Clear();
            _buttonVariants.AddRange(new[] { "primary", "secondary", "ghost", "danger" });
            _buttonSizes.Clear();
            _buttonSizes.AddRange(new[] { "sm", "md", "lg" });
            _aligns.Clear();
            _aligns.AddRange(new[] { "left", "center", "right" });
            _shapeNames.Clear();
            _shapeNames.AddRange(new[] { "roundedRect", "circle", "pill", "checkmark", "chevron", "cross", "ring", "arc" });
        }

        /// <summary> The on-scale spacing/padding snap values the design lint blesses. </summary>
        public static readonly float[] SpacingScale = { 0, 4, 8, 12, 16, 24, 32, 48, 64 };

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
