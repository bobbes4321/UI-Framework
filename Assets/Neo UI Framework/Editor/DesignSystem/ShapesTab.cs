using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Shapes" tab: a master–detail editor over <see cref="Theme.ShapeStyles"/>
    /// (<c>ownsLayout: true</c> + <see cref="DesignSystemGUI.BeginSplitPane"/>, like Typography/Presets/
    /// Motion) — a fixed-width, searchable LEFT list of shape styles (each row badged with a fill-color
    /// swatch and, for uniform-radius styles, the corner radius) with a pinned "New style…" create row,
    /// beside a flexible RIGHT detail pane that edits the selected style's FULL field set — radius
    /// (uniform or per-corner, in px or %), outline width, softness, fill mode (solid/gradient) +
    /// gradient second color/angle, elevation, and fill/outline colors — with a real rendered
    /// <see cref="NeoShape"/> preview and Duplicate / Delete actions. The catalog chrome (search / rows /
    /// create row / detail header / empty state) comes from the shared <see cref="DesignSystemCatalog"/>
    /// so Shapes reads like every other converted tab. Split out of the old monolithic
    /// <see cref="NeoDesignSystemWindow"/> (Phase 2.9); extended to full fidelity in Phase 2.7; converted
    /// to master-detail alongside Typography. Keeps the Phase-0 fixes: uniform-radius toggle that never
    /// stomps authored per-corner radii (B5) and routing edits through <c>SetShapeStyle</c> /
    /// <c>RaiseChanged</c> so live targets refresh (B4).
    /// </summary>
    internal static class ShapesTab
    {
        // --- resizable master column (DesignSystemGUI split-pane), mirroring Typography/Presets/Motion ---
        private const float DefaultLeftWidth = 240f;
        private const float LeftMinWidth = 180f;
        private const float RightMinWidth = 320f;
        private const string LeftWidthKey = "NeoUI.DesignSystem.Shapes.LeftWidth";

        // Render size fed to UIScreenshotter for the live preview texture — also the aspect the on-screen
        // preview rect is kept to (see PreviewShape) so a wide detail pane never distorts the swatch.
        private const float PreviewRenderWidth = 240f;
        private const float PreviewRenderHeight = 140f;

        /// <summary> Per-window UI state for the Shapes tab. Disposable so the window destroys the
        /// cached preview texture on disable (mirrors <see cref="TypographyTab.State"/>). </summary>
        internal sealed class State : IDisposable
        {
            // Selection is tracked by style NAME (not an index) so it survives add/remove/reorder; the
            // draw path clamps/falls back when the name disappears (see ResolveSelection).
            public string selectedName;
            public string newStyleName = "";
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

            // Live shape preview: a real NeoShape render (see RenderShape), cached and re-rendered only
            // when its look key changes (never per OnGUI). Falls back to a faux swatch on failure.
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
            "The style's unique name — how presets, widgets and ThemeShapeStyleTarget reference it.");
        private static readonly GUIContent LUniform = new GUIContent("Uniform radius",
            "One radius applied to all four corners; disable to set each corner independently.");
        private static readonly GUIContent LRadiusUnit = new GUIContent("Radius unit",
            "Whether radius values are absolute pixels or a percentage of the shape's shorter side.");
        private static readonly GUIContent LCornerRadius = new GUIContent("Corner radius",
            "Rounding applied to all four corners.");
        private static readonly GUIContent LBorderWidth = new GUIContent("Outline width",
            "Stroke width in px — 0 disables the outline.");
        private static readonly GUIContent LSoftness = new GUIContent("Softness",
            "Edge blur in px — 0 is crisp.");
        private static readonly GUIContent LFillMode = new GUIContent("Fill mode",
            "Solid color, or a two-color gradient fill.");
        private static readonly GUIContent LGradientAngle = new GUIContent("Gradient angle",
            "Linear gradient direction in degrees (0 = left to right, 90 = bottom to top).");
        private static readonly GUIContent LElevation = new GUIContent("Elevation",
            "Drop-shadow level 0-3 — consumed at build time by widgets that call WithElevation (e.g. Card).");

        internal static void Draw(DesignSystemTabContext ctx)
        {
            Theme theme = ctx.theme;
            var s = ctx.State<State>();

            List<string> names = theme.GetShapeStyleNames().ToList();
            ResolveSelection(s, names);

            using (DesignSystemGUI.BeginSplitPane(ctx.window))
            {
                DesignSystemGUI.BeginSplitLeft(ref s.leftScroll, ref s.leftWidth, LeftMinWidth, RightMinWidth);
                DrawBrowsePane(s, theme, names, ctx.window);
                DesignSystemGUI.EndSplitLeft(ref s.leftWidth, LeftMinWidth, RightMinWidth);

                DesignSystemGUI.BeginSplitRight(ref s.rightScroll);
                DrawDetailPane(s, theme);
                DesignSystemGUI.EndSplitRight();
            }
            s.PersistWidth(LeftWidthKey);
        }

        // Keep the selected name valid: fall back to the first style when the current one disappears
        // (removed/renamed), or clear it when the theme has no styles at all.
        private static void ResolveSelection(State s, List<string> names)
        {
            if (names.Count == 0) { s.selectedName = null; return; }
            if (s.selectedName == null || !names.Contains(s.selectedName)) s.selectedName = names[0];
        }

        // ---------------------------------------------------------------- left (browse) pane

        private static void DrawBrowsePane(State s, Theme theme, List<string> names, EditorWindow window)
        {
            EditorGUILayout.LabelField("Shape styles", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Shape styles — surface geometry (radius, outline, fill, elevation) referenced by widgets " +
                "and ThemeShapeStyleTarget.",
                EditorStyles.wordWrappedMiniLabel);

            if (names.Count == 0)
                EditorGUILayout.HelpBox("No shape styles yet. Run Setup → Create or Repair Starter Kit to " +
                    "seed Card/Panel/Control, or add one below.", MessageType.Info);
            else
            {
                DesignSystemCatalog.SearchField(ref s.search);
                string needle = string.IsNullOrEmpty(s.search) ? null : s.search.ToLowerInvariant();
                var visible = new List<string>();
                foreach (string name in names)
                {
                    if (needle != null && !name.ToLowerInvariant().Contains(needle)) continue;
                    if (!theme.TryGetShapeStyle(name, out ShapeStyle st)) continue;

                    visible.Add(name);
                    Color swatch = st.fillColor.Resolve(theme);
                    string badge = st.uniformRadius ? Mathf.RoundToInt(st.radius).ToString() : null;
                    if (DesignSystemCatalog.Row(name, name == s.selectedName,
                            drawAccessory: r => EditorGUI.DrawRect(r, swatch), trailingBadge: badge))
                        s.selectedName = name;
                }
                ApplyListNav(s, visible, window);
            }

            NeoGUI.Splitter();
            DrawNewStyleRow(s, theme, names);
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

        private static void DrawNewStyleRow(State s, Theme theme, List<string> names)
        {
            if (!DesignSystemCatalog.NewItemRow(ref s.newStyleName, "New style…")) return;

            string name = s.newStyleName;   // trimmed + non-blank by NewItemRow
            s.newStyleName = "";
            if (theme.GetShapeStyleNames().Any(n => n == name))
            {
                Debug.LogWarning($"[Neo.UI] A shape style named '{name}' already exists.");
                return;
            }

            Undo.RecordObject(theme, "Add shape style");
            theme.SetShapeStyle(new ShapeStyle { name = name });
            EditorUtility.SetDirty(theme);
            s.selectedName = name;
        }

        // ---------------------------------------------------------------- right (detail) pane

        private static void DrawDetailPane(State s, Theme theme)
        {
            if (s.selectedName == null || !theme.TryGetShapeStyle(s.selectedName, out ShapeStyle style))
            {
                DesignSystemCatalog.EmptyState(
                    "Select a shape style on the left to edit it —\nor add a new one below the list.");
                return;
            }

            DesignSystemCatalog.DetailHeader(style.name, out bool duplicate, out bool remove);
            if (duplicate) { DuplicateStyle(s, theme, style); return; }
            if (remove) { RemoveStyle(s, theme, style); return; }

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(LName, style.name);
            bool uniform = EditorGUILayout.Toggle(LUniform, style.uniformRadius);
            var radiusUnit = (ShapeRadiusUnit)EditorGUILayout.EnumPopup(LRadiusUnit, style.radiusUnit);
            float radius = style.radius;
            Vector4 perCorner = style.radiusPerCorner;
            float maxRadius = radiusUnit == ShapeRadiusUnit.Percent ? 100f : 48f;
            if (uniform)
            {
                radius = EditorGUILayout.Slider(LCornerRadius, style.radius, 0f, maxRadius);
            }
            else
            {
                // Per-corner: the individual fields must never be stomped by the uniform slider (B5).
                // Component order verified against NeoShape.ResolveCornerRadii: x=TL, y=TR, z=BR, w=BL.
                EditorGUILayout.LabelField("Corner radii", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    perCorner.x = Mathf.Max(0f, EditorGUILayout.FloatField("TL", perCorner.x));
                    perCorner.y = Mathf.Max(0f, EditorGUILayout.FloatField("TR", perCorner.y));
                    perCorner.z = Mathf.Max(0f, EditorGUILayout.FloatField("BR", perCorner.z));
                    perCorner.w = Mathf.Max(0f, EditorGUILayout.FloatField("BL", perCorner.w));
                }
            }
            float border = EditorGUILayout.Slider(LBorderWidth, style.borderWidth, 0f, 12f);
            float soft = EditorGUILayout.Slider(LSoftness, style.softness, 0f, 24f);
            var fillMode = (ShapeFillMode)EditorGUILayout.EnumPopup(LFillMode, style.fillMode);
            float gradientAngle = style.gradientAngle;
            if (fillMode != ShapeFillMode.Solid) // conditional display = draw or don't draw
                gradientAngle = EditorGUILayout.Slider(LGradientAngle, style.gradientAngle, 0f, 360f);
            int elevation = EditorGUILayout.IntSlider(LElevation, style.elevation, 0, 3);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(theme, "Edit shape style");
                style.uniformRadius = uniform;
                style.radiusUnit = radiusUnit;
                if (uniform)
                {
                    style.radius = radius;
                    style.radiusPerCorner = new Vector4(radius, radius, radius, radius);
                }
                else
                {
                    style.radiusPerCorner = perCorner;
                }
                style.borderWidth = border;
                style.softness = soft;
                style.fillMode = fillMode;
                style.gradientAngle = gradientAngle;
                style.elevation = elevation;
                TryRenameShapeStyle(theme, style, newName);

                theme.SetShapeStyle(style); // upsert (self-replace when unrenamed) raises RaiseChanged (B4)
                EditorUtility.SetDirty(theme);

                s.selectedName = style.name; // follow a rename
            }

            // Elevation is stored/round-tripped (ThemeBundles, spec, tests) AND now consulted at editor
            // build time by UIWidgetFactory.CreateCard (UIWidgetFactory.ResolveElevation): raising the
            // "Card" style's elevation above 0 overrides Card's built-in level-2 shadow the next time a
            // card/popup is generated or native-authored. It stays a build-time-only concern — binding a
            // style to a bare NeoShape (ThemeShapeStyleTarget) still only recolors/reshapes that ONE
            // shape (ApplyTo/ApplyStyle never read elevation) because runtime theme changes must never
            // structurally add/remove GameObjects (WYSIWYG + no-runtime-churn rules). Editing elevation
            // on styles other than "Card" is stored/round-trips but has no consumer yet — no other
            // built-in widget calls WithElevation.
            EditorGUILayout.HelpBox(
                style.name == UIWidgetFactory.StyleCard
                    ? "Elevation overrides the Card widget's built-in shadow (level 2) the next time a " +
                      "card/popup is built. It only takes effect at build time — existing prefabs need a " +
                      "regenerate/rebuild to pick up a change."
                    : "Elevation is authored and round-trips with this style, but only the built-in " +
                      "\"Card\" style currently drives a shadow (via UIWidgetFactory.CreateCard). Binding " +
                      "this style to a shape (ThemeShapeStyleTarget) restyles that shape only — it won't " +
                      "grow a shadow child.",
                MessageType.Info);

            // Color edits go through ColorRef (own undo/dirty); raise the theme-changed event so live
            // ThemeShapeStyleTargets refresh (B4) — keeps every shapes edit on one notify path.
            EditorGUI.BeginChangeCheck();
            DesignSystemGUI.ColorRef(theme, theme, "Fill", style.fillColor);
            DesignSystemGUI.ColorRef(theme, theme, "Outline color", style.borderColor);
            if (style.fillMode != ShapeFillMode.Solid) // conditional display = draw or don't draw
                DesignSystemGUI.ColorRef(theme, theme, "Gradient to", style.fillColorB);
            if (EditorGUI.EndChangeCheck()) theme.RaiseChanged();

            PreviewShape(s, theme, style);
        }

        private static void DuplicateStyle(State s, Theme theme, ShapeStyle style)
        {
            string unique = UniqueName(theme, style.name);
            var copy = new ShapeStyle
            {
                name = unique,
                radiusUnit = style.radiusUnit,
                uniformRadius = style.uniformRadius,
                radius = style.radius,
                radiusPerCorner = style.radiusPerCorner,
                borderWidth = style.borderWidth,
                softness = style.softness,
                fillMode = style.fillMode,
                gradientAngle = style.gradientAngle,
                elevation = style.elevation,
                // Deep-copy every color ref so the duplicate doesn't alias the original's ThemeColorRef.
                fillColor = new ThemeColorRef
                { useToken = style.fillColor.useToken, token = style.fillColor.token, color = style.fillColor.color },
                borderColor = new ThemeColorRef
                { useToken = style.borderColor.useToken, token = style.borderColor.token, color = style.borderColor.color },
                fillColorB = new ThemeColorRef
                { useToken = style.fillColorB.useToken, token = style.fillColorB.token, color = style.fillColorB.color },
            };
            Undo.RecordObject(theme, "Duplicate shape style");
            theme.SetShapeStyle(copy);
            EditorUtility.SetDirty(theme);
            s.selectedName = unique;
        }

        // "Name 2", "Name 3", … — the first suffix not already taken.
        private static string UniqueName(Theme theme, string baseName)
        {
            var existing = new HashSet<string>(theme.GetShapeStyleNames());
            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!existing.Contains(candidate)) return candidate;
            }
        }

        private static void RemoveStyle(State s, Theme theme, ShapeStyle style)
        {
            if (!EditorUtility.DisplayDialog("Remove shape style",
                    $"Remove shape style '{style.name}'? Shape styles are theme-wide — every " +
                    "NeoShape bound to this name (ThemeShapeStyleTarget) will lose its styling.",
                    "Remove", "Cancel"))
                return;

            // Select a neighbour after removal (the entry that slides into the removed index, clamped).
            List<string> names = theme.GetShapeStyleNames().ToList();
            int idx = names.IndexOf(style.name);

            Undo.RecordObject(theme, "Remove shape style");
            theme.RemoveShapeStyle(style.name); // raises the theme-changed event when removed
            EditorUtility.SetDirty(theme);

            names = theme.GetShapeStyleNames().ToList();
            s.selectedName = names.Count == 0 ? null : names[Mathf.Clamp(idx, 0, names.Count - 1)];
        }

        /// <summary> Renames a shape style in place, guarding against colliding with an existing name.
        /// <paramref name="style"/> must be the SAME reference <see cref="Theme.GetShapeStyle"/> /
        /// <see cref="Theme.TryGetShapeStyle"/> returns (it lives inside the theme's list), so mutating
        /// its <c>name</c> field here IS the rename — no remove-then-re-add churn, and no risk of briefly
        /// dropping the entry (mirrors <see cref="TypographyTab.TryRenameTextStyle"/>). Returns true if a
        /// rename was applied; false for a no-op (blank/unchanged name) or a rejected collision (logged,
        /// name left unchanged). </summary>
        internal static bool TryRenameShapeStyle(Theme theme, ShapeStyle style, string requestedName)
        {
            string trimmed = requestedName?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == style.name) return false;
            if (theme.GetShapeStyleNames().Any(n => n == trimmed))
            {
                Debug.LogWarning($"[Neo.UI] A shape style named '{trimmed}' already exists — rename ignored.");
                return false;
            }
            style.name = trimmed;
            return true;
        }

        // ---------------------------------------------------------------- preview

        private static void PreviewShape(State s, Theme theme, ShapeStyle style)
        {
            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            // Re-render only when the look key changes — every field this tab can edit, plus the active
            // variant (token-backed colors resolve differently per variant).
            string key = $"{style.name}|{style.radiusUnit}|{style.uniformRadius}|{style.radius}|{style.radiusPerCorner}" +
                         $"|{style.borderWidth}|{style.softness}|{style.fillMode}|{style.gradientAngle}" +
                         $"|{ColorUtility.ToHtmlStringRGBA(style.fillColor.Resolve(theme))}" +
                         $"|{ColorUtility.ToHtmlStringRGBA(style.borderColor.Resolve(theme))}" +
                         $"|{ColorUtility.ToHtmlStringRGBA(style.fillColorB.Resolve(theme))}" +
                         $"|{theme.ActiveVariantName}";
            if (key != s.previewKey)
            {
                if (s.preview != null) UnityEngine.Object.DestroyImmediate(s.preview);
                s.preview = RenderShape(theme, style);
                s.previewKey = key;
            }

            // Keep the render texture's aspect while adapting to the pane's available width (capped) —
            // never the old hardcoded 160×90, so the swatch stays proportional as the resizable detail
            // pane widens (mirrors TypographyTab.PreviewStyle's GetAspectRect usage).
            const float aspect = PreviewRenderWidth / PreviewRenderHeight;
            Rect r = GUILayoutUtility.GetAspectRect(aspect, GUILayout.MaxWidth(320f));
            if (s.preview != null)
                GUI.DrawTexture(r, s.preview, ScaleMode.ScaleToFit);
            else
            {
                // Faux fallback swatch — only reached when live rendering is unavailable (no graphics
                // device), e.g. -nographics batch runs.
                EditorGUI.DrawRect(r, style.borderColor.Resolve(theme));
                float b = Mathf.Max(0f, style.borderWidth);
                var inner = new Rect(r.x + b, r.y + b, r.width - 2 * b, r.height - 2 * b);
                EditorGUI.DrawRect(inner, style.fillColor.Resolve(theme));
            }
        }

        // Renders the style applied directly to a bare NeoShape via ShapeStyle.ApplyTo — the SAME method
        // ThemeShapeStyleTarget.ApplyStyle calls on a live bound shape, so this is a faithful preview.
        // Deliberately bypasses ThemeShapeStyleTarget itself: its ExecuteAlways OnEnable/Start apply chain
        // isn't guaranteed to have run inside one synchronous OnGUI call (Start is scheduled for a later
        // editor tick), where a plain Graphic field set is picked up immediately by the forced
        // Canvas.ForceUpdateCanvases() in UIScreenshotter's render pass. Mirrors ButtonsTab.RenderButton's
        // try/catch/null-fallback shape.
        private static Texture2D RenderShape(Theme theme, ShapeStyle style)
        {
            GameObject go = null;
            try
            {
                go = new GameObject("ShapeStylePreview", typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(160f, 90f);
                NeoShape shape = go.AddComponent<NeoShape>();
                shape.shape = ShapeType.RoundedRect;
                style.ApplyTo(shape, theme);
                shape.color = style.fillColor.Resolve(theme);
                Texture2D tex = UIScreenshotter.RenderToTexture(go, (int)PreviewRenderWidth, (int)PreviewRenderHeight);
                go = null; // moved into (and destroyed with) the render's preview scene
                return tex;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
