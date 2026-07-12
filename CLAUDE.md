# Neo UI Framework

Unity 6 clean-room rebuild of Doozy UI Manager 4 — a production-grade, fully extensible UI package. Namespaces: `Neo.*`, type prefix `Neo`.

## Layout

- `Runtime/Neo.UI` — containers, interactive, animation/tweens, flow graphs, signals, theming, ids, settings, `Graphics/NeoShape` (SDF-driven vector UI — all visuals batch on one shared material).
- `Editor/Neo.UI.Editor` — inspectors, drawers, flow graph window, agent spec tooling.
- `Editor/EditorUI/Neo.EditorUI` — **zero Neo.UI dependencies** — reusable editor UI kit (grid layout, search dropdowns, form rendering).
- `Tests/` — EditMode + PlayMode test asmdefs.
- `Assets/References/Doozy~` — reference source; port math/behavior, never copy editor code.

## Core principles

- **Extensible by design**: seams, not sealed lists. Every fixed set (widget kind, preset, theme token, flow node) must have an extensibility path — a `NeoKeyedRegistry<T>`, a `ScriptableObject`, `virtual`/`protected`, or `partial`. Ship defaults *through* the seam, never around it.
- **Agent-first**: category/name strings (not GUIDs), flat SOs, signals over UnityEvents.
- **Editor performance**: no animated chrome, no editor-tick subscriptions, no reflection scans, one settings asset (`Resources/NeoUISettings`).
- **New Input System only** — never `UnityEngine.Input`.

## Editor conventions

- **Inspectors** use `NeoUIEditor`/`NeoGUI.BeginFoldoutSection` with color-coded headers via `NeoColors`.
- **ID pickers** route through `IdDatabaseOptions.DrawCategoryNamePair` — auto-expands a searchable dropdown + quick-add + jump buttons. The rename seam: `INeoIdOwner.OwnId` → `NeoIdNaming` derives canonical GameObject names (`Type-Category_Name`), shared between native authoring and generation.
- **Color runtime-drivers** use `ColorDriverNotice` to warn when a component auto-drives a graphic's color. Registration seam: `NeoColorDrivers.Register`.
- **IMGUI** — cache GUIStyles/SerializedObjects/dropdown options (never recreate per-frame); multi-edit: guard `enumValueIndex < 0`, wrap writes in `BeginChangeCheck`. Example: `NeoListView`, `NeoStyles`.
- **Flow graph window** — one cached SerializedObject per window, no full repopulates on edits (preserves selection). Node creation is registry-driven (`FlowNodeKinds.Register`), not a type-check chain. Blueprint search overlay for creation + "Go To Node" jumps.
- **Play-mode pulse** — window subscribes (never polls) to `FlowController.OnActiveNodeChanged`, restyles in-place, flashes edges (`.flow-edge--pulse`), breadcrumb history. Nodes matched by name, not reference.

## Runtime invariants

- **No silent failures** — string-addressed lookups (command, flow, signal) that miss must warn. Examples: `UIView.ProcessCommand`, `FlowController.StartFlow`. Keep this on every new string-keyed lookup.
- **Back navigation** — `BackButton.Fire` is the unified sink (hardware Escape/gamepad, named back buttons, programmatic). Explicit wiring consumes the press to prevent double-navigation. Tests: `BackNavigationTests`.
- **Lifecycle** — cross-object commands defer to `Start()`, never OnEnable (order is arbitrary). Registries populate in OnEnable.
- **WYSIWYG** — prefab baked state = runtime start state (baked colors, `startValue`, enabled/disabled state). Any visual-driven widget needs this.
- **Dead-interaction lint** — `AgentValidation.ValidateInteractivity` flags buttons/tabs that do nothing. Run validate after every generate; keep in sync when adding wiring kinds.
- **Behavior tests** — `GeneratedFlowPlaythroughTests` (EditMode) clicks through every flow edge and asserts visibility. Add PlayMode tests for "renders but does nothing" bugs. `RuntimeBehaviourRegressionTests` covers enable-order races.
- **Tween lifetime** — bind tweens to targets via `tween.SetTarget(owner, get, set)` so destroyed targets self-stop instead of throwing forever. `UITick` logs-and-drops throwing tickables (loud once).

## Build & test workflows

**Editor open vs. headless:**
- Editor open — don't batch-compile. User will tab in and check the Console themselves, faster than Roslyn. Only run compile-check when user steps away or working headless (worktree, no editor). Roslyn reference: `/memory/roslyn-compile-check-worktree.md`.
- Editor closed — batch tests: `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode|PlayMode -projectPath .`

**Test isolation:**
- Tests use scratch roots (`NeoTestScratchRoot` EditMode, `NeoPlayModeScratchSettings` PlayMode) that redirect `UISpecGenerator.GeneratedRoot` to `Assets/NeoUITestScratch` and `NeoUISettings` to an in-memory clone. The committed demo under `Assets/Neo UI Generated` survives test runs.
- Production code reassigns `GeneratedRoot` ONLY via `NeoWorkspace.Scoped(showcase)` — a scoped context that throws if handed the default, preventing accidental deletes.

**Showcases:**
- Each showcase lives under `Assets/Showcases/{id}/` with its own `Generated/` root and `.unity` scene. Distinct roots = no collisions. Specs live in `Assets/Showcases/Specs/*.json`.
- `ShowcaseRegistry` lazy-discovers `ShowcaseDefinition` SOs — extensibility seam. `ShowcaseRunner.Regenerate` merges human drift via `SpecBaseline.Sync`.
- **Every user-visible feature must demo in a showcase.** Use `preview` action to render (no commits), verify across resolutions.

**Agent Bridge (with editor open):**
- Toggle `Tools → Neo UI → Advanced → Agent Bridge`, then write `Temp/neo-request.json` and read `Temp/neo-result.json`. Bridge runs `AssetDatabase.Refresh()` before each request.
- Headless: `Unity.exe -batchmode -projectPath . -executeMethod Neo.UI.Editor.AgentBridge.RunBatch -neoRequest req.json -neoResult res.json`
- Actions dispatch via `AgentBridgeActions` registry (extensibility seam); unknown actions error with available list.

**Key actions:**
- `{"action":"sync","incoming":"spec.json"}` — **the standing way to push agent changes.** Exports → diffs baseline → refuses if off-spec edits exist (unless `"force":true`) → three-way merge → generate → rewrite baseline. Preserves human prefab edits a stale spec would wipe.
- `{"action":"validate"}` — soft `designWarnings` + hard issues. Run after every generate.
- `{"action":"preview","spec":"spec.json","out":"dir"}` — renders views in-memory (no commits), agent render-loop.
- `{"action":"buildScene","flow":"name","showcase":"id"}` — flow-scoped scene build (prevents multi-spec leaks); showcases use isolated roots.
- `{"action":"regenerateShowcase","showcase":"id"}` — syncs a showcase's spec into its isolated root (use this, not `generate` which targets the shared default).
- `{"action":"bindings","spec":"spec.json","out":"manifest.json","stub":"Bindings.g.cs"}` — derives contract + emits partial-class stub. Stub lands OUTSIDE `GeneratedRoot` (survives regenerates).

**Round-trip safety:**
- Spec is source of truth; prefab is materialization. `SpecDiff.Compare` diffs two specs; `SpecMerge.Merge` is the three-way (baseline + live project + incoming). `OffSpecLint` flags off-spec edits that can't merge (dropped).
- Baseline: `.neo-baseline.json` in GeneratedRoot — the spec the assets were last generated from. Rewritten by every successful `generate`, `sync`, or "Fold Edits" in the Drift window.
- Human entry points: `Tools → Neo UI → Advanced → Sync With Spec…` (merge), `Check For Drift` (inspect), `Capture My Edits` (fold, no regen).
- Tests: `SpecDiffTests`, `SpecMergeTests`, `OffSpecLintTests`, `RoundTripSafetyTests`, `SyncProtocolTests`, `BaselineTests`.

**Key test families:**
- `GeneratedFlowPlaythroughTests` — clicks through every flow edge and asserts node/view/panel visibility (EditMode, sync dispatch).
- `RuntimeBehaviourRegressionTests` — enable-order races, stepper labels, progressor start state (PlayMode).
- `NativeAuthoringRoundTripTests` — native-created widgets == generated (byte-identical).
- `ValidateInteractivity` — flags dead buttons/tabs. Run after every generate.

**Extensibility seams (lazy discovery, no fork):**
- `NeoKeyedRegistry<T>` — central pattern. Examples: `FlowNodeKinds`, `NeoMenuItemKinds`, `AgentBridgeActions`, `NeoCommands`, `NeoDesignSystemTabs`, `ViewTransitionRegistry`, etc.
- `ScriptableObject` + `AssetPostprocessor` — drop an SO, it's discovered. Examples: `ShowcaseDefinition`, `UIAnimationPreset`, `NeoWidgetPreset`, `ThemeBundleDefinition`, `NeoLayoutTemplateDefinition`.
- Virtual/protected hooks — `INeoIdOwner`, `ColorDriverDescriptor` (register custom color-driver detection), `ShapeEffectRegistry`/`ParticleEffectRegistry` (register custom effects/particles).
- Partials — `NeoSetupWizard`, `NeoResetWizard` internals.
