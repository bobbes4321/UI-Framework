using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// The "kill the blank page" widget palette: a collapsible, searchable, categorized strip of tiles —
    /// one per <see cref="ComposerPalette"/> entry (built-in kinds + project kinds). A tile starts a
    /// <see cref="DragAndDrop"/> carrying its kind so it can be dropped onto the canvas or the tree, and a
    /// click stamps the kind into the current view as a fallback (handed back through <see cref="_addToView"/>).
    ///
    /// <para>Built entirely with the EditorUI kit, IMGUI rules honored: GUIStyles are cached (built once),
    /// and the entry list is fetched once when the pane opens (<see cref="OnOpen"/>) — never per OnGUI.</para>
    /// </summary>
    public sealed class PalettePane
    {
        private const float TileHeight = 22f;
        private const float RowPad = 2f;
        private const float SearchHeight = 20f;
        private const float SectionHeight = 18f;

        private readonly System.Action<string> _addToView;   // click-to-add fallback (window wires it)

        private string _filter = "";
        private Vector2 _scroll;

        // entries snapshotted on open, grouped by category in display order (NOT rebuilt per OnGUI)
        private readonly List<(string category, List<PaletteEntry> entries)> _groups =
            new List<(string, List<PaletteEntry>)>();
        private bool _loaded;

        // cached styles (built once)
        private GUIStyle _search, _section, _tile;

        public PalettePane(System.Action<string> addToView)
        {
            _addToView = addToView;
        }

        /// <summary> Snapshot the palette entries. Call when the pane becomes visible (open / reopen) so a
        /// project that registers a kind after this window opened still shows up on the next open, while a
        /// normal OnGUI pass never re-enumerates. </summary>
        public void OnOpen()
        {
            _groups.Clear();
            var byCategory = new Dictionary<string, List<PaletteEntry>>();
            foreach (PaletteEntry e in ComposerPalette.All)
            {
                if (!byCategory.TryGetValue(e.category, out List<PaletteEntry> list))
                    byCategory[e.category] = list = new List<PaletteEntry>();
                list.Add(e);
            }
            foreach (string category in ComposerPalette.Categories)
                if (byCategory.TryGetValue(category, out List<PaletteEntry> list))
                    _groups.Add((category, list));
            _loaded = true;
        }

        public void OnGUI(Rect rect)
        {
            if (!_loaded) OnOpen();
            EnsureStyles();

            var searchRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, SearchHeight - 4f);
            DrawSearch(searchRect);

            var listRect = new Rect(rect.x, rect.y + SearchHeight, rect.width, rect.height - SearchHeight);
            float contentHeight = MeasureHeight(rect.width - 16f);
            var viewRect = new Rect(0, 0, listRect.width - 16f, contentHeight);
            _scroll = GUI.BeginScrollView(listRect, _scroll, viewRect);

            string needle = string.IsNullOrEmpty(_filter) ? null : _filter.ToLowerInvariant();
            float y = 0f;
            int shown = 0;
            foreach (var group in _groups)
            {
                List<PaletteEntry> matches = Filtered(group.entries, needle);
                if (matches.Count == 0) continue;

                GUI.Label(new Rect(4f, y + 2f, viewRect.width - 8f, SectionHeight - 2f),
                    group.category.ToUpperInvariant(), _section);
                y += SectionHeight;

                foreach (PaletteEntry entry in matches)
                {
                    DrawTile(new Rect(4f, y, viewRect.width - 8f, TileHeight), entry);
                    y += TileHeight + RowPad;
                    shown++;
                }
                y += 4f;
            }
            GUI.EndScrollView();

            if (shown == 0 && needle != null)
                GUI.Label(new Rect(listRect.x + 6f, listRect.y + 4f, listRect.width - 8f, 18f),
                    $"No widget matches “{_filter}”", EditorStyles.miniLabel);
        }

        // ------------------------------------------------------------------ drawing

        private void DrawSearch(Rect rect)
        {
            EditorGUI.BeginChangeCheck();
            string next = EditorGUI.TextField(rect, _filter, _search);
            if (EditorGUI.EndChangeCheck()) _filter = next ?? "";
        }

        private void DrawTile(Rect rect, PaletteEntry entry)
        {
            Event e = Event.current;
            Color accent = ComposerPalette.AccentFor(entry);

            if (e.type == EventType.Repaint)
            {
                bool hover = rect.Contains(e.mousePosition);
                EditorGUI.DrawRect(rect, hover ? NeoColors.RowHover : NeoColors.SectionBackground);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent.WithAlpha(0.85f));
                _tile.Draw(new Rect(rect.x + 8f, rect.y, rect.width - 10f, rect.height),
                    new GUIContent(entry.label, entry.kind), false, false, false, false);
            }

            // a press on the tile arms a drag; on the first drag the DragAndDrop session begins
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(ComposerPalette.DragKey, entry.kind);
                DragAndDrop.objectReferences = new Object[0];
                _pendingDragKind = entry.kind;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _pendingDragKind == entry.kind
                     && rect.Contains(e.mousePosition))
            {
                DragAndDrop.StartDrag($"Add {entry.label}");
                _pendingDragKind = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && rect.Contains(e.mousePosition)
                     && _pendingDragKind == entry.kind)
            {
                // a click with no drag → click-to-add fallback (append to the current view)
                _pendingDragKind = null;
                _addToView?.Invoke(entry.kind);
                e.Use();
            }
        }

        private string _pendingDragKind;

        // ------------------------------------------------------------------ helpers

        private static List<PaletteEntry> Filtered(List<PaletteEntry> entries, string needle)
        {
            if (needle == null) return entries;
            var result = new List<PaletteEntry>();
            foreach (PaletteEntry e in entries)
                if ((e.label != null && e.label.ToLowerInvariant().Contains(needle))
                    || (e.kind != null && e.kind.ToLowerInvariant().Contains(needle)))
                    result.Add(e);
            return result;
        }

        private float MeasureHeight(float width)
        {
            string needle = string.IsNullOrEmpty(_filter) ? null : _filter.ToLowerInvariant();
            float h = 0f;
            foreach (var group in _groups)
            {
                List<PaletteEntry> matches = Filtered(group.entries, needle);
                if (matches.Count == 0) continue;
                h += SectionHeight + matches.Count * (TileHeight + RowPad) + 4f;
            }
            return h;
        }

        private void EnsureStyles()
        {
            _search ??= new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
            _section ??= new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 9 };
            _tile ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                clipping = TextClipping.Clip
            };
        }
    }
}
