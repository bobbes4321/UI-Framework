using System;
using System.Collections.Generic;
using System.Linq;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Design System window "Motion" tab. Two sections:
    /// <list type="bullet">
    /// <item><b>Default motion per animator role</b> (top, unchanged) — the
    /// <see cref="NeoUISettings.animatorDefaults"/> the Setup wizard seeds and animator <c>Reset()</c> /
    /// the widget factory consume, so editing here flows into every newly-added animator and freshly-
    /// generated button/view.</item>
    /// <item><b>Preset library</b> (Phase 2.4) — the full discovered <see cref="UIAnimationPreset"/> set as
    /// a grouped, searchable browser (the shared <see cref="AnimationPresetBrowserModel"/>, same grouping
    /// as <see cref="AnimationPresetBrowserPopup"/>), a <b>New Preset</b> row that creates an asset via the
    /// same folder convention <see cref="AnimationLibraryBootstrap"/> uses, and an embedded five-channel
    /// editor for the selected preset (the shared <see cref="AnimationPresetGUI"/> drawer, same form as the
    /// <see cref="UIAnimationPresetEditor"/> inspector) with a live Preview on the scene selection.</item>
    /// </list>
    /// Stateful: the tab's <see cref="State"/> caches the browser model, the selected preset's
    /// <see cref="SerializedObject"/> (recreated only on selection change) and a preview lifecycle that
    /// restores the previewed object on Stop / selection change / tab switch / window close.
    /// </summary>
    internal static class MotionTab
    {
        // How long the Motion tab may go undrawn (tab switched away / window hidden) before an in-flight
        // preview is treated as orphaned and the object restored. The Tick repaints while the tab IS active,
        // so lastDraw stays fresh; when Draw stops running, this fires.
        private const double PreviewIdleRestoreSeconds = 0.4;

        /// <summary> Per-window UI state for the Motion tab (disposed by the window; see <see cref="Dispose"/>). </summary>
        internal sealed class State : IDisposable
        {
            // --- library browser ---
            public AnimationPresetBrowserModel model;
            public int builtForCount = -1;      // discovered-count the model was built for (rebuild on change)
            public string filter = "";

            // --- selection + its cached SerializedObject (recreated only when the selection changes) ---
            public UIAnimationPreset selected;
            public SerializedObject selectedSO;

            // --- new-preset row ---
            public string newCategory = "";
            public string newName = "";

            // --- preview lifecycle (mirrors UIAnimationPresetEditor) ---
            // A throwaway animation the preset is copied into, so previewing never dirties the asset.
            private readonly UIAnimation _preview = new UIAnimation();
            private RectTransform _previewTarget;   // non-null == a preview is live and owes a restore
            private EditorWindow _window;           // for repaint-driven idle detection while previewing
            private double _lastDraw;
            private bool _subscribed;

            public bool Previewing => _previewTarget != null;
            public RectTransform PreviewTarget => _previewTarget;

            /// <summary> Called at the top of every Draw: keeps the window handle current and marks the tab alive. </summary>
            public void MarkDrawn(EditorWindow window)
            {
                _window = window;
                _lastDraw = EditorApplication.timeSinceStartup;
            }

            /// <summary> Repaints the hosting window (so a click/toggle shows immediately). </summary>
            public void RepaintHost()
            {
                if (_window != null) _window.Repaint();
            }

            public void EnsureSerialized()
            {
                if (selected == null) { selectedSO = null; return; }
                if (selectedSO == null || selectedSO.targetObject != selected)
                    selectedSO = new SerializedObject(selected);
            }

            public void Select(UIAnimationPreset preset)
            {
                if (selected == preset) return;
                RestorePreview();          // switching selection ends any live preview cleanly
                selected = preset;
                selectedSO = null;         // rebuilt lazily by EnsureSerialized
            }

            public void StartPreview(UIAnimationPreset preset, RectTransform target)
            {
                if (preset == null || target == null) return;
                RestorePreview();                       // put any prior preview back to rest first
                AnimationPreview.BeginPreview(target);  // snapshot pos/rot/scale/alpha for a clean revert
                preset.CopyTo(_preview);
                CanvasGroup group = target.GetComponent<CanvasGroup>();
                if (group == null && _preview.fade.enabled) group = target.gameObject.AddComponent<CanvasGroup>();
                _preview.SetTarget(target, group);
                _preview.CaptureStartValues();          // target is at rest here — captures color/start endpoints
                _preview.onFinish = null;
                _preview.Play();
                _previewTarget = target;
                if (!_subscribed) { EditorApplication.update += Tick; _subscribed = true; }
            }

            // Symmetric with AnimationPresetBrowserPopup.StopPreview: settle the scratch channels (incl.
            // color, which the transform snapshot doesn't cover) then hand the RectTransform back.
            public void RestorePreview()
            {
                if (_subscribed) { EditorApplication.update -= Tick; _subscribed = false; }
                if (_previewTarget == null) return;
                _preview.Stop(silent: true);
                _preview.RestoreStartValues();
                AnimationPreview.EndPreview(_previewTarget);
                _previewTarget = null;
            }

            private void Tick()
            {
                if (_previewTarget == null) { RestorePreview(); return; }
                // Motion.Draw refreshes _lastDraw every OnGUI; if it stops running (tab switched away /
                // window hidden) the preview is orphaned — put the object back. Repaint keeps Draw (and
                // _lastDraw) alive while the tab IS active, so this only trips once the tab goes inactive.
                if (EditorApplication.timeSinceStartup - _lastDraw > PreviewIdleRestoreSeconds)
                { RestorePreview(); return; }
                if (_window != null) _window.Repaint();
            }

            public void Dispose() => RestorePreview();
        }

        internal static object CreateState() => new State();

        internal static void Draw(DesignSystemTabContext ctx)
        {
            var s = ctx.State<State>();
            s.MarkDrawn(ctx.window);
            NeoUISettings settings = ctx.settings;

            // ---- 1. Default motion per animator role (unchanged) --------------------------------------
            EditorGUILayout.LabelField("Default motion per animator role", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Copied into new animator components and generated buttons/views. " +
                "Pick a preset per role, or clear (✕) to keep the built-in feel.",
                EditorStyles.wordWrappedMiniLabel);
            foreach (NeoAnimatorRole role in NeoAnimatorRoles.All)
                DrawMotionRole(settings, role);

            NeoGUI.Splitter();

            // ---- 2. Preset library: browse + author ---------------------------------------------------
            EditorGUILayout.LabelField("Preset library", EditorStyles.boldLabel);
            IReadOnlyList<UIAnimationPreset> all = AnimationPresetRegistry.All;
            if (all.Count == 0)
            {
                EditorGUILayout.HelpBox("No animation presets found yet. Seed the curated ~46-preset " +
                    "library with the button below, or drop your own UIAnimationPreset asset (right-click " +
                    "in Project → Create → Neo UI → Animation Preset).", MessageType.Info);
                if (GUILayout.Button(new GUIContent("Create or Repair Animation Library",
                        "Seed/repair the curated preset library the role dropdowns and this browser use")))
                {
                    int n = AnimationLibraryBootstrap.CreateOrRepair();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[Neo.UI] Animation library: {n} preset(s) created.");
                    s.model = null; // force a rebuild against the freshly-seeded set
                }
                return;
            }

            EnsureModel(s, all);
            DrawSearch(s);
            DrawBrowser(s);

            NeoGUI.Splitter();
            DrawNewPreset(s, all);

            if (s.selected != null)
            {
                NeoGUI.Splitter();
                DrawSelectedEditor(s, settings);
            }
        }

        // ---------------------------------------------------------------- role defaults (unchanged)

        private static void DrawMotionRole(NeoUISettings settings, NeoAnimatorRole role)
        {
            settings.TryGetDefaultAnimation(role.Id, out UIAnimationPreset current);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(role.DisplayName, role.Description), GUILayout.Width(150f));
                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                NeoDropdown.ValuePopup(rect, current != null ? current.fullName : "",
                    () => AnimationPresetRegistry.FullNamesForRole(role.Id),
                    chosen =>
                    {
                        Undo.RecordObject(settings, "Set motion default");
                        settings.SetDefaultAnimation(role.Id, AnimationPresetRegistry.GetByFullName(chosen));
                        EditorUtility.SetDirty(settings);
                    }, emptyLabel: "(built-in)");
                using (new EditorGUI.DisabledScope(current == null))
                    if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    {
                        Undo.RecordObject(settings, "Clear motion default");
                        settings.SetDefaultAnimation(role.Id, null);
                        EditorUtility.SetDirty(settings);
                    }
            }
        }

        // ---------------------------------------------------------------- library browser

        // Rebuild the browser model only when the discovered set changes (create/delete) — never per OnGUI.
        private static void EnsureModel(State s, IReadOnlyList<UIAnimationPreset> all)
        {
            if (s.model != null && s.builtForCount == all.Count) return;
            AnimationPresetBrowserModel previous = s.model;
            // No animator role in the tab context → no suggested categories (default: everything expanded).
            // Force the selected preset's own category open so it stays visible after a rebuild.
            s.model = new AnimationPresetBrowserModel(all, null, s.selected != null ? s.selected.fullName : null);
            s.model.CopyExpansionFrom(previous); // preserve the user's fold state across create/delete
            s.builtForCount = all.Count;
        }

        private static void DrawSearch(State s)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Search", GUILayout.Width(52f));
                s.filter = EditorGUILayout.TextField(s.filter ?? "");
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(s.filter)))
                    if (GUILayout.Button(new GUIContent("✕", "Clear the search"), GUILayout.Width(22f)))
                    { s.filter = ""; GUI.FocusControl(null); }
            }
        }

        private static void DrawBrowser(State s)
        {
            IReadOnlyList<AnimationPresetBrowserModel.Row> rows = s.model.BuildRows(s.filter);
            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("No presets match the search.", EditorStyles.miniLabel);
                return;
            }
            foreach (AnimationPresetBrowserModel.Row row in rows)
            {
                if (row.header != null) DrawHeaderRow(s, row.header);
                else DrawPresetRow(s, row.preset);
            }
        }

        private static void DrawHeaderRow(State s, AnimationPresetBrowserModel.Group group)
        {
            Event e = Event.current;
            bool searching = !string.IsNullOrEmpty(s.filter);
            bool open = searching || s.model.IsExpanded(group.category);
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, NeoColors.SectionBackground);
                GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height),
                    $"{(open ? "▾" : "▸")}  {group.category}  ({group.presets.Count})", EditorStyles.miniBoldLabel);
            }
            if (!searching && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                s.model.ToggleExpanded(group.category);
                e.Use();
                s.RepaintHost();
            }
        }

        private static void DrawPresetRow(State s, UIAnimationPreset preset)
        {
            Event e = Event.current;
            bool selected = preset == s.selected;
            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            if (e.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(rect, NeoColors.Animation.WithAlpha(0.22f));
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), NeoColors.Animation);
                }
                GUI.Label(new Rect(rect.x + 16f, rect.y, rect.width - 20f, rect.height),
                    new GUIContent(preset.presetName, preset.fullName), EditorStyles.label);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                s.Select(preset);
                e.Use();
                s.RepaintHost();
            }
        }

        // ---------------------------------------------------------------- new preset

        private static void DrawNewPreset(State s, IReadOnlyList<UIAnimationPreset> all)
        {
            EditorGUILayout.LabelField("New preset", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Category", GUILayout.Width(60f));
                Rect rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
                // Categories from whatever the discovered assets declare (never hardcoded) + free-text "+ Add".
                NeoDropdown.ValuePopup(rect, s.newCategory,
                    () => all.Where(p => p != null)
                             .Select(p => string.IsNullOrEmpty(p.category) ? "Custom" : p.category)
                             .Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList(),
                    chosen => s.newCategory = chosen,
                    emptyLabel: "(pick or add)",
                    onAddNew: value => s.newCategory = value);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Name", GUILayout.Width(60f));
                s.newName = EditorGUILayout.TextField(s.newName ?? "");
                using (new EditorGUI.DisabledScope(
                           string.IsNullOrWhiteSpace(s.newName) || string.IsNullOrWhiteSpace(s.newCategory)))
                    if (GUILayout.Button("Create", GUILayout.Width(70f)))
                        CreatePreset(s);
            }

            if (GUILayout.Button(new GUIContent("Create or Repair Animation Library",
                    "Seed any missing curated built-in presets (never clobbers your edits)")))
            {
                int n = AnimationLibraryBootstrap.CreateOrRepair();
                AssetDatabase.SaveAssets();
                Debug.Log($"[Neo.UI] Animation library: {n} preset(s) created.");
                s.model = null; // rebuild to include any newly-seeded presets
            }
        }

        private static void CreatePreset(State s)
        {
            string category = s.newCategory.Trim();
            string name = s.newName.Trim();
            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(name)) return;

            DesignSystemGUI.EnsureFolder(AnimationLibraryBootstrap.LibraryRoot);
            // Same "{Category}_{Name}.asset" convention AnimationLibraryBootstrap seeds under.
            string path = $"{AnimationLibraryBootstrap.LibraryRoot}/{category}_{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<UIAnimationPreset>(path) != null)
            {
                Debug.LogWarning($"[Neo.UI] An animation preset already exists at '{path}'.");
                return;
            }

            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = category;
            preset.presetName = name;
            preset.animation = new UIAnimation();
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery(); // matches the bootstrap's own creation path

            s.model = null;    // rebuild the browser to include the new asset
            s.newName = "";
            s.Select(preset);
            Debug.Log($"[Neo.UI] Created animation preset '{preset.fullName}' at '{path}'.");
        }

        // ---------------------------------------------------------------- embedded editor + preview

        private static void DrawSelectedEditor(State s, NeoUISettings settings)
        {
            s.EnsureSerialized();
            if (s.selectedSO == null || s.selectedSO.targetObject == null)
            {
                // The asset was deleted out from under us (elsewhere) — drop the selection.
                s.Select(null);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Editing: {s.selected.fullName}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Ping", "Select this asset in the Project window"),
                        EditorStyles.miniButton, GUILayout.Width(48f)))
                    EditorGUIUtility.PingObject(s.selected);
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.82f, 0.35f, 0.35f);
                bool delete = GUILayout.Button(new GUIContent("Delete", "Delete this preset asset"),
                    EditorStyles.miniButton, GUILayout.Width(58f));
                GUI.backgroundColor = prev;
                if (delete) { DeleteSelected(s, settings); return; }
            }

            // The one edit transaction: caller owns Update/ApplyModifiedProperties; ApplyModifiedProperties
            // records Undo and dirties the asset (SaveAssets from the window footer persists) — the same
            // semantics as the UIAnimationPresetEditor inspector consumer.
            s.selectedSO.Update();
            AnimationPresetGUI.Draw(s.selectedSO, "NeoUI.DesignSystem.Motion");
            s.selectedSO.ApplyModifiedProperties();

            GUILayout.Space(NeoGUI.Spacing);
            DrawPreviewControls(s);
        }

        private static void DrawPreviewControls(State s)
        {
            RectTransform selection = Selection.activeGameObject != null
                ? Selection.activeGameObject.transform as RectTransform
                : null;

            // Selection moved off the object we're previewing → restore it before it drifts out of sight.
            if (s.Previewing && selection != s.PreviewTarget) s.RestorePreview();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                if (s.Previewing)
                {
                    if (GUILayout.Button(new GUIContent("▶ Replay", "Restart the preview from the object's rest state."),
                            EditorStyles.miniButtonLeft))
                        s.StartPreview(s.selected, s.PreviewTarget);
                    if (GUILayout.Button(new GUIContent("■ Stop", "Restore the object to its original state."),
                            EditorStyles.miniButtonRight))
                        s.RestorePreview();
                }
                else
                {
                    using (new EditorGUI.DisabledScope(selection == null))
                    {
                        var content = new GUIContent(
                            selection == null ? "▶ Preview (select a UI object)" : "▶ Preview on selection",
                            selection == null
                                ? "Select a UI object in the scene to preview this preset"
                                : $"Play this preset on '{selection.name}'.");
                        if (GUILayout.Button(content, EditorStyles.miniButton))
                            s.StartPreview(s.selected, selection);
                    }
                }
            }
        }

        private static void DeleteSelected(State s, NeoUISettings settings)
        {
            UIAnimationPreset preset = s.selected;
            if (preset == null) return;
            string path = AssetDatabase.GetAssetPath(preset);
            if (!EditorUtility.DisplayDialog("Delete Animation Preset",
                    $"Delete '{preset.fullName}'?\n\n{path}\n\nThis cannot be undone.", "Delete", "Cancel"))
                return;

            s.RestorePreview();

            // No-silent-failure: if any motion-role default points at this preset, clear it (undo-recorded)
            // and say which roles were cleared — the default would otherwise dangle at a deleted asset.
            var clearedRoles = new List<string>();
            if (settings != null && settings.animatorDefaults != null)
            {
                foreach (NeoUISettings.AnimatorDefault entry in settings.animatorDefaults)
                    if (entry != null && entry.preset == preset && !string.IsNullOrEmpty(entry.role))
                        clearedRoles.Add(entry.role);
                if (clearedRoles.Count > 0)
                {
                    Undo.RecordObject(settings, "Clear motion defaults for deleted preset");
                    foreach (string role in clearedRoles) settings.SetDefaultAnimation(role, null);
                    EditorUtility.SetDirty(settings);
                }
            }

            string fullName = preset.fullName;
            s.Select(null);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
            AnimationPresetRegistry.InvalidateDiscovery();
            s.model = null; // rebuild the browser without the deleted asset

            if (clearedRoles.Count > 0)
                Debug.Log($"[Neo.UI] Deleted animation preset '{fullName}'. Cleared it from motion default " +
                          $"role(s): {string.Join(", ", clearedRoles)}.");
            else
                Debug.Log($"[Neo.UI] Deleted animation preset '{fullName}'.");
        }
    }
}
