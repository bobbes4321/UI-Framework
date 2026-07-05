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
    /// Design System window "Typography" tab: CRUD over <see cref="Theme.TextStyles"/> (font, size,
    /// style flags, spacing, color) with a rendered sample line per style — the text sibling of
    /// <see cref="ShapesTab"/> (Phase 2.2 of the design-system-cohesion-plan). Same undo/dirty
    /// discipline as Colors/Shapes: every field edit is <c>Undo.RecordObject(theme)</c> +
    /// <c>theme.SetTextStyle(...)</c> (upsert, raises <see cref="Theme.RaiseChanged"/> so live
    /// <see cref="ThemeTextStyleTarget"/>s refresh) + <c>EditorUtility.SetDirty(theme)</c>.
    /// </summary>
    internal static class TypographyTab
    {
        /// <summary> Per-window UI state for the Typography tab. Disposable so the window destroys the
        /// cached preview texture on disable (same pattern as <see cref="ButtonsTab.State"/>). </summary>
        internal sealed class State : IDisposable
        {
            public int styleIdx;
            public string newStyleName = "";

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

        internal static object CreateState() => new State();

        /// <summary> The registry entry the orchestrator plugs into <see cref="NeoDesignSystemTabs"/>'s
        /// built-ins list, ordered between Colors (0) and Buttons (10). </summary>
        internal static DesignSystemTabDescriptor Descriptor =>
            new DesignSystemTabDescriptor("typography", "Typography", 5, CreateState, Draw);

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
            if (names.Count > 0)
            {
                s.styleIdx = Mathf.Clamp(s.styleIdx, 0, names.Count - 1);
                Rect rect = EditorGUILayout.GetControlRect();
                rect = EditorGUI.PrefixLabel(rect, new GUIContent("Text style"));
                NeoDropdown.ValuePopup(rect, names[s.styleIdx], () => theme.GetTextStyleNames().ToList(),
                    chosen =>
                    {
                        int idx = theme.GetTextStyleNames().ToList().IndexOf(chosen);
                        if (idx >= 0) s.styleIdx = idx;
                    });
            }
            else
                EditorGUILayout.HelpBox("No text styles yet. Run Setup → Create or Repair Starter Kit to " +
                    "seed the type scale, or add one below.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                s.newStyleName = EditorGUILayout.TextField("New style", s.newStyleName);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(s.newStyleName))
                {
                    // Seed size from the currently-selected style so a new style isn't a jarring 16px
                    // outlier next to a project's existing scale; falls back to 16 when none exist yet.
                    float seedSize = 16f;
                    if (names.Count > 0 &&
                        theme.TryGetTextStyle(names[Mathf.Clamp(s.styleIdx, 0, names.Count - 1)], out TextStyle seedFrom))
                        seedSize = seedFrom.size;

                    Undo.RecordObject(theme, "Add text style");
                    theme.SetTextStyle(new TextStyle { name = s.newStyleName.Trim(), size = seedSize });
                    EditorUtility.SetDirty(theme);
                    s.newStyleName = "";
                    names = theme.GetTextStyleNames().ToList();
                    s.styleIdx = names.Count - 1;
                }
            }

            if (names.Count > 0 &&
                theme.TryGetTextStyle(names[Mathf.Clamp(s.styleIdx, 0, names.Count - 1)], out TextStyle style))
            {
                NeoGUI.Splitter();

                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField("Name", style.name);
                var font = (TMP_FontAsset)EditorGUILayout.ObjectField("Font", style.font, typeof(TMP_FontAsset), false);
                float size = Mathf.Max(1f, EditorGUILayout.FloatField("Size", style.size));
                var fontStyleFlags = (FontStyles)EditorGUILayout.EnumFlagsField("Style", style.fontStyle);
                float charSpacing = EditorGUILayout.FloatField("Character spacing", style.characterSpacing);
                float lineSpacing = EditorGUILayout.FloatField("Line spacing", style.lineSpacing);
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

                    names = theme.GetTextStyleNames().ToList();
                    s.styleIdx = Mathf.Max(0, names.IndexOf(style.name));
                }

                // Color edits go through ColorRef (own undo/dirty); raise the theme-changed event so live
                // ThemeTextStyleTargets refresh — same one-notify-path discipline as ShapesTab (B4).
                EditorGUI.BeginChangeCheck();
                DesignSystemGUI.ColorRef(theme, theme, "Color", style.color);
                if (EditorGUI.EndChangeCheck()) theme.RaiseChanged();

                PreviewStyle(s, theme, style);

                // No cheap reverse-index from text-style name -> referencing presets/elements exists (they
                // are plain strings, like tokens/shape styles), so — like tokens/shapes — this just removes.
                // ThemeTextStyleTarget.ApplyStyle no-ops when TryGetTextStyle misses (verified), so a
                // dangling reference simply stops re-styling live; it never throws or blanks the text.
                if (GUILayout.Button("Remove style"))
                {
                    Undo.RecordObject(theme, "Remove text style");
                    theme.RemoveTextStyle(style.name);
                    EditorUtility.SetDirty(theme);
                    s.styleIdx = Mathf.Max(0, s.styleIdx - 1);
                }
            }
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

        private static void PreviewStyle(State s, Theme theme, TextStyle style)
        {
            NeoGUI.Splitter();
            EditorGUILayout.LabelField("Preview", EditorStyles.miniBoldLabel);

            // Re-render only when the look key changes (name, font, size, style flags, spacing, resolved
            // color, active variant) — never per OnGUI.
            string key = $"{style.name}|{(style.font != null ? style.font.name : "-")}|{style.size:0.##}|" +
                         $"{style.fontStyle}|{style.characterSpacing:0.##}|{style.lineSpacing:0.##}|" +
                         $"{ColorUtility.ToHtmlStringRGBA(style.color.Resolve(theme))}|{theme.ActiveVariantName}";
            if (key != s.previewKey)
            {
                if (s.preview != null) UnityEngine.Object.DestroyImmediate(s.preview);
                s.preview = RenderSample(style.name);
                s.previewKey = key;
            }

            Rect r = GUILayoutUtility.GetRect(320f, 96f, GUILayout.Width(320f));
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
        private static Texture2D RenderSample(string styleName)
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
                Texture2D tex = UIScreenshotter.RenderToTexture(go, 320, 96);
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
