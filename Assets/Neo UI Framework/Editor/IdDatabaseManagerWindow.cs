using System;
using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The ID Database Manager (<c>Tools → Neo UI → ID Database Manager</c>): a single surface to SEE
    /// and MANAGE every id database on <see cref="NeoUISettings"/> — browse, full CRUD (add / rename /
    /// delete categories and names), a global search across all databases, and a usage/orphan view that
    /// cross-references DB entries against the ids actually referenced by UISpecs in the project.
    ///
    /// The window enumerates databases ONLY through <see cref="NeoUISettings.AllIdDatabases"/> — the
    /// same seam <see cref="NeoUISettings.GetDatabaseFor"/> resolves through — so it picks up every
    /// built-in AND any project-registered database with no list hardcoded here.
    ///
    /// IMGUI conventions (CLAUDE.md): cached styles, inline type-and-add rows (no modal dialogs),
    /// conditional draw, NeoColors family accents, CRUD through Undo + EditorUtility.SetDirty. The
    /// project scan is explicit (a Scan button) — never per-OnGUI.
    /// </summary>
    public class IdDatabaseManagerWindow : EditorWindow
    {
        private const string TabKey = "Neo.IdDbManager.Tab";

        [MenuItem("Tools/Neo UI/ID Database Manager", priority = 9)]
        public static void Open()
        {
            var window = GetWindow<IdDatabaseManagerWindow>("ID Databases");
            window.minSize = new Vector2(560f, 360f);
        }

        /// <summary>
        /// Deep-linked open — the one-click jump from an id dropdown row: lands on the Browse tab with
        /// <paramref name="database"/> (and, when given, <paramref name="category"/>/<paramref name="name"/>)
        /// pre-selected. A null/unregistered database (or missing category) just opens the window.
        /// </summary>
        public static void Open(IdDatabase database, string category = null, string name = null)
        {
            Open();
            SessionState.SetInt(TabKey, 0); // Browse & Edit
            GetWindow<IdDatabaseManagerWindow>("ID Databases").FocusEntry(database, category, name);
        }

        private void FocusEntry(IdDatabase database, string category, string name)
        {
            NeoUISettings settings = NeoUISettings.instance;
            if (settings == null || database == null) return;

            List<IdDatabaseDescriptor> databases = CollectDescriptors(settings);
            for (int i = 0; i < databases.Count; i++)
            {
                if (databases[i].database != database) continue;
                _selectedDatabase = i;
                CancelEdit();
                _selName = null;
                if (!string.IsNullOrEmpty(category) && database.ContainsCategory(category))
                {
                    SelectCategory(databases[i], category);
                    if (!string.IsNullOrEmpty(name)) _selName = name;
                }
                break;
            }
            Repaint();
        }

        // selection / browse state
        private int _selectedDatabase;
        private Vector2 _listScroll;     // databases rail (pane 1)
        private Vector2 _categoryScroll; // categories column (pane 2)
        private Vector2 _detailScroll;   // names detail (pane 3)

        // browse: the selected row (drives pane 3 + footer). _selCategory drives the names pane;
        // _selName (may be null) is the highlighted name within it.
        private string _selCategory;
        private string _selName;

        // browse: remembered selected category PER database (by label), so switching databases away
        // and back restores context. Cleared/repaired when the category no longer exists.
        private readonly Dictionary<string, string> _categoryByDatabase = new Dictionary<string, string>();

        // pane widths (databases rail + categories column) — drag the splitters between panes to
        // resize; [SerializeField] so the chosen widths survive domain reloads / window reopen. The
        // names pane (pane 3) takes whatever's left. Clamped against the window width each frame.
        [SerializeField] private float _databasesPaneWidth = 190f;
        [SerializeField] private float _categoriesPaneWidth = 200f;

        private const float MinPaneWidth = 120f;   // floor for either resizable pane
        private const float MinNamesWidth = 220f;  // keep the names pane usable on a narrow window

        // inline edit (one in flight) — _editKind: "renameCat"/"renameName"/"addName"/"addCategory"
        private string _editKind;
        private string _editCategory; // context for the edit (category being renamed / added into)
        private string _editName;     // old name being renamed (for renameName)
        private string _editBuffer = "";

        // browse: scoped substring filter over THIS database (separate from the global Search tab)
        private string _browseFilter = "";

        // browse: row filter — All / Orphans / Dangling
        private enum BrowseFilter { All, Orphans, Dangling }
        private BrowseFilter _rowFilter = BrowseFilter.All;

        // global Search tab
        private string _search = "";

        // rename behavior: when true, a rename ALSO rewrites matching references across UISpecs
        private bool _rewriteRefs = true;

        // usage scan (explicit, cached until re-scanned). _usageStale = an edit happened since the scan.
        private IdUsageScanner.Usage _usage;
        private bool _usageStale;
        private bool _orphansOnly;

        // cached icon-button styles (built once, never per-OnGUI)
        private GUIStyle _iconButton;
        private GUIStyle _nameLabel;
        private GUIStyle _categoryLabel;
        private GUIStyle _rightMeta;
        private GUIStyle _bulletDim;

        private void EnsureStyles()
        {
            if (_iconButton != null) return;
            _iconButton = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13, // larger glyphs / bigger hit target than the old crammed 11px icons
                padding = new RectOffset(0, 0, 0, 0)
            };
            _nameLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
            _categoryLabel = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = NeoColors.TextTitle }
            };
            _rightMeta = new GUIStyle(NeoStyles.MiniDim) { alignment = TextAnchor.MiddleRight };
            _bulletDim = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = NeoColors.TextDim }
            };
        }

        private void OnGUI()
        {
            EnsureStyles();
            NeoUISettings settings = NeoUISettings.instance;
            NeoGUI.ComponentHeader("ID Database Manager",
                "Browse, edit and audit every Category/Name id database", NeoColors.Data);

            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "No NeoUISettings asset found. Create one via Tools → Neo UI → Create or Repair Settings.",
                    MessageType.Warning);
                return;
            }

            List<IdDatabaseDescriptor> databases = CollectDescriptors(settings);

            int tab = NeoGUI.Tabs(TabKey, new[] { "Browse & Edit", "Search", "Usage / Orphans" });
            switch (tab)
            {
                case 0: DrawBrowse(databases); break;
                case 1: DrawSearch(databases); break;
                case 2: DrawUsage(databases); break;
            }
        }

        private static List<IdDatabaseDescriptor> CollectDescriptors(NeoUISettings settings)
        {
            var list = new List<IdDatabaseDescriptor>();
            foreach (IdDatabaseDescriptor descriptor in settings.AllIdDatabases())
                list.Add(descriptor);
            return list;
        }

        private static Color AccentFor(IdDatabaseDescriptor descriptor)
        {
            switch (descriptor.accent)
            {
                case "Interactive": return NeoColors.Interactive;
                case "Containers": return NeoColors.Containers;
                case "Signals": return NeoColors.Signals;
                case "Flow": return NeoColors.Flow;
                case "Theming": return NeoColors.Theming;
                case "Animation": return NeoColors.Animation;
                default: return NeoColors.Data;
            }
        }

        // ============================================================== Browse & Edit ==============
        //
        // Three-pane master-detail: Databases (rail) | Categories (column) | Names (detail).
        // Pane 1 selects a database; pane 2 lists that database's categories (filterable, with name
        // counts + orphan badges) and selects one; pane 3 shows the selected category's names with a
        // database-scoped toolbar (rewrite-refs toggle, row filter, scan) and a reserved right-edge
        // ACTION GUTTER so the ✎/✕ icons never collide with the ref-count/orphan markers.

        private void DrawBrowse(List<IdDatabaseDescriptor> databases)
        {
            // lazily scan once on first browse so badges have data without a manual click
            if (_usage == null && !_usageStale) EnsureUsageScanned();

            if (databases.Count == 0)
            {
                EditorGUILayout.HelpBox("No id databases registered.", MessageType.Info);
                return;
            }

            _selectedDatabase = Mathf.Clamp(_selectedDatabase, 0, databases.Count - 1);
            IdDatabaseDescriptor descriptor = databases[_selectedDatabase];

            // restore / repair the remembered category for this database before drawing pane 3
            SyncSelectedCategory(descriptor);

            ClampPaneWidths();

            EditorGUILayout.BeginHorizontal();
            DrawDatabasesPane(databases);
            DrawSplitter(ref _databasesPaneWidth);
            DrawCategoriesPane(descriptor);
            DrawSplitter(ref _categoriesPaneWidth);
            DrawNamesPane(descriptor);
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- pane 1: databases rail

        private void DrawDatabasesPane(List<IdDatabaseDescriptor> databases)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(_databasesPaneWidth));
            GUILayout.Label("Databases", NeoStyles.SectionTitle);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            for (int i = 0; i < databases.Count; i++)
            {
                IdDatabaseDescriptor descriptor = databases[i];
                int count = CountEntries(descriptor.database);
                bool selected = i == _selectedDatabase;

                Rect row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint)
                {
                    if (selected) EditorGUI.DrawRect(row, NeoColors.RowSelected);
                    EditorGUI.DrawRect(new Rect(row.x, row.y, 3f, row.height), AccentFor(descriptor));
                }
                var labelRect = new Rect(row.x + 8f, row.y, row.width - 44f, row.height);
                var countRect = new Rect(row.xMax - 34f, row.y, 30f, row.height);
                GUI.Label(labelRect, descriptor.label, EditorStyles.label);
                GUI.Label(countRect, count.ToString(), NeoStyles.MiniDim);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none) && i != _selectedDatabase)
                {
                    _selectedDatabase = i;
                    CancelEdit();
                    _selName = null; // category is restored per-database by SyncSelectedCategory
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------- pane 2: categories column

        private void DrawCategoriesPane(IdDatabaseDescriptor descriptor)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_categoriesPaneWidth));
            IdDatabase database = descriptor.database;

            // header: title + count
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Categories", NeoStyles.SectionTitle, GUILayout.ExpandWidth(false));
            int cats = CountCategories(database);
            GUILayout.Label(cats.ToString(), NeoStyles.MiniDim, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (database == null)
            {
                EditorGUILayout.HelpBox("Database asset not assigned.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // filter field (replaces the old scoped search — substring over category names, and any
            // owned name, so a name match still surfaces its category)
            _browseFilter = NeoSearchField(_browseFilter, _categoriesPaneWidth - 4f);

            List<string> categories = VisibleCategories(descriptor);

            _categoryScroll = EditorGUILayout.BeginScrollView(_categoryScroll);
            if (cats == 0)
            {
                if (_editKind != "addCategory")
                    GUILayout.Label("No categories yet.", NeoStyles.MiniDim);
            }
            else if (categories.Count == 0)
            {
                GUILayout.Label("  No categories match the filter.", NeoStyles.MiniDim);
            }
            foreach (string category in categories)
                DrawCategoryRow(descriptor, category);

            if (_editKind == "addCategory")
                DrawAddCategoryRow(descriptor);
            EditorGUILayout.EndScrollView();

            // + Category affordance at the bottom of the column
            if (_editKind != "addCategory" &&
                GUILayout.Button("+ Category", EditorStyles.miniButton))
                BeginAddCategory();

            EditorGUILayout.EndVertical();
        }

        private void DrawCategoryRow(IdDatabaseDescriptor descriptor, string category)
        {
            IdDatabase database = descriptor.database;
            Rect row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            bool hover = row.Contains(Event.current.mousePosition);
            bool selected = _selCategory == category;
            int nameCount = CountNames(database, category);
            int catOrphans = CategoryOrphanCount(descriptor, category);

            if (Event.current.type == EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(row, NeoColors.RowSelected);
                else if (hover) EditorGUI.DrawRect(row, NeoColors.RowHover);
            }

            // bullet · label | count · ⚠orphans  (right-aligned meta block)
            var bulletRect = new Rect(row.x + 6f, row.y, 12f, row.height);
            var labelRect = new Rect(row.x + 18f, row.y, row.width - 18f - 54f, row.height);
            var countRect = new Rect(row.xMax - 54f, row.y, 24f, row.height);
            var warnRect = new Rect(row.xMax - 28f, row.y, 26f, row.height);

            if (_editKind == "renameCat" && _editCategory == category)
            {
                CommitInline(InlineField(new Rect(row.x + 18f, row.y + 1f, row.width - 22f, row.height - 2f)),
                    () => CommitCategoryRename(descriptor, category, _editBuffer));
                return;
            }

            GUI.Label(bulletRect, "•", _bulletDim);
            GUI.Label(labelRect, category, _categoryLabel);
            GUI.Label(countRect, nameCount.ToString(), _rightMeta);
            if (catOrphans > 0)
                GUI.Label(warnRect, new GUIContent($"⚠{catOrphans}", $"{catOrphans} orphan id(s) in this category"),
                    WarnStyle());

            HandleCategoryMouse(descriptor, row, category);
        }

        // ---------------------------------------------------------------- pane 3: names detail

        private void DrawNamesPane(IdDatabaseDescriptor descriptor)
        {
            EditorGUILayout.BeginVertical();
            IdDatabase database = descriptor.database;
            Color accent = AccentFor(descriptor);

            if (database == null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(descriptor.label, NeoStyles.HeaderTitle);
                GUILayout.FlexibleSpace();
                if (descriptor.idType != null) NeoGUI.Badge(descriptor.idType.Name, accent);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox(
                    $"The '{descriptor.label}' database asset is not assigned on NeoUISettings. " +
                    "Run Tools → Neo UI → Create or Repair Settings.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawNamesToolbar(descriptor, accent);

            // empty database → invite the first id in the names pane (categories pane shows + Category)
            if (CountCategories(database) == 0)
            {
                DrawEmptyState(descriptor, accent);
                DrawSelectionFooter(descriptor);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            if (_selCategory == null || !database.ContainsCategory(_selCategory))
            {
                GUILayout.Space(16f);
                GUILayout.Label("Select a category to see its names.", NeoStyles.CenteredBold);
            }
            else
            {
                bool anyName = false;
                foreach (string name in new List<string>(database.GetNames(_selCategory)))
                {
                    if (!PassesRowFilter(descriptor, _selCategory, name)) continue;
                    anyName = true;
                    DrawNameRow(descriptor, _selCategory, name);
                }
                if (!anyName)
                    GUILayout.Label(
                        _rowFilter == BrowseFilter.Dangling
                            ? "  Dangling ids aren't stored here — see the Usage / Orphans tab."
                            : _rowFilter == BrowseFilter.Orphans
                                ? "  No orphan names in this category."
                                : "  No names — + Add name", NeoStyles.MiniDim);

                if (_editKind == "addName" && _editCategory == _selCategory)
                    DrawAddNameRow(descriptor, _selCategory);
                else if (_rowFilter == BrowseFilter.All)
                {
                    // + Add name affordance (only meaningful under the All filter)
                    Rect add = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                    var addRect = new Rect(add.x + 12f, add.y, 120f, add.height);
                    if (GUI.Button(addRect, "+ Add name", EditorStyles.miniButton))
                        BeginAddName(_selCategory);
                }
            }
            EditorGUILayout.EndScrollView();

            DrawSelectionFooter(descriptor);
            EditorGUILayout.EndVertical();
        }

        /// <summary> Database-scoped toolbar shown atop the names pane: header · rewrite toggle · filter · scan. </summary>
        private void DrawNamesToolbar(IdDatabaseDescriptor descriptor, Color accent)
        {
            IdDatabase database = descriptor.database;
            int names = _selCategory != null && database.ContainsCategory(_selCategory)
                ? CountNames(database, _selCategory) : 0;
            int orphans = OrphanCount(descriptor);

            // line 1: "Database · Category — N names"  +  type badge
            EditorGUILayout.BeginHorizontal();
            string title = _selCategory != null && database.ContainsCategory(_selCategory)
                ? $"{descriptor.label} · {_selCategory} — {names} name{(names == 1 ? "" : "s")}"
                : descriptor.label;
            GUILayout.Label(title, NeoStyles.HeaderTitle, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            if (descriptor.idType != null)
            {
                if (orphans > 0) NeoGUI.Badge($"⚠{orphans} orphan{(orphans == 1 ? "" : "s")}", NeoColors.Warning);
                if (_usageStale) NeoGUI.Badge("counts stale — Scan", NeoColors.TextDim);
            }
            EditorGUILayout.EndHorizontal();

            // line 2: rewrite-refs toggle · filter dropdown · ⟳ Scan  (all database-scoped)
            EditorGUILayout.BeginHorizontal();
            _rewriteRefs = GUILayout.Toggle(_rewriteRefs, new GUIContent(" Rewrite refs on rename",
                "Also rewrite every matching reference of this id-type across UISpec files (and the " +
                "baseline) when you rename. Prefabs/scenes are not touched — a regenerate/sync " +
                "materializes the change."), EditorStyles.toggle, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(descriptor.idType == null))
            {
                GUILayout.Label("Filter:", NeoStyles.MiniDim, GUILayout.ExpandWidth(false));
                var rect = GUILayoutUtility.GetRect(80f, 18f, GUILayout.Width(80f));
                if (EditorGUI.DropdownButton(rect, new GUIContent(_rowFilter.ToString()), FocusType.Keyboard, EditorStyles.miniButton))
                    ShowRowFilterMenu();
                if (GUILayout.Button(_usage == null ? "⟳ Scan" : "⟳", EditorStyles.miniButton,
                        GUILayout.Width(_usage == null ? 54f : 26f)))
                    EnsureUsageScanned(force: true);
            }
            EditorGUILayout.EndHorizontal();

            NeoGUI.Splitter();
        }

        private void ShowRowFilterMenu()
        {
            var menu = new GenericMenu();
            foreach (BrowseFilter value in (BrowseFilter[])Enum.GetValues(typeof(BrowseFilter)))
            {
                BrowseFilter captured = value;
                menu.AddItem(new GUIContent(value.ToString()), _rowFilter == value, () => _rowFilter = captured);
            }
            menu.ShowAsContext();
        }

        private void DrawEmptyState(IdDatabaseDescriptor descriptor, Color accent)
        {
            GUILayout.Space(24f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Space(8f);
            var content = new GUIContent($"No categories yet in “{descriptor.label}”.");
            GUILayout.Label(content, NeoStyles.CenteredBold);
            GUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (_editKind == "addCategory")
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(260f));
                DrawAddCategoryRow(descriptor);
                EditorGUILayout.EndVertical();
            }
            else if (NeoGUI.AccentButton("+ Add the first id", accent, 24f))
            {
                BeginAddCategory();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8f);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Categories that pass the substring filter (matched against the category name OR any of its
        /// names) — drives pane 2's list. The All/Orphans/Dangling row filter applies per-NAME in pane 3,
        /// so the category column stays a stable navigation surface regardless of the row filter.
        /// </summary>
        private List<string> VisibleCategories(IdDatabaseDescriptor descriptor)
        {
            var result = new List<string>();
            string query = (_browseFilter ?? "").Trim();
            bool hasQuery = query.Length > 0;
            IdDatabase database = descriptor.database;

            foreach (string category in database.GetCategories())
            {
                if (!hasQuery || Contains(category, query)) { result.Add(category); continue; }
                // otherwise surface the category if any of its names matches the substring
                foreach (string name in database.GetNames(category))
                    if (Contains(name, query)) { result.Add(category); break; }
            }
            return result;
        }

        private bool PassesRowFilter(IdDatabaseDescriptor descriptor, string category, string name)
        {
            switch (_rowFilter)
            {
                case BrowseFilter.Orphans: return IsOrphan(descriptor, category, name);
                case BrowseFilter.Dangling: return false; // dangling ids aren't in the DB — see the Usage tab
                default: return true;
            }
        }

        /// <summary>
        /// One NAME row in pane 3. A reserved right-edge ACTION GUTTER (✎/✕, fade-in on hover but space
        /// always allocated) keeps the icons from reflowing the row or colliding with the ref-count /
        /// orphan marker, which sit in their own block to the LEFT of the gutter with a clear gap.
        /// </summary>
        private void DrawNameRow(IdDatabaseDescriptor descriptor, string category, string name)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            bool hover = row.Contains(Event.current.mousePosition);
            bool selected = _selCategory == category && _selName == name;
            bool orphan = IsOrphan(descriptor, category, name);
            int refs = RefCount(descriptor, category, name);

            if (Event.current.type == EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(row, NeoColors.RowSelected);
                else if (hover) EditorGUI.DrawRect(row, NeoColors.RowHover);
            }

            // ---- fixed action gutter pinned to the right edge (always reserved) ----
            const float Gutter = 46f;          // holds ✎ + ✕ with real spacing
            const float MetaWidth = 96f;        // ref-count + orphan marker block
            const float MetaGap = 8f;           // clear gap so meta never touches the gutter
            float gutterX = row.xMax - Gutter;
            float metaX = gutterX - MetaGap - MetaWidth;

            var labelRect = new Rect(row.x + 12f, row.y, metaX - row.x - 12f, row.height);

            if (_editKind == "renameName" && _editCategory == category && _editName == name)
            {
                CommitInline(InlineField(new Rect(row.x + 12f, row.y + 2f, metaX - row.x - 12f, row.height - 4f)),
                    () => CommitNameRename(descriptor, category, name, _editBuffer));
                return;
            }

            GUI.Label(labelRect, name, _nameLabel);

            // ref-count + orphan marker (LEFT of the gutter, right-aligned within their block)
            if (descriptor.idType != null)
            {
                var refRect = new Rect(metaX, row.y, MetaWidth - 18f, row.height);
                GUI.Label(refRect, _usage == null ? "—" : $"{refs} ref{(refs == 1 ? "" : "s")}", _rightMeta);
                if (orphan)
                {
                    var warnRect = new Rect(metaX + MetaWidth - 16f, row.y, 16f, row.height);
                    GUI.Label(warnRect, new GUIContent("⚠", "Orphan — in the database but referenced by no spec"), WarnStyle());
                }
            }

            // action gutter: ✎ then ✕, spaced apart with padding + larger hit targets, fade in on hover
            if (hover && !IsEditing())
            {
                var editRect = new Rect(gutterX + 4f, row.y + 3f, 18f, 18f);
                var delRect = new Rect(gutterX + 26f, row.y + 3f, 18f, 18f);
                if (IconButton(editRect, "✎", "Rename")) BeginRenameName(category, name);
                if (IconButton(delRect, "✕", "Delete", NeoColors.Remove)) DeleteName(descriptor, category, name);
            }

            HandleRowMouse(descriptor, row, category, name);
        }

        // ---------------------------------------------------------------- inline add rows

        private void DrawAddNameRow(IdDatabaseDescriptor descriptor, string category)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            var fieldRect = new Rect(row.x + 12f, row.y + 2f, row.width - 12f - 50f, row.height - 4f);
            CommitInline(InlineField(fieldRect), () =>
            {
                Undo.RecordObject(descriptor.database, "Add Id Name");
                if (descriptor.database.Add(category, _editBuffer))
                {
                    EditorUtility.SetDirty(descriptor.database);
                    MarkEdited();
                }
            }, keepEditing: true, category: category);
        }

        private void DrawAddCategoryRow(IdDatabaseDescriptor descriptor)
        {
            Rect row = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            var fieldRect = new Rect(row.x + 18f, row.y + 1f, row.width - 22f, row.height - 2f);
            CommitInline(InlineField(fieldRect), () =>
            {
                Undo.RecordObject(descriptor.database, "Add Id Category");
                string newCategory = (_editBuffer ?? "").Trim();
                // a fresh category needs at least one name to materialize — seed the default name
                if (descriptor.database.Add(newCategory, CategoryNameId.DefaultName))
                {
                    EditorUtility.SetDirty(descriptor.database);
                    MarkEdited();
                    SelectCategory(descriptor, newCategory); // jump pane 3 to the new category
                }
            });
        }

        // ---------------------------------------------------------------- row interaction

        /// <summary> Selection (left-click) + context menu (right-click) for a category row in pane 2. </summary>
        private void HandleCategoryMouse(IdDatabaseDescriptor descriptor, Rect row, string category)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || !row.Contains(e.mousePosition)) return;

            if (e.button == 1)
            {
                SelectCategory(descriptor, category);
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Rename Category"), false, () => BeginRenameCategory(category));
                menu.AddItem(new GUIContent("Add name"), false, () => BeginAddName(category));
                menu.AddItem(new GUIContent("Delete Category"), false, () => DeleteCategory(descriptor, category));
                menu.ShowAsContext();
                e.Use();
                return;
            }

            if (e.button == 0)
            {
                bool wasSelected = _selCategory == category;
                SelectCategory(descriptor, category);
                if (e.clickCount == 2 && wasSelected && !IsEditing())
                    BeginRenameCategory(category); // double-click renames, Hierarchy-style
                e.Use();
                Repaint();
            }
        }

        /// <summary> Selection (left-click) + context menu (right-click) for a NAME row in pane 3. </summary>
        private void HandleRowMouse(IdDatabaseDescriptor descriptor, Rect row, string category, string name)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || !row.Contains(e.mousePosition)) return;

            if (e.button == 1)
            {
                _selCategory = category;
                _selName = name;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Rename"), false, () => BeginRenameName(category, name));
                menu.AddItem(new GUIContent("Delete"), false, () => DeleteName(descriptor, category, name));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Add name to category"), false, () => BeginAddName(category));
                if (descriptor.idType != null)
                    menu.AddItem(new GUIContent("Find usages"), false, () => FindUsages(category, name));
                menu.ShowAsContext();
                e.Use();
                return;
            }

            if (e.button == 0)
            {
                bool wasSelected = _selCategory == category && _selName == name;
                _selCategory = category;
                _selName = name;
                // double-click a label begins rename (Hierarchy style)
                if (e.clickCount == 2 && wasSelected && !IsEditing())
                    BeginRenameName(category, name);
                e.Use();
                Repaint();
            }
        }

        // ---------------------------------------------------------------- pane plumbing

        /// <summary> Keeps the two resizable pane widths within sane bounds: each at least
        /// <see cref="MinPaneWidth"/>, and their sum leaving at least <see cref="MinNamesWidth"/> for the
        /// names pane — so dragging a splitter (or shrinking the window) can never collapse pane 3. </summary>
        private void ClampPaneWidths()
        {
            _databasesPaneWidth = Mathf.Max(MinPaneWidth, _databasesPaneWidth);
            _categoriesPaneWidth = Mathf.Max(MinPaneWidth, _categoriesPaneWidth);

            float budget = Mathf.Max(0f, position.width - MinNamesWidth - SplitterThickness * 2f);
            if (_databasesPaneWidth + _categoriesPaneWidth > budget)
            {
                // give the databases rail priority, then clamp categories to whatever's left (>= min)
                _databasesPaneWidth = Mathf.Min(_databasesPaneWidth, Mathf.Max(MinPaneWidth, budget - MinPaneWidth));
                _categoriesPaneWidth = Mathf.Clamp(budget - _databasesPaneWidth, MinPaneWidth, _categoriesPaneWidth);
            }
        }

        private const float SplitterThickness = 6f;

        /// <summary> A draggable vertical splitter between panes: draws a thin rule, shows the
        /// resize cursor, and adjusts <paramref name="leftPaneWidth"/> (the pane to its left) as the
        /// user drags. <see cref="ClampPaneWidths"/> bounds the result the next frame. </summary>
        private void DrawSplitter(ref float leftPaneWidth)
        {
            Rect rect = GUILayoutUtility.GetRect(SplitterThickness, SplitterThickness,
                GUILayout.Width(SplitterThickness), GUILayout.ExpandHeight(true));

            if (Event.current.type == EventType.Repaint)
            {
                var line = new Rect(rect.x + SplitterThickness * 0.5f - 0.5f, rect.y, 1f, rect.height);
                EditorGUI.DrawRect(line, NeoColors.Separator);
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) { leftPaneWidth += e.delta.x; e.Use(); Repaint(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
            }
        }

        /// <summary> Select a category in pane 2 and remember it for this database. </summary>
        private void SelectCategory(IdDatabaseDescriptor descriptor, string category)
        {
            _selCategory = category;
            _selName = null;
            _categoryByDatabase[descriptor.label] = category;
        }

        /// <summary>
        /// Restore the remembered category for this database (or pick the first when none / it's gone),
        /// so switching databases away and back keeps pane-3 context.
        /// </summary>
        private void SyncSelectedCategory(IdDatabaseDescriptor descriptor)
        {
            IdDatabase database = descriptor.database;
            if (database == null) { _selCategory = null; return; }

            _categoryByDatabase.TryGetValue(descriptor.label, out string remembered);

            // if the live selection already names an existing category, keep + remember it
            if (_selCategory != null && database.ContainsCategory(_selCategory))
            {
                _categoryByDatabase[descriptor.label] = _selCategory;
                return;
            }

            if (remembered != null && database.ContainsCategory(remembered))
            {
                _selCategory = remembered;
                return;
            }

            // fall back to the first category (or nothing on an empty database)
            string first = null;
            foreach (string c in database.GetCategories()) { first = c; break; }
            _selCategory = first;
            _selName = null;
            if (first != null) _categoryByDatabase[descriptor.label] = first;
            else _categoryByDatabase.Remove(descriptor.label);
        }

        // ---------------------------------------------------------------- selection footer

        private void DrawSelectionFooter(IdDatabaseDescriptor descriptor)
        {
            NeoGUI.Splitter();
            Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            if (_selCategory == null)
            {
                GUI.Label(row, "  Select an id to see what references it.", NeoStyles.MiniDim);
                return;
            }

            string summary;
            bool canFind = descriptor.idType != null;
            if (_selName == null)
            {
                summary = $"  {descriptor.label} “{_selCategory}” — {CountNames(descriptor.database, _selCategory)} name(s)";
                canFind = false;
            }
            else if (descriptor.idType == null)
            {
                summary = $"  {descriptor.label} “{_selCategory}/{_selName}”";
            }
            else if (_usage == null)
            {
                summary = $"  {descriptor.label} “{_selCategory}/{_selName}” — Scan to compute references";
            }
            else
            {
                int refs = RefCount(descriptor, _selCategory, _selName);
                summary = refs > 0
                    ? $"  {descriptor.label} “{_selCategory}/{_selName}” — referenced in {refs} place(s)"
                    : $"  {descriptor.label} “{_selCategory}/{_selName}” — refs: none · orphan";
            }

            var labelRect = new Rect(row.x, row.y, row.width - 60f, row.height);
            var findRect = new Rect(row.xMax - 56f, row.y + 1f, 52f, 18f);
            GUI.Label(labelRect, summary, NeoStyles.MiniDim);
            using (new EditorGUI.DisabledScope(!canFind))
                if (GUI.Button(findRect, "Find", EditorStyles.miniButton))
                    FindUsages(_selCategory, _selName);
        }

        /// <summary> Switch to the Usage/Orphans tab, scanning if needed, to surface this id's references. </summary>
        private void FindUsages(string category, string name)
        {
            EnsureUsageScanned();
            _orphansOnly = false;
            SessionState.SetInt(TabKey, 2);
            GUIUtility.ExitGUI();
        }

        // ---------------------------------------------------------------- inline-edit plumbing

        private bool IsEditing() => _editKind != null;

        private void CancelEdit()
        {
            _editKind = null;
            _editCategory = _editName = null;
            _editBuffer = "";
        }

        private void BeginRenameCategory(string category)
        {
            CancelEdit();
            _editKind = "renameCat";
            _editCategory = category;
            _editBuffer = category;
            NeoInlineEdit.RequestFocus();
        }

        private void BeginRenameName(string category, string name)
        {
            CancelEdit();
            _editKind = "renameName";
            _editCategory = category;
            _editName = name;
            _editBuffer = name;
            NeoInlineEdit.RequestFocus();
        }

        private void BeginAddName(string category)
        {
            CancelEdit();
            _editKind = "addName";
            _editCategory = category;
            _editBuffer = "";
            _selCategory = category; // make sure pane 3 is showing the category we're adding into
            NeoInlineEdit.RequestFocus();
        }

        private void BeginAddCategory()
        {
            CancelEdit();
            _editKind = "addCategory";
            _editBuffer = "";
            NeoInlineEdit.RequestFocus();
        }

        private NeoInlineEdit.Result InlineField(Rect rect) => NeoInlineEdit.Field(rect, ref _editBuffer);

        /// <summary>
        /// Runs the commit/cancel handling for an inline field. On commit runs <paramref name="commit"/>;
        /// unless <paramref name="keepEditing"/> (the add-name flow keeps the field open for rapid entry),
        /// the edit ends. Re-focuses for the next add and exits GUI to avoid drawing stale rows.
        /// </summary>
        private void CommitInline(NeoInlineEdit.Result result, Action commit, bool keepEditing = false, string category = null)
        {
            if (result.Committed)
            {
                bool hadText = !string.IsNullOrWhiteSpace(_editBuffer);
                if (hadText) commit();
                if (keepEditing && hadText)
                {
                    _editBuffer = "";
                    NeoInlineEdit.RequestFocus();
                    GUIUtility.ExitGUI();
                    return;
                }
                CancelEdit();
                GUIUtility.ExitGUI();
            }
            else if (result.Cancelled)
            {
                CancelEdit();
                GUIUtility.ExitGUI();
            }
        }

        // ---------------------------------------------------------------- delete

        private void DeleteName(IdDatabaseDescriptor descriptor, string category, string name)
        {
            Undo.RecordObject(descriptor.database, "Delete Id Name");
            descriptor.database.Remove(category, name); // removes the category too if it was the last name
            EditorUtility.SetDirty(descriptor.database);
            if (_selCategory == category && _selName == name) _selName = null;
            // if that emptied (and thus removed) the category, drop the remembered selection so it repairs
            if (!descriptor.database.ContainsCategory(category))
            {
                _categoryByDatabase.Remove(descriptor.label);
                if (_selCategory == category) { _selCategory = null; }
            }
            MarkEdited();
            GUIUtility.ExitGUI();
        }

        private void DeleteCategory(IdDatabaseDescriptor descriptor, string category)
        {
            int names = CountNames(descriptor.database, category);
            if (names > 0 && !EditorUtility.DisplayDialog("Delete category",
                    $"Delete category “{category}” and its {names} name(s) from {descriptor.label}?",
                    "Delete", "Cancel"))
                return;

            Undo.RecordObject(descriptor.database, "Delete Id Category");
            descriptor.database.RemoveCategory(category);
            EditorUtility.SetDirty(descriptor.database);
            if (_selCategory == category) { _selCategory = null; _selName = null; }
            _categoryByDatabase.Remove(descriptor.label); // SyncSelectedCategory re-picks the first
            MarkEdited();
            GUIUtility.ExitGUI();
        }

        // ---------------------------------------------------------------- icon buttons / styles

        /// <summary> A hover-reveal icon button drawn into an explicit rect (no per-OnGUI style alloc). </summary>
        private bool IconButton(Rect rect, string glyph, string tooltip, Color? tint = null)
        {
            if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, NeoColors.RowHover);
            Color prev = GUI.contentColor;
            if (tint.HasValue) GUI.contentColor = tint.Value;
            bool pressed = GUI.Button(rect, new GUIContent(glyph, tooltip), _iconButton);
            GUI.contentColor = prev;
            return pressed;
        }

        private GUIStyle _warnStyle;
        private GUIStyle WarnStyle()
        {
            if (_warnStyle == null)
                _warnStyle = new GUIStyle(NeoStyles.MiniDim)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal = { textColor = NeoColors.Warning }
                };
            return _warnStyle;
        }

        private static string NeoSearchField(string value, float width)
        {
            // Unity's built-in toolbar search field — cheap, no per-frame style alloc on our side.
            return EditorGUILayout.TextField(value ?? "", EditorStyles.toolbarSearchField, GUILayout.Width(width));
        }

        // ============================================================== Rename (DB + references) ===

        /// <summary>
        /// Commits a NAME rename: optionally rewrites matching references across UISpecs first (with a
        /// confirm/preview), then applies the database-level rename. Both halves run together so the DB
        /// and the specs stay consistent.
        /// </summary>
        private void CommitNameRename(IdDatabaseDescriptor descriptor, string category, string oldName, string newName)
        {
            newName = (newName ?? "").Trim();
            if (string.IsNullOrEmpty(newName) || newName == oldName) return;

            var rename = IdReferenceRewriter.Rename.ForName(descriptor.idType, category, oldName, newName);
            string headline = $"Rename {descriptor.label} '{category}/{oldName}' → '{category}/{newName}'";
            if (!ConfirmRename(descriptor, rename, headline)) return;

            RewriteThenRenameDb(descriptor, rename, () =>
            {
                Undo.RecordObject(descriptor.database, "Rename Id Name");
                if (!descriptor.database.RenameName(category, oldName, newName))
                    Debug.LogWarning($"Could not rename '{category}/{oldName}' → '{newName}' (blank or already exists).");
                EditorUtility.SetDirty(descriptor.database);
            });
            if (_selCategory == category && _selName == oldName) _selName = newName;
        }

        /// <summary>
        /// Commits a CATEGORY rename: optionally rewrites every <c>OldCat/*</c> reference of this id-type
        /// across UISpecs first (with a confirm/preview), then applies the database-level rename.
        /// </summary>
        private void CommitCategoryRename(IdDatabaseDescriptor descriptor, string oldCategory, string newCategory)
        {
            newCategory = (newCategory ?? "").Trim();
            if (string.IsNullOrEmpty(newCategory) || newCategory == oldCategory) return;

            var rename = IdReferenceRewriter.Rename.ForCategory(descriptor.idType, oldCategory, newCategory);
            string headline = $"Rename {descriptor.label} category '{oldCategory}' → '{newCategory}'";
            if (!ConfirmRename(descriptor, rename, headline)) return;

            RewriteThenRenameDb(descriptor, rename, () =>
            {
                Undo.RecordObject(descriptor.database, "Rename Id Category");
                if (!descriptor.database.RenameCategory(oldCategory, newCategory))
                    Debug.LogWarning($"Could not rename category '{oldCategory}' → '{newCategory}' (blank or collides with an existing category).");
                EditorUtility.SetDirty(descriptor.database);
            });
            // keep the selection (live + remembered) pointing at the renamed category
            if (_selCategory == oldCategory) _selCategory = newCategory;
            if (_categoryByDatabase.TryGetValue(descriptor.label, out string remembered) && remembered == oldCategory)
                _categoryByDatabase[descriptor.label] = newCategory;
        }

        /// <summary>
        /// Shows the confirm/preview dialog (only when reference-rewrite is enabled and the database has a
        /// referenceable id-type). Returns true when the rename should proceed. When rewrite is off — or
        /// there's nothing to scan — it returns true silently (DB-only rename, the user's explicit choice).
        /// </summary>
        private bool ConfirmRename(IdDatabaseDescriptor descriptor, IdReferenceRewriter.Rename rename, string headline)
        {
            if (!_rewriteRefs || descriptor.idType == null) return true;

            IdReferenceRewriter.Result preview = IdReferenceRewriter.Preview(rename);
            if (preview.TotalReferences == 0)
                return true; // nothing references it — just rename the DB entry, no dialog needed

            var files = new System.Text.StringBuilder();
            foreach (IdReferenceRewriter.FileResult file in preview.changedFiles)
                files.Append("\n• ").Append(file.path).Append("  (").Append(file.references).Append(')');

            string message =
                $"{headline}\n\nThis will also update {preview.TotalReferences} reference(s) in " +
                $"{preview.FilesChanged} file(s):{files}\n\n" +
                "Prefabs/scenes are NOT rewritten — a regenerate/sync materializes the change. " +
                "Hand-authored specs are normalized to canonical form; generated specs stay byte-stable.";

            return EditorUtility.DisplayDialog("Rename id + update references", message, "Rename & Update", "Cancel");
        }

        /// <summary> Rewrites references first (if enabled), then applies the DB-level rename via <paramref name="renameDb"/>. </summary>
        private void RewriteThenRenameDb(IdDatabaseDescriptor descriptor, IdReferenceRewriter.Rename rename, Action renameDb)
        {
            if (_rewriteRefs && descriptor.idType != null)
            {
                IdReferenceRewriter.Result applied = IdReferenceRewriter.Apply(rename);
                foreach (string error in applied.parseErrors)
                    Debug.LogWarning($"[ID Rename] {error}");
                if (applied.FilesChanged > 0)
                    Debug.Log($"[ID Rename] Updated {applied.TotalReferences} reference(s) across {applied.FilesChanged} UISpec file(s). " +
                              "Prefabs/scenes are not rewritten — run Sync/regenerate to materialize the change.");
            }
            renameDb();
            MarkEdited(); // the rename changed reference data — counts are now stale until a rescan
        }

        // ============================================================== usage cache ================

        /// <summary> Scans the project (once, or when forced) and caches the result; clears the stale flag. </summary>
        private void EnsureUsageScanned(bool force = false)
        {
            if (_usage != null && !force && !_usageStale) return;
            _usage = IdUsageScanner.ScanProject();
            _usageStale = false;
        }

        /// <summary>
        /// Records that an edit happened: the cached usage counts no longer reflect the database, so the
        /// badges show a "stale — Scan" hint and the next browse open will rescan. We keep the old cache
        /// (rather than null it) so badges still show approximate counts until the user rescans.
        /// </summary>
        private void MarkEdited()
        {
            if (_usage != null) _usageStale = true;
        }

        // ============================================================== Search ======================

        private void DrawSearch(List<IdDatabaseDescriptor> databases)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Search all databases", NeoStyles.SectionTitle, GUILayout.Width(150f));
            _search = EditorGUILayout.TextField(_search);
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50f))) _search = "";
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrWhiteSpace(_search))
            {
                EditorGUILayout.HelpBox("Type to search Category and Name across every database.", MessageType.Info);
                return;
            }

            string query = _search.Trim();
            int hits = 0;
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            foreach (IdDatabaseDescriptor descriptor in databases)
            {
                if (descriptor.database == null) continue;
                Color accent = AccentFor(descriptor);
                var matches = new List<string>();
                foreach (string category in descriptor.database.GetCategories())
                {
                    bool categoryMatches = Contains(category, query);
                    foreach (string name in descriptor.database.GetNames(category))
                        if (categoryMatches || Contains(name, query))
                            matches.Add($"{category}/{name}");
                }
                if (matches.Count == 0) continue;

                hits += matches.Count;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(descriptor.label, NeoStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                NeoGUI.Badge(matches.Count.ToString(), accent);
                EditorGUILayout.EndHorizontal();
                foreach (string match in matches)
                    EditorGUILayout.LabelField("• " + match, EditorStyles.label);
                EditorGUILayout.EndVertical();
            }
            if (hits == 0)
                EditorGUILayout.HelpBox($"No matches for \"{query}\".", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        // ============================================================== Usage / Orphans ============

        private void DrawUsage(List<IdDatabaseDescriptor> databases)
        {
            EditorGUILayout.BeginHorizontal();
            if (NeoGUI.AccentButton(_usage == null ? "Scan Project" : "Re-scan Project", NeoColors.Data, 22f))
                EnsureUsageScanned(force: true);
            GUILayout.FlexibleSpace();
            _orphansOnly = GUILayout.Toggle(_orphansOnly, "Orphans only", EditorStyles.miniButton, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();

            if (_usage == null)
            {
                EditorGUILayout.HelpBox(
                    "Scan parses every *.json UISpec under Assets/ and cross-references the ids it " +
                    "references against each database.\n\n" +
                    "ORPHAN = in the database but used by no spec.   DANGLING = used by a spec but " +
                    "missing from the database.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                $"Scanned {_usage.filesScanned} spec file(s)." +
                (_usage.parseErrors.Count > 0 ? $"  {_usage.parseErrors.Count} file(s) skipped (see console)." : ""),
                NeoStyles.MiniDim);
            if (_usage.parseErrors.Count > 0 && GUILayout.Button("Log skipped files", EditorStyles.miniButton, GUILayout.Width(130f)))
                foreach (string error in _usage.parseErrors) Debug.LogWarning($"[ID Usage Scan] skipped {error}");

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            foreach (IdDatabaseDescriptor descriptor in databases)
            {
                if (descriptor.idType == null) continue; // label-only databases have no referenceable type
                Color accent = AccentFor(descriptor);
                IdUsageScanner.DatabaseReport report = IdUsageScanner.Reconcile(descriptor.database, descriptor.idType, _usage);

                bool clean = report.orphans.Count == 0 && report.dangling.Count == 0;
                if (_orphansOnly && report.orphans.Count == 0) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(descriptor.label, NeoStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                if (report.orphans.Count > 0) NeoGUI.Badge($"{report.orphans.Count} orphan", NeoColors.Warning);
                if (report.dangling.Count > 0) NeoGUI.Badge($"{report.dangling.Count} dangling", NeoColors.Remove);
                if (clean) NeoGUI.Badge("clean", NeoColors.Add);
                EditorGUILayout.EndHorizontal();

                foreach (IdUsageScanner.Ref reference in report.orphans)
                    DrawUsageRow(descriptor.database, reference, "orphan", NeoColors.Warning);
                if (!_orphansOnly)
                    foreach (IdUsageScanner.Ref reference in report.dangling)
                        DrawUsageRow(descriptor.database, reference, "dangling", NeoColors.Remove);

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawUsageRow(IdDatabase database, IdUsageScanner.Ref reference, string tag, Color color)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("• " + reference, EditorStyles.label);
            GUILayout.FlexibleSpace();
            NeoGUI.Badge(tag, color);
            if (tag == "orphan" && database != null)
            {
                if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(54f)))
                {
                    Undo.RecordObject(database, "Delete Orphan Id");
                    database.Remove(reference.category, reference.name);
                    EditorUtility.SetDirty(database);
                    EnsureUsageScanned(force: true); // outcome changed — refresh the orphan list in place
                    GUIUtility.ExitGUI();
                }
            }
            else if (tag == "dangling" && database != null)
            {
                if (GUILayout.Button("Add to DB", EditorStyles.miniButton, GUILayout.Width(74f)))
                {
                    Undo.RecordObject(database, "Add Dangling Id");
                    database.Add(reference.category, reference.name);
                    EditorUtility.SetDirty(database);
                    EnsureUsageScanned(force: true);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static int CountEntries(IdDatabase database)
        {
            if (database == null) return 0;
            int total = 0;
            foreach (IdDatabase.IdCategory category in database.Categories)
                total += category?.names?.Count ?? 0;
            return total;
        }

        private static int CountCategories(IdDatabase database)
        {
            if (database == null) return 0;
            int total = 0;
            foreach (IdDatabase.IdCategory _ in database.Categories) total++;
            return total;
        }

        private static int CountNames(IdDatabase database, string category)
        {
            if (database == null) return 0;
            int total = 0;
            foreach (string _ in database.GetNames(category)) total++;
            return total;
        }

        // ---------------------------------------------------------------- usage badge lookups (cached)

        // memo of the reference set for the resolved id-type, valid only while it points at the current
        // _usage instance (cleared lazily when _usage changes). Avoids re-allocating usage.For()'s empty
        // set every row and re-resolving the set per OnGUI.
        private object _refSetUsageToken;
        private System.Collections.Generic.HashSet<IdUsageScanner.Ref> _refSet;
        private Type _refSetType;

        private System.Collections.Generic.HashSet<IdUsageScanner.Ref> ReferencedSet(IdDatabaseDescriptor descriptor)
        {
            if (_usage == null || descriptor.idType == null) return null;
            if (!ReferenceEquals(_refSetUsageToken, _usage) || _refSetType != descriptor.idType)
            {
                _refSetUsageToken = _usage;
                _refSetType = descriptor.idType;
                _refSet = _usage.For(descriptor.idType);
            }
            return _refSet;
        }

        private bool IsOrphan(IdDatabaseDescriptor descriptor, string category, string name)
        {
            var set = ReferencedSet(descriptor);
            if (set == null) return false; // no scan yet — don't claim orphan
            return !set.Contains(new IdUsageScanner.Ref(category, name));
        }

        private int RefCount(IdDatabaseDescriptor descriptor, string category, string name)
        {
            if (_usage == null || descriptor.idType == null) return 0;
            return _usage.CountFor(descriptor.idType, new IdUsageScanner.Ref(category, name));
        }

        private int CategoryOrphanCount(IdDatabaseDescriptor descriptor, string category)
        {
            if (ReferencedSet(descriptor) == null) return 0;
            int count = 0;
            foreach (string name in descriptor.database.GetNames(category))
                if (IsOrphan(descriptor, category, name)) count++;
            return count;
        }

        private int OrphanCount(IdDatabaseDescriptor descriptor)
        {
            if (ReferencedSet(descriptor) == null) return 0;
            int count = 0;
            foreach (string category in descriptor.database.GetCategories())
                count += CategoryOrphanCount(descriptor, category);
            return count;
        }
    }
}
