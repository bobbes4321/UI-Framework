using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Buttons" tab: pick/add/edit/remove button variants (per-state colors +
    /// content token), edit sizes, and see a REAL rendered sample button that re-renders only when its
    /// look key changes. Split out of the old monolithic <see cref="NeoDesignSystemWindow"/> (Phase 2.9)
    /// — draw code unchanged, including the Phase-0 fixes: defensive null-list init (B7a) and unique
    /// size names (B7b). The live preview texture lives on the tab's <see cref="State"/>, which is
    /// <see cref="IDisposable"/> so the window destroys it on disable.
    /// </summary>
    internal static class ButtonsTab
    {
        /// <summary> Per-window UI state for the Buttons tab. Disposable so the window destroys the
        /// cached preview texture on disable (the old <c>OnDisable</c> behavior). </summary>
        internal sealed class State : IDisposable
        {
            public int btnIdx;
            public string newBtnVariant = "";

            // Live button preview: a real render of a sample button, cached and re-rendered only when
            // its look key changes (never per OnGUI). Falls back to a faux swatch if rendering fails.
            public Texture2D preview;
            public string previewKey;

            public void Dispose()
            {
                if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
                preview = null;
                previewKey = null;
            }
        }

        internal static object CreateState() => new State();

        // Fallback preview label (used only when live rendering is unavailable) — a cached static so the
        // faux-preview path never allocates a GUIStyle per OnGUI pass; its text color is re-applied on
        // the shared instance each draw since the content token can change.
        private static GUIStyle _btnPreviewFallbackLabel;

        private static GUIStyle BtnPreviewFallbackLabel =>
            _btnPreviewFallbackLabel ?? (_btnPreviewFallbackLabel = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleCenter });

        internal static void Draw(DesignSystemTabContext ctx)
        {
            NeoUISettings settings = ctx.settings;
            Theme theme = ctx.theme;
            var s = ctx.State<State>();

            // A pre-migration settings asset can deserialize these lists as null (B7a) — init defensively.
            settings.buttonVariants ??= new List<ButtonVariantAsset>();
            settings.buttonSizes ??= new List<ButtonSizeAsset>();

            EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
            int variantCount = settings.buttonVariants.Count;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (variantCount > 0)
                {
                    s.btnIdx = Mathf.Clamp(s.btnIdx, 0, variantCount - 1);
                    Rect rect = EditorGUILayout.GetControlRect();
                    rect = EditorGUI.PrefixLabel(rect, new GUIContent("Variant"));
                    NeoDropdown.ValuePopup(rect, settings.buttonVariants[s.btnIdx].name,
                        () => settings.buttonVariants.Select(v => v.name).ToList(),
                        chosen =>
                        {
                            int idx = settings.buttonVariants.FindIndex(v => v.name == chosen);
                            if (idx >= 0) s.btnIdx = idx;
                        });
                }
                else EditorGUILayout.LabelField("No variants yet — run Setup → Create or Repair Starter Kit " +
                    "to seed the five built-ins (primary/secondary/ghost/danger/success), or add one below.",
                    EditorStyles.wordWrappedLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                s.newBtnVariant = EditorGUILayout.TextField("New variant", s.newBtnVariant);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(s.newBtnVariant))
                {
                    Undo.RecordObject(settings, "Add button variant");
                    settings.buttonVariants.Add(new ButtonVariantAsset
                    {
                        name = s.newBtnVariant.Trim(),
                        contentToken = UIWidgetFactory.TokenTextOnPrimary,
                        colors = DefaultVariantColors(),
                    });
                    EditorUtility.SetDirty(settings);
                    s.btnIdx = settings.buttonVariants.Count - 1;
                    s.newBtnVariant = "";
                    variantCount = settings.buttonVariants.Count;
                }
            }

            if (variantCount > 0 && s.btnIdx < settings.buttonVariants.Count)
            {
                ButtonVariantAsset v = settings.buttonVariants[s.btnIdx];
                NeoGUI.Splitter();
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField("Name", v.name);
                DesignSystemGUI.ColorRef(theme, settings, "Normal", v.colors.normal);
                DesignSystemGUI.ColorRef(theme, settings, "Hover", v.colors.highlighted);
                DesignSystemGUI.ColorRef(theme, settings, "Pressed", v.colors.pressed);
                DesignSystemGUI.ColorRef(theme, settings, "Selected", v.colors.selected);
                DesignSystemGUI.ColorRef(theme, settings, "Disabled", v.colors.disabled);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(settings, "Edit button variant");
                    v.name = newName;
                    EditorUtility.SetDirty(settings);
                }
                DesignSystemGUI.TokenPicker(theme, "Content (label/icon)", v.contentToken, chosen =>
                {
                    Undo.RecordObject(settings, "Edit button variant");
                    v.contentToken = chosen;
                    EditorUtility.SetDirty(settings);
                });

                PreviewButton(s, theme, v);

                if (GUILayout.Button("Remove variant"))
                {
                    Undo.RecordObject(settings, "Remove button variant");
                    settings.buttonVariants.RemoveAt(s.btnIdx);
                    EditorUtility.SetDirty(settings);
                }
            }

            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Sizes", EditorStyles.boldLabel);
            for (int i = 0; i < settings.buttonSizes.Count; i++)
            {
                ButtonSizeAsset size = settings.buttonSizes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string n = EditorGUILayout.TextField(size.name, GUILayout.Width(120f));
                    float h = EditorGUILayout.FloatField(size.height, GUILayout.Width(60f));
                    string ls = EditorGUILayout.TextField(size.labelStyle);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(settings, "Edit size");
                        size.name = n; size.height = h; size.labelStyle = ls;
                        EditorUtility.SetDirty(settings);
                    }
                }
            }
            if (GUILayout.Button("Add size"))
            {
                Undo.RecordObject(settings, "Add size");
                // Unique name so TryGetButtonSize lookups aren't shadowed by duplicate "xl"s (B7b).
                settings.buttonSizes.Add(new ButtonSizeAsset
                { name = UniqueSizeName(settings.buttonSizes, "xl"), height = 64f, labelStyle = "ButtonLabel" });
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

        private static void PreviewButton(State s, Theme theme, ButtonVariantAsset v)
        {
            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            // Re-render only when the look key changes (variant name, normal fill, content token, variant).
            string key = $"{v.name}|{ColorUtility.ToHtmlStringRGBA(v.colors.normal.Resolve(theme))}" +
                         $"|{v.contentToken}|{theme.ActiveVariantName}";
            if (key != s.previewKey)
            {
                if (s.preview != null) UnityEngine.Object.DestroyImmediate(s.preview);
                s.preview = RenderButton(v.name);
                s.previewKey = key;
            }

            Rect r = GUILayoutUtility.GetRect(260f, 96f, GUILayout.Width(260f));
            if (s.preview != null)
                GUI.DrawTexture(r, s.preview, ScaleMode.ScaleToFit);
            else
            {
                EditorGUI.DrawRect(r, v.colors.normal.Resolve(theme));
                GUIStyle label = BtnPreviewFallbackLabel;
                label.normal.textColor = DesignSystemGUI.ResolveToken(theme, v.contentToken);
                GUI.Label(r, "Button", label);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                DesignSystemGUI.Swatch("N", v.colors.normal.Resolve(theme));
                DesignSystemGUI.Swatch("H", v.colors.highlighted.Resolve(theme));
                DesignSystemGUI.Swatch("P", v.colors.pressed.Resolve(theme));
                DesignSystemGUI.Swatch("D", v.colors.disabled.Resolve(theme));
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
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // "xl", "xl2", "xl3", … — first name not already taken by an existing size (B7b).
        private static string UniqueSizeName(List<ButtonSizeAsset> sizes, string baseName)
        {
            bool Taken(string n) => sizes.Any(size => string.Equals(size.name, n, System.StringComparison.Ordinal));
            if (!Taken(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                string candidate = baseName + i;
                if (!Taken(candidate)) return candidate;
            }
        }
    }
}
