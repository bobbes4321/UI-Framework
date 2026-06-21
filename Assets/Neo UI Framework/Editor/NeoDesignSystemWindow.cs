using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The design-system editor: one window to author the project's LOOK over the live
    /// <see cref="NeoUISettings"/> + <see cref="Theme"/> — Colors (theme tokens/variants), Buttons
    /// (per-state variant colors + sizes), Shapes (radius / outline / softness), and Presets (named
    /// component styles like "Primary Button"), and Motion (default animation preset per animator role).
    /// It edits the exact structures the widget factory already consults (`buttonVariants`, theme tokens,
    /// shape styles, <see cref="NeoWidgetPreset"/>, `animatorDefaults`), so changes flow straight into
    /// generated and native-built UI. A faux preview shows the chosen colors/outline.
    /// </summary>
    public sealed class NeoDesignSystemWindow : EditorWindow
    {
        private const string TabKey = "NeoUI.DesignSystem.Tab";
        private static readonly string[] Tabs = { "Colors", "Buttons", "Shapes", "Presets", "Motion" };

        private Vector2 _scroll;
        private int _variantIdx, _btnIdx, _shapeIdx;
        private string _newToken = "", _newVariant = "", _newBtnVariant = "", _newShape = "", _newPreset = "";

        // Live button preview: a real render of a sample button, cached and re-rendered only when its
        // look key changes (never per OnGUI). Falls back to a faux swatch if rendering is unavailable.
        private Texture2D _btnPreview;
        private string _btnPreviewKey;

        private void OnDisable()
        {
            if (_btnPreview != null) DestroyImmediate(_btnPreview);
            _btnPreview = null;
            _btnPreviewKey = null;
        }

        [MenuItem("Tools/Neo UI/Design System", priority = 12)]
        public static void Open()
        {
            var w = GetWindow<NeoDesignSystemWindow>(false, "Neo UI — Design System");
            w.minSize = new Vector2(460f, 520f);
        }

        private void OnGUI()
        {
            NeoGUI.ComponentHeader("Design System", "Author your colors, buttons, shapes and presets",
                NeoColors.Theming);

            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            Theme theme = settings != null ? settings.theme : null;
            if (settings == null || theme == null)
            {
                EditorGUILayout.HelpBox("No settings/theme yet. Run New Project Setup first.", MessageType.Info);
                if (GUILayout.Button("Open New Project Setup")) NeoSetupWizard.Open();
                return;
            }

            int tab = NeoGUI.Tabs(TabKey, Tabs);
            NeoGUI.Splitter();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (tab)
            {
                case 1: DrawButtons(settings, theme); break;
                case 2: DrawShapes(theme); break;
                case 3: DrawPresets(); break;
                case 4: DrawMotion(settings); break;
                default: DrawColors(theme); break;
            }
            EditorGUILayout.EndScrollView();

            NeoGUI.Splitter();
            if (GUILayout.Button("Save Assets")) AssetDatabase.SaveAssets();
        }

        // ============================================================ Colors

        private void DrawColors(Theme theme)
        {
            var variants = theme.Variants.Select(v => v.name).ToArray();
            if (variants.Length == 0) { EditorGUILayout.LabelField("No variants."); }
            else
            {
                _variantIdx = Mathf.Clamp(_variantIdx, 0, variants.Length - 1);
                _variantIdx = EditorGUILayout.Popup("Variant", _variantIdx, variants);
                if (theme.ActiveVariantName != variants[_variantIdx]) theme.ActiveVariantName = variants[_variantIdx];

                Theme.ThemeVariant variant = theme.GetVariant(variants[_variantIdx]);
                if (variant != null)
                {
                    foreach (Theme.TokenColor tc in variant.colors.ToList())
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUI.BeginChangeCheck();
                            Color c = EditorGUILayout.ColorField(tc.token, tc.color);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(theme, "Edit token");
                                theme.SetToken(tc.token, c, variant.name);
                                EditorUtility.SetDirty(theme);
                            }
                            if (GUILayout.Button("✕", GUILayout.Width(22f)))
                            {
                                Undo.RecordObject(theme, "Remove token");
                                theme.RemoveToken(tc.token);
                                EditorUtility.SetDirty(theme);
                            }
                        }
                    }
                }
            }

            NeoGUI.Splitter();
            using (new EditorGUILayout.HorizontalScope())
            {
                _newToken = EditorGUILayout.TextField("New token", _newToken);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(_newToken))
                {
                    Undo.RecordObject(theme, "Add token");
                    theme.SetToken(_newToken.Trim(), Color.gray);
                    EditorUtility.SetDirty(theme);
                    _newToken = "";
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _newVariant = EditorGUILayout.TextField("New variant", _newVariant);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(_newVariant))
                {
                    Undo.RecordObject(theme, "Add variant");
                    theme.AddVariant(_newVariant.Trim());
                    EditorUtility.SetDirty(theme);
                    _newVariant = "";
                }
            }
            if (GUILayout.Button(new GUIContent("Re-derive hover / pressed states",
                "Recompute Primary/Success/Danger hover+pressed from their base color")))
                DeriveStates(theme);
        }

        private static void DeriveStates(Theme theme)
        {
            Undo.RecordObject(theme, "Derive states");
            DerivePair(theme, UIWidgetFactory.TokenPrimary, UIWidgetFactory.TokenPrimaryHover, UIWidgetFactory.TokenPrimaryPressed);
            DerivePair(theme, UIWidgetFactory.TokenSuccess, UIWidgetFactory.TokenSuccessHover, UIWidgetFactory.TokenSuccessPressed);
            DerivePair(theme, UIWidgetFactory.TokenDanger, UIWidgetFactory.TokenDangerHover, UIWidgetFactory.TokenDangerPressed);
            EditorUtility.SetDirty(theme);
        }

        private static void DerivePair(Theme theme, string baseToken, string hover, string pressed)
        {
            if (!theme.HasToken(baseToken)) return;
            Color b = theme.GetColor(baseToken);
            theme.SetToken(hover, ColorUtils.DeriveHover(b));
            theme.SetToken(pressed, ColorUtils.DerivePressed(b));
        }

        // ============================================================ Buttons

        private void DrawButtons(NeoUISettings settings, Theme theme)
        {
            EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
            var names = settings.buttonVariants.Select(v => v.name).ToArray();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (names.Length > 0)
                {
                    _btnIdx = Mathf.Clamp(_btnIdx, 0, names.Length - 1);
                    _btnIdx = EditorGUILayout.Popup("Variant", _btnIdx, names);
                }
                else EditorGUILayout.LabelField("No custom variants (built-ins: primary/secondary/ghost/danger)");
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _newBtnVariant = EditorGUILayout.TextField("New variant", _newBtnVariant);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(_newBtnVariant))
                {
                    Undo.RecordObject(settings, "Add button variant");
                    settings.buttonVariants.Add(new ButtonVariantAsset
                    {
                        name = _newBtnVariant.Trim(),
                        contentToken = UIWidgetFactory.TokenTextOnPrimary,
                        colors = DefaultVariantColors(),
                    });
                    EditorUtility.SetDirty(settings);
                    _btnIdx = settings.buttonVariants.Count - 1;
                    _newBtnVariant = "";
                    names = settings.buttonVariants.Select(v => v.name).ToArray();
                }
            }

            if (names.Length > 0 && _btnIdx < settings.buttonVariants.Count)
            {
                ButtonVariantAsset v = settings.buttonVariants[_btnIdx];
                NeoGUI.Splitter();
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField("Name", v.name);
                ColorRef(theme, settings, "Normal", v.colors.normal);
                ColorRef(theme, settings, "Hover", v.colors.highlighted);
                ColorRef(theme, settings, "Pressed", v.colors.pressed);
                ColorRef(theme, settings, "Selected", v.colors.selected);
                ColorRef(theme, settings, "Disabled", v.colors.disabled);
                v.contentToken = TokenPicker(theme, "Content (label/icon)", v.contentToken);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(settings, "Edit button variant");
                    v.name = newName;
                    EditorUtility.SetDirty(settings);
                }

                PreviewButton(theme, v);

                if (GUILayout.Button("Remove variant"))
                {
                    Undo.RecordObject(settings, "Remove button variant");
                    settings.buttonVariants.RemoveAt(_btnIdx);
                    EditorUtility.SetDirty(settings);
                }
            }

            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Sizes", EditorStyles.boldLabel);
            for (int i = 0; i < settings.buttonSizes.Count; i++)
            {
                ButtonSizeAsset s = settings.buttonSizes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string n = EditorGUILayout.TextField(s.name, GUILayout.Width(120f));
                    float h = EditorGUILayout.FloatField(s.height, GUILayout.Width(60f));
                    string ls = EditorGUILayout.TextField(s.labelStyle);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(settings, "Edit size");
                        s.name = n; s.height = h; s.labelStyle = ls;
                        EditorUtility.SetDirty(settings);
                    }
                }
            }
            if (GUILayout.Button("Add size"))
            {
                Undo.RecordObject(settings, "Add size");
                settings.buttonSizes.Add(new ButtonSizeAsset { name = "xl", height = 64f, labelStyle = "ButtonLabel" });
                EditorUtility.SetDirty(settings);
            }
        }

        private static SelectableColorSet DefaultVariantColors() => new SelectableColorSet
        {
            normal = new ThemeColorRef(UIWidgetFactory.TokenPrimary) { useToken = true },
            highlighted = new ThemeColorRef(UIWidgetFactory.TokenPrimaryHover) { useToken = true },
            pressed = new ThemeColorRef(UIWidgetFactory.TokenPrimaryPressed) { useToken = true },
            selected = new ThemeColorRef(UIWidgetFactory.TokenPrimaryHover) { useToken = true },
            disabled = new ThemeColorRef(UIWidgetFactory.TokenOutline) { useToken = true },
        };

        private void PreviewButton(Theme theme, ButtonVariantAsset v)
        {
            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            // Re-render only when the look key changes (variant name, normal fill, content token, variant).
            string key = $"{v.name}|{ColorUtility.ToHtmlStringRGBA(v.colors.normal.Resolve(theme))}" +
                         $"|{v.contentToken}|{theme.ActiveVariantName}";
            if (key != _btnPreviewKey)
            {
                if (_btnPreview != null) DestroyImmediate(_btnPreview);
                _btnPreview = RenderButton(v.name);
                _btnPreviewKey = key;
            }

            Rect r = GUILayoutUtility.GetRect(260f, 96f, GUILayout.Width(260f));
            if (_btnPreview != null)
                GUI.DrawTexture(r, _btnPreview, ScaleMode.ScaleToFit);
            else
            {
                EditorGUI.DrawRect(r, v.colors.normal.Resolve(theme));
                var label = new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter, normal = { textColor = ResolveToken(theme, v.contentToken) } };
                GUI.Label(r, "Button", label);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Swatch("N", v.colors.normal.Resolve(theme));
                Swatch("H", v.colors.highlighted.Resolve(theme));
                Swatch("P", v.colors.pressed.Resolve(theme));
                Swatch("D", v.colors.disabled.Resolve(theme));
            }
        }

        // Renders a real sample button (current variant, live edits) to a texture; null if rendering is
        // unavailable (no graphics device) — the caller falls back to a faux swatch.
        private static Texture2D RenderButton(string variantName)
        {
            if (string.IsNullOrEmpty(variantName)) return null;
            GameObject go = null;
            try
            {
                var view = new ViewSpec { category = "DesignSystem", viewName = "Preview" };
                view.elements.Add(new ElementSpec
                { kind = "button", id = "DesignSystem/PreviewButton", label = "Button", variant = variantName });
                NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
                go = UISpecGenerator.BuildViewGameObject(view, settings, new GenerateReport());
                Texture2D tex = UIScreenshotter.RenderToTexture(go, 320, 120);
                go = null; // moved into (and destroyed with) the render's preview scene
                return tex;
            }
            catch (System.Exception)
            {
                return null;
            }
            finally
            {
                if (go != null) DestroyImmediate(go);
            }
        }

        // ============================================================ Shapes

        private void DrawShapes(Theme theme)
        {
            var names = theme.GetShapeStyleNames().ToArray();
            if (names.Length > 0)
            {
                _shapeIdx = Mathf.Clamp(_shapeIdx, 0, names.Length - 1);
                _shapeIdx = EditorGUILayout.Popup("Shape style", _shapeIdx, names);
            }
            else EditorGUILayout.LabelField("No shape styles yet.");

            using (new EditorGUILayout.HorizontalScope())
            {
                _newShape = EditorGUILayout.TextField("New style", _newShape);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(_newShape))
                {
                    Undo.RecordObject(theme, "Add shape style");
                    theme.SetShapeStyle(new ShapeStyle { name = _newShape.Trim() });
                    EditorUtility.SetDirty(theme);
                    _newShape = "";
                    names = theme.GetShapeStyleNames().ToArray();
                    _shapeIdx = names.Length - 1;
                }
            }

            if (names.Length > 0 && theme.TryGetShapeStyle(names[Mathf.Clamp(_shapeIdx, 0, names.Length - 1)], out ShapeStyle style))
            {
                NeoGUI.Splitter();
                EditorGUI.BeginChangeCheck();
                float radius = EditorGUILayout.Slider("Corner radius", style.radius, 0f, 48f);
                float border = EditorGUILayout.Slider("Outline width", style.borderWidth, 0f, 12f);
                float soft = EditorGUILayout.Slider("Softness", style.softness, 0f, 24f);
                ColorRef(theme, theme, "Fill", style.fillColor);
                ColorRef(theme, theme, "Outline color", style.borderColor);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(theme, "Edit shape style");
                    style.radius = radius; style.radiusPerCorner = new Vector4(radius, radius, radius, radius);
                    style.borderWidth = border; style.softness = soft;
                    EditorUtility.SetDirty(theme);
                }
                PreviewShape(theme, style);
            }
        }

        private void PreviewShape(Theme theme, ShapeStyle style)
        {
            NeoGUI.Splitter();
            EditorGUILayout.LabelField($"Preview (radius {style.radius:0}px, outline {style.borderWidth:0}px)",
                EditorStyles.miniBoldLabel);
            Rect outer = GUILayoutUtility.GetRect(120f, 60f, GUILayout.Width(120f));
            EditorGUI.DrawRect(outer, style.borderColor.Resolve(theme));
            float b = Mathf.Max(0f, style.borderWidth);
            var inner = new Rect(outer.x + b, outer.y + b, outer.width - 2 * b, outer.height - 2 * b);
            EditorGUI.DrawRect(inner, style.fillColor.Resolve(theme));
        }

        // ============================================================ Presets

        private void DrawPresets()
        {
            EditorGUILayout.LabelField("Component presets (NeoWidgetPreset)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Named component styles referenced by an element's \"preset\". " +
                "Select to edit in the Inspector.", EditorStyles.wordWrappedMiniLabel);

            foreach (NeoWidgetPreset p in NeoWidgetPresets.All.ToList())
            {
                if (p == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{p.presetName}", GUILayout.Width(200f));
                    EditorGUILayout.LabelField(p.targetKind, EditorStyles.miniLabel, GUILayout.Width(80f));
                    if (GUILayout.Button("Select", GUILayout.Width(70f)))
                    { Selection.activeObject = p; EditorGUIUtility.PingObject(p); }
                }
            }

            NeoGUI.Splitter();
            if (GUILayout.Button("New \"Primary Button\" preset"))
                CreatePreset("Primary Button", "button", "primary", "md", "Button", "ButtonLabel");

            using (new EditorGUILayout.HorizontalScope())
            {
                _newPreset = EditorGUILayout.TextField("New preset", _newPreset);
                if (GUILayout.Button("Create", GUILayout.Width(70f)) && !string.IsNullOrWhiteSpace(_newPreset))
                {
                    CreatePreset(_newPreset.Trim(), "button", null, null, null, null);
                    _newPreset = "";
                }
            }
        }

        private static void CreatePreset(string name, string kind, string variant, string size,
            string shape, string textStyle)
        {
            if (NeoWidgetPresets.TryGet(name, out _))
            {
                Debug.LogWarning($"[Neo.UI] A preset named '{name}' already exists.");
                return;
            }
            EnsureFolder(NeoWidgetPresets.PresetsRoot);
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            preset.presetName = name;
            preset.category = kind == "button" ? "Button" : "Custom";
            preset.targetKind = kind;
            preset.variant = variant;
            preset.sizeVariant = size;
            preset.shapeStyle = shape;
            preset.textStyle = textStyle;
            AssetDatabase.CreateAsset(preset, $"{NeoWidgetPresets.PresetsRoot}/{name}.asset");
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.InvalidateDiscovery();
            Selection.activeObject = preset;
            EditorGUIUtility.PingObject(preset);
        }

        // ============================================================ Motion

        // Default animation preset per animator role (NeoUISettings.animatorDefaults) — the same data the
        // Setup wizard seeds and animator Reset() / the widget factory consume. Editing here flows into
        // every newly-added animator and freshly-generated button/view.
        private void DrawMotion(NeoUISettings settings)
        {
            EditorGUILayout.LabelField("Default motion per animator role", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Copied into new animator components and generated buttons/views. " +
                "Pick a preset per role, or clear (✕) to keep the built-in feel.",
                EditorStyles.wordWrappedMiniLabel);

            if (AnimationPresetRegistry.All.Count == 0)
            {
                EditorGUILayout.HelpBox("No animation presets found yet.", MessageType.Info);
                if (GUILayout.Button("Create or Repair Animation Library"))
                {
                    AnimationLibraryBootstrap.CreateOrRepair();
                    AssetDatabase.SaveAssets();
                }
                return;
            }

            foreach (NeoAnimatorRole role in NeoAnimatorRoles.All)
                DrawMotionRole(settings, role);

            NeoGUI.Splitter();
            if (GUILayout.Button(new GUIContent("Create or Repair Animation Library",
                    "Seed/repair the curated preset library these dropdowns choose from")))
            {
                int n = AnimationLibraryBootstrap.CreateOrRepair();
                AssetDatabase.SaveAssets();
                Debug.Log($"[Neo.UI] Animation library: {n} preset(s) created.");
            }
        }

        private void DrawMotionRole(NeoUISettings settings, NeoAnimatorRole role)
        {
            settings.TryGetDefaultAnimation(role.Id, out UIAnimationPreset current);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(role.DisplayName, role.Description), GUILayout.Width(150f));
                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                NeoDropdown.ValuePopup(rect, current != null ? current.fullName : "",
                    () => AnimationPresetRegistry.FullNamesForRole(role.Id),
                    chosen =>
                    {
                        Undo.RecordObject(settings, "Set motion default");
                        settings.SetDefaultAnimation(role.Id, AnimationPresetRegistry.GetByFullName(chosen));
                        EditorUtility.SetDirty(settings);
                    }, emptyLabel: "(built-in)");
                using (new EditorGUI.DisabledScope(current == null))
                    if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    {
                        Undo.RecordObject(settings, "Clear motion default");
                        settings.SetDefaultAnimation(role.Id, null);
                        EditorUtility.SetDirty(settings);
                    }
            }
        }

        // ============================================================ helpers

        // ThemeColorRef editor: "T" toggles token-vs-raw; dirtyTarget is the asset that owns the ref.
        private void ColorRef(Theme theme, Object dirtyTarget, string label, ThemeColorRef cref)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                bool useTok = GUILayout.Toggle(cref.useToken, "T", EditorStyles.miniButton, GUILayout.Width(24f));
                if (useTok != cref.useToken)
                { Undo.RecordObject(dirtyTarget, "toggle token"); cref.useToken = useTok; EditorUtility.SetDirty(dirtyTarget); }

                if (cref.useToken)
                {
                    cref.token = TokenPicker(theme, null, cref.token);
                    EditorGUILayout.ColorField(GUIContent.none, ResolveToken(theme, cref.token), false, true, false,
                        GUILayout.Width(44f));
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    Color c = EditorGUILayout.ColorField(cref.color);
                    if (EditorGUI.EndChangeCheck())
                    { Undo.RecordObject(dirtyTarget, "edit color"); cref.color = c; EditorUtility.SetDirty(dirtyTarget); }
                }
            }
        }

        private static string TokenPicker(Theme theme, string label, string current)
        {
            List<string> tokens = theme.GetTokenNames().ToList();
            tokens.Insert(0, "(none)");
            int idx = Mathf.Max(0, tokens.IndexOf(current ?? "(none)"));
            int n = label == null
                ? EditorGUILayout.Popup(idx, tokens.ToArray())
                : EditorGUILayout.Popup(label, idx, tokens.ToArray());
            return n <= 0 ? null : tokens[n];
        }

        private static Color ResolveToken(Theme theme, string token) =>
            !string.IsNullOrEmpty(token) && theme.TryGetColor(token, out Color c) ? c : Color.gray;

        private static void Swatch(string label, Color c)
        {
            Rect r = GUILayoutUtility.GetRect(34f, 18f, GUILayout.Width(34f));
            EditorGUI.DrawRect(r, c);
            GUI.Label(r, label, EditorStyles.miniLabel);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
