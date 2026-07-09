using System;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The shared "catalog" grammar for master–detail Design System tabs: the small, reusable IMGUI
    /// vocabulary a tab's LEFT (browse) column and RIGHT (detail) header are built from — a search field,
    /// a selectable list row (with an optional accessory swatch/thumbnail and trailing mini badge), a
    /// pinned create-item row, the detail header's Duplicate/Delete buttons, and a "nothing selected"
    /// empty-state hint. It is deliberately generic — it knows nothing about <see cref="Theme"/> or
    /// <see cref="NeoUISettings"/>, only strings and callbacks — so Typography, Buttons and Shapes (and
    /// any project-registered tab, per the "extensible by design" hard constraint) share one look without
    /// each hand-rolling row chrome. PUBLIC for the same reason <see cref="DesignSystemGUI"/> is: a tab
    /// added via <see cref="NeoDesignSystemTabs.Register"/> must be able to read as a native one.
    /// <para>
    /// It complements <see cref="DesignSystemGUI"/>'s split-pane machinery (which owns the panes'
    /// layout/scroll) rather than replacing it: a tab opens the split pane, then fills the left column
    /// with <see cref="SearchField"/> / <see cref="Row"/> / <see cref="NewItemRow"/> and the right column
    /// with <see cref="DetailHeader"/> + its own form. IMGUI rules (CLAUDE.md): every GUIStyle is cached
    /// in a lazily-built static, and there are no per-OnGUI allocations beyond the transient
    /// <see cref="GUIContent"/>s the surrounding EditorGUILayout calls already make. It is a row/chrome
    /// vocabulary, not a framework — no ReorderableList / SerializedObject / state lives here.
    /// </para>
    /// </summary>
    public static class DesignSystemCatalog
    {
        // Row metrics — kept small; a browse list of type styles / variants / shapes reads as a compact
        // scannable column, not a fat inspector list.
        private const float RowHeight = 20f;
        private const float AccessorySize = 14f;
        private const float BadgeHeight = 14f;
        private const float BadgeMinWidth = 22f;
        private const float RowInset = 6f;

        // ------------------------------------------------------------------ cached styles (never per OnGUI)

        private static GUIStyle _rowLabel, _badge, _emptyState;

        private static GUIStyle RowLabel => _rowLabel ??= new GUIStyle(EditorStyles.label)
        { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };

        private static GUIStyle Badge => _badge ??= new GUIStyle(EditorStyles.miniLabel)
        { alignment = TextAnchor.MiddleCenter };

        private static GUIStyle EmptyStateStyle => _emptyState ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        { wordWrap = true, alignment = TextAnchor.MiddleCenter };

        private static readonly GUIContent ClearContent = new GUIContent("✕", "Clear the search");

        // ------------------------------------------------------------------ search field

        /// <summary> A "Search" field row matching the Motion/Presets browse look — a label, a text field
        /// and a ✕ clear button (disabled when the field is empty). <paramref name="search"/> is mutated in
        /// place. </summary>
        public static void SearchField(ref string search)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(52f));
                search = EditorGUILayout.TextField(search ?? "");
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(search)))
                    if (GUILayout.Button(ClearContent, GUILayout.Width(22f)))
                    { search = ""; GUI.FocusControl(null); }
            }
        }

        // ------------------------------------------------------------------ selectable list row

        /// <summary>
        /// A full-width, single-click selectable list row: a selected row gets a tint + left accent bar, a
        /// hovered row a subtle wash. An optional <paramref name="drawAccessory"/> paints a leading square
        /// (a color swatch, a thumbnail) into the rect it is handed; an optional
        /// <paramref name="trailingBadge"/> renders a right-aligned mini pill (e.g. a font size). Returns
        /// true on the frame it is clicked (and takes keyboard focus off any text field so an in-progress
        /// edit commits). Repaint-only drawing; no state kept here.
        /// </summary>
        public static bool Row(string label, bool selected, Action<Rect> drawAccessory = null, string trailingBadge = null)
        {
            Event e = Event.current;
            Rect rect = EditorGUILayout.GetControlRect(false, RowHeight);
            bool clicked = false;

            if (e.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(rect, NeoColors.RowSelected);
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), NeoColors.Interactive);
                }
                else if (rect.Contains(e.mousePosition))
                    EditorGUI.DrawRect(rect, NeoColors.RowHover);

                float x = rect.x + RowInset;
                if (drawAccessory != null)
                {
                    var accRect = new Rect(rect.x + 4f, rect.y + (rect.height - AccessorySize) * 0.5f,
                        AccessorySize, AccessorySize);
                    drawAccessory(accRect);
                    x = accRect.xMax + RowInset;
                }

                float labelRight = rect.xMax - 4f;
                if (!string.IsNullOrEmpty(trailingBadge))
                {
                    var content = new GUIContent(trailingBadge);
                    float bw = Mathf.Max(BadgeMinWidth, Badge.CalcSize(content).x + 8f);
                    var badgeRect = new Rect(rect.xMax - bw - 4f, rect.y + (rect.height - BadgeHeight) * 0.5f,
                        bw, BadgeHeight);
                    EditorGUI.DrawRect(badgeRect, NeoColors.SectionBackground);
                    Badge.Draw(badgeRect, content, false, false, false, false);
                    labelRight = badgeRect.x - 4f;
                }

                RowLabel.Draw(new Rect(x, rect.y, Mathf.Max(0f, labelRight - x), rect.height),
                    new GUIContent(label), false, false, false, false);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                clicked = true;
                GUI.FocusControl(null);
                e.Use();
            }
            return clicked;
        }

        // ------------------------------------------------------------------ pinned create row

        /// <summary>
        /// A pinned "create item" row: a text field (the <paramref name="placeholder"/> is shown as its
        /// prefix label, matching the existing tabs' New-item rows) plus an "Add" button disabled while the
        /// field is blank. Returns true when the user submits a non-blank name; <paramref name="name"/> is
        /// trimmed in place on submit so the caller receives the clean value. The caller resets the field.
        /// </summary>
        public static bool NewItemRow(ref string name, string placeholder)
        {
            bool submit = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                name = EditorGUILayout.TextField(placeholder, name ?? "");
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(name)))
                    if (GUILayout.Button("Add", GUILayout.Width(50f))) submit = true;
            }
            if (!submit) return false;
            name = name.Trim();
            return name.Length > 0;
        }

        // ------------------------------------------------------------------ detail header

        /// <summary> The detail pane's header: a bold <paramref name="title"/> on the left with
        /// right-aligned Duplicate / Delete buttons (Delete tinted, both tool-tipped) — the same grammar
        /// the Motion tab's detail header uses. The button presses are reported through the out params so
        /// the caller owns the (undo-recorded) mutation. </summary>
        public static void DetailHeader(string title, out bool duplicate, out bool remove)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                duplicate = GUILayout.Button(new GUIContent("Duplicate", "Copy this to a new item"),
                    EditorStyles.miniButtonLeft, GUILayout.Width(72f));
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = NeoColors.Remove;
                remove = GUILayout.Button(new GUIContent("Delete", "Delete this item"),
                    EditorStyles.miniButtonRight, GUILayout.Width(58f));
                GUI.backgroundColor = prev;
            }
        }

        // ------------------------------------------------------------------ keyboard list navigation

        /// <summary>
        /// Returns -1/+1 when Up/Down is pressed while no text field has the keyboard focus (so typing in
        /// a tab's search/name field never moves the browse-list selection), else 0. Consumes the event
        /// when it reports a delta so the key press doesn't also scroll the enclosing scroll view.
        /// Callers apply the delta to their own visible (search-filtered) ordered list built while drawing
        /// rows — selection itself stays name-based, this only reports which direction to move it.
        /// </summary>
        public static int ListNavDelta()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField) return 0;
            if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow) return 0;
            e.Use();
            return e.keyCode == KeyCode.UpArrow ? -1 : 1;
        }

        // ------------------------------------------------------------------ empty state

        /// <summary> The "nothing selected" hint for a detail pane — a vertically-centred, word-wrapped
        /// grey line (mirrors the Presets/Motion empty state). </summary>
        public static void EmptyState(string message)
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(message, EmptyStateStyle);
            GUILayout.FlexibleSpace();
        }
    }
}
