# UI Package (Neo UI)

Unity 6 (6000.4.10f1) clean-room rebuild of Doozy UI Manager 4 into a fully fledged, reusable UI
package. Full feature spec: `Assets/docs/neo-ui-package-feature-spec.md`. Editor UX design
rationale + field catalog: `Assets/docs/editor-ux-analysis.md`. Visual-polish roadmap (typography,
icons, variants, theme bundles, juice — phased, with acceptance criteria):
`Assets/docs/ui-beautification-plan.md`.

> Formerly "AlterEyes UI" — rebranded to **Neo UI Framework** (namespaces `Neo.*`, type prefix
> `Neo`). Older commits/issues that say AlterEyes / `AE*` map to the current Neo names.

## Layout

- `Assets/Neo UI Framework/Runtime` — asmdef `Neo.UI` (containers, interactive, animation/tweens,
  flow graphs, signals, theming, ids/databases, settings, and `Graphics/` — `NeoShape`, the SDF
  vector graphic all visuals are built from; its shader lives at `Resources/NeoShape.shader`, params
  ride vertex channels UV1-3+tangent so ONE shared material batches everything — never give an
  NeoShape its own material).
- `Assets/Neo UI Framework/Editor` — asmdef `Neo.UI.Editor` (inspectors in `Inspectors/`,
  property drawers in `Drawers/`, flow graph window in `Flow/`, agent spec tooling in `Agent/`).
- `Assets/Neo UI Framework/Editor/EditorUI` — asmdef `Neo.EditorUI`: **standalone** editor
  tooling kit (NeoGUI, NeoColors, NeoStyles, NeoSearchablePopup, NeoDropdown, NeoListView). It must keep
  zero references to Neo.UI so it stays liftable into other projects.
- `Assets/Neo UI Framework/Tests` — EditMode + PlayMode test asmdefs.
- `Assets/References/Doozy~` — Doozy 4 reference source (tilde = not imported). Port math/behavior
  from here; never import it, never copy its editor machinery.

## Hard constraints (apply to ALL code)

- **Extensible by design**: every subsystem must be open for a *consuming project* to extend without
  forking the package. There is nothing worse than a team getting 90% of the way there with a great
  package and then being unable to do the last thing because it wasn't provided out of the box — so a
  team using this package must be able to cleanly and easily expand on it. Wherever code defines a
  fixed set — a widget kind, a catalog kind, an animation preset, a theme token, an inspector field, a
  flow node, an id database — ask "what does a project plausibly add here?" and ship an explicit
  **seam** for it (a documented interface, a `ScriptableObject`/registry the project populates, a
  `virtual`/`protected` hook, a `partial`), not a sealed `enum`/`switch`/hardcoded list. Ship sensible
  defaults *through* that seam, never around it. The bias to avoid: baking a list shut because today
  only two cases exist. (Worked example: `composer-catalog-unification-plan.md`.)
- **Agent-first**: everything addressable by category/name strings (never GUIDs); flat force-text
  ScriptableObjects; prefer signals (`Signals.On<T>` / `Signals.Send`) over serialized UnityEvents.
- **Editor performance**: no animated inspector chrome, no editor-tick subscriptions for visuals,
  no reflection scans on selection, one settings asset max (`Resources/NeoUISettings`).
- **New Input System only** (`activeInputHandler=1`) — never `UnityEngine.Input`.

## Editor tooling conventions

All inspectors/drawers go through the EditorUI kit so everything looks and behaves the same:

- Inspectors: subclass `NeoUIEditor` (see `Editor/Inspectors/ComponentEditors.cs`); Selectable-based
  components subclass `SelectableEditor`/`SliderEditor` and tuck the base GUI into
  `NeoGUI.BeginFoldoutSection`. Header accents come from `NeoColors` (Interactive=blue,
  Containers=cyan, Animation=orange, Flow=purple, Theming=pink, Signals=teal, Data=yellow).
- Any category/name string gets a searchable dropdown: `IdDatabaseOptions.DrawCategoryNamePair`
  (databases live on `NeoUISettings`). Plain string pickers use `NeoDropdown.StringPopup/ValuePopup`
  with an inline "+ Add" row — never modal dialogs.
- IMGUI rules: never create GUIStyles/ReorderableLists/SerializedObjects per OnGUI pass (cache —
  see `NeoListView`/`NeoStyles`); fetch dropdown options only when the dropdown opens; conditional
  display = draw or don't draw; guard `enumValueIndex < 0` (multi-edit mixed values); wrap manual
  `GUI.Toggle`-style writes in `BeginChangeCheck` so multi-edit drawing never stomps data.
- The flow graph window keeps ONE cached SerializedObject (foldout state lives there — recreating
  it per frame breaks expansion) and never fully repopulates the view on value edits (that destroys
  the GraphView selection). Renames must propagate to `FlowEdge.toNode` and `FlowGraph.startNode`.
- The graph's "Create Node/…" context menu is keyed by kind through `Editor/Flow/FlowNodeKinds.cs`
  (`NeoKeyedRegistry<FlowNodeDescriptor>`, Wave 7 Task 7.2) instead of `FlowGraphWindow`'s old
  hand-listed `AddCreateEntry<T>()` calls and `node is UINode || node is PortalNode || …` type-check
  chain — a descriptor owns its menu label, bare-instance factory and default-output-seeding policy,
  so a project's registered `FlowNode` subtype appears in the create menu via `FlowNodeKinds.Register`
  without forking this file.

## Runtime robustness rules (learned from the first playable generated scene)

- **No silent failures**: registry/name-addressed commands that match nothing must
  `Debug.LogWarning` (see `UIView.ProcessCommand`, `FlowController.StartFlow`). When adding new
  string-addressed lookups, keep this invariant.
- **Lifecycle**: components that *command* others on startup (FlowController) defer their first
  action to `Start()` — OnEnable order across a loading scene is arbitrary and registries fill in
  OnEnable. Never auto-start cross-object behavior from OnEnable.
- **WYSIWYG**: a widget's baked prefab state must equal its runtime start state (Progressor
  `SetCustomValue`+`startValue`, baked toggle/tab colors + serialized `isOnValue`). Any new widget
  with runtime-driven visuals needs the same treatment.
- **Dead-interaction lint**: `AgentValidation.ValidateInteractivity` flags buttons/tabs in
  generated views that do nothing when clicked. Keep it in sync when adding interaction wiring
  kinds, and run `{"action":"validate"}` after every generate.
- **Behavior tests, not just screenshots**: `Tests/PlayMode/RuntimeBehaviourRegressionTests.cs`
  covers the enable-order race, stepper labels and progressor start state — add a PlayMode test
  whenever a "renders fine but does nothing" bug is fixed. `Tests/EditMode/GeneratedFlowPlaythroughTests.cs`
  is the pipeline-level net: it generates the canonical demo spec, instantiates its views + flow
  graph in-memory and clicks through every flow edge + tab, asserting node/view/panel visibility
  (EditMode because flow advance is synchronous signal dispatch — no play-mode ticking needed).

## Build & test workflows

- **While the Unity editor is open on this project** (check `Temp/UnityLockfile` + `tasklist` for
  Unity.exe): don't batch-compile and never kill the editor. Compile-check editor code with Unity's
  Roslyn instead:
  `dotnet "<UnityDir>/Editor/Data/DotNetSdkRoslyn/csc.dll" -nologo -target:library -langversion:9.0
  -define:UNITY_EDITOR -r:<Data/Managed/UnityEngine/*.dll> -r:<Data/NetStandard/ref/2.1.0/netstandard.dll>
  -r:<Library/ScriptAssemblies/{Neo.UI,UnityEngine.UI,UnityEditor.UI,Unity.TextMeshPro,Unity.InputSystem}.dll>
  <sources>` — use forward-slash output paths. Compile `Editor/EditorUI` alone (engine refs only) to
  verify the kit stays dependency-free.
- **When no editor holds the lock**: batch tests via
  `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode|PlayMode -projectPath .`.
- **Tests no longer touch the committed demo** — `UISpecGenerator.GeneratedRoot` is a redirectable
  property (default `DefaultGeneratedRoot` = `Assets/Neo UI Generated`). The global `[SetUpFixture]`
  `NeoTestScratchRoot` (EditMode) and `SettingsCheatsDemoPlayTest` (PlayMode, via reflection) point it
  at a throwaway `Assets/NeoUITestScratch` for the duration of a run, so every
  `AssetDatabase.DeleteAsset(GeneratedRoot)` in test teardown only ever hits scratch — the committed,
  GUID-referenced demo under `Assets/Neo UI Generated` survives. Production code reassigns
  `GeneratedRoot` ONLY via `NeoWorkspace.Scoped(showcase)` (a `readonly struct IDisposable` that
  save-restores the root even on exception and THROWS if handed `UISpecGenerator.DefaultGeneratedRoot`,
  so a scope can never target/delete the committed demo); tests still use the scratch redirect.
- **Showcases (the Hub front door)**: each self-contained demo lives under `Assets/Showcases/{id}/`
  with its OWN `Generated/` (views/popups/flow/presets + `.neo-baseline.json`) and committed
  `{id}.unity` scene — distinct ids derive distinct roots, so two showcases can never collide in one
  bucket (the "N flows share one root" throw becomes structurally impossible). Specs live in
  `Assets/Showcases/Specs/*.json`. `ShowcaseRegistry` is the extensibility seam: code-seeded built-ins
  (`ShowcaseRegistryDefaults`) PLUS lazy discovery of every `ShowcaseDefinition` asset (a consuming
  project adds a showcase by dropping one SO — no fork, no C#). Open one from `Tools → Neo UI → Hub`:
  `ShowcaseRunner.Open` is the safe one-click (generates ONLY when nothing exists — never clobbers
  human edits — else fast-path `OpenScene`); `ShowcaseRunner.Regenerate` routes through
  `SpecBaseline.Sync` (merges human drift, refuses on off-spec edits) — but when Sync refuses on PURELY
  off-spec findings with NO spec-level human drift (`humanChanges` empty), that's factory-version drift
  from a `UIWidgetFactory` code change (current widget internals ≠ the older internals baked into the
  committed prefab), not a human edit, so Regenerate auto-rebuilds those factory-owned internals from
  spec (a pristine showcase never deadlocks; the rebuilt-internals count is logged, never silent); it
  still surfaces the Sync window when there IS spec-level human drift. The `game-ui` showcase's
  `postBuild` attaches the HUD director + bindings (`ShowcaseAugment.AttachGameUIDirector`). Other
  generated/bootstrapped assets (Starter kit, settings, fonts) live OUTSIDE any showcase root and
  survive test runs. Retired: the hand-built `DemoSceneBuilder` and the `ShowcaseSceneBuilder` (its
  director attach moved to the `game-ui` postBuild); the `Create Demo Scene` / `Build Scene From
  Generated UI` / `Build Showcase Scene` menu items folded into the Hub.
- **Demo every end-user-facing feature in a showcase**: the showcase scenes are the package's living,
  browsable catalog of what it can do, so any change an end user can SEE must show up in one — adding a
  new button `variant`/`size`, a widget kind, a menu row, a theme bundle, a shape/effect, a layout
  capability, etc. Extend the relevant existing spec when the feature fits it (e.g. a new button
  variant → add it to `Assets/Showcases/Specs/buttons.json`); add a NEW `Assets/Showcases/Specs/*.json`
  + a `ShowcaseRegistryDefaults` seed entry when it's a whole new aspect (e.g. a new widget family).
  Skip this ONLY for purely internal/agent-facing plumbing with no visible surface (exporters, merge,
  validation, bridge actions). A user-facing feature isn't "done" until its demo renders correctly —
  verify with the bridge `preview` action across the resolution matrix (it commits nothing).
- **New Project Setup wizard** (`Tools → Neo UI → Setup → New Project Setup…`, `Editor/NeoSetupWizard.cs`)
  is the guided front door for a fresh project: pick a starting look — a theme bundle (includes discovered
  `ThemeBundleDefinition`s) OR **custom colors** (one color per intent; hover/pressed derived via
  `ThemeBundles.BuildPalette`; optionally saved as a reusable `ThemeBundleDefinition`) — and tick what to
  include (Starter Kit / Fonts / Widget Presets / **Animation Library** / Effect Assets), then one click
  orchestrates the existing idempotent bootstraps in order and applies the look last. Also picks **Motion
  defaults** (optional) — a preset per headline animator role (View show/hide, Button hover/press) written
  to `NeoUISettings.animatorDefaults` so new widgets feel like the project by default. Surfaced in the Hub
  Tools tab; covered by `HubToolCoverageTests` (every Setup/Advanced menu item must have a Hub tool).
- **Design System editor** (`Tools → Neo UI → Design System`, `Editor/NeoDesignSystemWindow.cs`) is the
  ongoing authoring window over the live `NeoUISettings` + `Theme`: tabs for **Colors** (variant tokens +
  derive hover/pressed), **Buttons** (`ButtonVariantAsset` per-state colors + `contentToken` + sizes),
  **Shapes** (`ShapeStyle` radius/outline/softness), **Presets** (create/select `NeoWidgetPreset`s incl. a
  one-click "Primary Button"), **Motion** (default animation preset per animator role —
  `NeoUISettings.animatorDefaults`, the same data the Setup wizard seeds and animator `Reset()`/the factory
  consume). The Buttons tab shows a REAL rendered sample button (re-rendered on change
  via `UIScreenshotter.RenderToTexture` — the extracted texture core of `CaptureLive`); Shapes shows a faux
  fill/outline swatch. Edits the exact structures the factory consults, so they flow into generated and
  native-built UI.
- **Animation presets** — the motion system, designer- and agent-authorable end to end. A `UIAnimation`
  has FIVE independent channels: Move / Rotate / Scale / Fade / **Color** (the color/tint channel reuses
  the existing `ColorAnimation` — endpoints resolve to a theme token, a `#hex`, or the start/current color;
  default OFF so legacy animations are unchanged). `Tools → Neo UI → Setup → Create or Repair Animation
  Library` (`Editor/AnimationLibraryBootstrap.cs`) seeds a curated **~46-preset** library across seven
  categories — `Show`/`Hide` (fades, four-way slides, zoom, spin, drop-bounce), `Hover`/`Press`/`Click`
  (scale/tilt/tint, punch, jello, rubber-band, spin360), `Toggle`, `Loop` (pulse, heartbeat, breathe, bob,
  spin, shimmer) — create-missing-only, auto-discovered via `AnimationPresetRegistry`.
  - **Project defaults (the "how widgets feel" seam):** `NeoUISettings.animatorDefaults` maps an animator
    *role* → a default preset. Roles are an extensible registry (`NeoAnimatorRoles`: View/Show, View/Hide,
    Button/Hover, Button/Press, Toggle/On, Toggle/Off, Loop, OneShot — a project adds one via `Register`).
    `NeoUISettings.ApplyDefaultAnimation(role, target)` copies the configured preset in. An animator
    component's `Reset()` seeds its slots from these defaults on add, and `UIWidgetFactory.AddHoverAndPressFeel`
    routes through them (built-in scale-pop is the fallback when a role is unset) — so generated, native-built
    AND hand-added widgets all honor the one project choice. Edit defaults in the Setup wizard or the Design
    System **Motion** tab.
  - **Per-state inspector picker:** every animator inspector (`AnimationPreviewEditor.cs`,
    `AnimatorEditorGUI.PresetPicker`) shows a searchable dropdown per slot — presets whose category suits the
    slot's role first — that copies a preset into that state (`AnimationPresetRegistry.FullNamesForRole`/
    `GetByFullName` are the shared option source for the picker, wizard and Motion tab).
  - **Per-element spec field** `"animations": { "hover", "press", "selected", "disabled", "loop" }` (preset
    NAMES, like a view's `showAnimation`): hover/press/selected/disabled drive a `UISelectableUIAnimator`,
    `loop` adds a play-on-start `UIAnimator`. The applied names are stamped on a `NeoAnimationSourceTag` so
    they round-trip byte-identically (the animation analog of `WidgetPresetTag`). Demoed by the `animations`
    showcase (`Assets/Showcases/Specs/animations.json`). The spec's top-level `presets` section also carries
    the color channel now (`color`: from/to = `start`/`current`/`#hex`/token). Tests: `AnimationColorChannelTests`,
    `AnimatorDefaultsTests`, `ElementAnimationsRoundTripTests`.
- Settings/databases are created via `Tools → Neo UI → Setup → Create or Repair Settings`; the themed
  widget prefab library + Dark/Light palette + type scale via `Tools → Neo UI → Setup → Create or
  Repair Starter Kit`. TMP SDF font assets (Inter + the Lucide icon font, committed under
  `Neo UI Framework/Fonts`) regenerate via `Tools → Neo UI → Setup → Create or Repair Fonts`
  (`FontAssetBootstrap` — also wires `NeoUISettings.iconFont`). The reusable **widget-preset** library
  (named component styles like "Primary Button"/"Section Header" — `NeoWidgetPreset` SOs referenced by
  an element's `preset`) is seeded via `Tools → Neo UI → Setup → Create or Repair Widget Presets`
  (`PresetLibraryBootstrap`); the `NeoWidgetPresets` registry is the seam — a project adds one by
  dropping a `NeoWidgetPreset` asset (lazy discovery, no fork). Demoed by the `presets` showcase
  (`Assets/Showcases/Specs/presets.json`) — the "Widget Presets" catalog of the seeded library plus
  elements that reference one via `"preset": "..."`. Curated theme bundles
  (CleanSlate/NeonArcade/SoftFantasy — complete token/type/shape/motion systems) apply via
  `Tools → Neo UI → Setup → Apply Theme Bundle` or spec `"theme": { "bundle": "..." }`
  (`Editor/ThemeBundles.cs`) and ALSO seed/recolor the preset library to that personality (so a bundle
  = a full component library); the acceptance render (demo spec × every bundle) is
  `-executeMethod Neo.UI.Editor.BeautificationAcceptance.Run` (needs graphics). A project adds its own
  bundle WITHOUT C# by dropping a `ThemeBundleDefinition` asset (`Neo UI/Theme Bundle Definition`) —
  `ThemeBundleRegistry` lazy-discovers it (like `NeoWidgetPresets`/`ShowcaseRegistry`).
- **Designer-extensible SOs (lazy discovery, no fork, no C#):** the consistent seam across the package
  is "drop an asset, it's discovered". `UIAnimationPreset` assets now AUTO-DISCOVER via
  `AnimationPresetRegistry` (the generator resolves animation names through it — an explicitly-wired
  `NeoUISettings.animationPresets` entry still wins; no manual database listing needed), `NeoWidgetPreset`
  via `NeoWidgetPresets`, `ShowcaseDefinition` via `ShowcaseRegistry`, `ThemeBundleDefinition` via
  `ThemeBundleRegistry`. Each pairs a lazy `EnsureDiscovered` + an `AssetPostprocessor` that invalidates
  on `.asset` import. Authoring SOs get EditorUI-kit inspectors (`Editor/Inspectors/AuthoringInspectors.cs`
  for `ShowcaseDefinition`/`AnimationPresetDatabase`, plus the sectioned `ThemeEditor`).
- **Settings/cheats menu items** (a catalog's `items[]`, e.g. `{"toggle": {...}}`/`{"slider": {...}}`)
  are keyed by kind through `Editor/Agent/Menus/NeoMenuItemKinds.cs` (`NeoKeyedRegistry<MenuItemKindDescriptor>`,
  moved out of `UISpecGenerator`/`UISpecExporter`/`UISpec` in Wave 7 Task 7.1) instead of the old
  `MapKind`/`BuildMenuRow`/`UnmapKind` switches — a descriptor owns its spec↔runtime kind mapping, row
  build recipe, id-database pre-registration and typed-value conversion, and `MenuItemSpec.Kinds` reads
  the registry so a project's registered kind shows up in `MenuCatalogInspector`'s kind popup for free.
  The built-in 8 (label/button/toggle/switch/slider/stepper/dropdown/rebind) map to the runtime
  `MenuControlKind` enum, which stays closed by design — a project-registered kind with no enum slot
  still parses/exports/round-trips at the spec level, but its generated row bakes as a non-interactive
  Label (logged) because `MenuItemDefinition.kind` has nowhere else to put it; making a custom kind
  fully interactive at runtime needs a Runtime/ change (out of scope for Task 7.1 — see its handoff
  notes) to `MenuItemDefinition`/`MenuControlBinder`/`MenuWidgetLibrary`.
- Build UI hierarchies in editor code through `UIWidgetFactory` (Editor/Agent) — it is the single
  source of widget structure; the spec generator AND exporter both rely on its child names.
- Agent workflow with the editor OPEN: toggle `Tools → Neo UI → Advanced → Agent Bridge` once, then
  write `Temp/neo-request.json` and read `Temp/neo-result.json`. With the editor CLOSED, run the
  same requests headlessly: `Unity.exe -batchmode -projectPath . -executeMethod
  Neo.UI.Editor.AgentBridge.RunBatch -neoRequest req.json -neoResult res.json` (req.json =
  one request or an array, processed in order; omit `-nographics` when screenshots/previews are in
  it; exit code 1 when any request fails). `AgentBridge.HandleRequest` dispatches by looking the
  request's `"action"` up in `AgentBridgeActions` — a `NeoKeyedRegistry<BridgeAction>` (id + a
  `mutatesAssets` flag that alone decides the Play-mode refusal + the handler) — rather than a sealed
  switch, so a consuming project adds its own action via `AgentBridgeActions.Register` without
  forking this file; the unknown-action error enumerates `AgentBridgeActions.All` dynamically.
  `sync` and `regenerateShowcase` both shape their `SyncResult` through the one shared
  `AgentBridge.WriteSyncResult` (they return identical result key-sets). Actions:
  `{"action":"generate","spec":"path.json"}` · `{"action":"export","out":"path.json"}` ·
  `{"action":"validate"}` (issues + soft `designWarnings` + `offSpecWarnings` — editor edits that
  won't survive a regenerate) · `{"action":"diff","baseline":"path.json"}` (exports the project and
  diffs it against `baseline` or the stored `.neo-baseline.json`; returns `changes` + `offSpecWarnings`)
  · `{"action":"merge","incoming":"new.json","out":"merged.json","conflictPolicy":"preferTheirs"}`
  (three-way merge of stored baseline + live project + incoming spec; preserves human prefab edits
  against a stale incoming spec — returns `applied`/`conflicts`/`dropped`) ·
  `{"action":"sync","incoming":"new.json","force":false,"conflictPolicy":"preferTheirs"}` — **the
  STANDING way an agent changes generated UI** (use this, not `generate`). It is the safe-regenerate
  protocol: export → drift+lint vs the baseline → refuse if off-spec edits exist (they'd be lost;
  pass `"force":true` to proceed and have them returned in `dropped`, never silently) → three-way
  merge → generate from the merged spec → rewrite the baseline. Preserves human prefab edits a stale
  `incoming` would wipe; returns `ok`/`refused`/`regenerated`/`applied`/`conflicts`/`offSpecWarnings`/
  `dropped`/`merged`. Omit `incoming` to just capture the human's edits into the baseline (export +
  fold drift, no regenerate). `generate` stays the raw, unsafe primitive — first generation,
  scratch/test roots, explicit clean rebuilds — and now warns (`warning`) when run against a drifted
  tree. ·
  `{"action":"screenshot","prefab":"Assets/...","out":"shot.png"}` ·
  `{"action":"preview","spec":"path.json","out":"dir"}` (renders a spec's views to PNGs across the
  resolution matrix IN-MEMORY — commits no prefabs/assets; the agent render-and-critique loop;
  `UISpecPreview`/`UIScreenshotter.CaptureLive`) · `{"action":"specReference"}` (writes
  `Assets/docs/spec-reference.md` + `neo-spec.schema.json`) ·
  `{"action":"buildScene","flow":"<name>","scene":"path.unity","showcase":"<id>"}` (playable scene
  from generated assets; opening a showcase is normally done from the Hub, not this action).
  `GeneratedRoot` is a SHARED bucket — every spec generated since the last wipe accumulates there.
  The build is therefore flow-scoped: it builds ONE flow (= one app) and instances ONLY the views
  that flow references. `"flow"` is optional when a single flow exists; with several it is REQUIRED —
  `GeneratedSceneBuilder.SelectFlowGraph` throws rather than silently picking one, so a second spec's
  screens can't leak into the scene. Optional `"scene"` = explicit scene output path (defaults to the
  legacy `GeneratedSceneBuilder.ScenePath`). Optional `"showcase"` = build a registered showcase by id:
  it generates from the showcase's spec when nothing is there yet and builds inside the showcase's
  isolated `Generated/` root + `{id}.unity` scene (`NeoWorkspace.Scoped`), so a showcase build never
  collides with the shared default root; an explicit `"flow"`/`"scene"` override the showcase's derived
  values. The committed ColorACube demo → flow `ColorACube`; the GameUI demo → flow `GameUI`.
  Regression: `Tests/EditMode/SceneBuilderFlowScopingTests.cs`) ·
  `{"action":"regenerateShowcase","showcase":"<id>"}` (the scoped, baseline-aware regen of a registered
  showcase from its changed spec INTO its own isolated `Generated/` root — routes through
  `ShowcaseRunner.Regenerate` → `SpecBaseline.Sync`, auto-rebuilds factory-owned widget internals when
  there's no spec-level human drift; the agent-first counterpart to the Hub's Regenerate. Returns the same
  result shape as `sync`. Use this — not `generate` (wrong root) or `buildScene` (only generates when
  nothing exists yet) — to push spec edits into an existing showcase) ·
  `{"action":"importSprites","folder":"Assets/..."}` (imports every texture under the folder as a
  Single sprite — run it BEFORE generating a spec whose image `src` points there; `textureType`
  alone leaves `spriteImportMode` None and no Sprite sub-asset exists) ·
  `{"action":"bindings","spec":"path.json","out":"GameUI.bindings.json","stub":"Assets/Scripts/Generated/GameUIBindings.g.cs","namespace":"Game.UI"}`
  (the developer wiring story: derives the binding contract — domain signals / data sources / settings /
  cheats / views — from the spec, then emits a `// <auto-generated>` partial-class C# stub the developer
  fills in. `spec` optional → falls back to the exported project; all of `out`/`stub`/`namespace`
  optional, `"manifestOnly":true` skips the stub; the manifest JSON is always inlined in the result. The
  stub lands OUTSIDE `GeneratedRoot` (default `Assets/Scripts/Generated`, also `Tools → Neo UI → Advanced
  → Generate Binding Stub`) so a UI regenerate never wipes it, and regenerates idempotently — greppable `const` ids
  for every view/signal/data source/setting, a `Wire()` of `Signals.On(…)`/`UserSettingsService.Bind(…)`
  calls into empty `partial void` hooks, and a `Populate…` helper per data source. Implement the hooks in
  your own SIBLING partial (`<Flow>UIBindings.Handlers.cs`); the generator never touches it.
  `BindingManifest`/`BindingStubGenerator`, tests `BindingManifestTests`/`BindingStubTests`)
  (screenshot output paths must live OUTSIDE Temp/ if they need to survive an editor exit; the
  screenshotter needs a graphics device — batch runs must omit -nographics).
- First-class domain signals: toggle/slider/dropdown take an optional `signal` ("Category/Name") that the
  widget publishes its typed value to (bool/float/int) IN ADDITION to its standard "…/Behaviour" stream —
  so game code does `Signals.On<bool>("Audio","Muted", …)` directly instead of branching the firehose
  (button equivalent is `onClick.signal`). Additive: standard streams + flow triggers are unchanged.
  Wired in `UISpecGenerator`, round-trips via `UISpecExporter` (`DomainSignalTests`, `BindingManifestTests`).
- Typed/incremental data: `UIData.Set<T>(category,name,rows,project)` supplies the domain→token projection
  once; `UIData.Update/Add/RemoveAt` patch a single spawned row via `UIBoundList.UpdateRow/InsertRow/RemoveRow`
  (re-token only the affected row, no full `Rebuild`). The string `Set(…)` API is untouched (`TypedDataTests`).
- Round-trip safety (the spec is the source of truth; the prefab is its materialization): before
  letting an agent regenerate over human prefab edits, check what would be lost. `SpecDiff.Compare`
  structurally diffs two specs (stable `SpecPath` addresses); `SpecMerge.Merge` is the three-way
  merge (base = stored baseline, ours = exported project, theirs = incoming spec) that folds human
  drift in so `generate` won't wipe it — collisions surface in `conflicts`, never swallowed.
  `OffSpecLint` flags edits BELOW a composite widget root (raw colors/materials, internal geometry,
  added/removed internals) that the exporter can't see — these can't be merged (they land in
  `dropped`/`offSpecWarnings`) so the fix is to bind a theme token or move the change into the spec.
  The baseline is a hidden `{GeneratedRoot}/.neo-baseline.json` (`NeoBaseline`, raw read/write) — the
  exact spec the committed assets were last generated from. It is rewritten by every successful
  `generate` (to the exported project, so drift reads zero right after), by `sync`, and by the Drift
  window's "Fold Edits". Human entry point: `Tools → Neo UI → Advanced → Check For Drift` (`DriftWindow`) — green
  = round-trips, red = will be lost; "Fold Edits Into Spec" re-captures the baseline. The **policy
  layer** on top (`SpecBaseline`, the `sync` action) enforces the invariant that the live, merged spec
  — not whatever the agent last wrote — is always the canonical input to the next generate; agents
  call `sync` rather than `generate`. Human sync entry points: `Tools → Neo UI → Advanced → Sync With Spec…`
  (merge an agent's incoming spec, surfacing conflicts/off-spec in a window) and `Tools → Neo UI →
  Advanced → Capture My Edits` (fold the current project into the baseline, no regenerate). Tests: `SpecDiffTests`,
  `SpecMergeTests`, `OffSpecLintTests`, `RoundTripSafetyTests`, `SyncProtocolTests`, `BaselineTests`.
- Spec element kinds: button, toggle, switch, tab, slider, progress, tabbar, list/scroll, vstack,
  hstack, grid, panel (a `UIPanel : UIContainer` content surface a tab shows/hides — see tab
  `controls` below), overlay (z-stack: children keep anchors/positions inside layout cells —
  card art + corner badges + pinned labels; `UIOverlay` marker round-trips it), spacer, text,
  image, shape (incl. Ring/Arc + `thickness`/`arcStart`/`arcSweep`),
  icon (Lucide name via `IconMap`), counter, input, stepper, safearea (SafeAreaFitter container
  with free-anchored children). Styling fields: `textStyle` (theme TextStyle by name — owns the
  size, raw `fontSize` is the styleless fallback), button `variant` (primary/secondary/ghost/
  danger) + string-form `size` (sm/md/lg — polymorphic with the `[w,h]` array, read back from
  `WidgetStyleTag`), `preset` (name of a reusable `NeoWidgetPreset` — the Figma-style component layer:
  resolved at generate as the BASE with element fields overriding; exports as the preset name + only
  the override delta via `WidgetPresetTag`, so the link survives round-trip — see
  `widget-presets-plan.md`; the preset's `motion` field seeds the element's on-start `loop` animation
  channel, stripped back out on export like any other delta. Which fields a preset governs lives in
  ONE place, `Editor/Agent/PresetFields.cs` — an ordered `PresetField` descriptor table (name +
  element/preset get-set-clear + equality) looped over by the generator's merge, the exporter's
  delta, and native authoring's Apply/Create/Update/Reset preset workflow; a project adds a custom
  preset-governed field via `PresetFields.Register` — the extension seam — instead of touching any
  of those call sites), `icon` + `badge` on button/tab, `gradient` `{from,to,angle}` on shape/image
  (rides NeoGradient, tokens stay live), `src` on image (sprite asset path — rides an NeoShape
  texture fill so `radius` rounds the corners; full-rect sprites only, the shared material
  survives because the texture binds per CanvasRenderer; missing sprites report an issue),
  `"style": "radial"` on progress (arc dial via
  `ShapeProgressTarget`), `bind`+`item` on list/grid (data-bound rows: `item` is the row template,
  cloned per `UIData.Set(category,name,rows)` row at runtime with `{key}` tokens filled — see
  `Runtime/Data/UIBoundList`+`UIData`; the template bakes inactive, spawned rows aren't exported),
  `cascade` on vstack/hstack/grid (`UICascadeChildren`), `controls` on a tab
  (id of a sibling `panel` it shows/hides — generator wires `UITab.containerReference` in a deferred
  per-view pass and bakes WYSIWYG start visibility; selected tab's panel shown, the rest hidden),
  `value: 1` on a standalone tab (bakes the selected sidebar entry; tabbars bake their first tab).
  **Responsive layout (Composer overhaul, `composer-authoring-overhaul`):** every element takes an
  optional `layout` object — a Figma-style per-axis constraint+offset model (`h`/`v` ∈
  left|right|leftRight|center|scale, `offset`, `size`, per-child `sizing` ∈ fixed|hug|fill) that maps
  to Unity anchorMin/Max/pivot + offsetMin/Max (NOT absolute `anchoredPosition`, the old "disappears
  in landscape" bug) so elements survive aspect/orientation change. Additive: `layout` wins over the
  legacy `anchor`/`position`/`size`/`flex`; the 16 anchor presets re-express as constraint pairs
  (`ConstraintLayout` in `UIWidgetFactory`, marker `NeoLayoutTag`). `padding4` `[l,t,r,b]` = per-side
  container padding (wins over uniform `padding`). Top-level `breakpoints` (ordered named conditions:
  orientation/minAspect/maxAspect/minWidth/maxWidth) + per-element `overrides` (breakpoint-name →
  delta `LayoutSpec`, cascades over base via `LayoutSpec.MergedWith`) drive a runtime
  `UIResponsiveRoot` that applies the matching breakpoint's pre-resolved layout on resize/orientation,
  only on change (base bakes = WYSIWYG). All round-trip byte-identical; merge via the single new
  `SpecPath` breakpoints-by-name key. Seams: `LayoutConstraints`/`LayoutSizingModes`/
  `BreakpointConditions` registries. Opt-in `Tools → Neo UI → Advanced → Migrate Spec To Layout Model` rewrites
  legacy → `layout` (never automatic). Tests: `LayoutSpecParseTests`, `ConstraintLayoutRoundTripTests`,
  `ConstraintResponsivenessTests`, `SpecMigrationTests`, `BreakpointRoundTripTests`,
  `BreakpointCascadeTests`, `ResponsiveDriverTests` (PlayMode), `PaddingRoundTripTests`.
  Popups: plain `{name,title,message}` keeps the canonical OK card; rich popups add `elements`
  (same vocabulary, stacked in the card), `size` [w,h] and `close: true` (X button on the card
  corner); a button `"onClick": {"close": true}` hides its popup (`HideContainerOnClick`); the
  generator fills the UIPopup indexed slots (labels/images/buttons) for the Doozy-style runtime
  APIs (`SetTexts/SetSprites/SetEvents`). Flow nodes take
  `view` (one), `views` (several) and `hide` lists. Export → generate → export must stay
  byte-identical (`SpecLayoutAndWidgetTests`, `TypographyTests`, `IconAndVariantTests`,
  `DepthAndShapeTests`, `JuiceTests`, `CompositionAndRichPopupTests`) — any new spec field needs
  deterministic export. NEVER let
  the exporter fall back to scanning all of Assets when a generated subfolder is missing
  (`UISpecExporter.FindGenerated`) — it would hijack committed demo/starter popups.
- **Shape effects + UI particles** (element MODIFIERS, not new kinds — they ride alongside any element):
  `"effect": { "id": "...", "params": {...} }` and `"particles": { ...scalars..., "modules":[…],
  "signal":{…} }`. Both are OPEN bags dispatched through editor descriptor registries so the spec
  pipeline carries NO per-effect switch — the extension seams are `ShapeEffectRegistry`
  (`IShapeEffectDescriptor`) and `ParticleEffectRegistry` (`IParticleModuleDescriptor`); a project adds
  an effect/module by registering one descriptor (each Tier-1 descriptor lives in its OWN file under
  `Editor/Agent/Effects/` — the per-descriptor seam — and is registered from
  `ShapeEffectRegistry.RegisterBuiltins`). **Tier-1 effects** (glowPulse / sheenSweep / gradientCycle /
  arcSpinner [Ring/Arc loading spinner — animates arcStart/arcSweep/ringThickness] / cornerMorph
  [breathing cornerRadius/cornerRadii] / borderPulse [focus-ring border+outline] / hueShift [rainbow
  fill hue] / transformJuice [RectTransform bob/sway/rotate/scale/squash — batch-safe because it never
  touches the material]) only animate fields `OnPopulateMesh` already reads (or the transform), so they
  stay on the shared NeoShape batch (`descriptor.BatchSafe == true`); a **Tier-2** `variant` effect
  swaps the material for a custom shader (`ShapeEffectDefinition`/`NeoShapeVariant`, `BatchSafe == false`)
  — a deliberate batch split. Built-in Tier-2 definitions (`dissolve` / `holoFoil` [iridescent foil] /
  `glitch` [RGB-split + block-tear]) are code-seeded by `NoiseAssetBootstrap`
  (`Tools → Neo UI → Setup → Create or Repair Effect Assets`).
  **Live control** — any effect param can be driven at runtime by a domain signal: add
  `"bindings": [ { "signal":"Category/Name", "param":"softnessMax", "min":2, "max":36 } ]` to an
  effect's params (the special `"param":"enabled"` consumes a bool signal to toggle the whole effect).
  A `slider`/`toggle`'s `signal` publishes into the stream; a `NeoSignalParamBinding` remaps it onto
  the param via the open `NeoShapeEffect.TrySetLiveParam(param, value)` seam (each effect owns its param
  names — no central switch). **Pointer interactivity** (the mobile-game feel): an effect bag takes
  `"trigger":"hover"|"press"` + `"triggerMode":"hold"|"playOnce"` (→ `NeoEffectTrigger`, runs the effect
  only on hover / while pressed); a particle bag takes `"atPointer": true` (→ `NeoParticlePointerBurst`,
  bursts at the click point); any element takes `"pointerGlow": { "color", "size", "softness" }`
  (→ `NeoPointerReactor`, a soft glow that follows the cursor). All three are runtime/play-mode only
  (editor stays WYSIWYG) and round-trip through the spec.
  A Tier-2 `variant` optionally ANIMATES a named material float over the same timeline: add
  `animate` (the shader property, e.g. `_DissolveAmount`) + `from`/`to` + the shared timeline keys
  (`duration`/`loop`/`pingPong`/`ease`/`restingPhase`) to its params and the descriptor attaches a
  `NeoMaterialFloatCycle` (the general Tier-2 material-float driver — `NeoShapeEffect` subclass). At
  runtime it lazily clones the variant's shared material per-instance and `SetFloat`s the clone
  (never the committed shared asset); in edit mode it is a material no-op so the baked default stays
  WYSIWYG. Omit `animate` ⇒ a static variant exactly as before (fully backward compatible).
  Particles render as POOLED `NeoShape` instances inside the canvas (one GameObject per live particle,
  sharing the one material — `NeoParticleEmitter`), so they inherit masking/sort/scaling for free;
  burst-only by default, `rate > 0` enables continuous emission, an optional `signal` adds
  `NeoParticleBurstOnSignal`. The published per-vertex channel layout a Tier-2 shader authors against
  is `Assets/docs/neoshape-channel-layout.md` (proof shaders `NeoShapeDissolve.shader` /
  `NeoShapeHoloFoil.shader` / `NeoShapeGlitch.shader`). Round-trip via
  `UISpecGenerator`/`UISpecExporter` (`ShapeEffectRoundTripTests`, `ShapeEffectLibraryTests`,
  `InteractiveEffectsTests`, `ParticleRoundTripTests`); the particle test guards the emitter's
  `FindProperty` field names against drift. Cost-honesty surfaces in `AgentValidation.ValidateDesign`
  (`designWarnings`): a Tier-2 variant warns it breaks the batch, a continuous/high-capacity emitter
  warns about per-frame cost. The `effects` showcase (`Assets/Showcases/Specs/effects.json`) demos the
  whole surface — Gallery (every effect), Interactive (hover/press/click/cursor triggers), Playground
  (sliders bound live to a glow), Combos (effects on real UI).
- **Native-Unity authoring** (`Editor/Authoring/`) is the first-class, familiar way to build UI — the
  GameObject menu + scene view + native inspectors + real prefabs — with a one-click path back to the
  spec, so a developer never has to learn a bespoke window. Three pieces: (1) **creation** —
  `GameObject → Neo UI → …` (`NeoCreateMenu`) drops a widget into the selection via
  `NeoSceneAuthoring.CreateWidget`, which routes through `UISpecGenerator.BuildElementLive` (the SAME
  build path generation uses, so a created widget is byte-identical to a generated one — proven by
  `NativeAuthoringRoundTripTests`) and bootstraps a Canvas/EventSystem (New Input System) like Unity's
  own UI create; the menu's "More Widgets…" is data-driven off `NeoWidgetPalette.All` so custom
  `NeoElementKinds` appear for free (rehomed off the Composer in Wave 2 Task 2.1, along with
  `NeoLayoutTemplates` — the curated layout-scaffold registry, `Editor/Authoring/Templates~/*.json` —
  and `NeoCatalogKinds`/`NeoWidgetOptions`, its `Editor/Agent/` spec-tooling siblings). The menu's
  "Insert Template…" lists `NeoLayoutTemplates.All` and instantiates the chosen scaffold's top-level
  elements (every view/popup's `elements`) under the selection via `NeoSceneAuthoring.InsertTemplate`
  — the SAME `BuildElementLive` path, all created roots under one undo step; a template's own
  title/message/close popup chrome is out of scope (only its `elements` insert) — proven by
  `TemplateInsertTests.NativeInsertTemplate_BuildsElementTreeUnderSelectedView`. (2) **capture** —
  `NeoCapture.CaptureView` folds a hand-built
  `UIView` back into its showcase's spec + baseline by materializing it into the showcase
  `Generated/Views` root and running the standing `SpecBaseline.CaptureEdits` protocol INSIDE
  `NeoWorkspace.Scoped` — reusing the whole export/off-spec-lint/merge/baseline safety layer (off-spec
  edits still refuse unless forced); attribution resolves via `GeneratedMarker.showcaseId` then the
  active scene path, else the user picks/creates a showcase (`NeoCapture.CreateShowcase`). (3)
  **scene-view overlay** — `NeoSceneOverlay` ([`Overlay(typeof(SceneView))`], Odin-Validator-style)
  shows a drift-status dot (`DriftStatus.Scan`, shared with `DriftWindow`) + one-click Capture-to-Spec
  / Validate / Check-Drift / Add-Widget / **Apply-Preset** / Create-Preset / Update-Preset / Reset-To-Preset
  when a `UIView` is selected; selection-driven and cached (no per-repaint scans). Native authoring IS
  the one authoring surface — the Composer (the from-scratch, no-agent spec-editing window that lived
  under `Editor/Composer/`) was removed 2026-07 once native authoring reached parity; its surviving,
  rehomed pieces are documented above and in this bullet (`NeoWidgetPalette`, `NeoLayoutTemplates`,
  `NeoCatalogKinds`, `NeoWidgetOptions` in `Editor/Agent`/`Editor/Authoring`; `PresetPickerPopup` in
  `Editor/Authoring`; `PresetThumbnailCache`/`PresetThumbnailRenderer` and `MenuCatalogInspector` in
  `Editor/Inspectors`; `SpecField`/`FieldKind` folded into `Editor/Agent/NeoElementKinds.cs`;
  `ComposerDevicePresets` rehomed to `Editor/Agent/ComposerDevicePresets.cs`; the old `ComposerFactory`
  rehomed and renamed to `Editor/Agent/SpecFactory.cs`'s `SpecFactory`) — template insertion lives on
  the native create menu ("Insert Template…"), breakpoint-override authoring is native (Capture Layout
  As Override / Preview Breakpoint on the scene-view overlay), and preset create/update/reset-from-
  selection are native scene-view overlay actions (below). `BuildElementLive` / `ViewPrefabPath` are the
  generator seams; `DriftStatus` the shared drift seam. **Apply-Preset** opens `PresetPickerPopup` (`Editor/Authoring/`, a
  kind-scoped thumbnail-card grid, thumbnails via `Editor/Inspectors/PresetThumbnailCache`+
  `PresetThumbnailRenderer`) anchored to the button; the chosen preset routes into
  `NeoSceneAuthoring.ApplyPreset`, which rebuilds the selected widget under that `NeoWidgetPreset` by
  capturing its spec via the now-`internal` `UISpecExporter.ExportElement`, keeping kind/id/label/icon but
  dropping captured styling so the preset drives the look; placement + sibling order preserved, one undo.
  **Create/Update/Reset** (`NeoSceneAuthoring.CreatePresetFromWidget`/`UpdatePresetFromWidget`/
  `ResetWidgetToPreset`) are the native counterpart to the (retired) Composer inspector's preset
  workflow: Create saves the selected widget's captured styling as a new `NeoWidgetPreset` asset and
  relinks the widget to it (via Apply-Preset); Update pushes the widget's current styling into its
  already-linked preset asset; Reset clears just the preset-governed fields back to the preset's own
  values (unlike Apply-Preset, it keeps the widget's other data — layout, bindings, etc. — intact) and
  rebuilds in place. Tests: `NativePresetWorkflowTests`.
- Soft design lint (`AgentValidation.ValidateDesign`, surfaced as `designWarnings` by the bridge's
  validate action): WCAG contrast on theme token pairs (3:1 for button labels — large text),
  raw fontSize where text styles exist, off-scale container spacing (4/8/12/16/24/32/48/64).
  It stays OUT of `ValidateAll` so hard validation contracts don't break.
