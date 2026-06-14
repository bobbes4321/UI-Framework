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

        // selection / browse state
        private int _selectedDatabase;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        // inline-add buffers (type-and-add, no dialogs)
        private string _newCategoryBuffer = "";
        private readonly Dictionary<string, string> _newNameBuffers = new Dictionary<string, string>();

        // inline-rename state (one rename in flight at a time)
        private string _renameKey;     // "cat" / "cat::name" being renamed
        private string _renameBuffer = "";

        // search
        private string _search = "";

        // rename behavior: when true, a rename ALSO rewrites matching references across UISpecs
        private bool _rewriteRefs = true;

        // usage scan (explicit, cached until re-scanned)
        private IdUsageScanner.Usage _usage;
        private bool _orphansOnly;

        private void OnGUI()
        {
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

        private void DrawBrowse(List<IdDatabaseDescriptor> databases)
        {
            EditorGUILayout.BeginHorizontal();

            // -- left: database list ------------------------------------------------------------
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(190f));
            GUILayout.Label("Databases", NeoStyles.SectionTitle);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            _selectedDatabase = Mathf.Clamp(_selectedDatabase, 0, Mathf.Max(0, databases.Count - 1));
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
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { _selectedDatabase = i; CancelRename(); }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // -- right: selected database detail ------------------------------------------------
            EditorGUILayout.BeginVertical();
            if (databases.Count == 0)
                EditorGUILayout.HelpBox("No id databases registered.", MessageType.Info);
            else
                DrawDatabaseDetail(databases[_selectedDatabase]);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawDatabaseDetail(IdDatabaseDescriptor descriptor)
        {
            Color accent = AccentFor(descriptor);
            IdDatabase database = descriptor.database;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(descriptor.label, NeoStyles.HeaderTitle);
            GUILayout.FlexibleSpace();
            if (descriptor.idType != null) NeoGUI.Badge(descriptor.idType.Name, accent);
            EditorGUILayout.EndHorizontal();

            if (database == null)
            {
                EditorGUILayout.HelpBox(
                    $"The '{descriptor.label}' database asset is not assigned on NeoUISettings. " +
                    "Run Tools → Neo UI → Create or Repair Settings.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            _rewriteRefs = GUILayout.Toggle(_rewriteRefs, " Rewrite references in UISpecs on rename",
                EditorStyles.toggle);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                _rewriteRefs
                    ? "Renaming also rewrites every matching reference of this id-type across UISpec " +
                      "files (and the baseline). Prefabs/scenes are NOT touched — a regenerate/sync " +
                      "materializes the change."
                    : "Renaming edits the database ONLY — it does NOT rewrite ids already referenced by " +
                      "specs/prefabs. Re-point those with a regenerate.", NeoStyles.MiniDim);

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            // categories + names
            foreach (string category in new List<string>(database.GetCategories()))
                DrawCategory(descriptor, category, accent);

            NeoGUI.Splitter();

            // add a new category (inline type-and-add)
            EditorGUILayout.BeginHorizontal();
            _newCategoryBuffer = EditorGUILayout.TextField("New Category", _newCategoryBuffer);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newCategoryBuffer)))
                if (GUILayout.Button("+ Add", GUILayout.Width(60f)))
                {
                    Undo.RecordObject(database, "Add Id Category");
                    database.Add(_newCategoryBuffer, CategoryNameId.DefaultName);
                    EditorUtility.SetDirty(database);
                    _newCategoryBuffer = "";
                    GUI.FocusControl(null);
                }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        private void DrawCategory(IdDatabaseDescriptor descriptor, string category, Color accent)
        {
            IdDatabase database = descriptor.database;
            string categoryKey = category;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            if (IsRenaming(categoryKey))
            {
                DrawRenameField(() => CommitCategoryRename(descriptor, category, _renameBuffer));
            }
            else
            {
                GUILayout.Label(category, NeoStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Rename", EditorStyles.miniButton, GUILayout.Width(58f)))
                    BeginRename(categoryKey, category);
                if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(54f)))
                {
                    Undo.RecordObject(database, "Delete Id Category");
                    database.RemoveCategory(category);
                    EditorUtility.SetDirty(database);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            // names under the category
            EditorGUI.indentLevel++;
            foreach (string name in new List<string>(database.GetNames(category)))
            {
                string nameKey = categoryKey + "::" + name;
                EditorGUILayout.BeginHorizontal();
                if (IsRenaming(nameKey))
                {
                    DrawRenameField(() => CommitNameRename(descriptor, category, name, _renameBuffer));
                }
                else
                {
                    GUILayout.Label("• " + name, EditorStyles.label);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Rename", EditorStyles.miniButton, GUILayout.Width(58f)))
                        BeginRename(nameKey, name);
                    if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(54f)))
                    {
                        Undo.RecordObject(database, "Delete Id Name");
                        database.Remove(category, name);
                        EditorUtility.SetDirty(database);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            // inline add-name row
            if (!_newNameBuffers.TryGetValue(categoryKey, out string buffer)) buffer = "";
            EditorGUILayout.BeginHorizontal();
            buffer = EditorGUILayout.TextField("New Name", buffer);
            _newNameBuffers[categoryKey] = buffer;
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(buffer)))
                if (GUILayout.Button("+ Add", GUILayout.Width(60f)))
                {
                    Undo.RecordObject(database, "Add Id Name");
                    database.Add(category, buffer);
                    EditorUtility.SetDirty(database);
                    _newNameBuffers[categoryKey] = "";
                    GUI.FocusControl(null);
                }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
        }

        private bool IsRenaming(string key) => _renameKey == key;

        private void BeginRename(string key, string current)
        {
            _renameKey = key;
            _renameBuffer = current;
            GUI.FocusControl(null);
        }

        private void CancelRename()
        {
            _renameKey = null;
            _renameBuffer = "";
        }

        private void DrawRenameField(Action commit)
        {
            GUI.SetNextControlName("NeoIdRename");
            _renameBuffer = EditorGUILayout.TextField(_renameBuffer);
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return;
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_renameBuffer)))
                if (GUILayout.Button("Save", EditorStyles.miniButton, GUILayout.Width(48f)) || enter)
                {
                    commit();
                    if (enter) Event.current.Use();
                    GUIUtility.ExitGUI();
                }
            if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(54f)))
            {
                CancelRename();
                GUIUtility.ExitGUI();
            }
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
            if (string.IsNullOrEmpty(newName)) { CancelRename(); return; }

            var rename = IdReferenceRewriter.Rename.ForName(descriptor.idType, category, oldName, newName);
            string headline = $"Rename {descriptor.label} '{category}/{oldName}' → '{category}/{newName}'";
            if (!ConfirmRename(descriptor, rename, headline)) { CancelRename(); return; }

            RewriteThenRenameDb(descriptor, rename, () =>
            {
                Undo.RecordObject(descriptor.database, "Rename Id Name");
                if (!descriptor.database.RenameName(category, oldName, newName))
                    Debug.LogWarning($"Could not rename '{category}/{oldName}' → '{newName}' (blank or already exists).");
                EditorUtility.SetDirty(descriptor.database);
            });
            CancelRename();
        }

        /// <summary>
        /// Commits a CATEGORY rename: optionally rewrites every <c>OldCat/*</c> reference of this id-type
        /// across UISpecs first (with a confirm/preview), then applies the database-level rename.
        /// </summary>
        private void CommitCategoryRename(IdDatabaseDescriptor descriptor, string oldCategory, string newCategory)
        {
            newCategory = (newCategory ?? "").Trim();
            if (string.IsNullOrEmpty(newCategory)) { CancelRename(); return; }

            var rename = IdReferenceRewriter.Rename.ForCategory(descriptor.idType, oldCategory, newCategory);
            string headline = $"Rename {descriptor.label} category '{oldCategory}' → '{newCategory}'";
            if (!ConfirmRename(descriptor, rename, headline)) { CancelRename(); return; }

            RewriteThenRenameDb(descriptor, rename, () =>
            {
                Undo.RecordObject(descriptor.database, "Rename Id Category");
                if (!descriptor.database.RenameCategory(oldCategory, newCategory))
                    Debug.LogWarning($"Could not rename category '{oldCategory}' → '{newCategory}' (blank or collides with an existing category).");
                EditorUtility.SetDirty(descriptor.database);
            });
            CancelRename();
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
                              "Composer (if open) — reload it to see the change.");
            }
            renameDb();
            _usage = null; // outcome changed — force a fresh usage scan next time the tab opens
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
                _usage = IdUsageScanner.ScanProject();
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
                    _usage = null; // outcome changed — force a fresh scan
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
                    _usage = null;
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
    }
}
