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
        public static readonly string[] ButtonVariants = { "primary", "secondary", "ghost", "danger" };
        public static readonly string[] ButtonSizes = { "sm", "md", "lg" };
        public static readonly string[] Aligns = { "left", "center", "right" };
        public static readonly string[] ShapeNames =
            { "roundedRect", "circle", "pill", "checkmark", "chevron", "cross", "ring", "arc" };

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
