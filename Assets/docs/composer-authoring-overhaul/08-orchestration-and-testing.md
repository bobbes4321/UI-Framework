# Orchestration — Waves, Dependencies, Verification, Risk, Migration, DoD

> Status: retired 2026-07 — the Composer was removed; native authoring supersedes it; see CLAUDE.md.

[← Master plan](00-master-plan.md) · Prev: [07 — Fidelity](07-preview-fidelity.md)

> The operational playbook for orchestrating the waves of parallel worktree agents.

---

## 1. Wave / dependency schedule (authoritative)

Integration branch: `composer-authoring-overhaul` (exists, at
`C:\Users\maxim\RiderProjects\UI-Framework-composer`). Each workstream agent branches a **worktree**
off the latest integration commit, does its work, and the orchestrator merges it back in the stated
order before the next wave starts.

| Wave | Workstream | Owns (high level) | Touches quartet? | Depends on (merged) | Parallel with |
|------|-----------|-------------------|------------------|---------------------|---------------|
| **1** | **A1** | `UISpec.cs` (+`LayoutSpec`, `padding4`), new `LayoutConstraints.cs`, `LayoutSizingModes.cs`, `NeoLayoutTag.cs` | YES | — | none (relay head) |
| **1** | **A2** | `UISpecGenerator.cs`, `UIWidgetFactory.cs` (+`ConstraintLayout`) | YES | A1 | none (relay) |
| **1** | **A3** | `UISpecExporter.cs`, round-trip tests | YES | A2 | none (relay) |
| **1** | **A4** | new `SpecMigration.cs` + menu + tests | NO | A3 | (tail; parallel-safe) |
| **2** | **B-core** | `UISpec.cs`, `UISpecGenerator.cs`, `UISpecExporter.cs`, `SpecPath.cs` (breakpoints), new `UIResponsiveRoot.cs`, `BreakpointConditions.cs` | YES | All A | **C** (file-disjoint) |
| **2** | **C** | `SpecPreviewPane.cs`, `UISpecPreview.cs`, `UIScreenshotter.cs`, new `ComposerDevicePresets.cs` | NO | All A; B-core's `IActiveBreakpoint` iface | **B-core** |
| **2** | **B-ui** | `NeoComposerWindow.cs`, `SpecDocument.cs`, new `BreakpointBar.cs` | NO | B-core | (tail after B-core) |
| **3** | **D** | `ComposerCanvas.cs`, new `ConstraintWriteback.cs`, `AlignmentGuides.cs` | NO | All A; (C for live-resize) | **F** (file-disjoint) |
| **3** | **F** | `SpecInspector.cs`, `SpecFieldCatalog.cs`, new `NeoConstraintWidget.cs` (EditorUI) | NO | All A; B (override scope) | **D** |
| **4** | **E** | new `ComposerPalette.cs`, `ComposerTemplates.cs`, `PalettePane.cs`, `Templates~/*`; additive edits to `NeoComposerWindow.cs`/`ComposerCanvas.cs`/`SpecTreeView.cs` | NO | All A, D | (single in wave) |
| **5** | **G** | `SpecPreviewPane.cs`, `SpecDocument.cs`, new `PreviewSampleData.cs` | NO | A–C | (single in wave) |
| **5** | **GATE** | full EditMode+PlayMode suite (editor closed) + `specReference` regen | — | everything | — |

### Merge-gate rules
- **Wave 1 is the hard gate.** Nothing in Wave 2+ starts until A1→A2→A3 (and ideally A4) are merged,
  the branch compiles via the Roslyn relay, AND `ConstraintLayoutRoundTripTests` +
  `SpecLayoutAndWidgetTests` are green (run when the editor is closed, or compile-verified + logic-
  reviewed if the editor must stay open).
- **A1/A2/A3 are a SERIAL relay** — same quartet files; each agent starts from the prior merge. Do
  NOT spawn them simultaneously on the same files.
- **Within Wave 2, B-core and C run in parallel** because they are file-disjoint (B = quartet+
  `SpecPath`+new runtime; C = preview/screenshot + new presets). B-core ships the `IActiveBreakpoint`
  interface FIRST (commit it early so C can compile against it). B-ui is a tail after B-core.
- **Within Wave 3, D and F run in parallel** (canvas vs inspector/catalog/EditorUI-widget), with the
  shared-type ownership split: D owns `ConstraintWriteback.cs`, F owns `NeoConstraintWidget.cs`;
  neither edits the other's file.
- **Wave 4 (E) and Wave 5 (G)** are single-workstream waves — no intra-wave contention.

### The quartet contention map (the thing that breaks parallel work)
Core quartet = `UISpec.cs`, `UISpecGenerator.cs`, `UISpecExporter.cs`, `UIWidgetFactory.cs`. Plus the
diff/merge-critical `SpecPath.cs`. Edits to these are serialized across the WHOLE plan:
- Wave 1: A1 (UISpec), A2 (generator+factory), A3 (exporter) — relay.
- Wave 2: B-core (UISpec + generator + exporter + SpecPath) — the ONLY Wave-2 quartet editor; C is
  disjoint. So Wave 2 has at most one quartet editor at a time → safe.
- Waves 3–5: **zero quartet edits** (all Composer/EditorUI). The only flagged exception is `padding4`
  (Pillar F), which is **folded into A1** to keep it out of Wave 3. If it slips, it becomes a tiny
  gated mini-task before Wave 3, never a parallel quartet edit.

---

## 2. Per-task verification — the Roslyn relay (editor stays open)

The Unity editor is OPEN on the main checkout (`C:\Users\maxim\RiderProjects\UI-Framework`,
`Temp/UnityLockfile` present, `Unity.exe` running). **Agents must never batch-compile or kill it.**
A worktree has NO `Library/ScriptAssemblies`, so compile-check by invoking Unity's bundled Roslyn,
referencing the OPEN checkout's `Library/ScriptAssemblies` (NOT the worktree's).

The template is the committed `neo-compilecheck.ps1` (at the worktree root). **Each worktree must set
`$Proj` to the OPEN checkout** so the module DLLs/nunit resolve:
```powershell
# In the worktree, point $Proj at the OPEN main checkout (which HAS Library/ScriptAssemblies):
$Proj = "C:\Users\maxim\RiderProjects\UI-Framework"
$Pkg  = "<worktree>\Assets\Neo UI Framework"   # but compile the WORKTREE's sources
```
> NOTE: `neo-compilecheck.ps1` currently hardcodes `$Proj = "C:\_git\UI Package"` and uses `$Pkg`
> derived from it. Fix-up task (do once per worktree, do NOT commit the local edit): set `$Proj` to
> the main checkout for the reference DLLs, and `$Pkg` to the **worktree's** package dir for the
> sources to compile. The reference set itself (engine `*Module.dll` minus `UnityEditor.dll`/
> `UnityEngine.dll`, `netstandard 2.1` ref, `NetStandard/compat/2.1.0/shims/netfx/*.dll`, module DLLs
> `UnityEngine.UI`/`Unity.TextMeshPro`/`Unity.InputSystem`/`UnityEditor.UI`/`UnityEngine.TestRunner`/
> `UnityEditor.TestRunner`, nunit from PackageCache `net40/unity-custom`) is already correct — see
> `neo-compilecheck.ps1:16-64`.

Build order (already encoded): **Runtime → EditorUI (engine-only, proves the kit stays Neo.UI-free)
→ Editor (refs Runtime + EditorUI) → EditMode tests (output name MUST be `Neo.UI.Tests.EditMode` for
InternalsVisibleTo) → PlayMode tests.** Per workstream:
- **Quartet/runtime workstreams (A, B-core):** run `-Target all` (Runtime+Editor+tests).
- **EditorUI control (F's `NeoConstraintWidget`):** run the **EditorUI-alone** step
  (`neo-compilecheck.ps1` "EditorUI kit") to prove zero Neo.UI references, then `-Target editor`.
- **Composer-only workstreams (C, D, E, F, G):** run `-Target editor` (+ `tests` for added EditMode
  tests). PlayMode tests only *compile* here; they RUN at the gate.

**MEMORY pointer:** the user's auto-memory has `roslyn-compile-check-worktree.md` with the exact
reference set and InternalsVisibleTo naming — consult it; it matches the above.

---

## 3. Testing strategy

### 3.1 New tests per pillar (all under `Tests/EditMode` unless noted)
- **A:** `LayoutSpecParseTests` (pure parse/emit), `ConstraintLayoutRoundTripTests`
  (export→generate→export byte-identical per constraint×axis + legacy presets),
  `SpecMigrationTests` (each of 16 presets migrates to pixel-identical generation),
  `ConstraintResponsivenessTests` (EditMode: resize parent, `ForceRebuildLayoutImmediate`, assert
  element rect tracks its constraint — the "doesn't disappear" proof). Add cases to
  `SpecLayoutAndWidgetTests`.
- **B:** `BreakpointRoundTripTests`, `BreakpointCascadeTests` (base+delta merge),
  `ResponsiveDriverTests` (PlayMode: simulate resize/orientation, assert driver applies the right
  override only on change). Add a `SpecPath` test for breakpoint-by-name keying (extend an existing
  `SpecDiffTests`/`SpecMergeTests`).
- **C:** `DevicePresetRegistryTests` (registry defaults + register-replace).
- **D:** `ConstraintWritebackTests` (rect+parent+constraint ⇄ offsets round-trips),
  `AlignDistributeTests` (operate on specs).
- **E:** `PaletteRegistryTests`, `TemplateInsertTests` (insert template → generates + round-trips).
- **F:** `InspectorFieldTests` (catalog returns new fields; get/set round-trips).
- **G:** `PreviewSampleDataTests`.

### 3.2 Round-trip byte-identity (the sacred invariant)
- Every new spec field gets a deterministic `ToJsonObject` position in `UISpec.cs` and a matching
  exporter read. The round-trip test pattern: build a `UISpec`, `ToJson`, generate prefabs, export,
  `ToJson` again, assert string equality. Mirror `SpecLayoutAndWidgetTests`.
- `NeoLayoutTag` (A) and `UIResponsiveRoot` (B) are the markers that make reverse-detection
  deterministic — without them, anchor/offset aliasing breaks byte-identity. Tests must cover the
  marker-present (new model) AND marker-absent (legacy) paths.

### 3.3 Behavior tests (CLAUDE.md "renders fine but does nothing")
- B's `ResponsiveDriverTests` is the key behavior test (a layout that adapts must actually adapt at
  runtime). A's `ConstraintResponsivenessTests` covers the static-generation responsiveness.
- Run `{"action":"validate"}` after any generate during development (dead-interaction lint).

### 3.4 The gated full-suite run (Wave 5 GATE)
- Full `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode|PlayMode -projectPath .` is
  only possible when **NO editor holds the lock**. This is a human-gated step (close the editor, or
  run on a CI machine). The plan's per-wave verification is Roslyn compile + targeted EditMode tests;
  the GATE is the authoritative pass.
- After the gate: regenerate `specReference` (`{"action":"specReference"}` → `spec-reference.md` +
  `neo-spec.schema.json`) so the new `layout`/`overrides`/`breakpoints`/`padding4` fields are
  documented and schema-validated.
- Regenerate the committed ColorACube demo only if migration was applied to it (it is NOT, by
  default — see Migration §5); if applied, use the headless `AgentBridge.RunBatch` generate+buildScene
  recipe from CLAUDE.md and verify the `NeoUIGeneratedDemo` scene + `SceneBuilderFlowScopingTests`.

---

## 4. Risk register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| **Round-trip breakage** from new fields | Med | High | `NeoLayoutTag`/`UIResponsiveRoot` markers for deterministic detection; byte-identity tests cover both new + legacy paths; legacy fields untouched when `layout` absent. |
| **Committed demo/starter break** (GUID-referenced) | Med | High | Legacy path stays byte-identical (A3 test asserts ColorACube zero-diff); migration is OPT-IN, never silent; do NOT migrate the committed demo by default. |
| **Merge conflicts across worktrees** | High (quartet) | High | Quartet edits serialized (relay A1→A2→A3; B-core sole Wave-2 quartet editor; Waves 3–5 zero quartet). `padding4` folded into A1. Single `SpecPath` edit (B-core) is isolated. File-ownership tables per sub-plan are binding. |
| **Editor-open compile constraint** (no Library in worktree) | Certain | Med | Roslyn relay referencing the OPEN checkout's `ScriptAssemblies`; never batch-compile/kill the editor. PlayMode tests only compiled per-wave, run at the gate. |
| **Force-expand is group-level not per-child** (Fill sizing) | Certain | Low | Documented OR-across-children behavior; per-child fill still works via `flexibleWidth=1`. |
| **Anchor reverse-detection aliasing** (`center` vs equal-inset `leftRight`) | Med | High | `NeoLayoutTag` marker disambiguates; exporter never relies on raw anchor reverse-lookup for the new model. |
| **Runtime driver perf** (per-frame layout) | Low | Med | Apply only on breakpoint *change*, only to elements with overrides, pre-resolved values (no runtime spec parsing). |
| **CanvasScaler changes agent renders** | Med | Med | Scaler gated behind `RenderOptions.deviceScale`; agent matrix keeps no-scaler default; acceptance render verified unchanged. |
| **EditorUI kit gains a Neo.UI dependency** (constraint widget) | Med | Med | `NeoConstraintWidget` operates on a kit-local POD; verified by the EditorUI-alone compile step. |
| **Reorder shows as modify-pairs in diff** (id-less elements) | Certain | Low | Pre-existing documented `SpecPath` v1 limitation; unchanged by this work. |

---

## 5. Migration

- **Default: do NOT migrate existing committed specs.** The legacy `anchor`/`position`/`size`/`flex`
  path stays fully supported and byte-identical (A3 guarantees). The 16 anchor presets keep working
  via `AnchorPresets`/`TryApplyAnchor`/`DetectAnchor` (untouched). ColorACube + starter kit continue
  to generate identical prefabs.
- **Opt-in migration (A4):** `Tools → Neo UI → Migrate Spec To Layout Model` rewrites a chosen spec's
  legacy fields into the equivalent `layout` (preset→constraint map in master §2.A). Proven
  pixel-identical by `SpecMigrationTests`; idempotent. Use it when an author *wants* the richer model
  on an old spec. Never runs automatically.
- **New specs** authored in the overhauled Composer use `layout` from the start (templates ship with
  it).
- **No one-time mass migration is required** for the codebase to function — it is purely an author
  convenience.

---

## 6. Definition of done (whole effort)

- [ ] **A:** constraint+offset model + per-child sizing; round-trips byte-identical (new + legacy);
      elements survive aspect/orientation change; 16 presets re-expressible; opt-in migration proven.
- [ ] **B:** breakpoint/override system; runtime driver adapts live on resize/orientation; cascade
      correct; round-trips; `SpecPath` keys breakpoints by name; Composer authors + previews them.
- [ ] **C:** free viewport — device-preset registry + custom W/H + rotate + free-drag + zoom/fit +
      live readout + CanvasScaler device scaling; agent render paths unchanged.
- [ ] **D:** constraint-aware writeback; drag-to-reorder; smart guides + equal-spacing; align/
      distribute; keyboard nudge/dup/delete; selection survives viewport resize.
- [ ] **E:** searchable categorized palette (incl. project kinds); drag-to-create onto canvas/tree;
      curated templates; registry seams.
- [ ] **F:** Figma-style constraint widget (EditorUI, Neo.UI-free); Fixed/Hug/Fill dropdowns;
      auto-layout panel; breakpoint override editing; cached IMGUI.
- [ ] **G:** live theme recolor + sample data in bound lists; (optional) flow playback; polish.
- [ ] Every new fixed set is a documented seam (`LayoutConstraints`, `LayoutSizingModes`,
      `BreakpointConditions`, `ComposerDevicePresets`, `ComposerPalette`, `ComposerTemplates`).
- [ ] No silent failures; WYSIWYG (base bakes); spec stays the single source of truth.
- [ ] **GATE:** full EditMode+PlayMode suite green (editor closed); `specReference` regenerated;
      ColorACube + starter kit unchanged (or, if intentionally migrated, regenerated + scene verified).
- [ ] All work merged to `composer-authoring-overhaul`; CLAUDE.md spec-field section updated for the
      new `layout`/`overrides`/`breakpoints`/`padding4`/sizing fields.
