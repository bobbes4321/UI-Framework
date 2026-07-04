using System.Collections.Generic;
using Neo.EditorUI;
using Neo.UI.Editor.Authoring; // NeoWidgetPalette — rehomed off the Composer in Task 2.1 (dies with the window in Wave 3)
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// The "kill the blank page" widget palette: a collapsible, searchable, categorized strip of tiles —
    /// one per <see cref="NeoWidgetPalette"/> entry (built-in kinds + project kinds). A tile starts a
    /// <see cref="DragAndDrop"/> carrying its kind so it can be dropped onto the canvas or the tree, and a
    /// click stamps the kind into the current view as a fallback (handed back through <see cref="_addToView"/>).
    ///
    /// <para>Built entirely with the EditorUI kit, IMGUI rules honored: GUIStyles are cached (built once),
    /// and the entry list is fetched once when the pane opens (<see cref="OnOpen"/>) — never per OnGUI.</para>
    /// </summary>
    public sealed class PalettePane
    {
        private const float CardW = 104f;          // 96px thumbnail + 4px padding each side
        private const float CardThumb = 96f;
        private const float CardLabel = 18f;
        private const float CardH = CardThumb + CardLabel + 6f;
        private const float CardGap = 4f;
        private const float SearchHeight = 20f;
        private const float SectionHeight = 18f;

        // click-to-add fallback (window wires it): (kind, presetOrNull). A Components tile passes its preset.
        private readonly System.Action<string, string> _addToView;

        private string _filter = "";
        private Vector2 _scroll;

        // entries snapshotted on open, grouped by category in display order (NOT rebuilt per OnGUI)
        private readonly List<(string category, List<PaletteEntry> entries)> _groups =
            new List<(string, List<PaletteEntry>)>();
        private bool _loaded;

        // cached styles (built once)
        private GUIStyle _search, _section, _tile, _label;

        public PalettePane(System.Action<string, string> addToView)
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
            foreach (PaletteEntry e in NeoWidgetPalette.All)
            {
                if (!byCategory.TryGetValue(e.category, out List<PaletteEntry> list))
                    byCategory[e.category] = list = new List<PaletteEntry>();
                list.Add(e);
            }
            foreach (string category in NeoWidgetPalette.Categories)
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
            float gridWidth = listRect.width - 16f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((gridWidth + CardGap) / (CardW + CardGap)));
            float contentHeight = MeasureHeight(cols);
            var viewRect = new Rect(0, 0, gridWidth, contentHeight);
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

                for (int i = 0; i < matches.Count; i++)
                {
                    int col = i % cols;
                    if (col == 0 && i > 0) y += CardH + CardGap;
                    var cardRect = new Rect(4f + col * (CardW + CardGap), y, CardW, CardH);
                    DrawCard(cardRect, matches[i]);
                    shown++;
                }
                y += CardH + 8f;
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

        private void DrawCard(Rect rect, PaletteEntry entry)
        {
            Event e = Event.current;
            Color accent = NeoWidgetPalette.AccentFor(entry);

            if (e.type == EventType.Repaint)
            {
                bool hover = rect.Contains(e.mousePosition);
                EditorGUI.DrawRect(rect, hover ? NeoColors.RowHover : NeoColors.SectionBackground);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), accent.WithAlpha(0.85f));

                var thumbRect = new Rect(rect.x + 4f, rect.y + 4f, CardThumb - 4f, CardThumb - 4f);
                // cached in-memory render of the actual widget; null when headless → fall back to a glyph.
                // A Components tile renders its preset applied to the target kind ("Primary Button" looks
                // primary), so the card previews the real component; a bare-kind tile renders the plain kind.
                Texture2D thumb = entry.IsPreset && NeoWidgetPresets.TryGet(entry.preset, out NeoWidgetPreset p)
                    ? PresetThumbnailCache.GetOrRender(p, (int)CardThumb)
                    : PresetThumbnailCache.GetOrRenderKind(entry.kind, (int)CardThumb);
                if (thumb != null)
                    GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit, alphaBlend: true);
                else
                    _tile.Draw(thumbRect, new GUIContent(entry.kind), false, false, false, false);

                _label.Draw(new Rect(rect.x + 2f, rect.yMax - CardLabel, rect.width - 4f, CardLabel),
                    new GUIContent(entry.label, entry.kind), false, false, false, false);
            }

            // a press on the tile arms a drag; on the first drag the DragAndDrop session begins. A
            // Components tile also carries its preset name (PresetDragKey) so the drop links the preset.
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(NeoWidgetPalette.DragKey, entry.kind);
                DragAndDrop.SetGenericData(NeoWidgetPalette.PresetDragKey, entry.preset);
                DragAndDrop.objectReferences = new Object[0];
                _pendingDragKind = entry.kind;
                _pendingDragPreset = entry.preset;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _pendingDragKind == entry.kind
                     && _pendingDragPreset == entry.preset && rect.Contains(e.mousePosition))
            {
                DragAndDrop.StartDrag($"Add {entry.label}");
                _pendingDragKind = null;
                _pendingDragPreset = null;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && rect.Contains(e.mousePosition)
                     && _pendingDragKind == entry.kind && _pendingDragPreset == entry.preset)
            {
                // a click with no drag → click-to-add fallback (append to the current view)
                _pendingDragKind = null;
                _pendingDragPreset = null;
                _addToView?.Invoke(entry.kind, entry.preset);
                e.Use();
            }
        }

        private string _pendingDragKind;
        private string _pendingDragPreset;

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

        private float MeasureHeight(int cols)
        {
            string needle = string.IsNullOrEmpty(_filter) ? null : _filter.ToLowerInvariant();
            float h = 0f;
            foreach (var group in _groups)
            {
                List<PaletteEntry> matches = Filtered(group.entries, needle);
                if (matches.Count == 0) continue;
                int rows = (matches.Count + cols - 1) / cols;
                h += SectionHeight + rows * (CardH + CardGap) + 8f;
            }
            return h;
        }

        private void EnsureStyles()
        {
            _search ??= new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
            _section ??= new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 9 };
            _tile ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                clipping = TextClipping.Clip
            };
            _label ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                clipping = TextClipping.Clip
            };
        }
    }
}
