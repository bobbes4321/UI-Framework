# Neo UI Remediation Plan — Wave-Based Execution Guide for an Orchestrator Agent

**Rev 2 — now includes the Composer retirement track.** The strategic decision the audit flagged as
open ("Composer vs native authoring") has been made by the project owner: **the Composer is being
retired as soon as possible; native-Unity authoring (`Editor/Authoring/`) is the one authoring
surface.** This revision inserts the retirement as Waves 2–3 (immediately after the foundation
wave) so that no later wave wastes effort polishing code that is about to be deleted, and it
removes/rewrites the tasks from Rev 1 that invested in Composer chrome (old tasks 1.7, 2.2, 4.2,
5.3 — noted inline where relevant).

**Input:** `neo-ui-architecture-audit.md` (repo root). Finding IDs (A1–A9, D1–D9, E1–E7) refer to
that document — give subagents the relevant audit section text along with their task.

**Who this is for:** a main orchestrator agent that spawns one subagent per task, wave by wave.
Subagents are assumed competent but not brilliant: every task states which files to touch, what
the change is, what NOT to do, and a machine-checkable acceptance test. If a subagent wants to
deviate from a stated decision, it must stop and report instead of improvising.

---

## 0. Standing rules (give these to EVERY subagent verbatim)

1. **Read `CLAUDE.md` at the repo root before touching anything.** Its hard constraints override
   this plan if they ever conflict — EXCEPT its Composer sections, which Waves 2–3 supersede
   (CLAUDE.md itself gets rewritten in Task 3.2).
2. **Scope discipline:** touch ONLY the files listed in your task. If the fix genuinely requires
   another file, stop and report back to the orchestrator instead of expanding scope.
3. **Never batch-compile or kill Unity while the editor is open.** Check first:
   `Temp/UnityLockfile` exists AND `tasklist | findstr Unity.exe` shows a process ⇒ editor is open.
   In that case verify compilation with Unity's Roslyn instead (see §1.1). Never run two Unity
   batch instances at once.
4. **Editor asmdef boundaries:** nothing under `Assets/Neo UI Framework/Editor/EditorUI/` may
   reference `Neo.UI` types. Runtime (`Assets/Neo UI Framework/Runtime/`) may never reference
   editor types or `UnityEngine.Input` (New Input System only). `NeoShape` never gets its own
   material.
5. **No silent failures:** any new string-addressed lookup that matches nothing must
   `Debug.LogWarning` with the failing key and context.
6. **Tests:** every behavioral fix ships WITH a regression test in the same task. EditMode tests go
   in `Assets/Neo UI Framework/Tests/EditMode/`, PlayMode in `.../PlayMode/`. Generation tests are
   automatically redirected to a scratch root by the `NeoTestScratchRoot` SetUpFixture — never
   write a test that touches `Assets/Neo UI Generated` or committed showcase folders directly.
7. **Known pre-existing test failures (NOT regressions, do not "fix"):**
   `OptionSets_SeedExactlyTheBuiltIns` (a committed "Important" variant asset feeds the live seam)
   and `ComposerProbeTests` when headless (moot after Wave 3 — the probe is deleted with the
   Composer).
8. **Docs-sync rule:** if your task adds/renames/removes a spec field, bridge action, or menu
   item, the same task must update `CLAUDE.md`, `Editor/Agent/SpecReference.cs`, and regenerate
   `Assets/docs/spec-reference.md` + `neo-spec.schema.json` (bridge action
   `{"action":"specReference"}` or the menu item). A Stop hook enforces this.
9. **When moving a file, use `git mv`** and move its `.meta` file with it (same rename) so Unity
   GUIDs survive. When deleting, delete the `.meta` too.
10. **Style:** match surrounding code. Registries follow the canonical shapes defined in Wave 1 —
    do not invent variants.
11. **Report format back to orchestrator:** files changed/moved/deleted, tests added (names),
    acceptance checks run + output, any deviation or discovered blocker.

---

## 1. Orchestrator verification toolkit

Run these at every wave gate (§1.5). All commands from repo root.

### 1.1 Compile check

- **Editor closed:** trigger a compile by running the EditMode test suite (§1.2) — compilation
  errors fail the run immediately.
- **Editor open:** use Unity's Roslyn directly (the documented project workflow):

  ```
  dotnet "<UnityDir>/Editor/Data/DotNetSdkRoslyn/csc.dll" -nologo -target:library -langversion:9.0
    -define:UNITY_EDITOR
    -r:<UnityDir>/Editor/Data/Managed/UnityEngine/*.dll
    -r:<UnityDir>/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll
    -r:Library/ScriptAssemblies/Neo.UI.dll -r:Library/ScriptAssemblies/UnityEngine.UI.dll
    -r:Library/ScriptAssemblies/UnityEditor.UI.dll -r:Library/ScriptAssemblies/Unity.TextMeshPro.dll
    -r:Library/ScriptAssemblies/Unity.InputSystem.dll
    -out:%TEMP%/neo-check.dll <all .cs files of the touched asmdef>
  ```

  Unity install: `C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor\` (verify once, then reuse).
  Compile `Editor/EditorUI` **alone with engine refs only** whenever a wave touched it.

### 1.2 Test runs (editor closed only)

```
Unity.exe -batchmode -nographics -runTests -testPlatform EditMode -projectPath . -testResults Temp/results-edit.xml
Unity.exe -batchmode -runTests -testPlatform PlayMode -projectPath . -testResults Temp/results-play.xml
```

`-runTests` returns before finishing — **poll for the results XML**. Parse
`<test-run ... result= failed= passed=`. Compare failures against §0.7. PlayMode needs graphics
(omit `-nographics`). EditMode at every gate; PlayMode at gates for waves 1, 3, 5, and 9.

### 1.3 Grep gates

Each task defines grep-based acceptance checks. A task is not done until its greps pass.

### 1.4 Git hygiene

- `git status` before/after each wave; any changed file no task claims ⇒ investigate first.
- Commit once per wave gate (`remediation wave N: <one-liner>`); never amend earlier wave commits.

### 1.5 Wave gate checklist

1. All subagent reports collected; deviations resolved or task re-run.
2. Compile check passes (EditorUI standalone too if touched).
3. EditMode suite: no NEW failures vs the Wave-0 baseline (adjusted for tests deliberately deleted
   in Wave 3 — the deletion task lists them).
4. All task grep gates pass.
5. Git diff reviewed for scope creep; commit.

---

## 2. Wave plan

**Parallelism rule:** tasks within a wave touch disjoint files and may run in parallel, EXCEPT
where a wave is marked SEQUENTIAL. If two tasks list the same file, run them sequentially.

**Decisions are pre-made.** Subagents implement them; they do not re-litigate.

---

### Wave 0 — Baseline (orchestrator itself, no subagents)

1. Run full EditMode + PlayMode suites (§1.2). Record every failure — this is the baseline
   (§0.7 lists the two expected ones).
2. `git status` — record the uncommitted working-tree state. Note specifically:
   `Editor/Composer/PresetPickerPopup.cs` is NEW and UNCOMMITTED — it is **kept and rehomed** in
   Task 2.2, not deleted. Do not revert working-tree files.
3. Save baseline to `Temp/remediation-baseline.md`, including the retirement decision:
   "Composer retired per owner decision 2026-07; native authoring is the authoring surface."

**Gate:** baseline recorded.

---

### Wave 1 — Foundation + independent leaf fixes (6 parallel tasks)

*(Rev-1's Task 1.7 — ComposerCanvas fixes — is DROPPED: the canvas is deleted in Wave 3.)*

#### Task 1.1 — Registry base classes (keystone for Wave 4)

*Audit refs: §5 verdict, A5, A6.* Create **new files only**:

- `Assets/Neo UI Framework/Editor/NeoKeyedRegistry.cs` — `public class NeoKeyedRegistry<T>`:
  - ctor `(Func<T,string> key, StringComparison comparison = Ordinal, Func<IEnumerable<T>> builtins = null, Func<T,bool> validate = null, string registryName = null)`;
    builtins seeded lazily, builtins-first, then insertion order.
  - `IReadOnlyList<T> All` — cached **snapshot** rebuilt on mutation (never the live list).
  - `bool TryGet(string key, out T value)`; `T GetOrWarn(string key)` — null + one
    `Debug.LogWarning($"[Neo.UI] {registryName}: no entry '{key}'.")` on miss.
  - `void Register(T value)` — null/empty-key/failed-validate ⇒ warn + ignore (never throw);
    duplicate key ⇒ **replace in place**.
  - `internal bool Remove(string key)`; `internal void ResetForTests()`.
- `Assets/Neo UI Framework/Editor/NeoAssetRegistry.cs` —
  `public class NeoAssetRegistry<TAsset,TEntry> : NeoKeyedRegistry<TEntry> where TAsset : ScriptableObject`:
  - ctor adds `Func<TAsset,TEntry> project` + the `"t:Type"` search filter.
  - **Discovery model (fixes audit A5):** manual `Register` entries live in a separate list;
    `EnsureDiscovered()` REBUILDS the merged view (discovered then manual; manual wins on clash)
    from a fresh `FindAssets` pass — so deleted/renamed assets are evicted and manual entries
    survive. Duplicate discovered keys ⇒ keep last + warn naming both asset paths.
  - `public void InvalidateDiscovery()`.
  - ONE shared `AssetPostprocessor` (same file): registries enroll by asset type; on
    import/delete/move, invalidate only registries whose type matches the changed paths
    (`AssetDatabase.GetMainAssetTypeAtPath`). Replaces the four copy-pasted postprocessors
    (removed in Wave 4) and ends the any-`.asset` blanket rescan (audit A4).
- `Assets/Neo UI Framework/Runtime/NeoRuntimeKeyedRegistry.cs` — Runtime-asmdef twin of
  `NeoKeyedRegistry<T>` (no UnityEditor) for `NeoAnimatorRoles` (migrated in Wave 4).
- Tests: `Tests/EditMode/NeoKeyedRegistryTests.cs` — replace-on-duplicate; warn-not-throw
  (`LogAssert.Expect`); builtins-first order; snapshot semantics; `ResetForTests`; `GetOrWarn`
  logs. Asset half: create two temp SO assets in scratch, discover, delete one, invalidate,
  assert eviction + manual-entry survival.

**Grep gate:** both classes exist; test file exists. Do NOT modify any existing registry here.

#### Task 1.2 — Runtime no-silent-failure sweep

*Audit ref: A3 (runtime half).* Files: `Runtime/Graphics/Effects/NeoSignalParamBinding.cs`,
`Runtime/Containers/UIView.cs`, `Runtime/Animation/AnimationPresetDatabase.cs`.

1. `NeoSignalParamBinding.cs:113` — capture `TrySetLiveParam`'s bool; on first `false` per binding,
   `Debug.LogWarning` naming effect type + param + signal; `_warned` flag so it logs once.
2. `UIView.cs:119-124` — `matched++` in the `ShowCategory`/`HideCategory` branches; extend the
   zero-match warning (`:133-136`) to those command types (message prints the category).
3. `AnimationPresetDatabase.cs:18-22` — warn (preset name + database asset name) before null.
4. Tests (`Tests/EditMode/NoSilentFailureTests.cs`, new) with `LogAssert.Expect(LogType.Warning, …)`
   for the category miss and the preset miss; binding warn as a PlayMode test if signal dispatch
   is needed (warns exactly once after two sends).

**Grep gate:** `LogWarning` present in all three regions; tests exist.

#### Task 1.3 — `ApplyTextOutline` generated-root leak

*Audit ref: A8.* File: `Editor/Agent/UIWidgetFactory.cs:226-240` only.
Replace `const string dir = "Assets/Neo UI Generated/Materials"` with
`UISpecGenerator.GeneratedRoot + "/Materials"`; create the folder chain relative to that root.
Test: with `GeneratedRoot` redirected (scratch fixture does this), `ApplyTextOutline` creates its
material under the scratch root, not under `Assets/Neo UI Generated`.

**Grep gate:** the literal `"Assets/Neo UI Generated/Materials"` no longer appears anywhere.

#### Task 1.4 — Runtime breakpoint empty-condition guard

*Audit ref: A7.* File: `Runtime/Containers/UIResponsiveRoot.cs:39-53`.
Add the guard the editor twins have (mirror `Editor/Agent/BreakpointConditions.cs:102`): all-unset
condition ⇒ `Matches` returns false. Test: an all-default `ResponsiveCondition` matches no
viewport.

#### Task 1.5 — Coverage: `spacer`, `importSprites`, real `buildScene`

*Audit ref: §2.5.* New test files only.

1. `Tests/EditMode/SpacerRoundTripTests.cs` — vstack with a `spacer` between two texts:
   generate → export → spacer survives with kind `"spacer"`; export→generate→export byte-identical
   (pattern: `SpecLayoutAndWidgetTests.cs`).
2. `Tests/EditMode/ImportSpritesActionTests.cs` — copy a small PNG into scratch, drive
   `{"action":"importSprites","folder":…}` through `AgentBridge.HandleRequest` JSON (invocation
   pattern: `SyncProtocolTests.cs:172-180`); assert `spriteImportMode == Single` + Sprite
   sub-asset loads.
3. `Tests/EditMode/SceneBuilderExecutionTests.cs` — generate the canonical demo spec into scratch,
   run the real scene build for its flow to a scratch scene path; assert scene exists, contains
   Canvas + EventSystem, and only flow-referenced views instantiated (assertion style:
   `SceneBuilderFlowScopingTests.cs`). Clean up in teardown.

#### Task 1.6 — `NeoDesignSystemWindow` perf violations

*Audit ref: A4.* File: `Editor/NeoDesignSystemWindow.cs` only.
1. `:280-281` — hoist the fallback `GUIStyle` to cached static.
2. `:526-535, :81, :165, :324` — replace per-frame option arrays + `EditorGUILayout.Popup` with
   `NeoDropdown.ValuePopup` + on-open `Func`, exactly like the same file's Motion tab (`:480-487`).
   Preserve "(none)" sentinels.

**Grep gate:** no `new GUIStyle(` in draw paths; the four sites call `NeoDropdown`.

**Wave 1 gate:** §1.5 + PlayMode run (1.2/1.4 touched runtime).

---

### Wave 2 — Composer retirement A: rehome shared pieces + close parity gaps (5 tasks; 2.5 runs LAST)

**Context for all Wave-2 subagents:** the Composer window and all its chrome
(`NeoComposerWindow`, `ComposerCanvas`, `SpecInspector`, `SpecPreviewPane`, `SpecTreeView`,
`PalettePane`, `BreakpointBar`, `ThemePaletteEditor`, `MenuCatalogEditor`, `ConstraintWriteback`,
`TreeDrag`, `AlignmentGuides`, `SpecDocument`, `PreviewFlowPlayback`, the whole `Automation/`
folder and the `composerSession` bridge action) will be DELETED in Wave 3. This wave extracts
everything that must outlive it and builds the minimal native-authoring parity features so the
deletion loses no capability the project actually uses. Do not improve doomed files; only move or
port what your task lists.

#### Task 2.1 — Rehome the authoring registries + spec tools; template insertion goes native

Files (all moves via `git mv`, class renames included, update every reference by grep):
- `Editor/Composer/ComposerPalette.cs` → `Editor/Authoring/NeoWidgetPalette.cs`, class
  `ComposerPalette` → `NeoWidgetPalette`. It is already consumed by the native `NeoCreateMenu`
  ("More Widgets…" is data-driven off it) — that consumer keeps working. Keep the Components
  (preset tiles) category: it now serves the create menu and Task 2.2's picker.
- `Editor/Composer/ComposerTemplates.cs` → `Editor/Authoring/NeoLayoutTemplates.cs`, class →
  `NeoLayoutTemplates`; move the `Editor/Composer/Templates~/` folder alongside it
  (`Editor/Authoring/Templates~/`) and fix its loader path.
- `Editor/Composer/ComposerCatalogKinds.cs` → `Editor/Agent/NeoCatalogKinds.cs`, class →
  `NeoCatalogKinds` (the exporter and menus pipeline need it after the window dies — audit
  E3/seam-plan Phase 2 builds on it in Wave 7).
- `Editor/Composer/ComposerOptions.cs` → `Editor/Agent/NeoWidgetOptions.cs`, class →
  `NeoWidgetOptions` (it owns the documented widget-attribute registries
  `RegisterVariant/RegisterSize/RegisterAlign/RegisterShape` + `SpacingScale` — seams that must
  survive; its Composer-picker consumers die, the registries don't).
- `Editor/Composer/SpecMigration.cs` → `Editor/Agent/SpecMigration.cs` (it is a spec-level tool
  with its own `Tools → Neo UI → Advanced → Migrate Spec To Layout Model` menu item, not window
  chrome).
- **New parity feature:** add "Insert Template" to the native create menu: a
  `GameObject → Neo UI → Insert Template…` entry (in `Editor/Authoring/NeoCreateMenu.cs`) that
  lists `NeoLayoutTemplates.All` in a searchable `NeoDropdown`/simple picker and instantiates the
  chosen template's elements into the selected container via
  `UISpecGenerator.BuildElementLive` (the same path `NeoSceneAuthoring.CreateWidget` uses).
- Update the existing tests that reference moved classes (`PaletteRegistryTests`,
  `TemplateInsertTests`, `WidgetAttributeRegistryTests`, `SpecMigrationTests`) — rename references
  only, keep assertions. Add one test: Insert-Template builds the template's element tree under a
  selected view (EditMode, scratch root).

**Grep gate:** `class ComposerPalette|ComposerTemplates|ComposerCatalogKinds|ComposerOptions` have
zero definitions; no references to the old names outside `Editor/Composer/` (references INSIDE
doomed Composer files may remain — they die in Wave 3; if a doomed file fails to compile because
of the rename, apply the mechanical rename there too, nothing more).

#### Task 2.2 — Rehome the preset picker + thumbnails; preset create/update/reset goes native

Files: `Editor/Composer/PresetPickerPopup.cs` (uncommitted, new) →
`Editor/Authoring/PresetPickerPopup.cs`; `Editor/Composer/PresetThumbnailCache.cs` +
`PresetThumbnailRenderer.cs` → `Editor/Inspectors/` (they render real widgets via
`UIScreenshotter` — inspector-grade infrastructure, also used by the palette tiles).
1. Wire the native surface: `NeoSceneOverlay`'s **Apply-Preset** button opens `PresetPickerPopup`
   (kind-scoped thumbnail grid) instead of / in addition to its current picker, and the chosen
   preset routes into the existing `NeoSceneAuthoring.ApplyPreset`.
2. **Port preset Create/Update/Reset-from-selection** (currently `SpecInspector.cs:271-321`,
   `:301-312` — doomed): add "Create Preset From Widget" / "Update Preset From Widget" /
   "Reset Widget To Preset" actions to the overlay (or the picker popup's footer — implementer's
   choice, report which). Implementation: capture the selected widget's element spec via the
   `internal UISpecExporter.ExportElement` seam (exactly how `NeoSceneAuthoring.ApplyPreset`
   already does), then copy/clear the preset-governed fields. COPY the field list from
   `SpecInspector.CapturePreset`/`ResetElementToPreset` for now — include `padding4` (the audit
   D1 drift) — and leave a `// TODO(Wave 5): route through PresetFields` comment; Wave 5 Task 5.3
   consolidates it.
3. Tests: `Tests/EditMode/NativePresetWorkflowTests.cs` — create-from-widget produces a
   `NeoWidgetPreset` whose fields match the widget; reset-to-preset clears overridden fields on
   the captured spec (assert via `ExportElement`); apply-preset keeps placement + sibling order
   (reuse `NativeAuthoringRoundTripTests` patterns).

**Grep gate:** `PresetPickerPopup` no longer under `Editor/Composer/`; overlay references it.

#### Task 2.3 — Native breakpoint-override authoring (parity for BreakpointBar)

*Context:* the Composer's `BreakpointBar` + inspector override-scoping is today the only UI for
authoring per-breakpoint `overrides`. Agents author them in spec JSON (unaffected). The native
replacement is deliberately minimal — capture-based, matching the native-authoring philosophy:

Files: `Editor/Authoring/NeoSceneAuthoring.cs`, `Editor/Authoring/NeoSceneOverlay.cs`; new
`Tests/EditMode/NativeBreakpointAuthoringTests.cs`.
1. Overlay gains a **Breakpoint** dropdown when a `UIView` with (or whose spec has) top-level
   `breakpoints` is selected: lists the spec's breakpoint names (from the captured/exported spec)
   + "(base)".
2. New action **"Capture Layout As Override"**: with breakpoint X selected, export the selected
   element's current layout (`ConstraintLayout.Detect` — already the single owner), diff it
   against the element's base `layout`, and write the delta into
   `element.overrides[X]` (`LayoutSpec` delta semantics — reuse the exact delta logic the
   exporter's breakpoint reconstruction uses at `UISpecExporter.cs:283-375`; call it, don't copy
   it). Persist via the standing `NeoCapture.CaptureView` protocol (spec + baseline), so the
   change round-trips like any other native edit.
3. New action **"Preview Breakpoint"**: resize handling is NOT built — instead apply the
   breakpoint's resolved layout to the live view via the same code path `UIResponsiveRoot` uses
   (call its apply with the chosen breakpoint's baked data), and a "(base)" selection restores.
   This is a scene-view preview aid, editor-only, no serialization.
4. Tests: capture-as-override writes the expected `overrides[X]` delta for a moved element and the
   result round-trips byte-identical through export→generate→export (extend
   `BreakpointRoundTripTests` patterns).

**Decision note:** breakpoint CREATION (defining the named conditions) stays spec-side — agents or
hand-editing the spec JSON; the overlay only authors per-element overrides against existing
breakpoints. Do not build a condition-editing UI.

#### Task 2.4 — Catalog editing without the Composer

*Context:* `MenuCatalogEditor` (Composer pane) is today's UI for editing settings/cheats catalog
SOs; it dies in Wave 3. Catalogs are plain ScriptableObjects — the native answer is a standard
custom inspector.
Files: new `Editor/Inspectors/MenuCatalogInspector.cs`; reference
`Editor/Composer/MenuCatalogEditor.cs` READ-ONLY as the porting source; extend
`Editor/Inspectors/AuthoringInspectors.cs` registration pattern if that's how inspectors are
wired.
1. Build a `NeoUIEditor`-based inspector for the catalog SO types (settings + cheats) covering
   what the pane covered: item list add/remove/reorder (`NeoListView` — cached, per the IMGUI
   rules), per-item kind dropdown driven by `MenuItemSpec.Kinds` (post-Wave-2.1 name:
   `NeoCatalogKinds` descriptors for the catalog kind; item kinds stay `MenuItemSpec.Kinds` until
   Wave 7 gives them a registry), label/id/default-value fields, and the `showFavourites`-style
   per-kind toggles the descriptor exposes.
2. Follow all EditorUI-kit conventions (cached styles, `BeginFoldoutSection`, searchable dropdowns
   via `NeoDropdown`, no per-OnGUI allocation of lists/SerializedObjects).
3. Test: `Tests/EditMode/MenuCatalogInspectorTests.cs` — smoke-create the inspector for a scratch
   catalog SO and assert its serialized edits (add item, change kind) persist; deeper UI testing
   is not required.

#### Task 2.5 — Kill-list inventory (runs AFTER 2.1–2.4 complete; read-only)

One subagent produces `Temp/composer-kill-list.md` for Wave 3:
1. List every file under `Editor/Composer/` (and its `Automation/` + `Scenarios~/`) remaining
   after the Wave-2 moves.
2. For each, grep for references FROM outside `Editor/Composer/`. Classify:
   **DELETE** (referenced only by other doomed files or by nothing),
   **REHOME** (referenced by survivors — should be empty after 2.1–2.4; if not, report each case
   with the referencing file so the orchestrator can decide), or **KEEP** (should be none).
   Known judgement calls to resolve by grep, explicitly: `SpecFieldCatalog` (if only
   `SpecInspector` consumes it ⇒ DELETE — its RegisterField seam dies with the inspector; note
   this in the kill list for the CLAUDE.md rewrite), `PreviewSampleData` (if
   `PresetThumbnailRenderer` or anything in `Editor/Agent/` uses it ⇒ REHOME to the consumer's
   folder; else DELETE — and note that audit D4's token-scanner duplication then resolves by
   deletion), `ComposerFactory`, `ComposerDevicePresets` (expected DELETE — the agent `preview`
   action's resolution matrix lives in `UIScreenshotter`, not here; verify), `ComposerProbeMetrics`.
3. Also list: `Editor/EditorUI/NeoConstraintWidget.cs` (DELETE if its only consumer was
   `SpecInspector` — verify by grep), every `Tests/EditMode/*` file that tests doomed classes
   (expected: `ComposerProbeTests`, `ComposerScenarioParseTests`, `SpecDocumentTests`,
   `SpecTreeViewTests`, `TreeDragTests`, `AlignDistributeTests`, `ConstraintWritebackTests`,
   `InspectorFieldTests`, `PreviewFlowPlaybackTests`, `PreviewSampleDataTests`,
   `SpecFieldCatalogTests`, `DevicePresetRegistryTests` — CONFIRM each by reading what it
   imports), the `composerSession` handler + registration in `Editor/Agent/AgentBridge.cs`, the
   `Tools → Neo UI → Composer` menu registration, and every CLAUDE.md / `Assets/docs/*` section
   that documents the Composer or `composerSession` (list section headings + line ranges for Task
   3.2).

**Wave 2 gate:** §1.5; EditMode green (moved-class tests renamed, new parity tests pass); the
kill-list exists and contains no unresolved REHOME rows.

---

### Wave 3 — Composer retirement B: deletion + docs (SEQUENTIAL: 3.1 then 3.2)

#### Task 3.1 — Delete the Composer

Input: `Temp/composer-kill-list.md`. Files: exactly the kill list.
1. Delete every DELETE-classified file + its `.meta`, including the `Automation/` folder,
   `Scenarios~/`, and the doomed test files (list them in the report — the orchestrator adjusts
   the §0.7/Wave-0 baseline: deleted tests are expected to disappear, not "fail").
2. `Editor/Agent/AgentBridge.cs`: remove the `composerSession` action (switch case + handler +
   its entry in the valid-actions error string + `mutatesAssets` list if present). NOTE: Wave 6
   Task 6.3 later converts this dispatch to a registry — here just remove the action cleanly.
3. Remove the `Tools → Neo UI → Composer` `[MenuItem]`.
4. Compile check both asmdefs (§1.1) — EditorUI standalone MUST still compile (proves
   `NeoConstraintWidget` removal, if listed, left the kit consistent).
5. Full EditMode run: zero new failures among SURVIVING tests.

**Grep gate:** `Editor/Composer/` directory no longer exists (or contains nothing);
`composerSession` appears nowhere under `Assets/Neo UI Framework/`; no compile references to any
deleted class.

#### Task 3.2 — Rewrite the record (CLAUDE.md + docs)

Files: `CLAUDE.md`, `Assets/docs/composer-authoring-overhaul/*.md` (headers only),
`Assets/docs/human-workflow-plans/02-spec-authoring-window.md` (header only),
`Assets/docs/editor-ux-analysis.md` (only if it references the Composer), `ONBOARDING.md`
(Composer references), guided by Task 2.5's section list.
1. CLAUDE.md: delete the Composer bullet and the `composerSession` action bullet; update the
   native-authoring bullet from "intended to supersede the Composer once at parity" to a statement
   that native authoring IS the authoring surface and the Composer was removed (date it); document
   the survivors under their new names/homes (`NeoWidgetPalette`, `NeoLayoutTemplates`,
   `NeoCatalogKinds`, `NeoWidgetOptions`, `PresetPickerPopup` in Authoring, template insertion,
   native breakpoint-override capture, the catalog inspector). Keep every non-Composer section
   untouched.
2. Each retired plan doc gets ONLY a dated status line at top ("Status: retired 2026-07 — the
   Composer was removed; native authoring supersedes it; see CLAUDE.md") — do not rewrite
   historical content.
3. Regenerate spec reference docs ONLY if an action/field changed (composerSession was an action ⇒
   yes: run `{"action":"specReference"}` or note that SpecReference doesn't list bridge actions —
   verify by grep and report).

**Wave 3 gate:** §1.5 + PlayMode run + grep: `Composer` case-sensitive under
`Assets/Neo UI Framework/` matches only the historical comments the kill list chose to keep (and
survivor file docs where the word is legitimately historical); CLAUDE.md contains no instruction
to use a deleted surface.

---

### Wave 4 — Registry migrations onto the Wave-1 base (4 parallel tasks)

**Shared instructions:** each registry keeps its public static facade as thin forwarders to a
private `NeoKeyedRegistry<T>`/`NeoAssetRegistry<>` — NO caller changes. Keep/add
`internal ResetForTests()`. Extend each mirror test with: replace-on-duplicate override; invalid
register warns (LogAssert), never throws.

**Pre-made policies:** `NeoCatalogKinds` (ex-ComposerCatalogKinds) throw→**warn-and-ignore**
(audit A6: throw in `[InitializeOnLoad]` poisons the registering type; ComposerDevicePresets is
deleted, so it's just this one now). `NeoAnimatorRoles` first-wins→**replace-with-warn** (Runtime
twin). `ThemeBundleRegistry` KEEPS `OrdinalIgnoreCase` via the `comparison` arg + one comment.
`AnimationPresetRegistry` GAINS `public static Register(UIAnimationPreset)`; duplicate
`presetName`s become last-discovered-wins **with a warning** naming both asset paths.

#### Task 4.1 — Asset-backed registries
Files: `Editor/Showcases/ShowcaseRegistry.cs`, `Editor/ThemeBundles.cs` (registry portion),
`Editor/Agent/NeoWidgetPresets.cs`, `Editor/AnimationPresetRegistry.cs`.
Migrate onto `NeoAssetRegistry<,>`; DELETE the four private `AssetPostprocessor` classes (shared
one covers them). `NeoWidgetPresets.All` filters destroyed/fake-null SOs. `ThemeBundleRegistry`
gains `ResetForTests` (keep `Remove` as forwarder). Add a delete-asset-eviction assertion to each
mirror test.

#### Task 4.2 — Rehomed authoring registries
Files: `Editor/Authoring/NeoWidgetPalette.cs`, `Editor/Authoring/NeoLayoutTemplates.cs`,
`Editor/Agent/NeoCatalogKinds.cs`, `Editor/Agent/NeoWidgetOptions.cs` (post-Wave-2 locations).
Migrate their keyed registries onto the base. `NeoWidgetPalette`: CACHE the composed `All`
(rebuild when `NeoElementKinds`/`NeoWidgetPresets` bump a new cheap `internal static int Version`
on mutation) instead of a fresh sorted list per access (audit registry bug 7).

#### Task 4.3 — Agent descriptor registries + `ShapeEffectDefinitions`
Files: `Editor/Agent/ShapeEffectRegistry.cs`, `Editor/Agent/ParticleEffectRegistry.cs`,
`Editor/Agent/LayoutConstraints.cs`, `Editor/Agent/LayoutSizingModes.cs`,
`Editor/Agent/BreakpointConditions.cs`, new `Editor/Agent/EffectParams.cs`.
1. Migrate the five (LayoutConstraints key = `$"{Id}:{(int)Axis}"`).
2. Fix silent lookups: `ParticleEffectRegistry.GetForConfig` (`:80-87`) + `BreakpointConditions`
   misses now warn (audit A3).
3. New `ShapeEffectDefinitions` facade over
   `NeoAssetRegistry<ShapeEffectDefinition, ShapeEffectDefinition>`; rewrite
   `VariantDescriptor.ResolveDefinition` (`ShapeEffectRegistry.cs:343-355`) to a registry lookup —
   deletes the per-call `FindAssets` scan (audit A4/A5).
4. Move duplicated `GetFloat`/`GetString` + `ParseColorRef`/`ColorRefToString` into
   `EffectParams`; both registries call it (audit D9).
Tests: definition eviction; LogAssert on formerly-silent misses.

#### Task 4.4 — Remaining registries + naming hygiene
Files: `Runtime/Animation/NeoAnimatorRoles.cs`, `Editor/Showcases/HubToolRegistry.cs`,
`Editor/Agent/NeoElementKinds.cs`, `Editor/Agent/NeoValidationRules.cs`.
`NeoAnimatorRoles` → Runtime twin, replace-with-warn, `internal ResetForTests`. `HubToolRegistry`
→ base with `validate: t => t.invoke != null`. `NeoElementKinds`: public `ClearForTests` →
`internal ResetForTests` (update test callers by grep). `NeoValidationRules`/
`NeoInteractivityProviders` stay keyless — naming alignment only, no behavior change.

**Wave 4 gate:** §1.5 + greps: only the shared registry postprocessor remains; no leftover
`_discovered` flags; full EditMode; PlayMode (NeoAnimatorRoles).

---

### Wave 5 — Spec pipeline core (SEQUENTIAL: 5.1 → 5.2 → 5.3 → 5.4)

Round-trip suite (`TypographyTests`, `SpecLayoutAndWidgetTests`, `IconAndVariantTests`,
`DepthAndShapeTests`, `JuiceTests`, `CompositionAndRichPopupTests`,
`ElementAnimationsRoundTripTests`, `PresetMotionTests`) must be green after EACH task.

#### Task 5.1 — Four hard round-trip breaks (fix + regression test each)
*Audit ref: A1.* Files: `Editor/Agent/UISpecGenerator.cs`, `Editor/Agent/UISpecExporter.cs`,
`Editor/Agent/UISpec.cs`, relevant Runtime marker component, new
`Tests/EditMode/RoundTripBreakRegressionTests.cs`.
1. **Dual view commands** — `UISpecExporter.cs:666-671`: `GetComponents<ViewCommandOnClick>()`
   loop; Show fills `onClickShowView`, Hide fills `onClickHideView`; duplicate same-type ⇒ keep
   first + warn. Test: both fields round-trip; second export byte-identical.
2. **`scroll` alias** — DECISION: normalize at parse. In `ElementSpec.Parse` map `"scroll"` →
   `"list"` (precedent: `padding4`→`padding` normalization). Update `SpecReference.cs` (alias
   note) + regenerate (§0.8). Remove the dual accept at `UISpecGenerator.cs:843-844`. Test:
   `scroll` parses to `list`, byte-stable from first export.
3. **Token loss (pointerGlow + shape outlineColor)** — persist the authored token: pointerGlow's
   `NeoPointerReactor` gains a serialized `colorToken` (empty when hex-authored); generator
   (`UISpecGenerator.cs:1262`) sets it; exporter (`UISpecExporter.cs:1030`) emits token when
   present, else hex. Same pattern for shape outline (`:962` region ↔ exporter `:860-862`) —
   carry the token on an existing marker component; do NOT add a new MonoBehaviour if an existing
   one can hold a string. Tests: token in → token out.
4. **toggle/tab `labelColor`** — DECISION: apply it like button. Generator toggle (`:772-781`) and
   tab (`:788-804`) put a `ThemeColorTarget` with the token on the label; exporter branches
   (`:631-641`, `:608-620`) read it back like the button branch (`:660`). Tests included.

#### Task 5.2 — Deterministic export + multi-flow export
*Audit ref: A2.* Files: `Editor/Agent/UISpecExporter.cs` (+ `UISpec.cs` only if the flow model is
single-entry), extend `RoundTripBreakRegressionTests.cs`.
1. `:55, :68, :76` — sort views/popups/flows by name/id, `StringComparer.Ordinal` (mirror the
   catalog sort + comment at `:40`).
2. `:81` — remove the `break`; export ALL flow graphs (extend the model additively if today it's
   single-flow; §0.8 applies if the schema changes).
3. Test: two flows in scratch → export twice → identical, both present, sorted.

#### Task 5.3 — `PresetFields` descriptor table (kills the D1 drift)
*Audit ref: D1.* Files: new `Editor/Agent/PresetFields.cs`;
`Editor/Agent/UISpecGenerator.cs:1141-1185`, `Editor/Agent/UISpecExporter.cs:437-471`,
`Editor/Authoring/NeoSceneAuthoring.cs` (ApplyPreset region), the Wave-2.2 ported
create/update/reset code (its `// TODO(Wave 5)` marker); new `Tests/EditMode/PresetFieldsTests.cs`.
1. `PresetFields`: ordered `IReadOnlyList<PresetField>` —
   `PresetField { string name; Func<ElementSpec,object> getElement; Action<ElementSpec,object> setElement; Func<NeoWidgetPreset,object> getPreset; Action<NeoWidgetPreset,object> setPreset; Func<object,object,bool> equal; Action<ElementSpec> clearElement; }`.
   The 11 fields from `ResolvePresetAndOverrides` (variant, sizeVariant, textStyle,
   style/shapeStyle, background, labelColor, icon, radius, padding, **padding4**, spacing) PLUS
   motion→`animations.loop` as a custom-get/set/clear field (mirror Generator `:1171-1183` /
   Exporter `:465-470`). `public static void Register(PresetField)` is the new project seam —
   note it in CLAUDE.md's preset bullet (§0.8).
2. Rewrite the consumer sites as loops: generator merge (element unset ⇒ copy), exporter delta
   (equal ⇒ clear), `NeoSceneAuthoring.ApplyPreset` (explicit clear-all — FIXES the audit icon
   bug: `icon` is in the table, so the preset's icon wins), and the Wave-2.2 ported
   create/update/reset (copy-all / clear-all).
3. Tests: every table field survives merge→delta round-trip; ApplyPreset on a widget with a
   different icon ends with the preset's icon; a registered custom PresetField flows through
   merge+delta; `PresetMotionTests` stays green.

#### Task 5.4 — Name/path constants
*Audit ref: D7.* Files: `Editor/Agent/UIWidgetFactory.cs`, `UISpecGenerator.cs`,
`UISpecExporter.cs`, `GeneratedSceneBuilder.cs`, `OffSpecLint.cs`, `Editor/ThemeBundles.cs`,
`Editor/Agent/AgentBridge.cs`, `Editor/Showcases/NeoUIHubWindow.cs`,
`Editor/Showcases/ShowcaseRunner.cs`, `Editor/Agent/BeautificationAcceptance.cs`.
1. `UISpecGenerator` gains `ViewsFolder/PopupsFolder/FlowFolder/PresetsFolder` static properties
   (root-relative); replace the ~20 concatenation sites (audit D7 list). CAREFUL: only replace
   sites concatenating off the ACTIVE `GeneratedRoot`; showcase-specific roots stay explicit
   (when in doubt, report).
2. `UIWidgetFactory` consts block (`:66-92`) gains `TabPrefix`/`TabName(name)` (replace
   `UIWidgetFactory.cs:831`, `UISpecGenerator.cs:834, 1717`) and
   `PopupTitleName/PopupMessageName/PopupButtonsName` (replace `UIWidgetFactory.cs:1048-1053`,
   `UISpecGenerator.cs:1873, 1876`, `UISpecExporter.cs:116-117, 121`).
3. Purely mechanical; acceptance = round-trip suite green + greps: `"Tab_"` only at the const;
   `"/Views"` concatenation only in the new properties.

**Wave 5 gate:** §1.5 + full EditMode + PlayMode + a bridge `preview` render of one showcase spec
if an editor is available.

---

### Wave 6 — DRY consolidation (3 tasks; parallel unless files overlap)

*(Rev-1's constraint-math core task is DISSOLVED: `ConstraintWriteback`, `SpecPreviewPane`, and
`NeoConstraintWidget` were deleted in Wave 3, which resolves audit D2 and the editor half of D3 by
deletion. The runtime D3 guard landed in Task 1.4.)*

#### Task 6.1 — Shared spec visitor + token scanner
*Audit refs: D4, D5.* Files: new `Editor/Agent/SpecWalk.cs`; `Editor/Agent/BindingManifest.cs`,
`Editor/IdRefSlots.cs`, `Editor/Agent/SpecMigration.cs` (post-move location), and — ONLY IF it
survived Wave 3 per the kill list — `PreviewSampleData` at its rehomed location; new
`Tests/EditMode/SpecWalkTests.cs`.
1. `SpecWalk.Elements(ViewSpec, bool includeItemTemplates, Action<ElementSpec>)` (+
   `ElementSpec`-rooted overload) — single definition of children + `item` recursion.
2. Migrate the surviving walkers (`IdRefSlots.VisitElement`,
   `BindingManifest.WalkElement`/`CollectTokens`, `SpecMigration`, and `PreviewSampleData` if
   alive — in which case also delete its private token scanner and call the public
   `BindingManifest.CollectTokens`, fixing D4; if it was deleted, note D4 as resolved-by-deletion).
3. Tests: `SpecWalk` visits item-template elements only when asked; token extraction from a nested
   template yields the same set via all callers.

#### Task 6.2 — Color-string parsing consolidation
*Audit refs: D6, A3 tail.* Files: `Editor/Agent/UISpecGenerator.cs:1489-1499`,
`Editor/Agent/EffectParams.cs` (from 4.3), plus any surviving hand-rolled hex parser (the three
audit-cited ones were in deleted Composer files — grep `TryParseHtmlString` under `Editor/` to
catch stragglers and route them through `ColorUtils.TryParseHex`).
One `ParseColorRef(string raw, Action<string> reportInvalid = null)` in `EffectParams` (or a
`SpecColor` static); generator passes its issue reporter, effect/particle path passes
`Debug.LogWarning` — the silent variant dies. Acceptance: round-trip + effect/particle tests green.

#### Task 6.3 — AgentBridge action registry + SyncResult dedup
*Audit refs: E1, D9.* Files: `Editor/Agent/AgentBridge.cs`, new
`Editor/Agent/AgentBridgeActions.cs`, new `Tests/EditMode/AgentBridgeActionsTests.cs`.
1. `AgentBridgeActions`: `NeoKeyedRegistry<BridgeAction>`,
   `BridgeAction { string id; bool mutatesAssets; Action<request,result> handler; }` (match the
   real request/result types). Seed the 13 built-ins (post-`composerSession` removal); handler
   methods stay in AgentBridge.cs — dispatch change only.
2. Replace the switch (`:128-148`) with registry lookup; unknown-action error enumerates
   `AgentBridgeActions.All` dynamically; play-mode guard reads `action.mutatesAssets`.
3. Extract the duplicated SyncResult shaping (`:380-407` vs `:604-621`) into one
   `WriteSyncResult(SyncResult, result)`.
4. CLAUDE.md agent-actions bullet mentions the registry seam (§0.8).
5. Tests: fake registered action dispatches through `HandleRequest` JSON + appears in the error
   text; `sync` and `regenerateShowcase` return identical result key-sets on a scratch run.

**Wave 6 gate:** §1.5; full EditMode.

---

### Wave 7 — Extensibility big rocks (SEQUENTIAL: 7.1 → 7.2; then 7.3/7.4 parallel)

*(Rev-1's SpecInspector field-seam task is DROPPED — the inspector was deleted in Wave 3.)*

#### Task 7.1 — Menus module extraction + `NeoMenuItemKinds` registry
*Audit ref: E3 (sanctioned Phase-2; TODO at `BindingManifest.cs:229-232`).* Files: new
`Editor/Agent/Menus/` module files; `Editor/Agent/UISpecGenerator.cs:1541-1813`,
`Editor/Agent/UISpecExporter.cs:503, 1228-1287`, `Editor/Agent/UISpec.cs:1628-1826`,
`Editor/Agent/BindingManifest.cs:229-232`, `Editor/Inspectors/MenuCatalogInspector.cs` (the
Wave-2.4 inspector consumes the kind list); new `Tests/EditMode/NeoMenuItemKindsTests.cs`.
1. **Move verbatim first, refactor second:** generator menus pipeline (`:1541-1813`), exporter
   menu export (`:1228-1287`), `MenuItemSpec`/catalog model (`UISpec.cs:1628-1826`) into the
   module (~530 lines out of the god-files).
2. **Registry:** `NeoMenuItemKinds` (`NeoKeyedRegistry<MenuItemKindDescriptor>`) owning: spec kind
   string ↔ `MenuControlKind` mapping (replaces `MapKind`/`UnmapKind`), row build (the
   `BuildMenuRow` case bodies), typed-value conversion (`ValueToTyped`), binding-manifest type
   (`TypeForKind` — resolves the TODO). Seed the 8 built-ins. `MenuItemSpec.Kinds` becomes
   registry-derived (updates the Wave-2.4 inspector's dropdown automatically).
3. `MenuControlKind` stays an enum for built-ins; a custom kind maps to a descriptor-supplied
   build path. If runtime `MenuPresenter` enum-switching blocks the seam, STOP after the
   editor-side registry + extraction and report — no runtime menu refactor without sign-off.
4. Exporter's `is CheatCatalog ? "cheats" : "settings"` (`:503, :1232`): catalogs expose their
   kind string via `NeoCatalogKinds` descriptors instead of a type check.
5. CLAUDE.md + `SpecReference.cs` updates if the menu-kind list is emitted (§0.8).
6. Tests: fake registered menu kind parses in a catalog spec, appears in the kind list, and
   round-trips export→generate→export; `SettingsCheats*` + `BindingManifestTests` stay green.

#### Task 7.2 — `FlowNodeKinds` registry
*Audit ref: E2.* Files: new `Editor/Flow/FlowNodeKinds.cs`;
`Editor/Flow/FlowGraphWindow.cs:616-642`; new `Tests/EditMode/FlowNodeKindsTests.cs`.
Descriptor `{ id, menuLabel, Func<FlowNode> create, Action<FlowNode> seedDefaultOutputs }`; seed
the 11 built-ins (each `AddCreateEntry<T>` line becomes a registration; the type checks at
`:640-642` move into descriptors). `BuildContextualMenu` iterates the registry. Do NOT touch the
cached SerializedObject / refresh logic (CLAUDE.md mandates). Test: registered fake node kind is
creatable + seeds its default outputs.

#### Task 7.3 — Generator container/decor seam
*Audit ref: E4.* Files: `Editor/Agent/UISpecGenerator.cs:1471-1472, 1051, 1062`; extend
`Tests/EditMode/NeoElementKindsTests.cs`.
`IsPlainContainer` consults `NeoElementKinds.TryGet` + `IElementKindContainer` first, string chain
as built-in fallback — registered container kinds get card decor/background/gradient. Test: fake
registered container kind with `background` receives the decor object.

#### Task 7.4 — `CreateTab` variant seam
*Audit ref: E5.* File: `Editor/Agent/UIWidgetFactory.cs:845-867`; extend
`Tests/EditMode/WidgetAttributeRegistryTests.cs`.
`CreateTab` consults `NeoUISettings.TryGetVariantColors` first, current switch as fallback — copy
the `CreateButton` pattern (`:494-517`). (§0.7: the pre-existing `OptionSets` failure is
unrelated.)

**Wave 7 gate:** §1.5 + full EditMode + PlayMode + `preview` render of the `game-ui` and `buttons`
showcase specs if an editor is available.

---

### Wave 8 — Schema, docs, polish (3 parallel tasks)

#### Task 8.1 — `SpecReference` schema truthfulness
*Audit ref: A9.* Files: `Editor/Agent/SpecReference.cs`; regenerate
`Assets/docs/spec-reference.md` + `Assets/docs/neo-spec.schema.json`.
1. Rewrite `BuildSchema`'s element-properties source (`:63-136`) to REFLECT `ElementSpec` (small
   override table only for JSON-name divergences per `UISpec.cs:1158-1160`). Emit real
   sub-schemas for `layout`, `overrides`, `animations`, `effect`, `particles`, `pointerGlow`,
   `padding4`, top-level `breakpoints`, typed `presets` (incl. color channel). Extend
   `FriendlyType` (`:320-331`).
2. Add `breakpoints` to the intro section list; fix the "can't drift" comment (`:13-17`) to be
   true again.
3. Acceptance: regenerate, then grep the schema for `breakpoints`, `"layout"`, `"overrides"`,
   `"padding4"`, `"effect"`, `"particles"`, `"pointerGlow"`, `"atPointer"`, `"triggerMode"` — all
   present; validate `Assets/Showcases/Specs/effects.json` against the schema (scratchpad script)
   — zero violations.

#### Task 8.2 — Stale docs refresh + CLAUDE.md
*Audit ref: §2.4 stale list (Composer items already handled in Task 3.2 — skip them here).*
Surgical corrections only: `Assets/docs/editor-ux-analysis.md` (§4 done-items; "AE suite"),
`Assets/docs/ui-beautification-plan.md` (tab-panel header contradiction; deferred list),
`Assets/docs/widget-presets-plan.md` (Status header; §6 motion = loop channel; registry path),
`Assets/docs/neo-ui-package-feature-spec.md` (§12 IMGUI-reversal annotation),
`Assets/docs/native-authoring-testing-guide.md` ("four tabs" → five), `ONBOARDING.md:435`
(retired menu item → Hub), `CLAUDE.md` (add the `presets` showcase; mention Wave 5–7 seams:
`PresetFields`, `AgentBridgeActions`, `NeoMenuItemKinds`, `FlowNodeKinds`). Plans keep historical
content with a dated Status line.

#### Task 8.3 — Dead code + micro-cleanups
Files: `Editor/Showcases/NeoUIHubWindow.cs:331, 376` (delete `_ = selected;`),
`Editor/Agent/AgentBridge.cs:288` (stale menu path in hint string — grep the real `[MenuItem]`
path), `Editor/Flow/FlowGraphWindow.cs:86` (subscribe `PollRuntimeState` only during play mode via
`playModeStateChanged`; everything else untouched per the flow-window mandates).

**Wave 8 gate:** §1.5 + Stop-hook/docs check passes; EditMode green.

---

### Wave 9 — Final verification (orchestrator + 1 read-only verifier subagent)

1. Full EditMode + PlayMode; zero new failures vs the adjusted baseline (Wave-3 deletions noted).
2. Verifier subagent confirms each row by Grep/Read (trusting no prior report):

| Item | Verification |
|---|---|
| Composer gone | `Editor/Composer/` absent; `composerSession` absent from bridge, tests, CLAUDE.md; `Tools → Neo UI → Composer` MenuItem gone |
| Parity delivered | Insert-Template menu exists; overlay opens `PresetPickerPopup`; create/update/reset preset natively (tests green); breakpoint capture-as-override + tests; `MenuCatalogInspector` exists |
| Survivors rehomed | `NeoWidgetPalette`, `NeoLayoutTemplates`, `NeoCatalogKinds`, `NeoWidgetOptions`, `SpecMigration` at new paths; zero references to old class names |
| A1 | `RoundTripBreakRegressionTests` green; `GetComponents<ViewCommandOnClick>`; `scroll` normalized in `ElementSpec.Parse` |
| A2 | sorted view/popup/flow export; no `break` on first flow |
| A3 | `LogWarning` at all six cited sites + LogAssert tests |
| A4 | no `new GUIStyle` in DesignSystem draw paths; no per-call `FindAssets` in `ResolveDefinition`; blanket postprocessor rescan gone |
| A5 | eviction tests green; only the shared registry postprocessor remains |
| A6/§5 | Wave-4 registries forward to the base; no `throw new ArgumentException` in any `Register` |
| A7 | empty-condition guard + test |
| A8 | `"Assets/Neo UI Generated/Materials"` literal gone |
| A9 | Task 8.1 schema greps pass |
| D1 | exactly ONE preset-field list (`PresetFields.cs`); consumer sites loop over it; icon/padding4 regression tests green |
| D2/D3 | resolved by deletion (verify `ConstraintWriteback` absent) + runtime guard (A7) |
| D4/D5 | `SpecWalk` exists; no private token scanner (or D4 noted resolved-by-deletion per kill list) |
| D6 | one `TryParseHex` usage pattern; one `ParseColorRef` |
| D7 | `Tab_`/chrome/folder literals only at const definitions |
| D9 | one `WriteSyncResult`; one `EffectParams` |
| E1–E5 | `AgentBridgeActions`, `FlowNodeKinds`, `NeoMenuItemKinds` exist; generator container seam consults `NeoElementKinds`; `CreateTab` consults `TryGetVariantColors` |
| Tests | `spacer`, `importSprites`, scene-build execution tests green |
| Boundaries | EditorUI standalone compile passes; no `UnityEngine.Input` outside the guarded UITooltip fallback |

3. Verifier reports PASS/FAIL per row with evidence; orchestrator re-opens failures.
4. Final commit + completion report mapping waves → commits → findings closed.

---

## 3. Explicitly OUT of scope (do not let subagents touch)

- Runtime `MenuPresenter`/`MenuControlKind` deep refactor beyond Task 7.1's editor-side seam.
- Migrating the 26 built-in element kinds into `NeoElementKinds` ("Phase 1" by design).
- Hard-validation built-ins bypassing `NeoValidationRules` (documented rationale).
- The Tier-1/Tier-2 batch split; exporter detection-chain ordering (D8's fingerprint table is
  deliberately deferred).
- Rebuilding any Composer feature beyond the four parity items in Wave 2 (no canvas, no in-editor
  multi-device viewport — Unity's Game view/Device Simulator and the bridge `preview` action cover
  that need; no spec-document undo layer — Unity undo covers native authoring).
- The uncommitted working-tree preset/animation feature files, except where a task lists one
  (notably `PresetPickerPopup.cs`, which Task 2.2 rehomes).
