using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// The package's single settings asset (per §12: one settings asset, max).
    /// Lives at Resources/NeoUISettings so it is loadable at runtime; references the theme,
    /// the ID databases (editor pickers) and the popup database.
    /// </summary>
    public class NeoUISettings : ScriptableObject
    {
        public const string ResourcesPath = "NeoUISettings";

        private static NeoUISettings s_instance;

        public static NeoUISettings instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = Resources.Load<NeoUISettings>(ResourcesPath);
                return s_instance;
            }
            set => s_instance = value;
        }

        [Header("Theming")]
        public Theme theme;

        [Tooltip("TMP font asset for the icon glyph font (Lucide); icon widgets render through it")]
        public TMPro.TMP_FontAsset iconFont;

        [Header("Animation Presets")]
        public AnimationPresetDatabase animationPresets;

        [Header("Popups")]
        public PopupDatabase popupDatabase;
        [Tooltip("Name of the GameObject acting as the dedicated popups canvas")]
        public string popupsCanvasName = "PopupsCanvas";

        [Header("Tooltips")]
        public string tooltipsCanvasName = "TooltipsCanvas";

        [Header("Sound")]
        [Tooltip("Played by UISoundRelay on button clicks")]
        public AudioClip clickSound;
        [Tooltip("Played by UISoundRelay when a toggle/switch turns on")]
        public AudioClip toggleOnSound;
        [Tooltip("Played by UISoundRelay when a toggle/switch turns off")]
        public AudioClip toggleOffSound;

        [Header("Input")]
        [Tooltip("Minimum seconds between accepted back-button fires")]
        public float backButtonCooldown = 0.1f;

        [Header("Ticker")]
        [Tooltip("Runtime tick FPS cap; 0 = uncapped")]
        public int runtimeFpsCap;

        [Header("ID Databases (editor pickers)")]
        public ViewIdDatabase viewIds;
        public ButtonIdDatabase buttonIds;
        public ToggleIdDatabase toggleIds;
        public SliderIdDatabase sliderIds;
        public TagIdDatabase tagIds;
        public StreamIdDatabase streamIds;
        public PanelIdDatabase panelIds;
        public DropdownIdDatabase dropdownIds;

        [Header("Menus (settings / cheats)")]
        [Tooltip("Default row/widget prefab library presenters build settings & cheat menus from")]
        public Neo.UI.Menus.MenuWidgetLibrary menuWidgets;

        // -------------------------------------------------------------------------------------
        // region: Widget attribute seams (extensibility-seam-widget-attributes-plan.md, Pattern A)
        // Additive, designer-authored data that opens the button-variant and icon sets WITHOUT a
        // code edit. Consulted FIRST by UIWidgetFactory.VariantColors / ButtonSize and
        // IconMap.TryGetGlyph; the built-in switch/dict remain the fallback (seam-first, Phase 1).
        // NOTE: a brand-new shape *primitive* (mesh + shader) is NOT opened here — only the variant/
        // size/align/icon sets and the shape NAME list. New primitives need the NeoShape graphics
        // seam (out of scope for this plan).
        // -------------------------------------------------------------------------------------

        [Header("Widget Variants & Sizes (project extensions)")]
        [Tooltip("Project-authored button variants (name + per-state colors + content token). " +
                 "Consulted before the 4 built-in variants; a matching name wins.")]
        public List<ButtonVariantAsset> buttonVariants = new List<ButtonVariantAsset>();

        [Tooltip("Project-authored button sizes (name → label height + label text style). " +
                 "Consulted before the 3 built-in sizes; a matching name wins.")]
        public List<ButtonSizeAsset> buttonSizes = new List<ButtonSizeAsset>();

        [Header("Icons (project extension)")]
        [Tooltip("Optional overlay blending custom glyphs (and aliases) with the built-in Lucide set. " +
                 "IconMap consults this before the built-in dictionary.")]
        public IconMapOverlay iconOverlay;

        /// <summary>
        /// Looks up a project-authored variant by name (case-insensitive). Returns its color set
        /// and content token. The built-in 4-case switch in <c>UIWidgetFactory.VariantColors</c>
        /// remains the fallback when this returns false.
        /// </summary>
        public bool TryGetVariantColors(string variant, out SelectableColorSet colors, out string contentToken)
        {
            colors = null;
            contentToken = null;
            if (string.IsNullOrEmpty(variant) || buttonVariants == null) return false;
            foreach (ButtonVariantAsset entry in buttonVariants)
            {
                if (entry == null || !string.Equals(entry.name, variant, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                colors = entry.colors;
                contentToken = entry.contentToken;
                return colors != null;
            }
            return false;
        }

        /// <summary>
        /// Looks up a project-authored button size by name (case-insensitive). The built-in
        /// sm/md/lg switch in <c>UIWidgetFactory.ButtonSize</c> remains the fallback.
        /// </summary>
        public bool TryGetButtonSize(string size, out float height, out string labelStyle)
        {
            height = 0f;
            labelStyle = null;
            if (string.IsNullOrEmpty(size) || buttonSizes == null) return false;
            foreach (ButtonSizeAsset entry in buttonSizes)
            {
                if (entry == null || !string.Equals(entry.name, size, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                height = entry.height;
                labelStyle = entry.labelStyle;
                return true;
            }
            return false;
        }

        public IdDatabase GetDatabaseFor(System.Type idType)
        {
            if (idType == typeof(ViewId)) return viewIds;
            if (idType == typeof(ButtonId)) return buttonIds;
            if (idType == typeof(ToggleId)) return toggleIds;
            if (idType == typeof(SliderId)) return sliderIds;
            if (idType == typeof(TagId)) return tagIds;
            if (idType == typeof(StreamId)) return streamIds;
            if (idType == typeof(PanelId)) return panelIds;
            if (idType == typeof(DropdownId)) return dropdownIds;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;
    }
}
