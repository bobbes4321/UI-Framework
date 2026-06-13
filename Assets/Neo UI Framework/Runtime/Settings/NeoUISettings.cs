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

        // ── Design-lint configuration (extensibility seam: validation rules) ─────────────────────
        // The blessed spacing scale + WCAG thresholds + contrast token pairs the soft design lint
        // (AgentValidation.ValidateDesign) and the Composer's spacing snap (ComposerOptions) read.
        // A consuming project on a different design system overrides these here — single source of
        // truth so the lint and the Composer always agree, with no package file edited.
        // See Assets/docs/extensibility-seam-validation-rules-plan.md.

        /// <summary> A text/surface token pair and the minimum WCAG contrast ratio it must meet. </summary>
        [System.Serializable]
        public struct ContrastPair
        {
            [Tooltip("Theme token of the foreground (text/icon) color")] public string text;
            [Tooltip("Theme token of the background (surface) color")] public string surface;
            [Tooltip("Minimum WCAG contrast ratio (e.g. 4.5 body, 3 large/label text)")] public float minimum;

            public ContrastPair(string text, string surface, float minimum)
            {
                this.text = text;
                this.surface = surface;
                this.minimum = minimum;
            }
        }

        [Header("Design Lint")]
        [Tooltip("The on-scale spacing/padding values the design lint blesses and the Composer snaps to. " +
                 "Override for a different design system (single source of truth for the blessed scale).")]
        public float[] spacingScale = { 0f, 4f, 8f, 12f, 16f, 24f, 32f, 48f, 64f };

        [Tooltip("Minimum contrast ratio for large text / icon glyphs on widget surfaces")]
        public float textContrastMin = 3f;

        [Tooltip("Minimum contrast ratio for affordances (knobs, handles) against their tracks")]
        public float affordanceContrastMin = 2f;

        [Tooltip("Theme token contrast pairs the design lint checks per variant. Empty = use the " +
                 "built-in default set.")]
        public ContrastPair[] contrastPairs = System.Array.Empty<ContrastPair>();

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
