using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Buttons" tab: a master–detail editor over
    /// <see cref="NeoUISettings.buttonVariants"/> (<c>ownsLayout: true</c> +
    /// <see cref="DesignSystemGUI.BeginSplitPane"/>, like Typography/Presets/Motion) — a fixed-width,
    /// searchable LEFT list of variants (each row's leading swatch shows the variant's resolved Normal
    /// color) with a pinned "New variant…" create row, beside a flexible RIGHT detail pane that edits the
    /// selected variant's name, five per-state colors and content token, a REAL rendered sample button
    /// (re-renders only when its look key changes) and Duplicate/Delete actions. Below the variant form,
    /// a shared "Sizes" section (variant-independent — heights/label styles consulted by every variant's
    /// `size` field) always renders, even with nothing selected. The catalog chrome (search / rows /
    /// create row / detail header / empty state) comes from the shared <see cref="DesignSystemCatalog"/>
    /// so Buttons reads like every other converted tab.
    /// <para>
    /// Carries forward two Phase-0 fixes from the original monolithic window: defensive null-list init
    /// for a pre-migration settings asset (B7a) and unique size names so <c>TryGetButtonSize</c> lookups
    /// are never shadowed by a duplicate (B7b).
    /// </para>
    /// </summary>
    internal static class ButtonsTab
    {
        // --- resizable master column (DesignSystemGUI split-pane), mirroring Typography/Presets/Motion ---
        private const float DefaultLeftWidth = 240f;
        private const float LeftMinWidth = 180f;
        private const float RightMinWidth = 320f;
        private const string LeftWidthKey = "NeoUI.DesignSystem.Buttons.LeftWidth";

        // Fixed render target for the sample-button preview (a button's proportions don't vary enough
        // across variants to warrant Typography's size-driven render target).
        private const int PreviewRenderWidth = 320;
        private const int PreviewRenderHeight = 120;

        /// <summary> Per-window UI state for the Buttons tab. Disposable so the window destroys the
        /// cached preview texture on disable (the old <c>OnDisable</c> behavior). </summary>
        internal sealed class State : IDisposable
        {
            // Selection is tracked by variant NAME (not an index) so it survives add/remove/reorder; the
            // draw path clamps/falls back when the name disappears (see ResolveSelection).
            public string selectedName;
            public string newVariantName = "";
            public string search = "";

            // Independent scroll positions for the two master-detail panes (caller-owned per the
            // split-pane helper's contract; kept here so nothing allocates per OnGUI).
            public Vector2 leftScroll;
            public Vector2 rightScroll;

            // Draggable master-column width (caller-owned + SessionState-persisted; see LeftWidthKey).
            public float leftWidth;
            private float _persistedWidth;

            /// <summary> Seeds <see cref="leftWidth"/> from SessionState (or <paramref name="def"/> first
            /// time) — call once from the state factory. </summary>
            public void LoadWidth(string key, float def) => leftWidth = _persistedWidth = SessionState.GetFloat(key, def);

            /// <summary> Writes <see cref="leftWidth"/> back to SessionState only when a drag actually
            /// changed it (never a per-OnGUI write) — call after the split pane closes. </summary>
            public void PersistWidth(string key)
            {
                if (leftWidth == _persistedWidth) return;
                SessionState.SetFloat(key, leftWidth);
                _persistedWidth = leftWidth;
            }

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

        internal static object CreateState()
        {
            var s = new State();
            s.LoadWidth(LeftWidthKey, DefaultLeftWidth);
            return s;
        }

        // Cached field labels (with tooltips) — static so the form never allocates a GUIContent per OnGUI.
        private static readonly GUIContent LName = new GUIContent("Name",
            "The variant id — how a spec's button `variant` field and the Starter Kit reference it.");
        private static readonly GUIContent LContentToken = new GUIContent("Content (label/icon)",
            "Theme token coloring the button's label + icon for this variant.");
        private static readonly GUIContent LSizesHeader = new GUIContent("Sizes (shared across all variants)",
            "Heights + label styles available to every variant via a widget's `size` field (sm/md/lg/…) " +
            "— editing here affects all variants using a given size, not just the one selected above.");
        private static readonly GUIContent LSizeName = new GUIContent("Name",
            "The size's spec string (\"size\": \"xl\") — must stay unique for lookups to resolve it.");
        private static readonly GUIContent LSizeHeight = new GUIContent("Height",
            "Button height in pixels at this size.");
        private static readonly GUIContent LSizeLabelStyle = new GUIContent("Label style",
            "Theme TextStyle name applied to the button's label at this size.");

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

            List<string> names = settings.buttonVariants.Select(v => v.name).ToList();
            ResolveSelection(s, names);

            using (DesignSystemGUI.BeginSplitPane(ctx.window))
            {
                DesignSystemGUI.BeginSplitLeft(ref s.leftScroll, ref s.leftWidth, LeftMinWidth, RightMinWidth);
                DrawBrowsePane(s, settings, theme, names, ctx.window);
                DesignSystemGUI.EndSplitLeft(ref s.leftWidth, LeftMinWidth, RightMinWidth);

                DesignSystemGUI.BeginSplitRight(ref s.rightScroll);
                DrawDetailPane(s, settings, theme);
                DesignSystemGUI.EndSplitRight();
            }
            s.PersistWidth(LeftWidthKey);
        }

        // Keep the selected name valid: fall back to the first variant when the current one disappears
        // (removed/renamed), or clear it when there are no variants at all.
        private static void ResolveSelection(State s, List<string> names)
        {
            if (names.Count == 0) { s.selectedName = null; return; }
            if (s.selectedName == null || !names.Contains(s.selectedName)) s.selectedName = names[0];
        }

        private static ButtonVariantAsset FindVariant(NeoUISettings settings, string name) =>
            string.IsNullOrEmpty(name) ? null : settings.buttonVariants.FirstOrDefault(v => v.name == name);

        // ---------------------------------------------------------------- left (browse) pane

        private static void DrawBrowsePane(State s, NeoUISettings settings, Theme theme, List<string> names,
            EditorWindow window)
        {
            EditorGUILayout.LabelField("Variants", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Button variants — referenced by spec `variant` and seeded by the Starter Kit.",
                EditorStyles.wordWrappedMiniLabel);

            if (names.Count == 0)
                EditorGUILayout.LabelField(
                    "No variants yet — run Setup → Create or Repair Starter Kit to seed the five " +
                    "built-ins (primary/secondary/ghost/danger/success), or add one below.",
                    EditorStyles.wordWrappedMiniLabel);
            else
            {
                DesignSystemCatalog.SearchField(ref s.search);
                string needle = string.IsNullOrEmpty(s.search) ? null : s.search.ToLowerInvariant();
                var visible = new List<string>();
                foreach (ButtonVariantAsset v in settings.buttonVariants)
                {
                    if (needle != null && !v.name.ToLowerInvariant().Contains(needle)) continue;
                    visible.Add(v.name);
                    ButtonVariantAsset captured = v; // capture for the accessory closure
                    if (DesignSystemCatalog.Row(v.name, v.name == s.selectedName,
                            rect => EditorGUI.DrawRect(rect, captured.colors.normal.Resolve(theme))))
                        s.selectedName = v.name;
                }
                ApplyListNav(s, visible, window);
            }

            NeoGUI.Splitter();
            DrawNewVariantRow(s, settings, names);
        }

        // Arrow-key browse navigation over the VISIBLE (search-filtered) list — selection stays
        // name-based; only repaints when the delta actually moves it.
        private static void ApplyListNav(State s, List<string> visible, EditorWindow window)
        {
            int delta = DesignSystemCatalog.ListNavDelta();
            if (delta == 0 || visible.Count == 0) return;
            int idx = Mathf.Max(0, visible.IndexOf(s.selectedName));
            string next = visible[Mathf.Clamp(idx + delta, 0, visible.Count - 1)];
            if (next == s.selectedName) return;
            s.selectedName = next;
            window.Repaint();
        }

        private static void DrawNewVariantRow(State s, NeoUISettings settings, List<string> names)
        {
            if (!DesignSystemCatalog.NewItemRow(ref s.newVariantName, "New variant…")) return;

            string name = s.newVariantName; // trimmed + non-blank by NewItemRow
            s.newVariantName = "";
            if (names.Contains(name))
            {
                Debug.LogWarning($"[Neo.UI] A button variant named '{name}' already exists.");
                return;
            }

            Undo.RecordObject(settings, "Add button variant");
            settings.buttonVariants.Add(new ButtonVariantAsset
            {
                name = name,
                contentToken = UIWidgetFactory.TokenTextOnPrimary,
                colors = DefaultVariantColors(),
            });
            EditorUtility.SetDirty(settings);
            s.selectedName = name;
        }

        // ---------------------------------------------------------------- right (detail) pane

        private static void DrawDetailPane(State s, NeoUISettings settings, Theme theme)
        {
            ButtonVariantAsset v = FindVariant(settings, s.selectedName);
            if (v == null)
                DesignSystemCatalog.EmptyState(
                    "Select a button variant on the left to edit it —\nor add a new one below the list.");
            else
                DrawVariantForm(s, settings, theme, v);

            NeoGUI.Splitter();
            DrawSizesSection(settings);
        }

        private static void DrawVariantForm(State s, NeoUISettings settings, Theme theme, ButtonVariantAsset v)
        {
            DesignSystemCatalog.DetailHeader(v.name, out bool duplicate, out bool remove);
            if (duplicate) { DuplicateVariant(s, settings, v); return; }
            if (remove) { RemoveVariant(s, settings, v); return; }

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(LName, v.name);
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
                s.selectedName = v.name; // follow a rename
            }
            DesignSystemGUI.TokenPicker(theme, LContentToken.text, v.contentToken, chosen =>
            {
                Undo.RecordObject(settings, "Edit button variant");
                v.contentToken = chosen;
                EditorUtility.SetDirty(settings);
            });

            PreviewButton(s, theme, v);
        }

        private static void DuplicateVariant(State s, NeoUISettings settings, ButtonVariantAsset v)
        {
            string unique = UniqueVariantName(settings, v.name);
            var copy = new ButtonVariantAsset
            {
                name = unique,
                contentToken = v.contentToken,
                // Fresh SelectableColorSet with FRESH ThemeColorRef instances — never share refs with the
                // original variant, or editing one would silently repaint the other.
                colors = new SelectableColorSet
                {
                    normal = CopyColorRef(v.colors.normal),
                    highlighted = CopyColorRef(v.colors.highlighted),
                    pressed = CopyColorRef(v.colors.pressed),
                    selected = CopyColorRef(v.colors.selected),
                    disabled = CopyColorRef(v.colors.disabled),
                },
            };
            Undo.RecordObject(settings, "Duplicate button variant");
            settings.buttonVariants.Add(copy);
            EditorUtility.SetDirty(settings);
            s.selectedName = unique;
        }

        private static ThemeColorRef CopyColorRef(ThemeColorRef src) =>
            new ThemeColorRef { useToken = src.useToken, token = src.token, color = src.color };

        // "Name 2", "Name 3", … — the first suffix not already taken.
        private static string UniqueVariantName(NeoUISettings settings, string baseName)
        {
            var existing = new HashSet<string>(settings.buttonVariants.Select(bv => bv.name));
            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!existing.Contains(candidate)) return candidate;
            }
        }

        private static void RemoveVariant(State s, NeoUISettings settings, ButtonVariantAsset v)
        {
            if (!EditorUtility.DisplayDialog("Remove button variant",
                    $"Remove the button variant \"{v.name}\"?\n\nButtons generated with this variant name " +
                    "will fall back to the built-in colors on the next build — the reference never throws " +
                    "or breaks the build, it just stops looking custom.",
                    "Remove", "Cancel"))
                return;

            // Select a neighbour after removal (the entry that slides into the removed index, clamped).
            List<string> names = settings.buttonVariants.Select(bv => bv.name).ToList();
            int idx = names.IndexOf(v.name);

            Undo.RecordObject(settings, "Remove button variant");
            settings.buttonVariants.Remove(v);
            EditorUtility.SetDirty(settings);

            names = settings.buttonVariants.Select(bv => bv.name).ToList();
            s.selectedName = names.Count == 0 ? null : names[Mathf.Clamp(idx, 0, names.Count - 1)];
        }

        private static SelectableColorSet DefaultVariantColors() => new SelectableColorSet
        {
            normal = new ThemeColorRef(UIWidgetFactory.TokenPrimary) { useToken = true },
            highlighted = new ThemeColorRef(UIWidgetFactory.TokenPrimaryHover) { useToken = true },
            pressed = new ThemeColorRef(UIWidgetFactory.TokenPrimaryPressed) { useToken = true },
            selected = new ThemeColorRef(UIWidgetFactory.TokenPrimaryHover) { useToken = true },
            disabled = new ThemeColorRef(UIWidgetFactory.TokenOutline) { useToken = true },
        };

        // ---------------------------------------------------------------- preview

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

            // Keep the texture's aspect: derive height from the available pane width (capped), so the
            // preview stays proportional as the resizable pane widens — never the old hardcoded 260×96.
            float aspect = (float)PreviewRenderWidth / PreviewRenderHeight;
            Rect r = GUILayoutUtility.GetAspectRect(aspect, GUILayout.MaxWidth(360f));
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
                Texture2D tex = UIScreenshotter.RenderToTexture(go, PreviewRenderWidth, PreviewRenderHeight);
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

        // ---------------------------------------------------------------- sizes (shared, variant-independent)

        // Always drawn below the variant form (or the empty state) — sizes aren't owned by any one
        // variant, so they stay visible regardless of what's selected on the left.
        private static void DrawSizesSection(NeoUISettings settings)
        {
            EditorGUILayout.LabelField(LSizesHeader, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(LSizeName, EditorStyles.miniLabel, GUILayout.Width(120f));
                EditorGUILayout.LabelField(LSizeHeight, EditorStyles.miniLabel, GUILayout.Width(60f));
                EditorGUILayout.LabelField(LSizeLabelStyle, EditorStyles.miniLabel);
            }
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
