using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// One-stop guided setup for a new project: choose a starting LOOK (a prebuilt theme bundle OR your
    /// own colors), pick what to include (Starter Kit / Fonts / Widget Presets / Effect Assets / default
    /// Animation Library), then one click orchestrates the existing idempotent bootstraps in the right
    /// order. The custom-theme path reuses the same palette derivation the built-in bundles use
    /// (<see cref="ThemeBundles.BuildPalette"/> — one color per intent, hover/pressed derived) and can
    /// save your palette as a reusable <see cref="ThemeBundleDefinition"/>. Safe to re-run: every step is
    /// create-or-repair.
    /// </summary>
    public sealed class NeoSetupWizard : EditorWindow
    {
        private const string KeepCurrent = "(keep current theme)";
        private const string CustomThemesRoot = "Assets/Neo UI Themes";

        private enum LookMode { Bundle, CustomColors }

        // include toggles
        private bool _starterKit = true;
        private bool _fonts = true;
        private bool _presets = true;
        private bool _animations = true;
        private bool _effects;

        // Motion defaults: role id → chosen preset full-name ("Category/Name"). Applied (as direct
        // references via NeoUISettings.SetDefaultAnimation) at the end of setup, after the library exists.
        private static readonly string[] MotionRoles =
        {
            NeoAnimatorRoles.ViewShow, NeoAnimatorRoles.ViewHide,
            NeoAnimatorRoles.ButtonHover, NeoAnimatorRoles.ButtonPress,
        };
        private readonly System.Collections.Generic.Dictionary<string, string> _motionDefaults =
            new System.Collections.Generic.Dictionary<string, string>();

        // look
        private LookMode _lookMode = LookMode.Bundle;
        private string[] _bundleOptions = { KeepCurrent };
        private int _bundle;
        private bool _saveCustom;
        private string _customName = "MyTheme";

        // custom palette (seeded from CleanSlate Dark)
        private Color _background = Hex(0x0B0D10), _surface = Hex(0x16191E), _surfaceElevated = Hex(0x21252C),
            _outline = Hex(0x343A44), _primary = Hex(0x3B82F6), _textOnPrimary = Hex(0xFFFFFF),
            _textStrong = Hex(0xF5F7FA), _textDefault = Hex(0xC6CCD6), _textMuted = Hex(0x8E96A4),
            _success = Hex(0x22C55E), _warning = Hex(0xF59E0B), _error = Hex(0xEF4444),
            _shadow = new Color(0f, 0f, 0f, 0.5f);
        private float _cardRadius = 12f, _controlRadius = 10f, _motionDuration = 0.18f;

        private string _summary;
        private Vector2 _scroll;

        [MenuItem("Tools/Neo UI/Setup/New Project Setup…", priority = 90)]
        public static void Open()
        {
            var window = GetWindow<NeoSetupWizard>(true, "Neo UI — New Project Setup");
            window.minSize = new Vector2(440f, 560f);
            window.RefreshBundleOptions();
        }

        private void OnEnable() => RefreshBundleOptions();

        private void RefreshBundleOptions()
        {
            var list = new List<string> { KeepCurrent };
            list.AddRange(ThemeBundles.Names);
            _bundleOptions = list.ToArray();
            if (_bundle >= _bundleOptions.Length) _bundle = 0;
        }

        private void OnGUI()
        {
            NeoGUI.ComponentHeader("New Project Setup",
                "Pick a look and what to include, then set up your project", NeoColors.Theming);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawDetectedState();
            EditorGUILayout.Space(4f);
            DrawLook();
            EditorGUILayout.Space(4f);
            DrawInclude();
            EditorGUILayout.Space(4f);
            DrawMotionDefaults();

            EditorGUILayout.Space(8f);
            if (GUILayout.Button(new GUIContent("Set Up Project",
                    "Run the selected steps (idempotent — safe to re-run)"), GUILayout.Height(30f)))
                RunSetup();

            if (!string.IsNullOrEmpty(_summary))
            {
                NeoGUI.Splitter();
                EditorGUILayout.HelpBox(_summary, MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Design System")) NeoDesignSystemWindow.Open();
                    if (GUILayout.Button("Open Hub")) NeoUIHubWindow.Open();
                    if (GUILayout.Button("Done")) Close();
                }
                EditorGUILayout.LabelField("Next: GameObject → Neo UI → View to start building.",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- detected

        private void DrawDetectedState()
        {
            NeoUISettings settings =
                AssetDatabase.LoadAssetAtPath<NeoUISettings>(NeoUISettingsBootstrap.SettingsAssetPath);
            bool hasSettings = settings != null;
            bool hasStarter = hasSettings && settings.theme != null
                && settings.theme.HasToken(UIWidgetFactory.TokenPrimary);
            bool hasFonts = hasSettings && settings.iconFont != null;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Detected:", GUILayout.Width(60f));
                Dot("Settings", hasSettings);
                Dot("Starter Kit", hasStarter);
                Dot("Fonts", hasFonts);
                Dot("Presets", NeoWidgetPresets.All.Count > 0);
                Dot("Anims", AnimationPresetRegistry.All.Count > 0);
            }
            if (hasSettings)
                EditorGUILayout.LabelField("A project is already set up — re-running only repairs/fills gaps.",
                    EditorStyles.miniLabel);
        }

        private static void Dot(string label, bool present)
        {
            Color prev = GUI.color;
            GUI.color = present ? NeoColors.Add : NeoColors.TextSubtle;
            GUILayout.Label(present ? $"● {label}" : $"○ {label}", EditorStyles.miniLabel, GUILayout.Width(82f));
            GUI.color = prev;
        }

        // ---------------------------------------------------------------- look

        private void DrawLook()
        {
            EditorGUILayout.LabelField("Starting look", EditorStyles.boldLabel);
            _lookMode = (LookMode)GUILayout.Toolbar((int)_lookMode, new[] { "Theme bundle", "Custom colors" });

            if (_lookMode == LookMode.Bundle) DrawBundlePicker();
            else DrawCustomColors();
        }

        private void DrawBundlePicker()
        {
            _bundle = EditorGUILayout.Popup("Bundle", _bundle, _bundleOptions);
            string chosen = _bundleOptions[_bundle];
            if (chosen != KeepCurrent && ThemeBundles.TryGet(chosen, out ThemeBundles.Bundle bundle)
                && !string.IsNullOrEmpty(bundle.description))
                Hint(bundle.description);
            else if (chosen == KeepCurrent)
                Hint("Keeps the current theme tokens; only creates the assets below.");
        }

        private void DrawCustomColors()
        {
            Hint("One color per intent — hover/pressed states are derived automatically, exactly like the " +
                 "built-in bundles. Defaults are a neutral dark palette; tweak what you care about.");
            _primary = EditorGUILayout.ColorField("Primary / accent", _primary);
            _background = EditorGUILayout.ColorField("Background", _background);
            _surface = EditorGUILayout.ColorField("Surface (cards)", _surface);
            _textDefault = EditorGUILayout.ColorField("Text", _textDefault);

            if (NeoGUI.BeginFoldoutSection("NeoUI.SetupWizard.MoreColors", "More colors"))
            {
                _surfaceElevated = EditorGUILayout.ColorField("Surface elevated", _surfaceElevated);
                _outline = EditorGUILayout.ColorField("Outline", _outline);
                _textOnPrimary = EditorGUILayout.ColorField("Text on primary", _textOnPrimary);
                _textStrong = EditorGUILayout.ColorField("Text strong", _textStrong);
                _textMuted = EditorGUILayout.ColorField("Text muted", _textMuted);
                _success = EditorGUILayout.ColorField("Success", _success);
                _warning = EditorGUILayout.ColorField("Warning", _warning);
                _error = EditorGUILayout.ColorField("Error / danger", _error);
                _shadow = EditorGUILayout.ColorField("Shadow", _shadow);
            }
            NeoGUI.EndFoldoutSection();

            if (NeoGUI.BeginFoldoutSection("NeoUI.SetupWizard.Shape", "Shape & motion"))
            {
                _cardRadius = EditorGUILayout.Slider("Card radius", _cardRadius, 0f, 32f);
                _controlRadius = EditorGUILayout.Slider("Control radius", _controlRadius, 0f, 32f);
                _motionDuration = EditorGUILayout.Slider("Motion duration", _motionDuration, 0.05f, 0.6f);
            }
            NeoGUI.EndFoldoutSection();

            _saveCustom = EditorGUILayout.ToggleLeft(
                new GUIContent("Save as reusable Theme Bundle Definition",
                    "Writes a ThemeBundleDefinition asset so this palette appears in the bundle list and " +
                    "the spec \"theme\":{\"bundle\":\"…\"} path"), _saveCustom);
            if (_saveCustom)
                _customName = EditorGUILayout.TextField("Bundle name", _customName);
        }

        // ---------------------------------------------------------------- include

        private void DrawInclude()
        {
            EditorGUILayout.LabelField("Include", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ToggleLeft("Core settings + databases (required)", true);
            _starterKit = EditorGUILayout.ToggleLeft(new GUIContent("Widget prefab library (Starter Kit)",
                "Themed button/toggle/slider/… prefabs + Dark/Light palette + type scale"), _starterKit);
            _fonts = EditorGUILayout.ToggleLeft(new GUIContent("Fonts (Inter + Lucide icons)",
                "TMP SDF font assets; wires the icon font"), _fonts);
            _presets = EditorGUILayout.ToggleLeft(new GUIContent("Widget preset library",
                "Named component styles (Primary Button, Section Header…)"), _presets);
            _animations = EditorGUILayout.ToggleLeft(new GUIContent("Default animation library",
                "Curated fade / slide / scale-pop / button / loop presets, referenced by name from specs"),
                _animations);
            _effects = EditorGUILayout.ToggleLeft(new GUIContent("Effect assets (Tier-2 materials)",
                "Noise/ramp textures + dissolve/holo/glitch materials for variant shape effects"), _effects);
        }

        // ---------------------------------------------------------------- motion defaults

        // "How widgets feel by default": pick a preset per headline animator role. Stored as
        // full-names and resolved to preset references at setup time (after the library is created).
        private void DrawMotionDefaults()
        {
            if (!NeoGUI.BeginFoldoutSection("NeoUI.SetupWizard.Motion", "Motion defaults (optional)"))
            {
                NeoGUI.EndFoldoutSection();
                return;
            }

            Hint("Pick how widgets feel by default — these presets are copied into new animator " +
                 "components and generated buttons/views. Leave a row blank to keep the built-in feel.");

            if (AnimationPresetRegistry.All.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No animation presets found yet — create the library to pick from it.", MessageType.Info);
                if (GUILayout.Button("Create Animation Library Now"))
                {
                    AnimationLibraryBootstrap.CreateOrRepair();
                    AssetDatabase.SaveAssets();
                    GUI.FocusControl(null);
                }
            }
            else
            {
                foreach (string role in MotionRoles) DrawMotionRow(role);
            }

            NeoGUI.EndFoldoutSection();
        }

        private void DrawMotionRow(string role)
        {
            NeoAnimatorRoles.TryGet(role, out NeoAnimatorRole info);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(info != null ? info.DisplayName : role, GUILayout.Width(150f));
                _motionDefaults.TryGetValue(role, out string current);
                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                NeoDropdown.ValuePopup(rect, current,
                    () => AnimationPresetRegistry.FullNamesForRole(role),
                    chosen => _motionDefaults[role] = chosen, emptyLabel: "(keep built-in)");
            }
        }

        // ---------------------------------------------------------------- run

        private void RunSetup()
        {
            var steps = new List<string>();

            NeoUISettings settings = NeoUISettingsBootstrap.EnsureSettings();
            steps.Add("Settings + databases");

            if (_fonts) { FontAssetBootstrap.EnsureIconFont(settings); steps.Add("Fonts"); }
            if (_starterKit) { StarterKitBootstrap.CreateOrRepair(); steps.Add("Starter Kit"); }
            if (_presets) { PresetLibraryBootstrap.CreateOrRepair(); steps.Add("Widget presets"); }
            if (_animations)
            {
                int n = AnimationLibraryBootstrap.CreateOrRepair();
                steps.Add($"Animation library ({n} new)");
            }
            if (_effects) { NoiseAssetBootstrap.CreateOrRepair(); steps.Add("Effect assets"); }

            // Theme — applied LAST so it recolors the freshly-seeded presets to the chosen personality.
            if (_lookMode == LookMode.Bundle)
            {
                string look = _bundleOptions[_bundle];
                if (look != KeepCurrent && ThemeBundles.TryGet(look, out ThemeBundles.Bundle bundle))
                {
                    ThemeBundles.Apply(bundle, settings, new GenerateReport());
                    steps.Add($"Theme bundle '{bundle.name}'");
                }
            }
            else
            {
                ThemeBundles.Bundle custom = BuildCustomBundle();
                ThemeBundles.Apply(custom, settings, new GenerateReport());
                steps.Add("Custom theme");
                if (_saveCustom && !string.IsNullOrWhiteSpace(_customName))
                {
                    SaveCustomBundle(custom);
                    steps.Add($"Saved bundle '{_customName.Trim()}'");
                }
            }

            // Motion defaults LAST: the library (and any custom presets) now exist to resolve against.
            int motion = 0;
            foreach (System.Collections.Generic.KeyValuePair<string, string> choice in _motionDefaults)
            {
                if (string.IsNullOrEmpty(choice.Value)) continue;
                UIAnimationPreset preset = AnimationPresetRegistry.GetByFullName(choice.Value);
                if (preset == null) continue;
                settings.SetDefaultAnimation(choice.Key, preset);
                motion++;
            }
            if (motion > 0)
            {
                EditorUtility.SetDirty(settings);
                steps.Add($"Motion defaults ({motion})");
            }

            AssetDatabase.SaveAssets();
            _summary = "Set up: " + string.Join(", ", steps) + ".";
            Debug.Log($"[Neo.UI] New Project Setup — {_summary}");
            Repaint();
        }

        private ThemeBundles.Bundle BuildCustomBundle()
        {
            Dictionary<string, Color> tokens = ThemeBundles.BuildPalette(
                _background, _surface, _surfaceElevated, _outline, _primary, _textOnPrimary, _textStrong,
                _textDefault, _textMuted, _success, _warning, _error, _shadow);
            return new ThemeBundles.Bundle
            {
                name = _saveCustom && !string.IsNullOrWhiteSpace(_customName) ? _customName.Trim() : "Custom",
                description = "Custom palette from the New Project Setup wizard",
                palettes = new List<(string, Dictionary<string, Color>)> { ("Custom", tokens) },
                cardRadius = _cardRadius,
                panelRadius = _cardRadius,
                controlRadius = _controlRadius,
                shadowSoftness = 16f,
                motionDuration = _motionDuration,
                motionEase = "OutCubic",
                headlineSpacing = -0.5f,
            };
        }

        // Persist the derived palette as raw tokens so re-applying the definition reproduces it exactly.
        private void SaveCustomBundle(ThemeBundles.Bundle custom)
        {
            if (!AssetDatabase.IsValidFolder(CustomThemesRoot))
                AssetDatabase.CreateFolder("Assets", "Neo UI Themes");

            var def = ScriptableObject.CreateInstance<ThemeBundleDefinition>();
            def.bundleName = custom.name;
            def.description = custom.description;
            def.cardRadius = custom.cardRadius;
            def.panelRadius = custom.panelRadius;
            def.controlRadius = custom.controlRadius;
            def.shadowSoftness = custom.shadowSoftness;
            def.motionDuration = custom.motionDuration;
            def.motionEase = custom.motionEase;
            def.headlineSpacing = custom.headlineSpacing;
            var variant = new ThemeBundleDefinition.Variant { name = "Custom" };
            foreach (KeyValuePair<string, Color> t in custom.palettes[0].tokens)
                variant.tokens.Add(new ThemeBundleDefinition.TokenColor { token = t.Key, color = t.Value });
            def.variants.Add(variant);

            AssetDatabase.CreateAsset(def, $"{CustomThemesRoot}/{custom.name}.asset");
            ThemeBundleRegistry.InvalidateDiscovery();
            RefreshBundleOptions();
        }

        // ---------------------------------------------------------------- helpers

        private static void Hint(string text)
        {
            Color prev = GUI.contentColor;
            GUI.contentColor = NeoColors.TextSubtle;
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
            GUI.contentColor = prev;
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
