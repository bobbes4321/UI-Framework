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
        // internal: NeoResetComponentDefaults' custom-themes entry deletes this same root
        internal const string CustomThemesRoot = "Assets/Neo UI Themes";

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

        // Cached "what's already installed" snapshot (NeoSetupStatus, shared with NeoUIHubWindow) —
        // recomputed on open/focus only, never per-OnGUI (CLAUDE.md editor-perf rules).
        private NeoSetupStatus.Snapshot _status;

        // Guards the one-time load of current-project state into the fields below: OnEnable also fires
        // after a domain reload while the window stays open, and re-loading then would stomp whatever
        // the user had already typed. This bool is a plain field so Unity's window serialization keeps
        // it true across that reload.
        private bool _loadedFromProject;

        [MenuItem("Tools/Neo UI/Setup/New Project Setup…", priority = 90)]
        public static void Open()
        {
            var window = GetWindow<NeoSetupWizard>(true, "Neo UI — New Project Setup");
            window.minSize = new Vector2(440f, 560f);
            window.RefreshBundleOptions();
        }

        private void OnEnable()
        {
            RefreshBundleOptions();
            RecomputeStatus();
            if (!_loadedFromProject)
            {
                LoadFromCurrentState();
                _loadedFromProject = true;
            }
        }

        private void OnFocus() => RecomputeStatus();

        private void RecomputeStatus() => _status = NeoSetupStatus.Compute();

        private void RefreshBundleOptions()
        {
            var list = new List<string> { KeepCurrent };
            list.AddRange(ThemeBundles.Names);
            _bundleOptions = list.ToArray();
            if (_bundle >= _bundleOptions.Length) _bundle = 0;
        }

        // ---------------------------------------------------------------- load current state

        /// <summary>
        /// Pre-fills the custom-color fields, shape sliders and motion-role dropdowns from the CURRENT
        /// project state (design-system-cohesion-plan Phase 1.2) so reopening the wizard reads as "here's
        /// what you have" instead of silently reverting to neutral defaults. Strictly read-only — never
        /// creates or mutates an asset. Only covers inverse mappings that are unambiguous:
        /// <list type="bullet">
        /// <item>a palette token maps to exactly one intent field (the same map
        /// <see cref="ThemeBundles.BuildPalette"/> writes, inverted);</item>
        /// <item>the Card/Control shape styles' plain radius maps to exactly one slider each;</item>
        /// <item>the "ShowDefault" preset — the exact asset <see cref="ThemeBundles"/> seeds — maps to the
        /// motion-duration slider.</item>
        /// </list>
        /// Anything else (theme-bundle provenance, per-channel ease/offsets, …) isn't a 1:1 field on this
        /// wizard, so it's deliberately left alone rather than guessed. Missing pieces (fresh project, no
        /// theme yet) simply keep the wizard's neutral-dark defaults.
        /// </summary>
        private void LoadFromCurrentState()
        {
            Theme theme = _status.settings != null ? _status.settings.theme : null;
            if (theme != null)
            {
                Dictionary<string, Color> palette = NeoSetupPalette.ReadFrom(theme);
                if (palette.TryGetValue("background", out Color background)) _background = background;
                if (palette.TryGetValue("surface", out Color surface)) _surface = surface;
                if (palette.TryGetValue("surfaceElevated", out Color surfaceElevated)) _surfaceElevated = surfaceElevated;
                if (palette.TryGetValue("outline", out Color outline)) _outline = outline;
                if (palette.TryGetValue("primary", out Color primary)) _primary = primary;
                if (palette.TryGetValue("textOnPrimary", out Color textOnPrimary)) _textOnPrimary = textOnPrimary;
                if (palette.TryGetValue("textStrong", out Color textStrong)) _textStrong = textStrong;
                if (palette.TryGetValue("textDefault", out Color textDefault)) _textDefault = textDefault;
                if (palette.TryGetValue("textMuted", out Color textMuted)) _textMuted = textMuted;
                if (palette.TryGetValue("success", out Color success)) _success = success;
                if (palette.TryGetValue("warning", out Color warning)) _warning = warning;
                if (palette.TryGetValue("error", out Color error)) _error = error;
                if (palette.TryGetValue("shadow", out Color shadow)) _shadow = shadow;

                if (theme.TryGetShapeStyle(UIWidgetFactory.StyleCard, out ShapeStyle card))
                    _cardRadius = card.radius;
                if (theme.TryGetShapeStyle(UIWidgetFactory.StyleControl, out ShapeStyle control))
                    _controlRadius = control.radius;
            }

            // Motion duration: ShowDefault is the exact preset ThemeBundles.ApplyMotion seeds, so its
            // fade (or scale, if fade is off) duration is an unambiguous read-back — every other
            // per-channel field (ease, offsets, …) has no 1:1 slider here, so it's left untouched.
            UIAnimationPreset showDefault = AnimationPresetRegistry.GetByFullName("Show/ShowDefault");
            if (showDefault?.animation != null)
            {
                if (showDefault.animation.fade != null && showDefault.animation.fade.enabled)
                    _motionDuration = showDefault.animation.fade.settings.duration;
                else if (showDefault.animation.scale != null && showDefault.animation.scale.enabled)
                    _motionDuration = showDefault.animation.scale.settings.duration;
            }

            // Motion-role defaults: mirror the project's current animatorDefaults into the dropdown rows.
            if (_status.settings != null)
            {
                foreach (string role in MotionRoles)
                {
                    if (_status.settings.TryGetDefaultAnimation(role, out UIAnimationPreset preset)
                        && preset != null)
                        _motionDefaults[role] = preset.fullName;
                }
            }
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
                EditorGUILayout.LabelField(
                    "Refine colors, buttons, shapes and motion anytime in the Design System window.",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- detected

        private void DrawDetectedState()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Detected:", GUILayout.Width(60f));
                Dot("Settings", _status.hasSettings);
                Dot("Starter Kit", _status.hasStarterKit);
                Dot("Fonts", _status.hasFonts);
                Dot("Presets", _status.hasPresets);
                Dot("Anims", _status.hasAnimations);
                Dot("Effects", _status.hasEffects);
            }
            if (_status.hasSettings)
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
                IncludeToggle(new GUIContent("Core settings + databases (required)"), true, _status.hasSettings);
            _starterKit = IncludeToggle(new GUIContent("Widget prefab library (Starter Kit)",
                "Themed button/toggle/slider/… prefabs + Dark/Light palette + type scale"),
                _starterKit, _status.hasStarterKit);
            _fonts = IncludeToggle(new GUIContent("Fonts (Inter + Lucide icons)",
                "TMP SDF font assets; wires the icon font"), _fonts, _status.hasFonts);
            _presets = IncludeToggle(new GUIContent("Widget preset library",
                "Named component styles (Primary Button, Section Header…)"), _presets, _status.hasPresets);
            _animations = IncludeToggle(new GUIContent("Default animation library",
                "Curated fade / slide / scale-pop / button / loop presets, referenced by name from specs"),
                _animations, _status.hasAnimations);
            _effects = IncludeToggle(new GUIContent("Effect assets (Tier-2 materials)",
                "Noise/ramp textures + dissolve/holo/glitch materials for variant shape effects"),
                _effects, _status.hasEffects);
        }

        /// <summary> A toggle row with a trailing "installed ✓" / "not set up yet" status so a returning
        /// user can see what their project already has, per design-system-cohesion-plan Phase 1.2. </summary>
        private static bool IncludeToggle(GUIContent content, bool value, bool installed)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.ToggleLeft(content, value, GUILayout.ExpandWidth(true));
                GUILayout.FlexibleSpace();
                Color prev = GUI.contentColor;
                GUI.contentColor = installed ? NeoColors.Add : NeoColors.TextSubtle;
                GUILayout.Label(installed ? "installed ✓" : "not set up yet", EditorStyles.miniLabel,
                    GUILayout.Width(96f));
                GUI.contentColor = prev;
            }
            return value;
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
            RecomputeStatus(); // refresh the "installed" indicators to reflect what just ran
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
        // Rehomed (Phase 2.8) into the shared ThemeBundles.SaveDefinition so the Design System window's
        // "Save current look as bundle" and this wizard write the identical asset — the wizard's custom
        // bundle is a single-variant Bundle, so the shared writer produces byte-identical output.
        private void SaveCustomBundle(ThemeBundles.Bundle custom)
        {
            ThemeBundles.SaveDefinition(custom, CustomThemesRoot);
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
