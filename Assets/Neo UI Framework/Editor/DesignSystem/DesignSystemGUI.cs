using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The IMGUI helpers shared across more than one Design System tab (moved verbatim out of the old
    /// monolithic <see cref="NeoDesignSystemWindow"/> when the tabs were split into their own files):
    /// the <see cref="ThemeColorRef"/> editor, the searchable theme-token dropdown, token→color
    /// resolution, a swatch, and the folder-ensure used when creating preset assets. Single
    /// implementation so every tab draws color refs / token pickers identically.
    /// </summary>
    internal static class DesignSystemGUI
    {
        // ThemeColorRef editor: "T" toggles token-vs-raw; dirtyTarget is the asset that owns the ref.
        internal static void ColorRef(Theme theme, Object dirtyTarget, string label, ThemeColorRef cref)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                bool useTok = GUILayout.Toggle(cref.useToken, "T", EditorStyles.miniButton, GUILayout.Width(24f));
                if (useTok != cref.useToken)
                { Undo.RecordObject(dirtyTarget, "toggle token"); cref.useToken = useTok; EditorUtility.SetDirty(dirtyTarget); }

                if (cref.useToken)
                {
                    TokenPicker(theme, null, cref.token, chosen =>
                    {
                        Undo.RecordObject(dirtyTarget, "edit token ref");
                        cref.token = chosen;
                        EditorUtility.SetDirty(dirtyTarget);
                    });
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

        // Searchable theme-token dropdown with a "(none)" sentinel (null token). The option list is only
        // built when the dropdown is opened; onPick fires with the chosen token, or null for "(none)".
        internal static void TokenPicker(Theme theme, string label, string current, System.Action<string> onPick)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            if (label != null) rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            NeoDropdown.ValuePopup(rect, current ?? "(none)",
                () =>
                {
                    List<string> tokens = theme.GetTokenNames().ToList();
                    tokens.Insert(0, "(none)");
                    return tokens;
                },
                chosen => onPick(chosen == "(none)" ? null : chosen),
                optionSwatch: token => theme.TryGetColor(token, out Color c) ? c : (Color?)null);
        }

        internal static Color ResolveToken(Theme theme, string token) =>
            !string.IsNullOrEmpty(token) && theme.TryGetColor(token, out Color c) ? c : Color.gray;

        internal static void Swatch(string label, Color c)
        {
            Rect r = GUILayoutUtility.GetRect(34f, 18f, GUILayout.Width(34f));
            EditorGUI.DrawRect(r, c);
            GUI.Label(r, label, EditorStyles.miniLabel);
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------ master-detail split pane
        //
        // For tabs that opt out of the window's own scroll view (DesignSystemTabDescriptor.ownsLayout —
        // Motion, Presets, and any project-registered tab that wants one): a resizable LEFT (master)
        // column next to a flexible, independently-scrolling RIGHT (detail) column, together filling all
        // remaining window height. Scope-based (no Action callbacks) so nothing allocates per OnGUI;
        // scroll positions AND the master-column width are caller-owned — kept in the tab's own
        // per-window state, per the IMGUI rule against per-OnGUI-allocated state — and passed by ref.
        // Usage: `using (DesignSystemGUI.BeginSplitPane(window)) { BeginSplitLeft(ref scroll, ref width,
        // min, rightMin); ... master rows ...; EndSplitLeft(ref width, min, rightMin); BeginSplitRight(
        // ref scroll); ... detail ...; EndSplitRight(); }`. The separator between the two panes is a drag
        // handle (see EndSplitLeft) — the caller persists the dragged width (e.g. via SessionState).

        // Px reserved on the RIGHT of the left pane's inner content so an expanding control (a text
        // field, the trailing ✕ clear button) never lays out UNDER the vertical scrollbar and clips: a
        // scroll view hands its content the FULL view-rect width during layout, but once the content
        // overflows vertically the scrollbar eats ~13px off the right edge — so we constrain the inner
        // content column to (paneWidth − this) and hide the horizontal scrollbar entirely (content can't
        // overflow sideways then, so a too-wide row wraps to the viewport instead of scrolling off the
        // edge). Fixes both split-pane tabs' left panes at once — no per-row band-aids.
        private const float LeftScrollbarAllowance = 15f;

        // Draggable separator: the visible line stays 1px, but the mouse hit-zone (resize cursor + drag
        // capture) is this wide so the handle is grabbable without pixel-hunting for a 1px target.
        private const float SeparatorHitWidth = 5f;

        // Set at the top of every BeginSplitPane so EndSplitLeft can repaint the host live during a drag
        // (a static helper otherwise holds no window handle). OnGUI is single-threaded and never
        // reentrant across windows, so this can't bleed between two open Design System windows.
        private static EditorWindow s_splitHost;

        /// <summary> Disposable scope returned by <see cref="BeginSplitPane"/>; closes the outer
        /// expand-height horizontal group. Always opened via a <c>using</c> statement, with one
        /// <see cref="BeginSplitLeft"/>/<see cref="EndSplitLeft"/> pair followed by one
        /// <see cref="BeginSplitRight"/>/<see cref="EndSplitRight"/> pair nested inside it. </summary>
        internal readonly struct SplitPaneScope : System.IDisposable
        {
            public void Dispose() => GUILayout.EndHorizontal();
        }

        /// <summary> Opens a master-detail split pane that fills all remaining vertical space in the
        /// window. <paramref name="host"/> is the hosting window — repainted live while the separator is
        /// dragged. Pair with a <c>using</c> block (<see cref="SplitPaneScope"/>); nest a
        /// <see cref="BeginSplitLeft"/>/<see cref="EndSplitLeft"/> pair then a
        /// <see cref="BeginSplitRight"/>/<see cref="EndSplitRight"/> pair inside it. </summary>
        internal static SplitPaneScope BeginSplitPane(EditorWindow host)
        {
            s_splitHost = host;
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            return default;
        }

        /// <summary> Opens the split pane's LEFT (master) column at <paramref name="width"/> px (clamped
        /// to <paramref name="minWidth"/> … window-width−<paramref name="rightMinWidth"/> — the same
        /// clamp <see cref="EndSplitLeft"/>'s drag uses, applied here too so a stale/oversized stored
        /// width self-corrects) with its own scroll view. <paramref name="scroll"/> is caller-owned (the
        /// tab's per-window state) — the updated position is written back through the ref, read it after
        /// <see cref="EndSplitLeft"/>. Must be closed with <see cref="EndSplitLeft"/> before opening the
        /// right column. </summary>
        internal static void BeginSplitLeft(ref Vector2 scroll, ref float width, float minWidth, float rightMinWidth)
        {
            width = ClampSplitWidth(width, minWidth, rightMinWidth);
            GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
            // Hide the HORIZONTAL scrollbar (GUIStyle.none) so content can never scroll sideways off the
            // pane; keep the standard vertical scrollbar. Fixed width so the column is exactly `width`.
            scroll = EditorGUILayout.BeginScrollView(scroll, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Width(width), GUILayout.ExpandHeight(true));
            // Inner content column, sized short of the vertical scrollbar so expanding controls (text
            // fields, the trailing ✕) stay fully inside the visible viewport rather than clipping under it.
            GUILayout.BeginVertical(GUILayout.Width(width - LeftScrollbarAllowance));
        }

        /// <summary> Closes the LEFT column opened by <see cref="BeginSplitLeft"/> and draws the
        /// draggable separator between the two panes: a wide invisible hit-zone (resize cursor + drag
        /// capture) with a 1px line (<see cref="NeoColors.Separator"/> — the same token
        /// <see cref="NeoGUI.Splitter"/> uses) down its middle. Dragging mutates <paramref name="width"/>
        /// by ref, clamped to <paramref name="minWidth"/> … window-width−<paramref name="rightMinWidth"/>;
        /// the caller persists the new value. </summary>
        internal static void EndSplitLeft(ref float width, float minWidth, float rightMinWidth)
        {
            GUILayout.EndVertical();          // inner content column
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();          // fixed-width left column

            // Wide hit-zone reserved in layout; the 1px line is drawn centred inside it. A control id +
            // hotControl capture the drag so it survives the pointer straying off the thin line.
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Rect area = GUILayoutUtility.GetRect(SeparatorHitWidth, 0f,
                GUILayout.Width(SeparatorHitWidth), GUILayout.ExpandHeight(true));
            Rect line = new Rect(area.x + (area.width - 1f) * 0.5f, area.y, 1f, area.height);
            EditorGUIUtility.AddCursorRect(area, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && area.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        width = ClampSplitWidth(width + e.delta.x, minWidth, rightMinWidth);
                        e.Use();
                        s_splitHost?.Repaint();   // live resize (the ref width takes effect next BeginSplitLeft)
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
                case EventType.Repaint:
                    EditorGUI.DrawRect(line, NeoColors.Separator);
                    break;
            }
        }

        // Clamp the left-pane width to [minWidth, currentViewWidth − rightMinWidth] so the right (detail)
        // pane always keeps at least rightMinWidth. currentViewWidth is the live window content width, so
        // the ceiling tracks window resizes for free (Max guards a window narrower than min+rightMin).
        private static float ClampSplitWidth(float width, float minWidth, float rightMinWidth)
        {
            float max = Mathf.Max(minWidth, EditorGUIUtility.currentViewWidth - rightMinWidth);
            return Mathf.Clamp(width, minWidth, max);
        }

        /// <summary> Opens the split pane's flexible RIGHT (detail) column with its own scroll view.
        /// <paramref name="scroll"/> is caller-owned — read it back after <see cref="EndSplitRight"/>.
        /// Must be the last thing opened inside a <see cref="BeginSplitPane"/> scope. </summary>
        internal static void BeginSplitRight(ref Vector2 scroll)
        {
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        }

        /// <summary> Closes the RIGHT column opened by <see cref="BeginSplitRight"/>. </summary>
        internal static void EndSplitRight()
        {
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
