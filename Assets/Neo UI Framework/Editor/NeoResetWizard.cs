using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The uninstall counterpart to <see cref="NeoSetupWizard"/>: rehearse the fresh-project experience
    /// by deleting what the create-or-repair bootstraps (and the generators) created, per part. Rows come
    /// from <see cref="NeoResetComponents"/> — the same extensibility seam pattern as the Hub tools — with
    /// the curated libraries (animations, transitions, fonts) and user-authored content (showcase scenes,
    /// custom theme bundles, binding stubs) kept by default. The exact asset paths that would be deleted
    /// are previewed before anything runs, a confirmation dialog gates execution, and everything here is
    /// re-creatable via <c>Tools → Neo UI → Setup</c> (committed files also restore via git).
    /// </summary>
    public sealed class NeoResetWizard : EditorWindow
    {
        // selection + cached plan — recomputed on open/focus/toggle change only, never per-OnGUI
        // (CLAUDE.md editor-perf rules). _fullPlan spans ALL components so unchecked rows can still show
        // whether anything of theirs exists; _selectedPlan is the checked subset Execute runs.
        private readonly Dictionary<string, bool> _selected = new Dictionary<string, bool>();
        private ResetPlan _fullPlan = new ResetPlan();
        private ResetPlan _selectedPlan = new ResetPlan();

        private string _summary;
        private Vector2 _scroll;
        private bool _seededSelection;

        [MenuItem("Tools/Neo UI/Setup/Reset To Clean Slate…", priority = 200)]
        public static void Open()
        {
            var window = GetWindow<NeoResetWizard>(true, "Neo UI — Reset To Clean Slate");
            window.minSize = new Vector2(460f, 480f);
        }

        private void OnEnable()
        {
            if (!_seededSelection)
            {
                foreach (ResetComponentDescriptor component in NeoResetComponents.All)
                    _selected[component.id] = !component.keepByDefault;
                _seededSelection = true;
            }
            RebuildPlans();
        }

        private void OnFocus() => RebuildPlans();

        private void RebuildPlans()
        {
            var allIds = new List<string>();
            var selectedIds = new List<string>();
            foreach (ResetComponentDescriptor component in NeoResetComponents.All)
            {
                allIds.Add(component.id);
                if (_selected.TryGetValue(component.id, out bool on) && on) selectedIds.Add(component.id);
            }
            _fullPlan = NeoProjectReset.BuildPlan(allIds);
            _selectedPlan = NeoProjectReset.BuildPlan(selectedIds);
        }

        private void OnGUI()
        {
            NeoGUI.ComponentHeader("Reset To Clean Slate",
                "Delete what setup created to rehearse the from-scratch experience", NeoColors.Remove);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Hint("Everything below is re-creatable: each part has a create-or-repair step under " +
                 "Tools → Neo UI → Setup, showcases regenerate from the Hub, and committed files can be " +
                 "restored with git. Curated libraries and user-authored content are kept by default.");
            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            foreach (ResetComponentDescriptor component in NeoResetComponents.All)
                DrawComponentRow(component);
            if (EditorGUI.EndChangeCheck()) RebuildPlans();

            EditorGUILayout.Space(4f);
            DrawPathPreview();

            EditorGUILayout.Space(8f);
            int total = _selectedPlan.TotalPathCount;
            using (new EditorGUI.DisabledScope(total == 0))
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = NeoColors.Remove;
                if (GUILayout.Button(new GUIContent(
                        total == 0 ? "Nothing Selected To Delete" : $"Delete {total} Asset Path(s)…",
                        "Shows a confirmation with the exact paths before anything is deleted"),
                        GUILayout.Height(30f)))
                    ConfirmAndExecute();
                GUI.backgroundColor = prev;
            }

            if (!string.IsNullOrEmpty(_summary))
            {
                NeoGUI.Splitter();
                EditorGUILayout.HelpBox(_summary, MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open New Project Setup")) NeoSetupWizard.Open();
                    if (GUILayout.Button("Open Hub")) NeoUIHubWindow.Open();
                    if (GUILayout.Button("Done")) Close();
                }
                EditorGUILayout.LabelField(
                    "Next: run the New Project Setup wizard to experience the first-run flow.",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawComponentRow(ResetComponentDescriptor component)
        {
            int existing = ExistingPathCount(component.id);
            using (new EditorGUILayout.HorizontalScope())
            {
                _selected.TryGetValue(component.id, out bool on);
                using (new EditorGUI.DisabledScope(existing == 0))
                    _selected[component.id] =
                        EditorGUILayout.ToggleLeft(new GUIContent(component.label, component.tooltip), on,
                            GUILayout.ExpandWidth(true));
                GUILayout.FlexibleSpace();
                Color prev = GUI.contentColor;
                GUI.contentColor = existing > 0 ? NeoColors.Remove : NeoColors.TextSubtle;
                GUILayout.Label(existing > 0 ? $"{existing} path(s)" : "nothing to delete",
                    EditorStyles.miniLabel, GUILayout.Width(96f));
                GUI.contentColor = prev;
            }
        }

        private int ExistingPathCount(string componentId)
        {
            foreach (ResetPlanEntry entry in _fullPlan.entries)
                if (entry.component.id == componentId)
                    return entry.paths.Count;
            return 0;
        }

        private void DrawPathPreview()
        {
            if (!NeoGUI.BeginFoldoutSection("NeoUI.ResetWizard.Paths",
                    $"What will be deleted ({_selectedPlan.TotalPathCount} paths)"))
            {
                NeoGUI.EndFoldoutSection();
                return;
            }
            if (_selectedPlan.TotalPathCount == 0)
                EditorGUILayout.LabelField("Nothing — tick a part above.", EditorStyles.miniLabel);
            foreach (ResetPlanEntry entry in _selectedPlan.entries)
            {
                EditorGUILayout.LabelField(entry.component.label, EditorStyles.miniBoldLabel);
                foreach (string path in entry.paths)
                    EditorGUILayout.LabelField("    " + path, EditorStyles.miniLabel);
            }
            NeoGUI.EndFoldoutSection();
        }

        private void ConfirmAndExecute()
        {
            int total = _selectedPlan.TotalPathCount;
            var parts = new List<string>();
            foreach (ResetPlanEntry entry in _selectedPlan.entries) parts.Add(entry.component.label);

            bool confirmed = EditorUtility.DisplayDialog("Reset To Clean Slate",
                $"This deletes {total} asset path(s):\n\n• {string.Join("\n• ", parts)}\n\n" +
                "Everything is re-creatable via Tools → Neo UI → Setup, and committed files can be " +
                "restored with git. Continue?",
                "Delete", "Cancel");
            if (!confirmed) return;

            ResetReport report = NeoProjectReset.Execute(_selectedPlan);
            _summary = report.Summary;
            RebuildPlans();
            Repaint();
        }

        private static void Hint(string text)
        {
            Color prev = GUI.contentColor;
            GUI.contentColor = NeoColors.TextSubtle;
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
            GUI.contentColor = prev;
        }
    }
}
