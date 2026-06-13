using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Outcome of a <see cref="SpecBaseline.Sync"/> run — the structured form the AgentBridge
    /// <c>sync</c> action and the Sync window both render. Nothing is ever dropped silently: human
    /// drift that round-trips lands in <see cref="applied"/>; collisions land in <see cref="conflicts"/>;
    /// edits the spec cannot represent land in <see cref="offSpecWarnings"/> (a refusal) or, when forced,
    /// in <see cref="dropped"/> (the recorded loss).
    /// </summary>
    public sealed class SyncResult
    {
        /// <summary> The run succeeded (merged + regenerated, or captured) with no failing conflict. </summary>
        public bool ok;
        /// <summary> Off-spec edits were present and <c>force</c> was not set, so nothing was regenerated. </summary>
        public bool refused;
        /// <summary> The merged spec was generated into assets (false for the capture-only form). </summary>
        public bool regenerated;
        /// <summary> <c>.neo-baseline.json</c> was rewritten to reflect the new canonical spec. </summary>
        public bool baselineUpdated;

        /// <summary> The spec the assets now reflect (merged spec, or the captured project). </summary>
        public UISpec merged;
        /// <summary> What the human changed in the editor since the baseline (drift). </summary>
        public List<SpecChange> humanChanges = new List<SpecChange>();
        /// <summary> Human drift folded into the merged spec. </summary>
        public List<SpecChange> applied = new List<SpecChange>();
        /// <summary> Nodes changed in BOTH the human project and the incoming spec (incoming won by default). </summary>
        public List<SpecChange> conflicts = new List<SpecChange>();
        /// <summary> Non-round-trippable editor edits (the refusal reason, or — when forced — the loss). </summary>
        public List<OffSpecFinding> offSpecWarnings = new List<OffSpecFinding>();
        /// <summary> Off-spec edits actually discarded because <c>force</c> overrode the refusal. </summary>
        public List<OffSpecFinding> dropped = new List<OffSpecFinding>();

        public GenerateReport generateReport;
        public string note;
    }

    /// <summary>
    /// The agent ↔ human collaboration protocol (Plan 4) — the <b>policy layer</b> built on Plan 1's
    /// merge/diff/lint machinery (<see cref="SpecDiff"/>, <see cref="OffSpecLint"/>,
    /// <see cref="SpecMerge"/>) and on the <see cref="NeoBaseline"/> baseline file.
    ///
    /// <para>The one invariant it enforces: <i>the live, merged spec — not whatever the agent last
    /// wrote — is always the canonical input to the next generate.</i> So instead of calling the raw,
    /// destructive <see cref="UISpecGenerator.Generate(UISpec)"/>, an agent (or human) calls
    /// <see cref="Sync"/>, which folds the human's editor drift back in and refuses to silently discard
    /// edits that cannot round-trip.</para>
    ///
    /// <para>Raw baseline read/write lives in <see cref="NeoBaseline"/> (Plan 1); this class is the
    /// protocol around <i>when</i> it is consulted and rewritten.</para>
    /// </summary>
    public static class SpecBaseline
    {
        /// <summary> Path of the committed <c>.neo-baseline.json</c> (follows <c>GeneratedRoot</c>). </summary>
        public static string Path => NeoBaseline.Path;
        public static bool Exists => NeoBaseline.Exists;
        public static UISpec Load() => NeoBaseline.Load();
        public static void Save(UISpec spec) => NeoBaseline.Save(spec);

        /// <summary>
        /// "Capture My Edits": export the live project and fold its drift into the baseline so the
        /// human's manual work becomes the canonical input to the next agent sync — no regenerate.
        /// Still refuses (unless <paramref name="force"/>) when off-spec edits are present, since
        /// capturing the exported project as the baseline would silently leave those edits out of it.
        /// </summary>
        public static SyncResult CaptureEdits(bool force = false) =>
            Sync(null, ConflictPolicy.PreferTheirs, force);

        /// <summary>
        /// The safe-regenerate protocol. In order:
        /// <list type="number">
        /// <item>export the live project → <c>current</c>;</item>
        /// <item>drift + off-spec lint of <c>current</c> vs the baseline;</item>
        /// <item>if off-spec edits exist, <b>refuse</b> (and regenerate nothing) unless <paramref name="force"/>
        ///   — with <c>force</c> they are recorded in <see cref="SyncResult.dropped"/>, never lost silently;</item>
        /// <item>three-way merge <c>base = baseline</c>, <c>ours = current</c>, <c>theirs = incoming</c>;</item>
        /// <item>generate from the merged spec;</item>
        /// <item>the generate rewrites <c>.neo-baseline.json</c> to the merged spec.</item>
        /// </list>
        /// A null <paramref name="incoming"/> is the capture-only form (steps 1-2 then write baseline,
        /// no merge, no regenerate).
        /// </summary>
        public static SyncResult Sync(UISpec incoming,
            ConflictPolicy policy = ConflictPolicy.PreferTheirs, bool force = false)
        {
            var sr = new SyncResult();

            // 1. export the live project
            UISpec current = UISpecExporter.ExportProject();

            // 2. drift + off-spec lint vs the baseline (both empty/undefined when no baseline exists)
            UISpec baseline = NeoBaseline.Load();
            bool hadBaseline = baseline != null;
            if (hadBaseline) sr.humanChanges = SpecDiff.Compare(baseline, current);
            sr.offSpecWarnings = OffSpecLint.ScanProject(baseline);

            // 3. off-spec gate — never silently discard edits that can't round-trip
            if (sr.offSpecWarnings.Count > 0 && !force)
            {
                sr.ok = false;
                sr.refused = true;
                sr.note = $"Refused: {sr.offSpecWarnings.Count} editor edit(s) cannot round-trip and would be " +
                          "lost on regenerate. Fix them (bind a theme token / fold the change into the spec) " +
                          "or pass \"force\":true to proceed and accept the loss.";
                Debug.LogWarning($"[Neo.UI] sync {sr.note}");
                return sr;
            }
            if (sr.offSpecWarnings.Count > 0) // forced past the gate — record the loss explicitly
                sr.dropped.AddRange(sr.offSpecWarnings);

            // capture-only form: fold the exported project into the baseline, no regenerate
            if (incoming == null)
            {
                NeoBaseline.Save(current);
                sr.merged = current;
                sr.baselineUpdated = true;
                sr.ok = true;
                sr.note = hadBaseline
                    ? "Captured the current project as the new baseline (no regenerate)."
                    : "No baseline existed — established one from the current project (no regenerate).";
                return sr;
            }

            // 4. three-way merge: base = baseline, ours = human-drifted project, theirs = incoming spec
            UISpec mergeBase = hadBaseline ? baseline : new UISpec(); // 2-way union with no common ancestor
            MergeResult merge = SpecMerge.Merge(mergeBase, current, incoming, policy);
            sr.merged = merge.merged;
            sr.applied = merge.applied;
            sr.conflicts = merge.conflicts;
            sr.dropped.AddRange(merge.dropped);

            // 5 + 6. generate from the merged spec; the generate rewrites .neo-baseline.json to it
            // (warnOnDrift:false — the drift was just merged in, so warning about it would mislead)
            sr.generateReport = UISpecGenerator.Generate(merge.merged, writeBaseline: true, warnOnDrift: false);
            sr.regenerated = true;
            sr.baselineUpdated = !sr.generateReport.hasProblems;

            sr.ok = !merge.failed && !sr.generateReport.hasProblems;
            if (!hadBaseline)
                sr.note = "No baseline existed — performed a 2-way union (establish a baseline for true " +
                          "three-way merges). Wrote the merged spec as the new baseline.";
            else if (sr.conflicts.Count > 0)
                sr.note = $"{sr.conflicts.Count} conflict(s): the incoming spec won by default — " +
                          "review them in the Sync/Drift window or re-run with conflictPolicy:preferOurs.";
            return sr;
        }
    }
}
