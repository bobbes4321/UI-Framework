using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A reusable snapshot of round-trip drift — the export → diff → off-spec-lint triad, captured once so
    /// both the <see cref="DriftWindow"/> and the scene-view overlay read the SAME result instead of each
    /// re-running (and re-implementing) the scan. The colour semantics are shared too:
    /// red = off-spec edits (lost on regenerate), yellow = safe drift, green = clean, grey = no baseline.
    /// </summary>
    public sealed class DriftStatus
    {
        public UISpec baseline;
        public List<SpecChange> changes = new List<SpecChange>();
        public List<OffSpecFinding> offSpec = new List<OffSpecFinding>();

        public bool HasBaseline => baseline != null;
        public int RoundTrips => changes.Count;
        public bool HasOffSpec => offSpec.Count > 0;

        /// <summary> Dot/badge colour for the current state (see class summary). </summary>
        public Color DotColor =>
            !HasBaseline ? NeoColors.TextSubtle
            : HasOffSpec ? NeoColors.Remove
            : RoundTrips > 0 ? NeoColors.Warning
            : NeoColors.Add;

        /// <summary> Short human label for the current state. </summary>
        public string Summary =>
            !HasBaseline ? "no baseline"
            : HasOffSpec ? $"{offSpec.Count} off-spec (lost on regenerate)"
            : RoundTrips > 0 ? $"{RoundTrips} edit(s) — round-trips safely"
            : "in sync with spec";

        /// <summary>
        /// Runs the scan against the CURRENT <see cref="UISpecGenerator.GeneratedRoot"/>. Callers scoping a
        /// showcase wrap this in <c>using NeoWorkspace.Scoped(showcase)</c>. Expensive (exports the project)
        /// — call on demand / on selection change, never per OnGUI.
        /// </summary>
        public static DriftStatus Scan()
        {
            var s = new DriftStatus();
            s.baseline = NeoBaseline.Load();
            if (s.baseline != null)
            {
                UISpec current = UISpecExporter.ExportProject();
                s.changes = SpecDiff.Compare(s.baseline, current);
                s.offSpec = OffSpecLint.ScanProject(s.baseline);
            }
            return s;
        }

        // ---- shared IMGUI rows (used by DriftWindow and the scene overlay) ----

        public static void ChangeRow(SpecChange change)
        {
            string detail = change.kind == SpecChangeKind.Modified ? $"{change.before} → {change.after}"
                : change.kind == SpecChangeKind.Added ? $"added {change.after}"
                : $"removed {change.before}";
            EditorGUILayout.LabelField($"• {change.path}", detail, EditorStyles.miniLabel);
        }

        public static void FindingRow(OffSpecFinding finding)
        {
            EditorGUILayout.LabelField($"• {finding.message}", EditorStyles.wordWrappedMiniLabel);
            Color previous = GUI.contentColor;
            GUI.contentColor = NeoColors.TextSubtle;
            EditorGUILayout.LabelField($"    Fix: {finding.fix}", EditorStyles.wordWrappedMiniLabel);
            GUI.contentColor = previous;
        }
    }
}
