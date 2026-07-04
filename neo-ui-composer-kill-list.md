# Composer Kill-List — Inventory for Wave 3 (Task 2.5 output)

Produced read-only, after Wave 2 Tasks 2.1–2.4 landed (rehomed registries/templates to
`Editor/Authoring`+`Editor/Agent`, `PresetPickerPopup` + thumbnail cache/renderer rehomed to
`Editor/Authoring`/`Editor/Inspectors`, native breakpoint-override authoring added, `MenuCatalogInspector`
added). Every file below is classified by grepping the WHOLE repo (outside `Editor/Composer/`) for real
code references to each type — comment-only mentions ("the (doomed) Composer's X") are NOT counted as
references. Where a doc-comment already told the story correctly, that's noted; where grep contradicts
the plan's stated expectation, that's called out loudly.

**NOTE (orchestrator, post-Wave-3):** this file originally lived at `Temp/composer-kill-list.md` and was
lost between Wave 2 and Wave 3 — Unity's batchmode runs appear to churn/clean files under `Temp/` across
invocations, so it is NOT durable storage for cross-wave coordination artifacts. Restored here verbatim
from the orchestrator's own conversation record for the permanent record. All items below were already
acted on in Wave 3 Task 3.1 (which independently re-verified every claim via fresh greps before trusting
it, since the source file had vanished by the time it ran). Future coordination docs go at the repo root
or the session scratchpad, never under `Temp/`.

## TL;DR for the orchestrator — 3 things Wave 3 cannot skip

1. **`SpecFieldCatalog` cannot be blanket-deleted.** The `SpecField` class and `FieldKind` enum it
   declares are the return type of `INeoElementKind.Fields` — a member of the keystone extensibility
   interface declared in the SURVIVOR file `Editor/Agent/NeoElementKinds.cs` (and implemented by the
   `ProbeKind` test helper in the SURVIVOR test `NeoElementKindsTests.cs`). Deleting the whole file
   verbatim breaks the build of the extensibility seam itself. See §2 "REHOME" below for the required
   split.
2. **`ComposerDevicePresets` cannot be deleted — contradicts the plan's own "expected DELETE" note.**
   `Editor/Agent/UISpecPreview.cs` (survivor; backs the agent `preview`/`screenshot` bridge actions)
   derives `DefaultResolutions` directly from `ComposerDevicePresets.All`. `DevicePresetRegistryTests.cs`
   (on the plan's expected-doomed test list) actually spends 2 of its 7 tests proving that derivation.
   Verified false: the resolution matrix does NOT already live in `UIScreenshotter` independently of the
   Composer.
3. **`ComposerFactory` and `SpecDocument`/`NeoLayoutTemplates.Insert(SpecDocument,…)` are real
   production dependencies of survivor files that Tasks 2.1–2.4 left unrehomed.** `ComposerFactory` is
   called from `NeoSceneAuthoring.CreateWidget` (production) and two survivor tests. The
   `Insert(SpecDocument,…)` overload on `NeoLayoutTemplates` (Editor/Authoring, a survivor file) only has
   one real caller — the doomed `NeoComposerWindow.cs` — but 6 of `TemplateInsertTests.cs`'s 7 tests
   exercise it via `SpecDocument`, so deleting `SpecDocument.cs` requires also deleting that overload and
   pruning those 6 tests, keeping only `NativeInsertTemplate_BuildsElementTreeUnderSelectedView`.

---

## 1. Files under `Editor/Composer/` remaining after Wave 2 Tasks 2.1–2.4

(Confirmed via `find`; the `PresetPickerPopup.cs`/`.meta`, `PresetThumbnailCache.cs`,
`PresetThumbnailRenderer.cs`, `ComposerPalette.cs`→`NeoWidgetPalette.cs`,
`ComposerCatalogKinds.cs`→`NeoCatalogKinds.cs`, `ComposerOptions.cs`→`NeoWidgetOptions.cs`,
`ComposerTemplates.cs`→`NeoLayoutTemplates.cs`, `SpecMigration.cs`, and `Templates~/*.json` files are
ALREADY gone/moved per current `git status` — not listed again here.)

### Top-level `.cs` (21 files)

| File | Class(es) | External (non-Composer, non-doomed-test) references | Verdict |
|---|---|---|---|
| `AlignDistribute.cs` | `AlignDistribute`, `AlignDistribute.Op` | none (only `AlignDistributeTests.cs`, doomed) | **DELETE** |
| `AlignmentGuides.cs` | `AlignmentGuides`, `Guide`, `Pip` | none | **DELETE** |
| `BreakpointBar.cs` | `BreakpointBar` | none real — `NeoSceneAuthoring.cs`/`NeoSceneOverlay.cs`/`NativeBreakpointAuthoringTests.cs` only mention it in **doc comments** ("native parity for the (doomed) Composer's BreakpointBar") | **DELETE** |
| `ComposerCanvas.cs` | `ComposerCanvas`, `ElementBox` | none real — `NeoElementKinds.cs` mentions `ComposerCanvas.IsContainerKind` only in a **doc comment** | **DELETE** |
| `ComposerDevicePresets.cs` | `ComposerDevicePresets`, `DevicePreset` | **REAL**: `Editor/Agent/UISpecPreview.cs` (`DefaultResolutions` derives from `ComposerDevicePresets.All`) — a survivor consumed by the agent `preview`/`screenshot` bridge actions | **REHOME** (to `Editor/Agent/`, alongside `UISpecPreview`, or wherever the agent render-matrix code lives) — plan's "expected DELETE" is **wrong**, see TL;DR #2 |
| `ComposerFactory.cs` | `ComposerFactory` | **REAL**: `Editor/Authoring/NeoSceneAuthoring.cs` (`ComposerFactory.NewElement` — production, `CreateWidget`), `NativeAuthoringRoundTripTests.cs`, `NativeCaptureTests.cs` | **REHOME** — the file's own doc comment already says "promoted to the `Neo.UI.Editor` root namespace when the Composer retires"; this is 2.1–2.4 leftover work, not new scope |
| `ComposerFlowBridge.cs` | `ComposerFlowBridge` | none (verified `Editor/Flow/` doesn't reference it either) | **DELETE** |
| `ConstraintWriteback.cs` | `ConstraintWriteback` | none real (only `AlignDistributeTests.cs`/`ConstraintWritebackTests.cs`, both doomed) | **DELETE** |
| `MenuCatalogEditor.cs` | `MenuCatalogEditor` | none real — `Editor/Inspectors/MenuCatalogInspector.cs` + its test mention it only in **doc comments** ("the standing replacement for the Composer's doomed `MenuCatalogEditor` pane") | **DELETE** — 2.4 already built its native replacement |
| `NeoComposerWindow.cs` | `NeoComposerWindow` | **REAL**: `Editor/Showcases/HubToolRegistry.cs` (Hub tool `id="composer"`, `invoke = Composer.NeoComposerWindow.Open`), `Editor/Showcases/NeoUIHubWindow.cs` ("Edit in Composer" button, `Composer.NeoComposerWindow.Open(s)`), `ComposerProbeTests.cs` (doomed) | **DELETE the class**, but Wave 3 MUST also remove/replace the two call sites above (see §3) |
| `PalettePane.cs` | `PalettePane` | none (only `NeoComposerWindow.cs`) | **DELETE** |
| `PreviewFlowPlayback.cs` | `PreviewFlowPlayback` | none real (only `PreviewFlowPlaybackTests.cs`, doomed) | **DELETE** |
| `PreviewSampleData.cs` | `PreviewSampleData` | none real — confirmed `PresetThumbnailRenderer.cs` (in `Editor/Inspectors/`, the rehomed survivor) does NOT use it; only `SpecPreviewPane.cs` (doomed) and `PreviewSampleDataTests.cs` (doomed) do | **DELETE** — audit D4's token-scanner duplication resolves by deletion, as the task predicted |
| `SpecDocument.cs` | `SpecDocument`, `SpecDocument.DocumentOrigin` | **REAL**: `Editor/Authoring/NeoLayoutTemplates.cs.Insert(SpecDocument,…)` — but that overload's ONLY real caller is `NeoComposerWindow.cs` (doomed); its other caller is `TemplateInsertTests.cs` (survivor test file, 6/7 tests use it) | **DELETE `SpecDocument.cs`**, but Wave 3 must ALSO delete the `Insert(SpecDocument,…)` overload + its `using Neo.UI.Editor.Composer;` import from `NeoLayoutTemplates.cs`, and prune `TemplateInsertTests.cs` down to just `NativeInsertTemplate_BuildsElementTreeUnderSelectedView` (see TL;DR #3) |
| `SpecFieldCatalog.cs` | `SpecFieldCatalog`, `SpecField`, `FieldKind` | **REAL**: `Editor/Agent/NeoElementKinds.cs` — `INeoElementKind.Fields` is typed `IEnumerable<SpecField>`; `NeoElementKindsTests.cs`'s `ProbeKind.Fields` implements it using `new SpecField(..., FieldKind.Text, ...)` | **SPLIT, do not blanket-delete** — rehome the `SpecField` class + `FieldKind` enum (pure data types, zero Composer dependency) to a survivor location (e.g. into `NeoElementKinds.cs` or a new small file beside it in `Editor/Agent/`); the `SpecFieldCatalog` static class itself (the giant per-kind field table + `For`/`AllKeys`/`RegisterField` — only ever consumed by the doomed `SpecInspector.cs`) is a clean **DELETE**. Also prune `NeoElementKindsTests.cs` (see §4) and `ComposerCatalogKindsTests.cs` (see §4). See TL;DR #1 — **or**, alternatively, the orchestrator may decide to retire `INeoElementKind.Fields` itself as a seam now that there's no generic inspector left to feed (a real capability call, not mine to make) |
| `SpecInspector.cs` | `SpecInspector` | none real — `NeoSceneAuthoring.cs`/`NeoSceneOverlay.cs`/`NativePresetWorkflowTests.cs` mention it only in **doc comments** | **DELETE** — 2.2 already ported its preset workflow natively |
| `SpecPreviewPane.cs` | `SpecPreviewPane` | none real — `PresetThumbnailRenderer.cs` mentions it only in a **doc comment** ("the Composer's `SpecPreviewPane` use") | **DELETE** |
| `SpecTreeView.cs` | `SpecTreeView`, `SpecNodeKind` | **REAL, but narrow**: `ComposerCatalogKindsTests.cs` (class `NeoCatalogKindsTests`, a SURVIVOR test file for `NeoCatalogKinds`) has exactly one test, `AddPicker_OptionsEqual_All`, calling `SpecTreeView.CatalogKindLabels()`/`CatalogKindIdForLabel()` | **DELETE the class**; Wave 3 must delete just that one test method from `ComposerCatalogKindsTests.cs` (rest of the file tests `NeoCatalogKinds` itself and survives) |
| `ThemePaletteEditor.cs` | `ThemePaletteEditor` | none | **DELETE** |
| `TreeDrag.cs` | `TreeDrag`, `TreeDrag.Zone` | none real (only `TreeDragTests.cs`, doomed) | **DELETE** |

### `Automation/` (7 `.cs` files + `Scenarios~/`)

| File | Class(es) | External references | Verdict |
|---|---|---|---|
| `ComposerDriver.cs` | `ComposerDriver` | none | **DELETE** |
| `ComposerProbe.cs` | `ComposerProbe` | `Editor/Agent/AgentBridge.cs` (`HandleComposerSession`, dies with it), `ComposerProbeTests.cs`/`ComposerScenarioParseTests.cs` (doomed) | **DELETE** |
| `ComposerProbeActions.cs` | `ComposerProbeActions` | `ComposerScenarioParseTests.cs` only (doomed) | **DELETE** |
| `ComposerProbeMetrics.cs` | `ComposerProbeMetrics` | none (only `NeoComposerWindow.cs`) | **DELETE** — matches plan's expectation |
| `ComposerScenario.cs` | `ComposerScenario`, `ScenarioStep` | `AgentBridge.cs` (dies with the handler), doomed tests | **DELETE** |
| `SessionReport.cs` | `SessionReport`, `StepRecord` | `AgentBridge.cs` (dies with the handler), `ComposerProbeTests.cs` (doomed) | **DELETE** |
| `WindowCapture.cs` | `WindowCapture` | none | **DELETE** |
| `Scenarios~/*.json` (7 files) + `README.md` | data | only referenced by path inside doomed Composer/Automation code and CLAUDE.md's `composerSession` doc bullet; no test loads them by path (scenarios are built inline in the doomed test files) | **DELETE** (whole `Automation/` + `Scenarios~/` tree, plus the `Automation.meta`) |

---

## 2. Judgement calls the task named explicitly — resolved

- **`SpecFieldCatalog`**: NOT a clean DELETE as the task's stated heuristic ("if only `SpecInspector`
  consumes it ⇒ DELETE") predicted — see TL;DR #1 and the table row above. The heuristic's premise is
  false: `SpecField`/`FieldKind` (declared in the same file) are consumed by the survivor
  `INeoElementKind.Fields` interface member. Split required.
- **`PreviewSampleData`**: confirmed DELETE — neither `PresetThumbnailRenderer.cs` nor anything under
  `Editor/Agent/` references it; its only consumer is the doomed `SpecPreviewPane.cs`. Audit D4's
  token-scanner duplication resolves by deletion, exactly as anticipated.
- **`ComposerFactory`**: real external reference found (`NeoSceneAuthoring.cs` + 2 survivor tests) →
  **REHOME**, not evaluated as DELETE by the task text (the task only flagged it as "a judgement call to
  resolve by grep" without a stated expectation).
- **`ComposerDevicePresets`**: task said "expected DELETE — the agent `preview` action's resolution
  matrix lives in `UIScreenshotter`, not here; verify." **Verified false** — see TL;DR #2. → **REHOME**.
- **`ComposerProbeMetrics`**: no external references found → **DELETE**, consistent with (the task named
  it without an explicit expectation, but DELETE is the only defensible read).
- **`Editor/EditorUI/NeoConstraintWidget.cs`**: grepped repo-wide; its only consumer is
  `Editor/Composer/SpecInspector.cs` (plus two files under `Assets/docs/composer-authoring-overhaul/`,
  which are historical planning docs, not code) → **DELETE**, confirmed by grep exactly as the task
  expected.

---

## 3. Non-Composer-folder call sites that reference doomed Composer surface (must change in Wave 3)

- **`Editor/Agent/AgentBridge.cs`** — the `composerSession` handler + registration:
  - line 120: `composerSession` in the `mutatesAssets` play-mode guard list
  - line 143: `case "composerSession": HandleComposerSession(request, result); break;`
  - line 146: `composerSession` listed in the "Unknown action" error message's action list
  - lines 169–199: the `HandleComposerSession` method itself (and its preceding doc comment)
- **`Editor/Composer/NeoComposerWindow.cs:38`** — `[MenuItem("Tools/Neo UI/Composer", priority = 11)]`
  (dies with the file, but flagging the exact menu path for Task 3.2's doc sweep)
- **`Editor/Showcases/HubToolRegistry.cs:77-83`** — the `"composer"` `HubTool` entry
  (`invoke = Composer.NeoComposerWindow.Open`), category `Author`
- **`Editor/Showcases/NeoUIHubWindow.cs:366-372`** — the per-showcase card's "Edit in Composer" button
  (`Composer.NeoComposerWindow.Open(s)`)
- **`Editor/Authoring/NeoLayoutTemplates.cs`** — the `Insert(SpecDocument,…)` overload (only real caller
  is the doomed window) + its `using Neo.UI.Editor.Composer;` import — must be deleted alongside
  `SpecDocument.cs` (see TL;DR #3)
- **`Editor/Authoring/NeoSceneAuthoring.cs`** and **`Editor/Agent/UISpecPreview.cs`** — keep their
  `using Neo.UI.Editor.Composer;` imports pointed at wherever `ComposerFactory`/`ComposerDevicePresets`
  land after rehoming (i.e. just update the `using` to the new namespace, not remove the dependency)

## 4. Tests that reference doomed Composer types

### Confirmed fully-doomed (whole file deletes cleanly — 11 of the 12 the task named)

`ComposerProbeTests.cs`, `ComposerScenarioParseTests.cs`, `SpecDocumentTests.cs`, `SpecTreeViewTests.cs`,
`TreeDragTests.cs`, `AlignDistributeTests.cs`, `ConstraintWritebackTests.cs`, `InspectorFieldTests.cs`,
`PreviewFlowPlaybackTests.cs`, `PreviewSampleDataTests.cs`, `SpecFieldCatalogTests.cs` — each imports
`Neo.UI.Editor.Composer` and every test method in the file exercises a class that is DELETEd above (no
survivor logic mixed in). **DELETE all 11 whole files.**

### NOT fully doomed — the 12th name on the task's list, plus 3 more the task didn't name

- **`DevicePresetRegistryTests.cs`** (on the task's expected-doomed list) — **WRONG**, do not delete
  outright. 5 of 7 tests (`All_ShipsTheBuiltInSpread`, `Builtins_ListedInRegistrationOrder_LegacyTrioFirst`,
  `PhonePortrait_MatchesLegacyDimensions`, `TryGet_UnknownId_ReturnsFalse`,
  `Register_AppendsNovelPreset_ThenReplacesByIdInPlace`) test `ComposerDevicePresets` directly and should
  move wherever it's rehomed; the other 2 (`DefaultResolutions_DeriveFromTheRegistry`,
  `DefaultResolutions_ReflectANewlyRegisteredPreset`) test the survivor `UISpecPreview.DefaultResolutions`
  and must be kept regardless. **Whole file survives, rehome with `ComposerDevicePresets`.**
- **`NeoElementKindsTests.cs`** (keystone seam test, NOT on the task's doomed list — a real survivor) —
  one nested helper (`ProbeKind.Fields`, lines ~31-35) and one test's three assertions (lines ~76, 130-134
  in `Register_NewKind_ReachesEveryConsumerSite` or similar) use `SpecFieldCatalog`/`SpecField`/
  `FieldKind`. Once `SpecFieldCatalog` (the static class) is deleted, prune just the
  `SpecFieldCatalog.ClearRegisteredForTests()`/`.ElementKinds`/`.For(...)` lines from this file; keep the
  `SpecField`/`FieldKind` usage in `ProbeKind.Fields` (those types survive per §1).
- **`ComposerCatalogKindsTests.cs`** (class `NeoCatalogKindsTests`, NOT on the task's doomed list — a
  real survivor testing the rehomed `NeoCatalogKinds`) — exactly one test,
  `AddPicker_OptionsEqual_All`, calls `SpecTreeView.CatalogKindLabels()`/`CatalogKindIdForLabel()`. Delete
  just that test method when `SpecTreeView` dies; the rest of the file survives.
  *(Aside, not in scope to fix here: this file is still named `ComposerCatalogKindsTests.cs` even though
  its class was renamed `NeoCatalogKindsTests` in Task 2.1 — a leftover rename the orchestrator may want
  to `git mv` to `NeoCatalogKindsTests.cs` while touching the file anyway.)*
- **`TemplateInsertTests.cs`** (NOT on the task's doomed list — a real survivor testing
  `NeoLayoutTemplates`) — 6 of 7 tests (`Builtins_AreRegistered` even doesn't use SpecDocument, only
  `Template_InsertsIntoEmptySpec_AndGenerates`, `Template_RoundTripsByteIdentical`,
  `Insert_NameCollision_SuffixesAndWarns_NeverOverwrites`, `Register_ReplacesById` do) use `SpecDocument`
  + `NeoLayoutTemplates.Insert(SpecDocument,…)`. Only `NativeInsertTemplate_BuildsElementTreeUnderSelectedView`
  exercises the surviving native path. See TL;DR #3 — Wave 3 needs a deliberate decision here: either (a)
  delete the `SpecDocument`-based tests and rely on `NativeInsertTemplate_...` as the sole coverage for
  `NeoLayoutTemplates.All`/`TryGet`/`Register`/collision-suffix behavior (losing direct coverage of that
  logic unless the native test is extended to cover collision-suffixing too), or (b) port the
  `SpecDocument`-based assertions onto a lightweight non-Composer harness before deleting `SpecDocument`.
  Flagging for the orchestrator rather than picking, since it trades off test coverage.

  **Orchestrator resolution (Wave 3):** chose (b) — Task 3.1 promoted the merge/collision-suffix logic to
  a new `NeoLayoutTemplates.Insert(UISpec, TemplateEntry, out List<string>)` overload and rewrote the 3
  SpecDocument-dependent tests against it. All 7 tests' coverage survives.
- **`NativeAuthoringRoundTripTests.cs`**, **`NativeCaptureTests.cs`** — both real survivors that import
  `Neo.UI.Editor.Composer` solely for `ComposerFactory.NewElement(...)`. No test logic needs to change,
  only the `using` once `ComposerFactory` is rehomed (Wave 3: rehomed as `Editor/Agent/SpecFactory.cs`,
  class renamed `ComposerFactory` → `SpecFactory`).

---

## 5. CLAUDE.md / `Assets/docs/*` sections documenting the Composer or `composerSession`

### `CLAUDE.md` (repo root) — for Task 3.2

- **Lines 271–289**: the whole `{"action":"composerSession",...}` bullet under "Build & test workflows"
  (the agent bridge actions list) — describes the probe, `Automation/` layout, scenario seam, gated
  metrics, and its tests. Delete/replace entirely.
- **Line 283**: also documents `Editor/Composer/Automation/` module names
  (`ComposerProbe`/`ComposerDriver`/`WindowCapture`/`ComposerScenario`/`ComposerProbeActions`/
  `SessionReport`/`ComposerProbeMetrics`) — all dead once §1 above lands.
- **Lines 458–480**: the entire `- The **Composer** (...)` bullet describing the window, palette, free
  viewport, canvas, constraint inspector, breakpoint authoring, preset picker, live preview. Delete
  entirely (native authoring's bullets, lines ~418-457, already cover the superseding functionality).
- **Stale even today** (pre-existing drift, not caused by this task, but Task 3.2 should fix while it's
  in there): line 462 says "`ComposerPalette` registry" and line 465 says "`ComposerTemplates`,
  `Editor/Composer/Templates~/*`" — both were already renamed to `NeoWidgetPalette`
  (`Editor/Authoring/NeoWidgetPalette.cs`) and `NeoLayoutTemplates`
  (`Editor/Authoring/NeoLayoutTemplates.cs`, `Templates~` under `Editor/Authoring/`) by Task 2.1, so these
  two mentions in the doomed Composer bullet are already wrong even before Wave 3 deletes the bullet.
- Line 342 ("Responsive layout (Composer overhaul, `composer-authoring-overhaul`)") and line 426
  ("rehomed off the Composer in Wave 2 Task 2.1") are historical/narrative mentions of the word
  "Composer" that do NOT document current Composer surface — leave as-is (they're about
  `NeoElementKinds`/`NeoWidgetPalette`, survivors).
- Line 444 ("supersede the Composer once at parity") is the one sentence in the Native-Unity authoring
  bullet that should be updated/removed once Wave 3 actually deletes the Composer (parity achieved →
  retired, not "once at parity").

### `Assets/docs/*`

- `Assets/docs/spec-reference.md`, `Assets/docs/neo-spec.schema.json`, `Assets/docs/editor-ux-analysis.md`,
  `Assets/docs/neo-ui-package-feature-spec.md`, `Assets/docs/ui-beautification-plan.md` — grepped, **zero**
  mentions of "Composer". Nothing to change in the actively-synced/generated docs.
- The following are **historical planning documents**, entirely about the Composer's now-abandoned
  authoring-overhaul project; none are referenced by the docs-sync Stop hook (that hook only tracks
  CLAUDE.md + `SpecReference.cs` + the generated spec-reference/schema per CLAUDE.md's docs-sync rule).
  Flagging for the orchestrator to decide whether to archive/delete or leave as historical record — no
  code depends on them:
  - `Assets/docs/composer-authoring-overhaul/00-master-plan.md` through `08-orchestration-and-testing.md`
    (9 files, entirely about the Composer)
  - `Assets/docs/composer-catalog-unification-plan.md`
  - `Assets/docs/human-workflow-plans/02-spec-authoring-window.md` (and passing mentions in
    `01-roundtrip-safety.md`, `04-agent-human-collaboration-protocol.md`, `README.md`)
  - `Assets/docs/widget-presets-plan.md`, `Assets/docs/extensibility-seam-element-kinds-plan.md`,
    `Assets/docs/extensibility-seam-validation-rules-plan.md`,
    `Assets/docs/extensibility-seam-widget-attributes-plan.md`,
    `Assets/docs/extensibility-seams-master-plan.md`, `Assets/docs/native-authoring-testing-guide.md`
    (all mention "Composer" in passing while documenting a mostly-still-live seam; each needs a read, not
    a blanket action)

---

## 6. Summary counts

- **DELETE outright** (production `.cs`): `AlignDistribute.cs`, `AlignmentGuides.cs`, `BreakpointBar.cs`,
  `ComposerCanvas.cs`, `ComposerFlowBridge.cs`, `ConstraintWriteback.cs`, `MenuCatalogEditor.cs`,
  `NeoComposerWindow.cs`, `PalettePane.cs`, `PreviewFlowPlayback.cs`, `PreviewSampleData.cs`,
  `SpecDocument.cs`, `SpecInspector.cs`, `SpecPreviewPane.cs`, `SpecTreeView.cs`, `ThemePaletteEditor.cs`,
  `TreeDrag.cs`, plus the `SpecFieldCatalog` static class body (not the whole file) = **17 files + 1
  partial** deleted outright, all of `Automation/` (7 `.cs` + `Scenarios~/` + `README.md`) = **7 more
  files + a data folder**.
- **REHOME** (real survivor dependents, not yet moved): `ComposerFactory.cs`, `ComposerDevicePresets.cs`,
  the `SpecField`/`FieldKind` types split out of `SpecFieldCatalog.cs`.
- **KEEP under Editor/Composer**: none.
- **Test files: DELETE whole** (11): listed in §4. **Test files: PRUNE, don't delete** (4):
  `DevicePresetRegistryTests.cs` (survives whole, rehomes), `NeoElementKindsTests.cs`,
  `ComposerCatalogKindsTests.cs`, `TemplateInsertTests.cs`. **Test files: import-only update** (2):
  `NativeAuthoringRoundTripTests.cs`, `NativeCaptureTests.cs`.
- **Non-Composer production call sites needing edits**: `Editor/Agent/AgentBridge.cs` (composerSession
  handler), `Editor/Showcases/HubToolRegistry.cs` (Hub tool entry), `Editor/Showcases/NeoUIHubWindow.cs`
  (Edit-in-Composer button), `Editor/Authoring/NeoLayoutTemplates.cs` (drop the `SpecDocument` overload).
- **`Editor/EditorUI/NeoConstraintWidget.cs`**: DELETE, confirmed sole consumer was `SpecInspector.cs`.
- **CLAUDE.md**: two bullets to delete (lines 271-289, 458-480) + one stale sentence to fix (line 444) +
  note two already-stale mentions (lines 462, 465) for Task 3.2 to clean up while it's in the neighborhood.
