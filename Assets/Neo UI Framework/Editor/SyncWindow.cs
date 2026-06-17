using System;
using System.Collections.Generic;
using System.IO;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Human entry points for the agent ↔ human collaboration protocol (Plan 4):
    /// <list type="bullet">
    /// <item><b>Sync With Spec…</b> — pick an agent's incoming spec and run the safe-regenerate flow
    /// (<see cref="SpecBaseline.Sync"/>): the human's editor drift is merged in, conflicts are surfaced,
    /// and off-spec edits block the sync (with an explicit Force option) rather than vanishing.</item>
    /// <item><b>Capture My Edits</b> — fold the current project into the baseline so a manual editing
    /// session becomes the canonical input to the next agent sync, no regenerate.</item>
    /// </list>
    /// Results render in the three Plan 1 buckets (green round-trips, yellow conflicts, red lost),
    /// matching <see cref="DriftWindow"/>.
    /// </summary>
    public sealed class SyncWindow : EditorWindow
    {
        private SyncResult _result;
        private string _incomingPath;
        private ConflictPolicy _policy = ConflictPolicy.PreferTheirs;
        private Vector2 _scroll;

        // When launched from the Hub for a specific showcase, every sync/force re-run must execute
        // inside that showcase's isolated root (NeoWorkspace.Scoped) — otherwise "Re-run Sync" /
        // "Force sync" runs against the DEFAULT root and can never resolve the showcase. Null for the
        // standalone "Sync With Spec…" menu, which intentionally operates on the default root.
        private Showcase _showcase;

        [MenuItem("Tools/Neo UI/Advanced/Sync With Spec…", priority = 13)]
        public static void SyncWithSpec()
        {
            string path = EditorUtility.OpenFilePanel("Select the incoming UI spec (JSON)", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            SyncWindow window = GetWindow<SyncWindow>(false, "Neo UI Sync");
            window.minSize = new Vector2(460f, 360f);
            window._incomingPath = path;
            window.RunSync(force: false);
        }

        [MenuItem("Tools/Neo UI/Advanced/Capture My Edits", priority = 14)]
        public static void CaptureMyEdits()
        {
            SyncResult sr = SpecBaseline.CaptureEdits();
            if (sr.refused)
            {
                if (!EditorUtility.DisplayDialog("Off-spec edits present", sr.note,
                        "Capture anyway (drop them)", "Cancel"))
                {
                    Show(sr, null);
                    return;
                }
                sr = SpecBaseline.CaptureEdits(force: true);
            }
            Debug.Log($"[Neo.UI] Capture My Edits — {sr.note}");
            Show(sr, null);
        }

        internal static void Show(SyncResult result, string incomingPath, Showcase showcase = null)
        {
            SyncWindow window = GetWindow<SyncWindow>(false, "Neo UI Sync");
            window.minSize = new Vector2(460f, 360f);
            window._result = result;
            window._incomingPath = incomingPath;
            window._showcase = showcase; // null = standalone menu (default root); set = scope re-runs
            window.Repaint();
        }

        private void RunSync(bool force)
        {
            UISpec incoming;
            try
            {
                incoming = UISpec.FromJson(File.ReadAllText(_incomingPath));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Sync With Spec", $"Could not read the incoming spec:\n{e.Message}", "OK");
                return;
            }

            // Scope to the showcase's isolated root when launched from the Hub; the standalone menu
            // (no showcase) runs on the default root exactly as before.
            if (_showcase != null)
                using (NeoWorkspace.Scoped(_showcase)) Sync(incoming, force);
            else
                Sync(incoming, force);
            Repaint();
        }

        // Runs the sync (and the explicit, never-silent force override on a refusal). The caller wraps
        // this in NeoWorkspace.Scoped when a showcase context is present, so both calls share that scope.
        private void Sync(UISpec incoming, bool force)
        {
            _result = SpecBaseline.Sync(incoming, _policy, force);
            // off-spec edits block the destructive regenerate — offer the explicit, never-silent override
            if (_result.refused
                && EditorUtility.DisplayDialog("Off-spec edits present", _result.note,
                    "Force sync (drop them)", "Cancel"))
                _result = SpecBaseline.Sync(incoming, _policy, force: true);
        }

        private void OnGUI()
        {
            NeoGUI.ComponentHeader("Sync With Spec",
                "Merge an agent's spec into the project without losing human edits", NeoColors.Containers);

            if (!string.IsNullOrEmpty(_incomingPath))
            {
                EditorGUILayout.LabelField("Incoming", Path.GetFileName(_incomingPath), EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _policy = (ConflictPolicy)EditorGUILayout.EnumPopup("Conflict policy", _policy);
                    if (GUILayout.Button("Re-run Sync", GUILayout.Width(110f), GUILayout.Height(20f)))
                        RunSync(force: false);
                }
            }

            if (_result == null)
            {
                EditorGUILayout.HelpBox(
                    "Run Tools → Neo UI → Sync With Spec… to merge an incoming agent spec, or " +
                    "Capture My Edits to fold the current project into the baseline.", MessageType.Info);
                return;
            }

            NeoGUI.Splitter();
            DrawSummary();
            NeoGUI.Splitter();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_result.refused)
                EditorGUILayout.HelpBox(_result.note, MessageType.Warning);
            else if (!string.IsNullOrEmpty(_result.note))
                EditorGUILayout.HelpBox(_result.note, _result.ok ? MessageType.Info : MessageType.Warning);

            Bucket("Folded in (round-trips safely)", NeoColors.Add, _result.applied);
            Bucket("Conflicts (incoming won by default)", NeoColors.Warning, _result.conflicts);
            if (_result.humanChanges.Count > 0)
                Bucket("Human drift since baseline", NeoColors.Containers, _result.humanChanges);

            if (_result.offSpecWarnings.Count > 0)
            {
                EditorGUILayout.Space();
                Section("Off-spec — blocks sync (or LOST when forced)", NeoColors.Remove);
                foreach (OffSpecFinding finding in _result.offSpecWarnings) FindingRow(finding);
            }
            if (_result.dropped.Count > 0)
            {
                EditorGUILayout.Space();
                Section("Dropped (forced past the off-spec gate — these were lost)", NeoColors.Remove);
                foreach (OffSpecFinding finding in _result.dropped) FindingRow(finding);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                NeoGUI.Badge(_result.ok ? "ok" : _result.refused ? "refused" : "problems",
                    _result.ok ? NeoColors.Add : NeoColors.Remove);
                NeoGUI.Badge(_result.regenerated ? "regenerated" : "no regenerate", NeoColors.Containers);
                NeoGUI.Badge($"{_result.applied.Count} folded", NeoColors.Add);
                NeoGUI.Badge($"{_result.conflicts.Count} conflicts", NeoColors.Warning);
                NeoGUI.Badge($"{_result.offSpecWarnings.Count} off-spec", NeoColors.Remove);
            }
        }

        private static void Bucket(string title, Color color, List<SpecChange> changes)
        {
            if (changes == null || changes.Count == 0) return;
            Section(title, color);
            foreach (SpecChange change in changes) ChangeRow(change);
        }

        private static void Section(string title, Color color)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.contentColor = previous;
        }

        private static void ChangeRow(SpecChange change)
        {
            string detail = change.kind == SpecChangeKind.Modified ? $"{change.before} → {change.after}"
                : change.kind == SpecChangeKind.Added ? $"added {change.after}"
                : $"removed {change.before}";
            EditorGUILayout.LabelField($"• {change.path}", detail, EditorStyles.miniLabel);
        }

        private static void FindingRow(OffSpecFinding finding)
        {
            EditorGUILayout.LabelField($"• {finding.message}", EditorStyles.wordWrappedMiniLabel);
            Color previous = GUI.contentColor;
            GUI.contentColor = NeoColors.TextSubtle;
            EditorGUILayout.LabelField($"    Fix: {finding.fix}", EditorStyles.wordWrappedMiniLabel);
            GUI.contentColor = previous;
        }
    }
}
