using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// The dockable compose palette (<c>Tools → Neo UI → Palette</c>) — the Doozy "UI Menu" of this
    /// package, meant to live docked beside the Project/Console panes while the scene view is open: a
    /// searchable thumbnail grid of everything addable — every <see cref="NeoWidgetPalette"/> entry
    /// (bare widget kinds AND discovered <see cref="NeoWidgetPreset"/> component tiles) plus every
    /// <see cref="NeoLayoutTemplates"/> scaffold. Click a tile to spawn it into the current selection
    /// (the same <see cref="NeoSceneAuthoring"/> path the GameObject menu and Ctrl-K commands use —
    /// Canvas/EventSystem bootstrap, undo, WYSIWYG parity with generation all come for free), or DRAG
    /// it into the Hierarchy / Scene view (<see cref="NeoPaletteDropHandlers"/> reads the reserved
    /// <see cref="NeoWidgetPalette.DragKey"/>/<see cref="NeoWidgetPalette.PresetDragKey"/>/
    /// <see cref="NeoWidgetPalette.TemplateDragKey"/> payloads).
    /// <para>
    /// Preset tiles honor the bottom-bar <b>Linked / Detached</b> mode — the analog of Doozy's
    /// Link-vs-Clone instantiate modes: Linked keeps the widget's <c>preset</c> reference (it restyles
    /// when the preset asset changes, round-trips as the preset name + override delta); Detached
    /// (<see cref="NeoSceneAuthoring.CreateWidgetDetached"/>) bakes the preset's styling into the
    /// element as a one-off disconnected copy.
    /// </para>
    /// <para>
    /// Beyond Doozy: a ★ Favorites section (star a tile), a Recent strip of the last spawns, and real
    /// rendered thumbnails (the shared <see cref="PresetThumbnailCache"/> — kinds and presets render
    /// through the same in-memory preview path generation uses, so a tile IS what you get). Tile size,
    /// category, favorites, recents and spawn mode persist via <see cref="EditorPrefs"/>.
    /// </para>
    /// Perf discipline (CLAUDE.md IMGUI rules): the section model is cached and rebuilt only when the
    /// palette/template registry snapshots, the filters, or the favorites/recents version actually
    /// change — never per OnGUI; GUIStyles are built once; thumbnails come from the shared cache at a
    /// fixed render size (drawn scaled, so the size slider never re-renders); the "adds into" hint is
    /// recomputed only on <see cref="Selection.selectionChanged"/>.
    /// </summary>
    public sealed class NeoPaletteWindow : EditorWindow
    {
        // ------------------------------------------------------------------ persisted state keys
        private const string TileSizeKey = "NeoUI.Palette.TileSize";
        private const string CategoryKey = "NeoUI.Palette.Category";
        private const string DetachedKey = "NeoUI.Palette.Detached";
        private const string FavoritesKey = "NeoUI.Palette.Favorites";
        private const string RecentsKey = "NeoUI.Palette.Recents";

        // ------------------------------------------------------------------ layout metrics
        private const float MinCardW = 84f;
        private const float MaxCardW = 200f;
        private const float DefaultCardW = 120f;
        private const float SnapStep = 16f;   // slider snap — keeps the live resize smooth but chunky
        private const float CardLabelH = 18f;
        private const float Gap = 6f;
        private const float GridChrome = 30f; // scroll view inner margin + vertical scrollbar
        private const int MaxRecents = 8;

        private const string AllCategories = "All";
        private const string FavoritesCategory = "★ Favorites";
        private const string TemplatesCategory = "Templates";

        // ------------------------------------------------------------------ tile / section model

        /// <summary> One grid tile — a palette entry (bare kind or preset) or an insertable template.
        /// The preset asset is resolved once at section-build time so drawing never hits the registry. </summary>
        private readonly struct Tile
        {
            public readonly PaletteEntry entry;
            public readonly TemplateEntry template;
            public readonly NeoWidgetPreset presetAsset;
            public readonly bool isTemplate;
            public readonly string key; // stable favorites/recents identity

            public Tile(PaletteEntry e, NeoWidgetPreset presetAsset)
            {
                entry = e;
                template = default;
                this.presetAsset = presetAsset;
                isTemplate = false;
                key = e.IsPreset ? "preset:" + e.preset : "kind:" + e.kind;
            }

            public Tile(TemplateEntry t)
            {
                entry = default;
                template = t;
                presetAsset = null;
                isTemplate = true;
                key = "template:" + t.id;
            }

            public string Label => isTemplate ? template.label : entry.label;

            public string Tooltip => isTemplate
                ? (string.IsNullOrEmpty(template.description) ? template.label : template.description)
                : entry.IsPreset
                    ? $"{entry.label} — component preset ({entry.kind}). Click to add, drag into the scene."
                    : $"{entry.label} ({entry.kind}). Click to add, drag into the scene.";
        }

        private sealed class Section
        {
            public string title;
            public readonly List<Tile> tiles = new List<Tile>();
        }

        // ------------------------------------------------------------------ window state
        private readonly List<Section> _sections = new List<Section>();
        private IReadOnlyList<PaletteEntry> _builtPalette;
        private IReadOnlyList<TemplateEntry> _builtTemplates;
        private string _builtCategory, _builtSearch;
        private int _builtStateVersion = -1;

        private Vector2 _scroll;
        private string _category = AllCategories;
        private string _search = "";
        private float _cardW = DefaultCardW;
        private string _targetHint = "";

        // press-to-drag tracking (a click spawns, a drag past the threshold starts DnD instead).
        // Presses are tracked via GUIUtility.hotControl with a per-CARD control id — never by tile
        // key: a tile in the Recent/Favorites strip is the SAME key as its category-section card, so
        // a key match let the strip's copy (drawn first) swallow the MouseUp meant for the card the
        // user actually clicked, making every already-recent tile spawn exactly nothing.
        private Vector2 _pressPos;

        // favorites / recents are static + EditorPrefs-backed so drop-handler spawns (no window
        // instance in scope) still record recents, and every open palette window agrees.
        private static HashSet<string> s_favorites;
        private static List<string> s_recents;
        private static int s_stateVersion;

        // ------------------------------------------------------------------ styles (built once)
        private static GUIStyle _cardLabel, _cardFallback, _starOn, _starOff;

        private static GUIStyle CardLabel => _cardLabel ??= new GUIStyle(EditorStyles.miniLabel)
        { alignment = TextAnchor.MiddleCenter, fontSize = 10, clipping = TextClipping.Clip };

        private static GUIStyle CardFallback => _cardFallback ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        { alignment = TextAnchor.MiddleCenter, fontSize = 12, wordWrap = true };

        private static GUIStyle StarOn => _starOn ??= StarStyle(NeoColors.Data);
        private static GUIStyle StarOff => _starOff ??= StarStyle(NeoColors.TextDim);

        private static GUIStyle StarStyle(Color color)
        {
            var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
            style.normal.textColor = color;
            return style;
        }

        // ------------------------------------------------------------------ open

        [MenuItem("Tools/Neo UI/Palette", priority = 13)]
        public static void Open()
        {
            var window = GetWindow<NeoPaletteWindow>("Neo Palette");
            window.minSize = new Vector2(240f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true; // hover highlight + star reveal need repaints while the mouse moves
            _cardW = Mathf.Clamp(EditorPrefs.GetFloat(TileSizeKey, DefaultCardW), MinCardW, MaxCardW);
            _category = EditorPrefs.GetString(CategoryKey, AllCategories);
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorPrefs.SetFloat(TileSizeKey, _cardW);
            EditorPrefs.SetString(CategoryKey, _category);
            // The cache is shared editor-wide; clearing on close mirrors PresetsTab/PresetPickerPopup —
            // the next consumer lazily re-renders, no textures linger.
            PresetThumbnailCache.Clear();
        }

        // The REAL resolution (NeoSceneAuthoring.FindDropParent, shared — not a hand-kept mirror) so the
        // hint tells the truth about where a click lands, including the climb out of leaf widgets.
        private void OnSelectionChanged()
        {
            RectTransform target = NeoSceneAuthoring.FindDropParent(Selection.activeGameObject);
            if (target != null)
            {
                _targetHint = target.name;
            }
            else
            {
                Canvas canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Exclude);
                _targetHint = canvas != null ? canvas.name : "a new Canvas";
            }
            Repaint();
        }

        // ------------------------------------------------------------------ OnGUI

        private void OnGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.MouseMove) Repaint();

            DrawToolbar();
            EnsureSections();
            DrawGrid();
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                Rect catRect = GUILayoutUtility.GetRect(new GUIContent(_category),
                    EditorStyles.toolbarDropDown, GUILayout.Width(110f));
                NeoDropdown.ValuePopup(catRect, _category, CategoryOptions, chosen =>
                {
                    if (string.IsNullOrEmpty(chosen)) return;
                    _category = chosen;
                    EditorPrefs.SetString(CategoryKey, chosen);
                    Repaint();
                });

                _search = GUILayout.TextField(_search ?? "", EditorStyles.toolbarSearchField,
                    GUILayout.MinWidth(60f));

                float raw = GUILayout.HorizontalSlider(_cardW, MinCardW, MaxCardW, GUILayout.Width(72f));
                float snapped = Mathf.Round(raw / SnapStep) * SnapStep;
                if (!Mathf.Approximately(snapped, _cardW))
                {
                    _cardW = Mathf.Clamp(snapped, MinCardW, MaxCardW);
                    EditorPrefs.SetFloat(TileSizeKey, _cardW);
                    Repaint();
                }
            }
        }

        private static List<string> CategoryOptions()
        {
            var options = new List<string> { AllCategories, FavoritesCategory };
            options.AddRange(NeoWidgetPalette.Categories);
            options.Add(TemplatesCategory);
            return options;
        }

        private void DrawGrid()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_sections.Count == 0)
            {
                string message = !string.IsNullOrEmpty(_search)
                    ? $"Nothing matches \"{_search}\"."
                    : _category == FavoritesCategory
                        ? "No favorites yet — hover a tile and click its star."
                        : "The palette is empty. Run Tools → Neo UI → Setup → Create or Repair Widget " +
                          "Presets to seed the component library.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            float thumbH = Mathf.Round(_cardW * 0.8f);
            float cardH = thumbH + CardLabelH + 6f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - GridChrome + Gap) / (_cardW + Gap)));

            foreach (Section section in _sections)
            {
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(section.title, EditorStyles.miniBoldLabel);
                for (int i = 0; i < section.tiles.Count; i += columns)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int c = 0; c < columns && i + c < section.tiles.Count; c++)
                        {
                            Rect rect = GUILayoutUtility.GetRect(_cardW, cardH,
                                GUILayout.Width(_cardW), GUILayout.Height(cardH));
                            DrawCard(rect, section.tiles[i + c]);
                        }
                    }
                    GUILayout.Space(Gap);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(new GUIContent($"Adds into: {_targetHint}",
                        "Where a clicked tile lands — the selected widget/view when it lives under a " +
                        "Canvas, else the scene Canvas (created if missing). Drag a tile to choose a " +
                        "different parent."),
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                bool detached = SpawnDetached;
                var modeContent = new GUIContent(detached ? "Detached" : "Linked",
                    "How preset tiles spawn. Linked: the widget keeps its preset reference and restyles " +
                    "when the preset asset changes. Detached: the preset's styling is baked in as a " +
                    "one-off copy with no link.");
                if (GUILayout.Button(modeContent, EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    SpawnDetached = !detached;
                    Repaint();
                }
            }
        }

        // ------------------------------------------------------------------ card

        private void DrawCard(Rect rect, Tile tile)
        {
            Event e = Event.current;
            var starRect = new Rect(rect.xMax - 20f, rect.y + 4f, 16f, 16f);
            bool favorite = IsFavorite(tile.key);

            if (e.type == EventType.Repaint)
            {
                bool hover = rect.Contains(e.mousePosition);
                Color accent = tile.isTemplate ? NeoColors.Containers : NeoWidgetPalette.AccentFor(tile.entry);
                EditorGUI.DrawRect(rect, hover ? NeoColors.RowHover : NeoColors.SectionBackground);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), accent.WithAlpha(hover ? 1f : 0.85f));

                var thumbRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - CardLabelH - 8f);
                // Rendered once at a fixed size and drawn scaled — the size slider never re-renders.
                Texture2D thumb = tile.isTemplate ? null
                    : tile.entry.IsPreset
                        ? PresetThumbnailCache.GetOrRender(tile.presetAsset, PresetThumbnailRenderer.PickerSize)
                        : PresetThumbnailCache.GetOrRenderKind(tile.entry.kind, PresetThumbnailRenderer.PickerSize);
                if (thumb != null) GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit, alphaBlend: true);
                else CardFallback.Draw(thumbRect, new GUIContent(tile.Label), false, false, false, false);

                CardLabel.Draw(new Rect(rect.x + 2f, rect.yMax - CardLabelH, rect.width - 4f, CardLabelH),
                    new GUIContent(tile.Label, tile.Tooltip), false, false, false, false);

                if (favorite || hover)
                    GUI.Label(starRect, new GUIContent(favorite ? "★" : "☆", "Favorite"),
                        favorite ? StarOn : StarOff);
            }

            // One control id per drawn CARD (hotControl also captures the release when the mouse
            // leaves the window mid-press, replacing the old rawType-MouseUp sweep).
            int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when e.button == 0 && rect.Contains(e.mousePosition):
                    if (starRect.Contains(e.mousePosition))
                    {
                        ToggleFavorite(tile.key);
                        Repaint();
                    }
                    else
                    {
                        GUIUtility.hotControl = controlId;
                        _pressPos = e.mousePosition;
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    if ((e.mousePosition - _pressPos).sqrMagnitude > 25f)
                    {
                        GUIUtility.hotControl = 0; // DragAndDrop owns the gesture from here
                        StartTileDrag(tile);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    if (rect.Contains(e.mousePosition)) SpawnTile(tile);
                    e.Use();
                    break;
            }
        }

        private static void StartTileDrag(Tile tile)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            if (tile.isTemplate)
            {
                DragAndDrop.SetGenericData(NeoWidgetPalette.TemplateDragKey, tile.template.id);
            }
            else
            {
                DragAndDrop.SetGenericData(NeoWidgetPalette.DragKey, tile.entry.kind);
                if (tile.entry.IsPreset)
                    DragAndDrop.SetGenericData(NeoWidgetPalette.PresetDragKey, tile.entry.preset);
            }
            DragAndDrop.StartDrag($"Neo UI: {tile.Label}");
        }

        private void SpawnTile(Tile tile)
        {
            if (tile.isTemplate)
                SpawnPayload(null, null, tile.template.id, Selection.activeGameObject);
            else
                SpawnPayload(tile.entry.kind, tile.entry.preset, null, Selection.activeGameObject);
            Repaint();
        }

        // ------------------------------------------------------------------ spawn (shared with drops)

        /// <summary> Whether preset tiles spawn as detached copies (bake styling, no preset link) instead
        /// of linked widgets. Shared, EditorPrefs-backed, so tile clicks and hierarchy/scene drops agree. </summary>
        internal static bool SpawnDetached
        {
            get => EditorPrefs.GetBool(DetachedKey, false);
            set => EditorPrefs.SetBool(DetachedKey, value);
        }

        /// <summary>
        /// The one spawn implementation behind every palette entry point — tile click, Hierarchy drop and
        /// Scene-view drop (<see cref="NeoPaletteDropHandlers"/>): routes the payload into the right
        /// <see cref="NeoSceneAuthoring"/> call (template insert / linked create / detached create per
        /// <see cref="SpawnDetached"/>) and records the tile in the Recent strip. Returns the created
        /// root, or null when nothing was built (already warned by the authoring layer).
        /// </summary>
        internal static GameObject SpawnPayload(string kind, string preset, string templateId, GameObject parent)
        {
            if (!string.IsNullOrEmpty(templateId))
            {
                if (!NeoLayoutTemplates.TryGet(templateId, out TemplateEntry template))
                {
                    Debug.LogWarning($"Neo UI: palette payload named unknown template '{templateId}'.");
                    return null;
                }
                GameObject inserted = NeoSceneAuthoring.InsertTemplate(template, parent);
                if (inserted != null) PushRecent("template:" + templateId);
                return inserted;
            }

            if (string.IsNullOrEmpty(kind)) return null;
            GameObject created = !string.IsNullOrEmpty(preset) && SpawnDetached
                ? NeoSceneAuthoring.CreateWidgetDetached(kind, preset, parent)
                : NeoSceneAuthoring.CreateWidget(kind, preset, parent);
            if (created != null)
                PushRecent(!string.IsNullOrEmpty(preset) ? "preset:" + preset : "kind:" + kind);
            return created;
        }

        // ------------------------------------------------------------------ favorites / recents

        private static HashSet<string> Favorites() =>
            s_favorites ??= new HashSet<string>(
                EditorPrefs.GetString(FavoritesKey, string.Empty)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

        private static List<string> Recents() =>
            s_recents ??= new List<string>(
                EditorPrefs.GetString(RecentsKey, string.Empty)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));

        private static bool IsFavorite(string key) => Favorites().Contains(key);

        private static void ToggleFavorite(string key)
        {
            HashSet<string> favorites = Favorites();
            if (!favorites.Remove(key)) favorites.Add(key);
            EditorPrefs.SetString(FavoritesKey, string.Join("\n", favorites));
            s_stateVersion++;
        }

        private static void PushRecent(string key)
        {
            List<string> recents = Recents();
            recents.Remove(key);
            recents.Insert(0, key);
            if (recents.Count > MaxRecents) recents.RemoveRange(MaxRecents, recents.Count - MaxRecents);
            EditorPrefs.SetString(RecentsKey, string.Join("\n", recents));
            s_stateVersion++;
        }

        // ------------------------------------------------------------------ section model

        private void EnsureSections()
        {
            IReadOnlyList<PaletteEntry> palette = NeoWidgetPalette.All;
            IReadOnlyList<TemplateEntry> templates = NeoLayoutTemplates.All;
            if (_builtStateVersion == s_stateVersion
                && ReferenceEquals(palette, _builtPalette)
                && ReferenceEquals(templates, _builtTemplates)
                && _builtCategory == _category
                && _builtSearch == _search)
                return;

            _sections.Clear();
            _builtPalette = palette;
            _builtTemplates = templates;
            _builtCategory = _category;
            _builtSearch = _search;
            _builtStateVersion = s_stateVersion;

            var byKey = new Dictionary<string, Tile>(StringComparer.Ordinal);
            var paletteTiles = new List<Tile>(palette.Count);
            foreach (PaletteEntry entry in palette)
            {
                NeoWidgetPreset asset = null;
                if (entry.IsPreset) NeoWidgetPresets.TryGet(entry.preset, out asset);
                var tile = new Tile(entry, asset);
                paletteTiles.Add(tile);
                byKey[tile.key] = tile;
            }
            var templateTiles = new List<Tile>(templates.Count);
            foreach (TemplateEntry template in templates)
            {
                var tile = new Tile(template);
                templateTiles.Add(tile);
                byKey[tile.key] = tile;
            }

            // Searching spans EVERYTHING (Doozy-style), regardless of the category filter.
            if (!string.IsNullOrEmpty(_search))
            {
                var results = new Section { title = "Results" };
                foreach (Tile tile in paletteTiles) if (Matches(tile, _search)) results.tiles.Add(tile);
                foreach (Tile tile in templateTiles) if (Matches(tile, _search)) results.tiles.Add(tile);
                if (results.tiles.Count > 0) _sections.Add(results);
                return;
            }

            if (_category == FavoritesCategory)
            {
                AddFavoritesSection(paletteTiles, templateTiles);
                return;
            }
            if (_category == TemplatesCategory)
            {
                AddSection(TemplatesCategory, templateTiles);
                return;
            }
            if (_category == AllCategories)
            {
                AddFavoritesSection(paletteTiles, templateTiles);
                AddRecentsSection(byKey);
                AddPaletteSections(paletteTiles, only: null);
                AddSection(TemplatesCategory, templateTiles);
                return;
            }
            AddPaletteSections(paletteTiles, only: _category);
        }

        private void AddPaletteSections(List<Tile> tiles, string only)
        {
            Section current = null;
            foreach (Tile tile in tiles)
            {
                string category = tile.entry.category;
                if (only != null && !string.Equals(category, only, StringComparison.Ordinal)) continue;
                if (current == null || !string.Equals(current.title, category, StringComparison.Ordinal))
                {
                    current = new Section { title = category };
                    _sections.Add(current);
                }
                current.tiles.Add(tile);
            }
        }

        private void AddFavoritesSection(List<Tile> paletteTiles, List<Tile> templateTiles)
        {
            var section = new Section { title = FavoritesCategory };
            foreach (Tile tile in paletteTiles) if (IsFavorite(tile.key)) section.tiles.Add(tile);
            foreach (Tile tile in templateTiles) if (IsFavorite(tile.key)) section.tiles.Add(tile);
            if (section.tiles.Count > 0) _sections.Add(section);
        }

        private void AddRecentsSection(Dictionary<string, Tile> byKey)
        {
            var section = new Section { title = "Recent" };
            foreach (string key in Recents())
                if (byKey.TryGetValue(key, out Tile tile))
                    section.tiles.Add(tile);
            if (section.tiles.Count > 0) _sections.Add(section);
        }

        private void AddSection(string title, List<Tile> tiles)
        {
            if (tiles.Count == 0) return;
            var section = new Section { title = title };
            section.tiles.AddRange(tiles);
            _sections.Add(section);
        }

        private static bool Matches(Tile tile, string needle)
        {
            if (tile.isTemplate)
                return Contains(tile.template.label, needle) || Contains(tile.template.id, needle);
            return Contains(tile.entry.label, needle)
                || Contains(tile.entry.kind, needle)
                || Contains(tile.entry.category, needle);
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack)
            && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
