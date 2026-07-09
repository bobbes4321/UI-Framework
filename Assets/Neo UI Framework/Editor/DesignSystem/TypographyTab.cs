using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Typography" tab: a master–detail editor over <see cref="Theme.TextStyles"/>
    /// (<c>ownsLayout: true</c> + <see cref="DesignSystemGUI.BeginSplitPane"/>, like Presets/Motion) — a
    /// fixed-width, searchable LEFT list of text styles (each row badged with its size) with a pinned
    /// "New style…" create row, beside a flexible RIGHT detail pane that edits the selected style (font,
    /// size, style flags, spacing, color) with a rendered sample line and Duplicate / Delete actions.
    /// The catalog chrome (search / rows / create row / detail header / empty state) comes from the shared
    /// <see cref="DesignSystemCatalog"/> so Typography reads like every other converted tab. Same
    /// undo/dirty discipline as Colors/Shapes: every field edit is <c>Undo.RecordObject(theme)</c> +
    /// <c>theme.SetTextStyle(...)</c> (upsert, raises <see cref="Theme.RaiseChanged"/> so live
    /// <see cref="ThemeTextStyleTarget"/>s refresh) + <c>EditorUtility.SetDirty(theme)</c>.
    /// </summary>
    internal static class TypographyTab
    {
        // --- resizable master column (DesignSystemGUI split-pane), mirroring Presets/Motion ---
        private const float DefaultLeftWidth = 240f;
        private const float LeftMinWidth = 180f;
        private const float RightMinWidth = 320f;
        private const string LeftWidthKey = "NeoUI.DesignSystem.Typography.LeftWidth";
        private const string SortBySizeKey = "NeoUI.DesignSystem.Typography.SortBySize";

        /// <summary> Per-window UI state for the Typography tab. Disposable so the window destroys the
        /// cached preview texture on disable. </summary>
        internal sealed class State : IDisposable
        {
            // Selection is tracked by style NAME (not an index) so it survives add/remove/reorder; the
            // draw path clamps/falls back when the name disappears (see ResolveSelection).
            public string selectedName;
            public string newStyleName = "";
            public string search = "";

            // Display-only ordering toggle (never reorders Theme.TextStyles itself) — persisted so the
            // choice survives window close/reopen, mirroring the leftWidth persistence discipline below.
            public bool sortBySize;

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

            // Live sample-line preview: a real render of a "text" element using the selected style,
            // cached and re-rendered only when its look key changes (never per OnGUI).
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
            s.sortBySize = SessionState.GetBool(SortBySizeKey, false);
            return s;
        }

        /// <summary> The registry entry the orchestrator plugs into <see cref="NeoDesignSystemTabs"/>'s
        /// built-ins list, ordered between Colors (0) and Buttons (10). <c>ownsLayout</c> so the tab drives
        /// its own dual-pane scroll containers (see class summary). </summary>
        internal static DesignSystemTabDescriptor Descriptor =>
            new DesignSystemTabDescriptor("typography", "Typography", 5, CreateState, Draw, ownsLayout: true);

        // Cached field labels (with tooltips) — static so the form never allocates a GUIContent per OnGUI.
        private static readonly GUIContent LName = new GUIContent("Name",
            "The style's unique name — how a spec's textStyle, presets and widget labels reference it.");
        private static readonly GUIContent LFont = new GUIContent("Font",
            "TMP font asset for this style; leave empty to inherit the default font.");
        private static readonly GUIContent LSize = new GUIContent("Size", "Font size in points.");
        private static readonly GUIContent LStyle = new GUIContent("Style",
            "Bold / italic / underline / … flags applied to the text.");
        private static readonly GUIContent LCharSpacing = new GUIContent("Character spacing",
            "Extra tracking between characters (TMP characterSpacing units).");
        private static readonly GUIContent LLineSpacing = new GUIContent("Line spacing",
            "Extra leading between lines (TMP lineSpacing units).");
        private static readonly GUIContent LSortBySize = new GUIContent("↕ size",
            "Sort the list by font size (largest first) instead of theme order. Display order only — " +
            "never reorders Theme.TextStyles.");

        // Fallback preview label (used only when live rendering is unavailable) — a cached static so the
        // faux-preview path never allocates a GUIStyle per OnGUI pass; size/style/color are re-applied on
        // the shared instance each draw since the selected text style can change.
        private static GUIStyle _fallbackLabel;

        private static GUIStyle FallbackLabel =>
            _fallbackLabel ?? (_fallbackLabel = new GUIStyle(EditorStyles.label) { wordWrap = true });

        internal static void Draw(DesignSystemTabContext ctx)
        {
            Theme theme = ctx.theme;
            var s = ctx.State<State>();

            List<string> names = theme.GetTextStyleNames().ToList();
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
            EditorGUILayout.LabelField("Text styles", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The theme's type scale — referenced by spec textStyle, presets and widget labels.",
                EditorStyles.wordWrappedMiniLabel);

            if (names.Count == 0)
                EditorGUILayout.HelpBox("No text styles yet. Run Setup → Create or Repair Starter Kit to " +
                    "seed the type scale, or add one below.", MessageType.Info);
            else
            {
                DesignSystemCatalog.SearchField(ref s.search);

                bool sortBySize = GUILayout.Toggle(s.sortBySize, LSortBySize, EditorStyles.miniButton,
                    GUILayout.Width(56f));
                if (sortBySize != s.sortBySize)
                {
                    s.sortBySize = sortBySize;
                    SessionState.SetBool(SortBySizeKey, sortBySize);
                }

                // Display order only — never mutates Theme.TextStyles. LINQ's OrderByDescending is a
                // stable sort, so styles tied on size keep their original theme order.
                IEnumerable<string> ordered = s.sortBySize
                    ? names.OrderByDescending(n => theme.TryGetTextStyle(n, out TextStyle st) ? st.size : 0f)
                    : names;

                string needle = string.IsNullOrEmpty(s.search) ? null : s.search.ToLowerInvariant();
                var visible = new List<string>();
                foreach (string name in ordered)
                {
                    if (needle != null && !name.ToLowerInvariant().Contains(needle)) continue;
                    visible.Add(name);
                    string badge = theme.TryGetTextStyle(name, out TextStyle st)
                        ? Mathf.RoundToInt(st.size).ToString()
                        : null;
                    if (DesignSystemCatalog.Row(name, name == s.selectedName, trailingBadge: badge))
                        s.selectedName = name;
                }
                ApplyListNav(s, visible, window);
            }

            NeoGUI.Splitter();
            DrawNewStyleRow(s, theme, names);
        }

        // Arrow-key browse navigation over the VISIBLE (search-filtered, display-ordered) list — selection
        // stays name-based; only repaints when the delta actually moves it.
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
            if (theme.GetTextStyleNames().Any(n => n == name))
            {
                Debug.LogWarning($"[Neo.UI] A text style named '{name}' already exists.");
                return;
            }

            // Seed size from the currently-selected style so a new style isn't a jarring 16px outlier next
            // to a project's existing scale; falls back to 16 when none exist yet.
            float seedSize = 16f;
            if (s.selectedName != null && theme.TryGetTextStyle(s.selectedName, out TextStyle seedFrom))
                seedSize = seedFrom.size;

            Undo.RecordObject(theme, "Add text style");
            theme.SetTextStyle(new TextStyle { name = name, size = seedSize });
            EditorUtility.SetDirty(theme);
            s.selectedName = name;
        }

        // ---------------------------------------------------------------- right (detail) pane

        private static void DrawDetailPane(State s, Theme theme)
        {
            if (s.selectedName == null || !theme.TryGetTextStyle(s.selectedName, out TextStyle style))
            {
                DesignSystemCatalog.EmptyState(
                    "Select a text style on the left to edit it —\nor add a new one below the list.");
                return;
            }

            DesignSystemCatalog.DetailHeader(style.name, out bool duplicate, out bool remove);
            if (duplicate) { DuplicateStyle(s, theme, style); return; }
            if (remove) { RemoveStyle(s, theme, style); return; }

            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(LName, style.name);
            var font = (TMP_FontAsset)EditorGUILayout.ObjectField(LFont, style.font, typeof(TMP_FontAsset), false);
            float size = Mathf.Max(1f, EditorGUILayout.FloatField(LSize, style.size));
            var fontStyleFlags = (FontStyles)EditorGUILayout.EnumFlagsField(LStyle, style.fontStyle);
            float charSpacing = EditorGUILayout.FloatField(LCharSpacing, style.characterSpacing);
            float lineSpacing = EditorGUILayout.FloatField(LLineSpacing, style.lineSpacing);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(theme, "Edit text style");
                style.font = font;
                style.size = size;
                style.fontStyle = fontStyleFlags;
                style.characterSpacing = charSpacing;
                style.lineSpacing = lineSpacing;
                TryRenameTextStyle(theme, style, newName);

                theme.SetTextStyle(style); // upsert (self-replace when unrenamed) + RaiseChanged
                EditorUtility.SetDirty(theme);

                s.selectedName = style.name; // follow a rename
            }

            // Color edits go through ColorRef (own undo/dirty); raise the theme-changed event so live
            // ThemeTextStyleTargets refresh — same one-notify-path discipline as ShapesTab (B4).
            EditorGUI.BeginChangeCheck();
            DesignSystemGUI.ColorRef(theme, theme, "Color", style.color);
            if (EditorGUI.EndChangeCheck()) theme.RaiseChanged();

            PreviewStyle(s, theme, style);
        }

        private static void DuplicateStyle(State s, Theme theme, TextStyle style)
        {
            string unique = UniqueName(theme, style.name);
            var copy = new TextStyle
            {
                name = unique,
                font = style.font,
                size = style.size,
                fontStyle = style.fontStyle,
                characterSpacing = style.characterSpacing,
                lineSpacing = style.lineSpacing,
                // Deep-copy the color ref so the duplicate doesn't alias the original's ThemeColorRef.
                color = new ThemeColorRef
                { useToken = style.color.useToken, token = style.color.token, color = style.color.color },
            };
            Undo.RecordObject(theme, "Duplicate text style");
            theme.SetTextStyle(copy);
            EditorUtility.SetDirty(theme);
            s.selectedName = unique;
        }

        // "Name 2", "Name 3", … — the first suffix not already taken.
        private static string UniqueName(Theme theme, string baseName)
        {
            var existing = new HashSet<string>(theme.GetTextStyleNames());
            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!existing.Contains(candidate)) return candidate;
            }
        }

        private static void RemoveStyle(State s, Theme theme, TextStyle style)
        {
            if (!EditorUtility.DisplayDialog("Remove text style",
                    $"Remove the text style \"{style.name}\"?\n\nElements and presets still referencing it " +
                    "by name will simply stop being re-styled on the next generate — a dangling reference " +
                    "never throws or blanks the text.",
                    "Remove", "Cancel"))
                return;

            // Select a neighbour after removal (the entry that slides into the removed index, clamped).
            List<string> names = theme.GetTextStyleNames().ToList();
            int idx = names.IndexOf(style.name);

            Undo.RecordObject(theme, "Remove text style");
            theme.RemoveTextStyle(style.name);
            EditorUtility.SetDirty(theme);

            names = theme.GetTextStyleNames().ToList();
            s.selectedName = names.Count == 0 ? null : names[Mathf.Clamp(idx, 0, names.Count - 1)];
        }

        /// <summary> Renames a text style in place, guarding against colliding with an existing name.
        /// <paramref name="style"/> must be the SAME reference <see cref="Theme.GetTextStyle"/> /
        /// <see cref="Theme.TryGetTextStyle"/> returns (it lives inside the theme's list), so mutating
        /// its <c>name</c> field here IS the rename — no remove-then-re-add churn, and no risk of
        /// briefly dropping the entry. Returns true if a rename was applied; false for a no-op
        /// (blank/unchanged name) or a rejected collision (logged, name left unchanged). </summary>
        internal static bool TryRenameTextStyle(Theme theme, TextStyle style, string requestedName)
        {
            string trimmed = requestedName?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == style.name) return false;
            if (theme.GetTextStyleNames().Any(n => n == trimmed))
            {
                Debug.LogWarning($"[Neo.UI] A text style named '{trimmed}' already exists — rename ignored.");
                return false;
            }
            style.name = trimmed;
            return true;
        }

        // ---------------------------------------------------------------- preview

        private static void PreviewStyle(State s, Theme theme, TextStyle style)
        {
            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            // Scale the render target with the font size so a large display style ("The quick brown fox" at
            // 48–64px) fits on one unclipped line INSIDE the render canvas before ScaleToFit shrinks it to
            // the pane — the old fixed 320×96 target wrapped/clipped big styles. 1 world unit = 1px in the
            // screenshot rig (ortho size = height/2), so width scales roughly with glyph run length.
            int renderW = Mathf.Clamp(Mathf.RoundToInt(style.size * 12f), 320, 1280);
            int renderH = Mathf.Clamp(Mathf.RoundToInt(style.size * 2.4f), 96, 320);

            // Re-render only when the look key changes (name, font, size, style flags, spacing, resolved
            // color, active variant, render size) — never per OnGUI.
            string key = $"{style.name}|{(style.font != null ? style.font.name : "-")}|{style.size:0.##}|" +
                         $"{style.fontStyle}|{style.characterSpacing:0.##}|{style.lineSpacing:0.##}|" +
                         $"{ColorUtility.ToHtmlStringRGBA(style.color.Resolve(theme))}|{theme.ActiveVariantName}|" +
                         $"{renderW}x{renderH}";
            if (key != s.previewKey)
            {
                if (s.preview != null) UnityEngine.Object.DestroyImmediate(s.preview);
                s.preview = RenderSample(style.name, renderW, renderH);
                s.previewKey = key;
            }

            // Keep the texture's aspect: derive height from the available pane width (capped), so the
            // preview stays proportional as the resizable pane widens — never the old hardcoded 320×96.
            float aspect = (float)renderW / renderH;
            Rect r = GUILayoutUtility.GetAspectRect(aspect, GUILayout.MaxWidth(480f));
            if (s.preview != null)
                GUI.DrawTexture(r, s.preview, ScaleMode.ScaleToFit);
            else
            {
                GUIStyle label = FallbackLabel;
                label.fontSize = Mathf.Clamp(Mathf.RoundToInt(style.size), 8, 64);
                label.fontStyle = ToUnityFontStyle(style.fontStyle);
                label.normal.textColor = style.color.Resolve(theme);
                EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.06f));
                GUI.Label(r, "The quick brown fox", label);
            }
        }

        // Renders a real sample "text" element (current style, live edits) to a texture; null if
        // rendering is unavailable (no graphics device) — the caller falls back to a faux label.
        private static Texture2D RenderSample(string styleName, int width, int height)
        {
            if (string.IsNullOrEmpty(styleName)) return null;
            GameObject go = null;
            try
            {
                var view = new ViewSpec { category = "DesignSystem", viewName = "TypographyPreview" };
                view.elements.Add(new ElementSpec
                { kind = "text", label = "The quick brown fox", textStyle = styleName, align = "left" });
                NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
                go = UISpecGenerator.BuildViewGameObject(view, settings, new GenerateReport());
                Texture2D tex = UIScreenshotter.RenderToTexture(go, width, height);
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

        private static FontStyle ToUnityFontStyle(FontStyles tmpStyle)
        {
            bool bold = (tmpStyle & FontStyles.Bold) != 0;
            bool italic = (tmpStyle & FontStyles.Italic) != 0;
            if (bold && italic) return FontStyle.BoldAndItalic;
            if (bold) return FontStyle.Bold;
            if (italic) return FontStyle.Italic;
            return FontStyle.Normal;
        }
    }
}
