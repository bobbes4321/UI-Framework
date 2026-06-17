using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The front door of the package: a single window that opens every showcase AND every Neo UI tool
    /// in one click — no knowledge of the generate → build → open sequence (or the menu tree) required.
    /// Two tabs:
    /// <list type="bullet">
    /// <item><b>Showcases</b> — a <b>setup-status strip</b> (Settings / Starter Kit / Fonts indicators +
    ///   Repair All), then a <b>searchable, category-grouped gallery</b> with lazily rendered thumbnails
    ///   (only for showcases already generated — never paying the render cost for unbuilt ones) and a
    ///   <b>per-showcase action row</b>: Open, Regenerate (→ <see cref="SpecBaseline.Sync"/>), Edit in
    ///   Composer, and an inline Check-Drift badge.</item>
    /// <item><b>Tools</b> — a launcher for every Neo UI window / wizard / menu action, grouped by
    ///   category. Sourced from <see cref="HubToolRegistry"/> (a project adds its own with one Register
    ///   call) — windows open directly, menu actions route through their exact menu path.</item>
    /// </list>
    /// Built entirely on the EditorUI kit. No editor-tick subscriptions, no animated chrome — setup
    /// status and drift recompute on focus / explicit action only (CLAUDE.md editor-perf rules).
    /// </summary>
    public sealed class NeoUIHubWindow : EditorWindow
    {
        private const string ThumbnailCacheDir = "Temp/neo-gallery";
        private const int ThumbnailMaxEdge = 320;
        private const string TabKey = "Neo.UI.Hub.Tab";

        private enum Tri { Unknown, Present, Missing }

        private static readonly string[] TabLabels = { "Showcases", "Tools" };

        private Tri _settingsState = Tri.Unknown;
        private Tri _starterState = Tri.Unknown;
        private Tri _fontsState = Tri.Unknown;

        private string _search = "";
        private string _categoryFilter = AllCategories;
        private const string AllCategories = "All";

        private string _selectedId;
        private Vector2 _scroll;
        private Vector2 _toolsScroll;

        // drift badge cache, keyed by showcase id (recomputed on demand, never on tick)
        private readonly Dictionary<string, (int roundTrip, int offSpec, bool hasBaseline)> _drift =
            new Dictionary<string, (int, int, bool)>();
        // lazily rendered thumbnails keyed by showcase id
        private readonly Dictionary<string, Texture2D> _thumbnails = new Dictionary<string, Texture2D>();

        private GUIStyle _placeholder;
        private GUIStyle _toolButton;

        [MenuItem("Tools/Neo UI/Hub", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<NeoUIHubWindow>("Neo UI Hub");
            window.minSize = new Vector2(560f, 440f);
            window.Show();
        }

        private void OnEnable() => RecomputeSetup();
        private void OnFocus() => RecomputeSetup();

        private void OnDisable()
        {
            foreach (Texture2D tex in _thumbnails.Values)
                if (tex != null) DestroyImmediate(tex);
            _thumbnails.Clear();
        }

        // ------------------------------------------------------------------ setup status

        private void RecomputeSetup()
        {
            NeoUISettings settings =
                AssetDatabase.LoadAssetAtPath<NeoUISettings>(NeoUISettingsBootstrap.SettingsAssetPath);
            _settingsState = settings != null ? Tri.Present : Tri.Missing;
            // starter kit: a factory-referenced token present on the theme = the kit was expanded
            _starterState = settings != null && settings.theme != null
                && settings.theme.HasToken(UIWidgetFactory.TokenPrimary) ? Tri.Present : Tri.Missing;
            // fonts: the icon font wired onto settings is the canonical "fonts generated" signal
            _fontsState = settings != null && settings.iconFont != null ? Tri.Present : Tri.Missing;
            Repaint();
        }

        private void RepairAll()
        {
            NeoUISettings settings = NeoUISettingsBootstrap.EnsureSettings();
            StarterKitBootstrap.CreateOrRepair();
            FontAssetBootstrap.EnsureIconFont(settings);
            AssetDatabase.SaveAssets();
            RecomputeSetup();
        }

        // ------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            EnsureStyles();
            NeoGUI.ComponentHeader("Neo UI Hub",
                "The front door — every showcase and every tool, one click away", NeoColors.Containers);

            GUILayout.Space(2f);
            int tab = NeoGUI.Tabs(TabKey, TabLabels);
            NeoGUI.Splitter();
            GUILayout.Space(2f);

            if (tab == 1) DrawToolsTab();
            else DrawShowcasesTab();
        }

        // ------------------------------------------------------------------ showcases tab

        private void DrawShowcasesTab()
        {
            DrawSetupStrip();
            NeoGUI.Splitter();
            DrawFilterBar();

            List<Showcase> showcases = Filtered();
            if (showcases.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    ShowcaseRegistry.All.Count == 0
                        ? "No showcases registered yet.\nDrop a ShowcaseDefinition asset, or seed built-ins."
                        : "No showcases match the current filter / search.",
                    _placeholder);
                GUILayout.FlexibleSpace();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (IGrouping<string, Showcase> group in GroupByCategory(showcases))
            {
                DrawSectionLabel(string.IsNullOrEmpty(group.Key) ? "Uncategorized" : group.Key,
                    AccentForCategory(group.Key));
                foreach (Showcase s in group) DrawShowcaseRow(s);
            }
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ tools tab

        private void DrawToolsTab()
        {
            GUILayout.Label(
                "Open any Neo UI window, wizard or menu action. Project-registered tools appear here too.",
                NeoStyles.MiniDim);
            GUILayout.Space(2f);

            IReadOnlyList<HubTool> tools = HubToolRegistry.All;
            if (tools.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("No tools registered.", _placeholder);
                GUILayout.FlexibleSpace();
                return;
            }

            _toolsScroll = EditorGUILayout.BeginScrollView(_toolsScroll);
            foreach (IGrouping<string, HubTool> group in GroupTools(tools))
            {
                Color accent = AccentForCategory(group.Key);
                DrawSectionLabel(string.IsNullOrEmpty(group.Key) ? "Other" : group.Key, accent);
                DrawToolGrid(group, accent);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary> A responsive grid of tool buttons — column count tracks the window width. </summary>
        private void DrawToolGrid(IEnumerable<HubTool> group, Color sectionAccent)
        {
            const float minButtonWidth = 168f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 16f) / minButtonWidth));

            int i = 0;
            bool rowOpen = false;
            foreach (HubTool t in group)
            {
                if (i % columns == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    rowOpen = true;
                }

                Color accent = t.accent ?? sectionAccent;
                Color previousBg = GUI.backgroundColor;
                GUI.backgroundColor = accent;
                var content = new GUIContent(t.label, t.tooltip);
                if (GUILayout.Button(content, _toolButton, GUILayout.Height(34f), GUILayout.ExpandWidth(true)))
                    InvokeTool(t);
                GUI.backgroundColor = previousBg;

                i++;
                if (i % columns == 0) { EditorGUILayout.EndHorizontal(); rowOpen = false; }
            }
            if (rowOpen) EditorGUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        private static void InvokeTool(HubTool t)
        {
            try { t.invoke?.Invoke(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Neo.UI] Hub tool '{t.id}' threw: {e.Message}");
            }
        }

        private static IEnumerable<IGrouping<string, HubTool>> GroupTools(IEnumerable<HubTool> tools) =>
            tools.GroupBy(t => t.category ?? "")
                 .OrderBy(g => CategoryOrder(g.Key))
                 .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        /// <summary> Keeps the built-in buckets in a deliberate order; unknown categories sort after. </summary>
        private static int CategoryOrder(string category) => category switch
        {
            HubToolRegistryDefaults.Author => 0,
            HubToolRegistryDefaults.Data => 1,
            HubToolRegistryDefaults.Setup => 2,
            HubToolRegistryDefaults.Advanced => 3,
            _ => 4,
        };

        // ------------------------------------------------------------------ shared chrome

        /// <summary> Section label with a leading accent tick, used by both tabs for a consistent look. </summary>
        private static void DrawSectionLabel(string text, Color accent)
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect tick = GUILayoutUtility.GetRect(3f, 14f, GUILayout.Width(3f));
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(tick, accent);
                GUILayout.Space(4f);
                GUILayout.Label(text, EditorStyles.boldLabel);
            }
        }

        /// <summary> Maps a category name to its NeoColors family accent (CLAUDE.md accent map). </summary>
        private static Color AccentForCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return NeoColors.TextSubtle;
            switch (category)
            {
                case HubToolRegistryDefaults.Author: return NeoColors.Containers;
                case HubToolRegistryDefaults.Data: return NeoColors.Data;
                case HubToolRegistryDefaults.Setup: return NeoColors.Theming;
                case HubToolRegistryDefaults.Advanced: return NeoColors.Flow;
                // showcase categories
                case "Apps": return NeoColors.Containers;
                case "Menus": return NeoColors.Interactive;
                case "Widgets": return NeoColors.Signals;
                default: return NeoColors.TextSubtle;
            }
        }

        private void EnsureStyles()
        {
            if (_placeholder != null) return;
            _placeholder = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 12
            };
            _toolButton = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 11,
                padding = new RectOffset(8, 8, 4, 4)
            };
        }

        private void DrawSetupStrip()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Setup", EditorStyles.miniBoldLabel, GUILayout.Width(42f));
                Indicator("Settings", _settingsState);
                Indicator("Starter Kit", _starterState);
                Indicator("Fonts", _fontsState);
                GUILayout.FlexibleSpace();

                bool allGood = _settingsState == Tri.Present && _starterState == Tri.Present
                    && _fontsState == Tri.Present;
                using (new EditorGUI.DisabledScope(allGood))
                    if (GUILayout.Button(allGood ? "All set" : "Repair All", GUILayout.Width(90f)))
                        RepairAll();
            }
        }

        private static void Indicator(string label, Tri state)
        {
            Color dot = state == Tri.Present ? NeoColors.Add
                : state == Tri.Missing ? NeoColors.Warning : NeoColors.TextSubtle;
            Color previous = GUI.color;
            GUI.color = dot;
            GUILayout.Label("●", GUILayout.Width(14f)); // ●
            GUI.color = previous;
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(72f));
        }

        private void DrawFilterBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string[] categories = Categories();
                int current = Mathf.Max(0, Array.IndexOf(categories, _categoryFilter));
                int next = EditorGUILayout.Popup(current, categories, GUILayout.Width(160f));
                _categoryFilter = categories[Mathf.Clamp(next, 0, categories.Length - 1)];

                GUILayout.FlexibleSpace();
                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(180f));
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    ShowcaseRegistry.InvalidateDiscovery();
                    _drift.Clear();
                    ClearThumbnails();
                    RecomputeSetup();
                }
            }
        }

        private void DrawShowcaseRow(Showcase s)
        {
            bool selected = string.Equals(s.id, _selectedId, StringComparison.Ordinal);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // thumbnail (lazy; only for already-generated showcases)
                    Rect thumb = GUILayoutUtility.GetRect(72f, 48f, GUILayout.Width(72f), GUILayout.Height(48f));
                    DrawThumbnail(s, thumb);

                    using (new EditorGUILayout.VerticalScope())
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(s.title ?? s.id, EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                            DrawDriftBadge(s);
                            bool built = IsGenerated(s);
                            NeoGUI.Badge(built ? "built" : "not built",
                                built ? NeoColors.Add : NeoColors.TextSubtle);
                        }
                        if (!string.IsNullOrEmpty(s.description))
                            GUILayout.Label(s.description, EditorStyles.wordWrappedMiniLabel);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open", GUILayout.Height(22f)))
                    {
                        _selectedId = s.id;
                        ShowcaseRunner.Open(s);
                        InvalidateShowcaseCaches(s);
                        RecomputeSetup();
                    }
                    if (GUILayout.Button("Regenerate", GUILayout.Height(22f))) Regenerate(s);
                    if (GUILayout.Button("Edit in Composer", GUILayout.Height(22f)))
                    {
                        if (string.IsNullOrEmpty(s.specPath))
                            Debug.LogWarning($"[Neo.UI] Showcase '{s.id}' has no spec to edit.");
                        else
                            Composer.NeoComposerWindow.Open(s); // scopes Save to the showcase's own root
                    }
                    if (GUILayout.Button("Check Drift", GUILayout.Height(22f))) CheckDrift(s);
                }
                _ = selected;
            }
        }

        private void DrawThumbnail(Showcase s, Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.22f));
            Texture2D tex = ResolveThumbnail(s);
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
            else GUI.Label(rect, IsGenerated(s) ? "…" : "", _placeholder);
        }

        // ------------------------------------------------------------------ actions

        private void Regenerate(Showcase s)
        {
            _selectedId = s.id;
            SyncResult result = ShowcaseRunner.Regenerate(s);
            if (result == null) return;
            InvalidateShowcaseCaches(s);

            if (result.refused || result.conflicts.Count > 0 || result.offSpecWarnings.Count > 0)
            {
                // surface the conflict / off-spec detail in the existing Sync window rather than a toast;
                // pass the showcase so its "Re-run Sync" / "Force sync" run scoped to its isolated root
                SyncWindow.Show(result, s.specPath, s);
                Debug.LogWarning($"[Neo.UI] Showcase '{s.id}' regenerate needs review — {result.note}");
            }
            else
            {
                Debug.Log($"[Neo.UI] Showcase '{s.id}' regenerated — {result.note}");
            }
            Repaint();
        }

        private void CheckDrift(Showcase s)
        {
            using (NeoWorkspace.Scoped(s))
            {
                UISpec baseline = NeoBaseline.Load();
                if (baseline == null)
                {
                    _drift[s.id] = (0, 0, false);
                }
                else
                {
                    UISpec current = UISpecExporter.ExportProject();
                    List<SpecChange> changes = SpecDiff.Compare(baseline, current);
                    List<OffSpecFinding> offSpec = OffSpecLint.ScanProject(baseline);
                    _drift[s.id] = (changes.Count, offSpec.Count, true);
                }
            }
            Repaint();
        }

        private void DrawDriftBadge(Showcase s)
        {
            if (!_drift.TryGetValue(s.id, out (int roundTrip, int offSpec, bool hasBaseline) d)) return;
            if (!d.hasBaseline) { NeoGUI.Badge("no baseline", NeoColors.TextSubtle); return; }
            if (d.offSpec > 0) NeoGUI.Badge($"{d.offSpec} off-spec", NeoColors.Remove);
            else if (d.roundTrip > 0) NeoGUI.Badge($"{d.roundTrip} drift", NeoColors.Warning);
            else NeoGUI.Badge("clean", NeoColors.Add);
        }

        // ------------------------------------------------------------------ thumbnails

        private Texture2D ResolveThumbnail(Showcase s)
        {
            if (_thumbnails.TryGetValue(s.id, out Texture2D cached)) return cached;

            // committed thumbnail wins (built-ins prefer a baked PNG)
            if (!string.IsNullOrEmpty(s.thumbnail))
            {
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(s.thumbnail);
                if (asset != null) { _thumbnails[s.id] = asset; return asset; }
            }

            // otherwise render the first generated view — but ONLY if the showcase is already built,
            // so we never pay the render cost for an unbuilt showcase
            Texture2D rendered = IsGenerated(s) ? RenderFirstView(s) : null;
            _thumbnails[s.id] = rendered; // cache even null so we don't retry every repaint
            return rendered;
        }

        private Texture2D RenderFirstView(Showcase s)
        {
            try
            {
                using (NeoWorkspace.Scoped(s))
                {
                    string folder = $"{s.GeneratedRoot}/Views";
                    if (!AssetDatabase.IsValidFolder(folder)) return null;
                    string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                    if (guids.Length == 0) return null;

                    // deterministic pick: first view prefab by path
                    GameObject prefab = guids
                        .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                        .Where(p => p != null && p.GetComponent<UIView>() != null)
                        .OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (prefab == null) return null;

                    Directory.CreateDirectory(ThumbnailCacheDir);
                    string path = $"{ThumbnailCacheDir}/hub_{s.id}.png";
                    UIScreenshotter.Capture(prefab, path, ThumbnailMaxEdge, ThumbnailMaxEdge);
                    if (!File.Exists(path)) return null;
                    var tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
                    DestroyImmediate(tex);
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Neo.UI] Hub couldn't render thumbnail for '{s.id}': {e.Message}");
                return null;
            }
        }

        private void ClearThumbnails()
        {
            foreach (Texture2D tex in _thumbnails.Values)
                // never destroy committed assets (asset textures aren't HideAndDontSave-flagged by us)
                if (tex != null && (tex.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(tex);
            _thumbnails.Clear();
        }

        private void InvalidateShowcaseCaches(Showcase s)
        {
            _drift.Remove(s.id);
            if (_thumbnails.TryGetValue(s.id, out Texture2D tex))
            {
                if (tex != null && (tex.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(tex);
                _thumbnails.Remove(s.id);
            }
        }

        // ------------------------------------------------------------------ filtering

        private static bool IsGenerated(Showcase s) =>
            AssetDatabase.IsValidFolder($"{s.GeneratedRoot}/Views");

        private List<Showcase> Filtered()
        {
            var list = new List<Showcase>();
            foreach (Showcase s in ShowcaseRegistry.All)
            {
                if (_categoryFilter != AllCategories
                    && !string.Equals(s.category ?? "", _categoryFilter, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(_search))
                {
                    string hay = $"{s.title} {s.id} {s.description}";
                    if (hay.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                list.Add(s);
            }
            return list;
        }

        private static IEnumerable<IGrouping<string, Showcase>> GroupByCategory(IEnumerable<Showcase> showcases) =>
            showcases.GroupBy(s => s.category ?? "").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        private string[] Categories()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Showcase s in ShowcaseRegistry.All)
                if (!string.IsNullOrEmpty(s.category)) set.Add(s.category);
            var list = new List<string> { AllCategories };
            list.AddRange(set);
            return list.ToArray();
        }
    }
}
