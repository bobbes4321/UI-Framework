using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using Neo.UI.Editor.Authoring;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Presets" tab (Phase 2.3): a real editor over the discovered
    /// <see cref="NeoWidgetPreset"/> library, no longer a list-and-ping shell. Laid out master–detail
    /// (<c>ownsLayout: true</c> on the descriptor + <see cref="DesignSystemGUI.BeginSplitPane"/>) so a card
    /// click never pushes the editor below the fold: a fixed-width, independently-scrolling LEFT (browse)
    /// column — kind filter + search, the thumbnail card grid (the same visuals as the scene-view overlay's
    /// <c>PresetPickerPopup</c> — shared <see cref="PresetThumbnailCache"/> / <see cref="PresetThumbnailRenderer"/>),
    /// and the create / new-from-selection actions — beside a flexible, independently-scrolling RIGHT
    /// (detail) column that renders the SAME form as the preset inspector (via the shared
    /// <see cref="WidgetPresetGUI"/> drawer) with per-preset duplicate / delete actions, or a friendly
    /// empty-state hint when nothing is selected. The left column is sized to exactly two card columns
    /// (<see cref="LeftPaneWidth"/>, derived from <see cref="CardW"/>/<see cref="Gap"/> so cards never clip).
    /// <para>
    /// Perf discipline (CLAUDE.md flow-window + IMGUI rules): ONE cached <see cref="SerializedObject"/> for
    /// the selected preset, recreated only when the selection changes — never per OnGUI. Thumbnails come
    /// from the shared cache (rendered once per look, keyed by a content hash so an edit refreshes the
    /// card automatically) — never rendered per OnGUI. The "new from selection" enablement probe runs only
    /// when the scene selection changes, not every repaint. Both panes' scroll positions live in the tab's
    /// per-window <see cref="State"/> (never allocated per OnGUI). GUIStyles are built lazily and reused.
    /// </para>
    /// </summary>
    internal static class PresetsTab
    {
        // Card metrics (mirrors PresetPickerPopup so the grid reads identically).
        private const float CardW = 132f;
        private const float CardThumb = 104f;
        private const float CardLabelH = 18f;
        private const float CardH = CardThumb + CardLabelH + 6f;
        private const float Gap = 6f;

        // Default left (browse) pane width: exactly two card columns plus the vertical-scrollbar
        // allowance, derived from the card metrics so cards never clip (CLAUDE.md: derive, don't
        // hardcode a magic number). The pane is user-resizable (DesignSystemGUI split-pane drag) — this
        // is only the starting width; DrawGrid derives its column count from the CURRENT width, so
        // widening the pane reveals more columns.
        private const int LeftColumns = 2;
        private const float DefaultLeftWidth =
            LeftColumns * CardW + (LeftColumns - 1) * Gap + NeoGUI.ScrollbarAllowance;

        // Drag clamp + SessionState key (per-tab, keyed like NeoDesignSystemWindow.TabKey) so a resize
        // survives window close/reopen within the session. RightMinWidth keeps the detail pane usable.
        private const float LeftMinWidth = 200f;
        private const float RightMinWidth = 320f;
        private const string LeftWidthKey = "NeoUI.DesignSystem.Presets.LeftWidth";

        private const string AllKinds = "All";

        /// <summary>
        /// Per-window UI state for the Presets tab. <see cref="IDisposable"/> so the window can dispose the
        /// cached <see cref="SerializedObject"/> and release the shared thumbnail cache on close (mirrors
        /// how <c>PresetPickerPopup</c> clears the cache on its close — the cache OWNS its textures).
        /// </summary>
        internal sealed class State : IDisposable
        {
            public string kindFilter = AllKinds;
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

            // The hosting window (set each draw from the context) so a card click can request a repaint.
            public EditorWindow window;

            // In-tab selection (NOT Unity Selection — leaving the scene selection intact matters for the
            // "New From Selection" action) + the ONE cached SerializedObject over it.
            public NeoWidgetPreset selected;
            public SerializedObject so;

            // Create-flow fields.
            public string newName = "";
            public string newKind = "button";

            // Cached "is the current scene selection a preset-able Neo widget" probe — recomputed only
            // when the active object changes, never per OnGUI (ExportElement isn't free).
            public int probedSelectionId;
            public bool selectionIsWidget;

            // --- "all presets" snapshot cache (CLAUDE.md: no uncached LINQ in draw paths) ---
            // Rebuilt only when the registry's discovered count changes (create/delete/import) — mirrors
            // MotionTab.EnsureModel's builtForCount guard.
            private List<NeoWidgetPreset> _allCache;
            private int _allBuiltForCount = -1;

            // --- filtered "visible" snapshot cache, keyed off the "all" cache reference + the exact filter
            // inputs it was built against. An in-place field edit (rename/re-kind via the embedded form on
            // the right) can change filter membership WITHOUT touching the registry count, so
            // DrawEditorPane calls MarkFilterDirty() on every applied edit and that version rides along.
            private List<NeoWidgetPreset> _visibleCache;
            private List<NeoWidgetPreset> _visibleBuiltForAll;
            private string _visibleBuiltForKind;
            private string _visibleBuiltForSearch;
            private int _visibleBuiltForVersion = -1;
            private int _filterVersion;

            /// <summary> Returns the cached null-filtered snapshot of <see cref="NeoWidgetPresets.All"/>,
            /// rebuilding only when the discovered count changed. </summary>
            public List<NeoWidgetPreset> GetAll()
            {
                IReadOnlyList<NeoWidgetPreset> registryAll = NeoWidgetPresets.All;
                if (_allCache == null || _allBuiltForCount != registryAll.Count)
                {
                    _allCache = registryAll.Where(p => p != null).ToList();
                    _allBuiltForCount = registryAll.Count;
                }
                return _allCache;
            }

            /// <summary> Returns the cached kind/search-filtered snapshot of <paramref name="all"/>,
            /// rebuilding only when the source list, the kind filter, the search text or
            /// <see cref="MarkFilterDirty"/> changed since the last build. </summary>
            public List<NeoWidgetPreset> GetVisible(List<NeoWidgetPreset> all, string kindFilter, string search)
            {
                if (_visibleCache != null && _visibleBuiltForAll == all
                    && _visibleBuiltForKind == kindFilter && _visibleBuiltForSearch == search
                    && _visibleBuiltForVersion == _filterVersion)
                    return _visibleCache;

                string needle = string.IsNullOrEmpty(search) ? null : search.ToLowerInvariant();
                _visibleCache = all.Where(p => Matches(p, kindFilter, needle)).ToList();
                _visibleBuiltForAll = all;
                _visibleBuiltForKind = kindFilter;
                _visibleBuiltForSearch = search;
                _visibleBuiltForVersion = _filterVersion;
                return _visibleCache;
            }

            /// <summary> Invalidates the "visible" filter cache — call after an in-place preset field edit
            /// (rename/re-kind) that could change filter membership without changing the registry count. </summary>
            public void MarkFilterDirty() => _filterVersion++;

            public void Dispose()
            {
                so?.Dispose();
                so = null;
                selected = null;
                // The cache is shared editor-wide; clearing on close mirrors the picker popup and just
                // forces a cheap lazy re-render for the next consumer (no textures leak).
                PresetThumbnailCache.Clear();
            }
        }

        internal static object CreateState()
        {
            var s = new State();
            s.LoadWidth(LeftWidthKey, DefaultLeftWidth);
            return s;
        }

        // Card label + headless fallback styles — built once, reused (never per OnGUI).
        private static GUIStyle _cardLabel, _cardFallback;

        private static GUIStyle CardLabel => _cardLabel ??= new GUIStyle(EditorStyles.miniLabel)
        { alignment = TextAnchor.MiddleCenter, fontSize = 10, clipping = TextClipping.Clip };

        private static GUIStyle CardFallback => _cardFallback ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
        { alignment = TextAnchor.MiddleCenter, fontSize = 12, wordWrap = true };

        internal static void Draw(DesignSystemTabContext ctx)
        {
            var s = ctx.State<State>();
            s.window = ctx.window;

            // Cached snapshot of the discovered library (State.GetAll — rebuilt only on registry-count
            // change, never per OnGUI event; see State.GetAll/GetVisible).
            List<NeoWidgetPreset> all = s.GetAll();

            using (DesignSystemGUI.BeginSplitPane(ctx.window))
            {
                // LEFT (browse): filter + search, thumbnail grid, create actions. Resizable via the
                // separator drag (DesignSystemGUI.EndSplitLeft); width rides s.leftWidth, persisted below.
                DesignSystemGUI.BeginSplitLeft(ref s.leftScroll, ref s.leftWidth, LeftMinWidth, RightMinWidth);
                DrawBrowsePane(s, all);
                DesignSystemGUI.EndSplitLeft(ref s.leftWidth, LeftMinWidth, RightMinWidth);

                // Card clicks in the browse pane may have changed the selection this frame — resync the
                // cached SerializedObject before the detail pane reads it (still recreated only on change).
                SyncSelection(s);

                // RIGHT (detail): the embedded editor for the selected preset, or an empty-state hint.
                DesignSystemGUI.BeginSplitRight(ref s.rightScroll);
                DrawDetailPane(s);
                DesignSystemGUI.EndSplitRight();
            }
            s.PersistWidth(LeftWidthKey);
        }

        // ---------------------------------------------------------------- left (browse) pane

        private static void DrawBrowsePane(State s, List<NeoWidgetPreset> all)
        {
            EditorGUILayout.LabelField("Component presets", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Named component styles referenced by an element's \"preset\". " +
                "Select a card to edit it on the right.", EditorStyles.wordWrappedMiniLabel);

            DrawToolbar(s, all);
            DrawGrid(s, all);

            NeoGUI.Splitter();
            DrawCreateRow(s);
        }

        // ---------------------------------------------------------------- right (detail) pane

        private static void DrawDetailPane(State s)
        {
            if (s.selected == null || s.so == null)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Select a preset on the left to edit it.",
                    EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                return;
            }

            DrawEditorPane(s);
        }

        // ---------------------------------------------------------------- toolbar (filter + search)

        private static void DrawToolbar(State s, List<NeoWidgetPreset> all)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect kindRect = GUILayoutUtility.GetRect(new GUIContent(s.kindFilter), EditorStyles.popup,
                    GUILayout.Width(140f));
                NeoDropdown.ValuePopup(kindRect, s.kindFilter,
                    () =>
                    {
                        var kinds = new List<string> { AllKinds };
                        kinds.AddRange(all.Select(p => p.targetKind)
                            .Where(k => !string.IsNullOrEmpty(k)).Distinct().OrderBy(k => k, StringComparer.Ordinal));
                        return kinds;
                    },
                    chosen => s.kindFilter = string.IsNullOrEmpty(chosen) ? AllKinds : chosen);

                GUILayout.Space(6f);
                EditorGUILayout.LabelField("Search", GUILayout.Width(48f));
                s.search = EditorGUILayout.TextField(s.search ?? "");
            }
        }

        // ---------------------------------------------------------------- thumbnail card grid

        private static void DrawGrid(State s, List<NeoWidgetPreset> all)
        {
            // Cached (State.GetVisible — keyed off the "all" list + kind filter + search + edit version;
            // never an uncached .Where().ToList() per OnGUI event).
            List<NeoWidgetPreset> visible = s.GetVisible(all, s.kindFilter, s.search);

            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox(all.Count == 0
                    ? "No widget presets yet. Create one below, right-click in Project (Neo UI/Widget Preset), " +
                      "or run Setup → Create or Repair Widget Presets."
                    : "No presets match the current filter/search.", MessageType.Info);
                return;
            }

            // Grid lives inside the resizable left pane (not the full window), so columns derive from the
            // CURRENT pane width minus the vertical-scrollbar allowance — deterministic and live every
            // frame, so the grid reflows immediately in BOTH directions (a width MEASURED inside the
            // scroll view gets inflated by the grid's own rows and can never shrink back — see
            // NeoGUI.ScrollbarAllowance). The leftover width is distributed into the cards
            // (NeoGUI.FitColumns) so rows fill the pane edge to edge — widening the pane widens the
            // cards until the next column fits.
            float avail = s.leftWidth - NeoGUI.ScrollbarAllowance;
            int columns = NeoGUI.FitColumns(avail, CardW, Gap, out float cardW);
            for (int i = 0; i < visible.Count; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns && i + c < visible.Count; c++)
                    {
                        if (c > 0) GUILayout.Space(Gap);
                        NeoWidgetPreset preset = visible[i + c];
                        Rect r = GUILayoutUtility.GetRect(cardW, CardH, GUILayout.Width(cardW), GUILayout.Height(CardH));
                        DrawCard(r, preset, ReferenceEquals(preset, s.selected), s);
                    }
                }
                GUILayout.Space(Gap);
            }
        }

        private static bool Matches(NeoWidgetPreset p, string kindFilter, string needle)
        {
            if (kindFilter != AllKinds && !string.Equals(p.targetKind, kindFilter, StringComparison.Ordinal))
                return false;
            if (needle == null) return true;
            return (p.presetName ?? "").ToLowerInvariant().Contains(needle)
                || (p.category ?? "").ToLowerInvariant().Contains(needle)
                || (p.targetKind ?? "").ToLowerInvariant().Contains(needle);
        }

        private static void DrawCard(Rect rect, NeoWidgetPreset preset, bool selected, State s)
        {
            Event e = Event.current;

            if (e.type == EventType.Repaint)
            {
                bool hover = rect.Contains(e.mousePosition);
                Color accent = NeoColors.Theming;
                EditorGUI.DrawRect(rect, selected ? accent.WithAlpha(0.22f)
                    : hover ? NeoColors.RowHover : NeoColors.SectionBackground);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), accent.WithAlpha(selected ? 1f : 0.85f));
                if (selected)
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), accent);

                var thumbRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, CardThumb - 4f);
                Texture2D thumb = PresetThumbnailCache.GetOrRender(preset, (int)CardThumb);
                if (thumb != null) GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit, alphaBlend: true);
                else CardFallback.Draw(thumbRect, new GUIContent(preset.presetName), false, false, false, false);

                CardLabel.Draw(new Rect(rect.x + 2f, rect.yMax - CardLabelH, rect.width - 4f, CardLabelH),
                    new GUIContent(preset.presetName, preset.description), false, false, false, false);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                s.selected = preset;
                GUI.FocusControl(null);
                s.window?.Repaint();
                e.Use();
            }
        }

        // ---------------------------------------------------------------- create flow

        private static void DrawCreateRow(State s)
        {
            EditorGUILayout.LabelField("Create", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                s.newName = EditorGUILayout.TextField("New preset", s.newName);

                Rect kindRect = GUILayoutUtility.GetRect(new GUIContent(s.newKind), EditorStyles.popup,
                    GUILayout.Width(110f));
                NeoDropdown.ValuePopup(kindRect, s.newKind,
                    () => new List<string>(ElementSpec.KnownKinds),
                    chosen => { if (!string.IsNullOrEmpty(chosen)) s.newKind = chosen; });

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(s.newName)))
                    if (GUILayout.Button("Create", GUILayout.Width(70f)))
                    {
                        NeoWidgetPreset created = CreatePreset(s.newName.Trim(), s.newKind);
                        if (created != null) { s.selected = created; s.newName = ""; }
                    }
            }

            // New From Selection — routes through the SAME native path (NeoSceneAuthoring), only enabled
            // when the current scene selection is a preset-able Neo widget (probe cached per selection).
            RefreshSelectionProbe(s);
            GameObject sel = Selection.activeGameObject;
            using (new EditorGUI.DisabledScope(!s.selectionIsWidget))
            {
                string tip = s.selectionIsWidget
                    ? $"Capture '{sel.name}' styling into a new preset and relink it."
                    : "Select a Neo widget inside a view in the scene to capture its styling as a preset.";
                if (GUILayout.Button(new GUIContent("New From Selection", tip)) && sel != null)
                {
                    string baseName = !string.IsNullOrWhiteSpace(s.newName) ? s.newName.Trim() : sel.name;
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{NeoWidgetPresets.PresetsRoot}/{baseName}.asset");
                    DesignSystemGUI.EnsureFolder(NeoWidgetPresets.PresetsRoot);
                    NeoWidgetPreset created = NeoSceneAuthoring.CreatePresetFromWidget(sel, path);
                    if (created != null) { s.selected = created; s.newName = ""; }
                }
            }
        }

        // Rebuilds the "current selection is a Neo widget" flag only when the active object changes.
        private static void RefreshSelectionProbe(State s)
        {
            int id = Selection.activeInstanceID;
            if (id == s.probedSelectionId) return;
            s.probedSelectionId = id;
            GameObject go = Selection.activeGameObject;
            s.selectionIsWidget = go != null && NeoSceneAuthoring.TryExportForPresetWorkflow(go) != null;
        }

        // Bare preset (name + kind); the author fills in styling in the embedded editor. Warns + aborts on
        // a name collision so the feedback is explicit (duplicate/from-selection auto-unique the name).
        private static NeoWidgetPreset CreatePreset(string name, string kind)
        {
            if (NeoWidgetPresets.TryGet(name, out _))
            {
                Debug.LogWarning($"[Neo.UI] A preset named '{name}' already exists.");
                return null;
            }
            if (string.IsNullOrEmpty(kind)) kind = "button";

            DesignSystemGUI.EnsureFolder(NeoWidgetPresets.PresetsRoot);
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            preset.presetName = name;
            preset.targetKind = kind;
            preset.category = kind == "button" ? "Button" : "Custom";
            AssetDatabase.CreateAsset(preset, $"{NeoWidgetPresets.PresetsRoot}/{name}.asset");
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.InvalidateDiscovery();
            PresetThumbnailCache.Invalidate();
            return preset;
        }

        // ---------------------------------------------------------------- embedded editor pane

        // Keeps the cached SerializedObject in sync with the in-tab selection — recreated only when the
        // selection actually changes (the flow-window rule), and dropped when the asset is gone.
        private static void SyncSelection(State s)
        {
            if (s.selected == null)
            {
                if (s.so != null) { s.so.Dispose(); s.so = null; }
                return;
            }
            if (s.so == null || s.so.targetObject != s.selected)
            {
                s.so?.Dispose();
                s.so = new SerializedObject(s.selected);
            }
        }

        private static void DrawEditorPane(State s)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Editing: {s.selected.presetName}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Duplicate", GUILayout.Width(80f))) Duplicate(s);
                if (GUILayout.Button("Delete", GUILayout.Width(70f))) Delete(s);
            }

            // Delete clears the selection (and its SerializedObject) mid-draw — bail before touching it.
            if (s.so == null) return;

            // The shared drawer owns the form; this tab owns the Update/Apply transaction (its contract).
            // A distinct key prefix keeps window foldout state independent from the inspector's.
            s.so.UpdateIfRequiredOrScript();
            WidgetPresetGUI.Draw(s.so, "NeoUI.DesignSystem.Presets");
            // A rename/re-kind edit can change grid filter membership without touching the registry count
            // (create/delete already invalidate via NeoWidgetPresets.InvalidateDiscovery) — bump the filter
            // version so State.GetVisible rebuilds instead of serving a stale cached snapshot.
            if (s.so.ApplyModifiedProperties()) s.MarkFilterDirty();
        }

        private static void Duplicate(State s)
        {
            string src = AssetDatabase.GetAssetPath(s.selected);
            if (string.IsNullOrEmpty(src)) return;
            string dst = AssetDatabase.GenerateUniqueAssetPath(
                $"{NeoWidgetPresets.PresetsRoot}/{s.selected.presetName} Copy.asset");
            if (!AssetDatabase.CopyAsset(src, dst)) { Debug.LogWarning($"[Neo.UI] Could not duplicate '{src}'."); return; }

            var copy = AssetDatabase.LoadAssetAtPath<NeoWidgetPreset>(dst);
            if (copy != null)
            {
                // Keep the addressable id in sync with the new file name (presetName is the spec key).
                copy.presetName = System.IO.Path.GetFileNameWithoutExtension(dst);
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssets();
            }
            NeoWidgetPresets.InvalidateDiscovery();
            PresetThumbnailCache.Invalidate();
            s.selected = copy;
        }

        private static void Delete(State s)
        {
            NeoWidgetPreset preset = s.selected;
            if (preset == null) return;
            if (!EditorUtility.DisplayDialog("Delete preset",
                    $"Delete the preset \"{preset.presetName}\"?\n\nThis removes the asset from the project. " +
                    "Elements still referencing it by name will fall back to their own fields on the next generate.",
                    "Delete", "Cancel"))
                return;

            string path = AssetDatabase.GetAssetPath(preset);
            s.selected = null;               // drops the cached SerializedObject on the next SyncSelection
            s.so?.Dispose();
            s.so = null;
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
            NeoWidgetPresets.InvalidateDiscovery();
            PresetThumbnailCache.Invalidate();
        }
    }
}
