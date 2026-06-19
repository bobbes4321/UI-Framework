using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The human-facing round-trip safety net (Plan 1): before letting an agent regenerate, see
    /// what a human changed in the editor since the project was generated, split into
    /// <list type="bullet">
    /// <item>edits that will round-trip safely (green),</item>
    /// <item>edits that conflict with a candidate spec (yellow — populated by a merge), and</item>
    /// <item>off-spec edits that the next generate will silently lose (red, with the fix).</item>
    /// </list>
    /// "Fold edits into spec" captures the current project as the new baseline so the round-tripping
    /// drift is preserved on the next generate. Off-spec edits cannot be folded — the window names
    /// them so they aren't lost without warning.
    /// </summary>
    public sealed class DriftWindow : EditorWindow
    {
        private UISpec _baseline;
        private List<SpecChange> _changes = new List<SpecChange>();
        private List<OffSpecFinding> _offSpec = new List<OffSpecFinding>();
        private bool _scanned;
        private Vector2 _scroll;

        [MenuItem("Tools/Neo UI/Advanced/Check For Drift", priority = 15)]
        public static void Open()
        {
            DriftWindow window = GetWindow<DriftWindow>(false, "Neo UI Drift");
            window.minSize = new Vector2(420f, 320f);
            window.Rescan();
        }

        private void OnEnable()
        {
            if (!_scanned) Rescan();
        }

        private void Rescan()
        {
            DriftStatus status = DriftStatus.Scan();
            _baseline = status.baseline;
            _changes = status.changes;
            _offSpec = status.offSpec;
            _scanned = true;
            Repaint();
        }

        private void OnGUI()
        {
            NeoGUI.ComponentHeader("Round-trip Safety",
                "What a human changed since this project was generated", NeoColors.Containers);

            if (_baseline == null)
            {
                EditorGUILayout.HelpBox(
                    "No baseline found. The baseline is the spec the project was last generated from — " +
                    "it's the reference drift is measured against. Establish one from the current project " +
                    "to start tracking editor edits.", MessageType.Info);
                if (GUILayout.Button("Establish Baseline From Current Project", GUILayout.Height(28f)))
                {
                    NeoBaseline.Establish();
                    Rescan();
                }
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Re-scan", GUILayout.Height(22f))) Rescan();
                GUI.enabled = _changes.Count > 0;
                if (GUILayout.Button("Fold Edits Into Spec & Update Baseline", GUILayout.Height(22f))
                    && EditorUtility.DisplayDialog("Fold Edits Into Spec",
                        "Capture the current project as the new baseline? Round-tripping edits will be " +
                        "preserved on the next generate. Off-spec edits (red) cannot be folded and will " +
                        "still be lost on regenerate.", "Fold", "Cancel"))
                {
                    NeoBaseline.Establish();
                    Rescan();
                }
                GUI.enabled = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                NeoGUI.Badge($"{_changes.Count} round-trip", NeoColors.Add);
                NeoGUI.Badge($"{_offSpec.Count} off-spec (lost)", NeoColors.Remove);
            }

            NeoGUI.Splitter();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_changes.Count == 0 && _offSpec.Count == 0)
                EditorGUILayout.HelpBox("No drift — the project matches its baseline.", MessageType.None);

            if (_changes.Count > 0)
            {
                Section("Round-trips safely", NeoColors.Add);
                foreach (SpecChange change in _changes) DriftStatus.ChangeRow(change);
            }

            if (_offSpec.Count > 0)
            {
                EditorGUILayout.Space();
                Section("Off-spec — will be LOST on regenerate", NeoColors.Remove);
                foreach (OffSpecFinding finding in _offSpec) DriftStatus.FindingRow(finding);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void Section(string title, Color color)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.contentColor = previous;
        }

    }
}
