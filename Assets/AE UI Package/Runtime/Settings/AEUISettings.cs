using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// The package's single settings asset (per §12: one settings asset, max).
    /// Lives at Resources/AEUISettings so it is loadable at runtime; references the theme,
    /// the ID databases (editor pickers) and the popup database.
    /// </summary>
    public class AEUISettings : ScriptableObject
    {
        public const string ResourcesPath = "AEUISettings";

        private static AEUISettings s_instance;

        public static AEUISettings instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = Resources.Load<AEUISettings>(ResourcesPath);
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
        public AlterEyes.UI.Menus.MenuWidgetLibrary menuWidgets;

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
