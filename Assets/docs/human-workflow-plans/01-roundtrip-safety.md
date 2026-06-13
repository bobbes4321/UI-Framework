# Plan 1 — Round-trip Safety: Merge Engine, Drift Detection & Off-Spec Lint

## Problem

The spec is the source of truth; the prefab is its materialization. When a human edits a
generated prefab in the Unity editor, three failure modes lose their work silently:

1. **No merge.** `UISpecGenerator` overwrites prefabs wholesale via `PrefabUtility.SaveAsPrefabAsset`
   (`UISpecGenerator.cs` ~line 288). Re-generating from a *stale* spec wipes any prefab edit not
   already folded back into that spec.
2. **Silent off-spec loss.** `UISpecExporter` only reads back the "spec-exportable layer" (geometry,
   container decor, widget values, theme tokens, text). Widget *internals* are factory-owned and not
   exported (`UISpecExporter.cs:256-268`). A human who tints a button's inner Label child directly,
   adds a child inside a widget, or assigns a raw material sees it work in-editor, but it evaporates
   on the next generate with **no warning**.
3. **No visibility.** Nobody is told, before a destructive regenerate, what is about to be lost.

This plan builds the **machinery** to fix all three. Plan 4 builds the *workflow/policy* on top of it.

## Goal

Three composable capabilities, all operating on the existing `UISpec` model and the live project:

- **A. Spec diff** — structurally compare two `UISpec` instances and report per-node changes.
- **B. Drift detection** — compare the *current project* (via `UISpecExporter`) against a *baseline
  spec* and report what a human changed in the editor since generation.
- **C. Off-spec lint** — detect editor edits that will NOT survive a round trip and surface them as
  warnings telling the human how to make the change survive.
- **D. Merge** — fold human drift into a spec, producing a new canonical spec, so the next generate
  preserves the human's work.

These are libraries + AgentBridge actions + a menu command. They do not change generate/export
fidelity itself — they wrap it with safety.

## Current-state references

- `UISpec.FromJson` / `UISpec.ToJson` — `UISpec.cs:44`, `UISpec.cs:83`. Deterministic via `MiniJson`.
- Full model: `ThemeSpec`, `PresetSpec`, `ViewSpec`, `ElementSpec` (all kinds/fields,
  `UISpec.cs:333-587`), `PopupSpec`, `MenuCatalogSpec`/`MenuItemSpec`, `FlowSpec`.
- `UISpecExporter.ExportProject` — `UISpecExporter.cs:28-78`. Returns a `UISpec` for the live project.
- Exporter fidelity boundary — `UISpecExporter.cs:256-268` ("containers recurse; widget internals do not").
- `GeneratedMarker` collision guard — `UISpecGenerator.cs:281-285`.
- `AgentBridge.HandleRequest` dispatch — `AgentBridge.cs:80-122`.

## Design

### A. Spec diff — `SpecDiff.cs`

New file: `Assets/Neo UI Framework/Editor/Agent/SpecDiff.cs` (asmdef `Neo.UI.Editor`).

```csharp
public enum SpecChangeKind { Added, Removed, Modified }

public sealed class SpecChange {
    public SpecChangeKind kind;
    public string path;        // stable address, e.g. "views/Menu/Main/elements[2]/background"
    public string section;     // "theme" | "view" | "popup" | "settings" | "cheats" | "flow"
    public string before;      // serialized scalar / "(node)" for structural
    public string after;
    public bool roundTrips;    // false => this change cannot be represented in the spec (off-spec)
}

public static class SpecDiff {
    public static List<SpecChange> Compare(UISpec baseline, UISpec candidate);
}
```

**Node identity (how to match nodes across two specs — critical, do this right):**
- Views: by `id` (`category/viewName`).
- Popups / menu catalogs: by `id`.
- Flow nodes: by `name`; edges by `(to, trigger)`.
- Theme: by token name, per variant.
- **Elements within a view:** by `id` when the element has one (interactive widgets do). For
  id-less elements (text/image/shape/stacks), match by **structural position path**:
  `elements[i]` then recurse `children[j]`. This means reordering id-less siblings reads as
  modify+modify rather than a move — acceptable for v1; document it.

**Path format** is the stable address used everywhere (diff output, merge targeting, lint, and the
Composer window's selection). Define it once in `SpecPath.cs` as helpers
(`SpecPath.View(id)`, `.Element(viewId, indexChain)`, `.ThemeToken(variant, token)`, etc.) so diff,
merge, and the window all agree.

### B. Drift detection — wraps A

Drift = `SpecDiff.Compare(baseline, UISpecExporter.ExportProject())`. The baseline is the last spec
the project was generated from (see Plan 4 for where the baseline is stored —
`Assets/Neo UI Generated/.neo-baseline.json`, committed). If no baseline exists, drift is undefined
and the tools report "no baseline — run export to establish one."

### C. Off-spec lint — `OffSpecLint.cs`

New file: `Assets/Neo UI Framework/Editor/Agent/OffSpecLint.cs`.

This is the hard part: the exporter *can't* see off-spec edits (that's why they're off-spec). So lint
must inspect the live prefab against what the factory would produce. Approach:

1. For each generated view/popup prefab, regenerate its widget subtree **in memory** from the
   baseline spec node via `UIWidgetFactory` (no asset write — build into a temp GameObject, like the
   preview path does).
2. Walk the live prefab and the freshly-built reference in lockstep by child name (the factory's
   names are the contract — `UIWidgetFactory`).
3. Flag divergences that the exporter does **not** capture:
   - Color/material on a factory-owned internal child (not behind `ThemeColorTarget`/`NeoGradient`).
   - Added/removed children inside a widget's internal subtree.
   - Raw `Material` assignment not matching the text-outline match rule (`UISpecExporter.cs:623-635`).
   - Internal geometry changes on factory-owned children.
4. Emit `OffSpecFinding { path, message, fix }` where `fix` is the actionable remedy, e.g.
   *"Color set directly on 'Play/Label' — bind it to a theme token (ThemeColorTarget) so it survives
   regeneration, or move this into the spec."*

Keep lint **advisory** and OUT of `AgentValidation.ValidateAll` (same rule the design lint follows —
`AgentValidation.ValidateDesign`). Surface it through a new validate sub-result `offSpecWarnings`.

### D. Merge — `SpecMerge.cs`

New file: `Assets/Neo UI Framework/Editor/Agent/SpecMerge.cs`.

```csharp
public sealed class MergeResult {
    public UISpec merged;
    public List<SpecChange> applied;     // human changes folded in
    public List<SpecChange> conflicts;   // changed in BOTH human drift and incoming spec
    public List<OffSpecFinding> dropped;  // off-spec edits that cannot be merged (reported, not lost silently)
}

public static class SpecMerge {
    // Three-way merge: base = last generated baseline; ours = human-drifted (exported) project;
    // theirs = incoming spec (e.g. the agent's new spec). Returns ours∪theirs with conflicts flagged.
    public static MergeResult Merge(UISpec baseLine, UISpec ours, UISpec theirs);
}
```

Three-way semantics per node path:
- Changed only in `ours` → take ours (human edit preserved).
- Changed only in `theirs` → take theirs (agent edit applied).
- Changed in both, same value → take either.
- Changed in both, different value → **conflict**: default to `theirs` (agent intent wins on collision)
  but record in `conflicts` so the workflow (Plan 4) can prompt. Make the conflict-winner policy a
  parameter (`ConflictPolicy.PreferTheirs | PreferOurs | Fail`).
- Added in `ours` (human added a node) → keep. Added in `theirs` → keep. Removed in one side, kept
  in other → removal wins only if the other side didn't modify it.

Off-spec findings from lint (C) can never be merged into the spec by definition — they go into
`dropped` so the caller can refuse/warn rather than silently lose them.

### AgentBridge actions

Extend the switch in `AgentBridge.HandleRequest` (`AgentBridge.cs:100-114`):

- `{"action":"diff","baseline":"path.json"}` — exports the project, diffs against `baseline`
  (or the stored `.neo-baseline.json`), returns `{ changes: [...], offSpecWarnings: [...] }`.
- `{"action":"merge","incoming":"new-spec.json","out":"merged.json"}` — three-way merge of stored
  baseline + live project + incoming spec; writes merged spec; returns applied/conflicts/dropped.
- Extend `{"action":"validate"}` to also return `offSpecWarnings` (additive; existing `issues` and
  `designWarnings` unchanged).

Document the new actions in the `AgentBridge` class summary comment (`AgentBridge.cs:15-29`) and in
`CLAUDE.md`'s Agent-workflow action list.

### Menu command (human entry point)

`Tools → Neo UI → Check For Drift` — runs drift + off-spec lint against the stored baseline and
opens a results window listing: changes that will round-trip (green), conflicts (yellow), and
off-spec edits that will be lost (red, with the fix text). A "Fold edits into spec" button runs the
merge and updates the baseline. This is the human-facing safety net before they let an agent regenerate.

## New & modified files

| File | Action |
|------|--------|
| `Editor/Agent/SpecPath.cs` | NEW — stable node addressing helpers |
| `Editor/Agent/SpecDiff.cs` | NEW — structural diff (A) |
| `Editor/Agent/OffSpecLint.cs` | NEW — off-spec edit detection (C) |
| `Editor/Agent/SpecMerge.cs` | NEW — three-way merge (D) |
| `Editor/Agent/AgentBridge.cs` | EDIT — add `diff`/`merge` actions, extend `validate` |
| `Editor/DriftWindow.cs` | NEW — `Tools → Neo UI → Check For Drift` results window (uses NeoGUI) |
| `CLAUDE.md` | EDIT — document new actions + the safety story |

## Edge cases

- **No baseline.** All tools degrade to "establish a baseline first" rather than erroring.
- **Element reorder of id-less siblings** reads as modify pairs (documented limitation v1).
- **Polymorphic `size`** (string variant vs `[w,h]` array — `UISpec.cs:446-448`, `514-516`): diff must
  compare `sizeVariant` and `size` independently, never string-vs-array.
- **Theme `bundle`** is never exported (`UISpec.cs:126-130`); diff/merge operate on expanded tokens,
  not the bundle name. A human can't "drift" the bundle field.
- **Multi-edit / prefab variants:** lint must skip prefab instances and operate on prefab assets.

## Testing

EditMode tests in `Assets/Neo UI Framework/Tests/EditMode/`:

- `SpecDiffTests.cs` — round-trip identity (`Compare(spec, spec)` is empty); single-field change
  detection per section; element add/remove/modify; flow edge changes.
- `SpecMergeTests.cs` — the three-way matrix above; conflict policy variants; `ours`-only and
  `theirs`-only adds both survive; off-spec findings land in `dropped`.
- `OffSpecLintTests.cs` — generate a view, mutate an internal child color in memory, assert a finding
  with the correct `path` and a non-empty `fix`; assert a token-based edit produces NO finding.
- `RoundTripSafetyTests.cs` — generate → mutate exportable layer in prefab → diff shows the change as
  `roundTrips=true` → merge into spec → regenerate → mutation survives.

Follow `CLAUDE.md` build rules: compile-check via Unity Roslyn `csc.dll` while the editor is open;
batch tests only when no editor holds `Temp/UnityLockfile`.

## Acceptance criteria

1. `diff` correctly reports every exportable-layer change a human makes to a generated prefab.
2. `merge` preserves human edits when given a stale incoming spec (the core "no lost work" guarantee),
   with conflicts surfaced not swallowed.
3. `validate` returns `offSpecWarnings` naming every non-round-trippable edit with an actionable fix.
4. `Check For Drift` window shows the three buckets and can fold edits into the spec + update baseline.
5. No change to existing generate/export byte-identical round-trip tests.
