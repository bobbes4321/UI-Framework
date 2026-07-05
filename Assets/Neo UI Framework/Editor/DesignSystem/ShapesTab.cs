using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Shapes" tab: pick/add/remove a <see cref="ShapeStyle"/> and edit its FULL
    /// field set — radius (uniform or per-corner, in px or %), outline width, softness, fill mode
    /// (solid/gradient) + gradient second color/angle, elevation, and fill/outline colors — with a real
    /// rendered <see cref="NeoShape"/> preview. Split out of the old monolithic
    /// <see cref="NeoDesignSystemWindow"/> (Phase 2.9); extended to full fidelity in Phase 2.7. Keeps the
    /// Phase-0 fixes: uniform-radius toggle that never stomps authored per-corner radii (B5) and routing
    /// edits through <c>SetShapeStyle</c> / <c>RaiseChanged</c> so live targets refresh (B4).
    /// </summary>
    internal static class ShapesTab
    {
        /// <summary> Per-window UI state for the Shapes tab. Disposable so the window destroys the
        /// cached preview texture on disable (mirrors <see cref="ButtonsTab.State"/>). </summary>
        internal sealed class State : IDisposable
        {
            public int shapeIdx;
            public string newShape = "";

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

        internal static object CreateState() => new State();

        internal static void Draw(DesignSystemTabContext ctx)
        {
            Theme theme = ctx.theme;
            var s = ctx.State<State>();

            List<string> names = theme.GetShapeStyleNames().ToList();
            if (names.Count > 0)
            {
                s.shapeIdx = Mathf.Clamp(s.shapeIdx, 0, names.Count - 1);
                Rect rect = EditorGUILayout.GetControlRect();
                rect = EditorGUI.PrefixLabel(rect, new GUIContent("Shape style"));
                NeoDropdown.ValuePopup(rect, names[s.shapeIdx],
                    () => theme.GetShapeStyleNames().ToList(),
                    chosen =>
                    {
                        int idx = theme.GetShapeStyleNames().ToList().IndexOf(chosen);
                        if (idx >= 0) s.shapeIdx = idx;
                    });
            }
            else
                EditorGUILayout.HelpBox("No shape styles yet. Run Setup → Create or Repair Starter Kit to " +
                    "seed Card/Panel/Control, or add one below.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                s.newShape = EditorGUILayout.TextField("New style", s.newShape);
                if (GUILayout.Button("Add", GUILayout.Width(60f)) && !string.IsNullOrWhiteSpace(s.newShape))
                {
                    Undo.RecordObject(theme, "Add shape style");
                    theme.SetShapeStyle(new ShapeStyle { name = s.newShape.Trim() });
                    EditorUtility.SetDirty(theme);
                    s.newShape = "";
                    names = theme.GetShapeStyleNames().ToList();
                    s.shapeIdx = names.Count - 1;
                }
            }

            if (names.Count == 0
                || !theme.TryGetShapeStyle(names[Mathf.Clamp(s.shapeIdx, 0, names.Count - 1)], out ShapeStyle style))
                return;

            NeoGUI.Splitter();
            EditorGUI.BeginChangeCheck();
            bool uniform = EditorGUILayout.Toggle("Uniform radius", style.uniformRadius);
            var radiusUnit = (ShapeRadiusUnit)EditorGUILayout.EnumPopup("Radius unit", style.radiusUnit);
            float radius = style.radius;
            Vector4 perCorner = style.radiusPerCorner;
            float maxRadius = radiusUnit == ShapeRadiusUnit.Percent ? 100f : 48f;
            if (uniform)
            {
                radius = EditorGUILayout.Slider("Corner radius", style.radius, 0f, maxRadius);
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
            float border = EditorGUILayout.Slider("Outline width", style.borderWidth, 0f, 12f);
            float soft = EditorGUILayout.Slider("Softness", style.softness, 0f, 24f);
            var fillMode = (ShapeFillMode)EditorGUILayout.EnumPopup("Fill mode", style.fillMode);
            float gradientAngle = style.gradientAngle;
            if (fillMode != ShapeFillMode.Solid) // conditional display = draw or don't draw
                gradientAngle = EditorGUILayout.Slider("Gradient angle", style.gradientAngle, 0f, 360f);
            int elevation = EditorGUILayout.IntSlider("Elevation", style.elevation, 0, 3);
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
                theme.SetShapeStyle(style); // upsert raises the theme-changed event (B4)
                EditorUtility.SetDirty(theme);
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

            if (GUILayout.Button("Remove style"))
            {
                if (EditorUtility.DisplayDialog("Remove shape style",
                        $"Remove shape style '{style.name}'? Shape styles are theme-wide — every " +
                        "NeoShape bound to this name (ThemeShapeStyleTarget) will lose its styling.",
                        "Remove", "Cancel"))
                {
                    Undo.RecordObject(theme, "Remove shape style");
                    theme.RemoveShapeStyle(style.name); // raises the theme-changed event when removed
                    EditorUtility.SetDirty(theme);
                    s.shapeIdx = Mathf.Max(0, s.shapeIdx - 1);
                }
            }
        }

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

            Rect r = GUILayoutUtility.GetRect(160f, 90f, GUILayout.Width(160f));
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
                Texture2D tex = UIScreenshotter.RenderToTexture(go, 240, 140);
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
