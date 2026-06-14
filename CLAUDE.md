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
  GUID-referenced demo under `Assets/Neo UI Generated` survives. Production code must never reassign
  `GeneratedRoot`.
- **To (re)generate the committed demo itself**: headless `AgentBridge.RunBatch` with
  `[{"action":"generate","spec":"Assets/Mockups/ColorACube/color-a-cube.json"},{"action":"buildScene","flow":"ColorACube"}]`
  (the generated views/popups/flow + the `NeoUIGeneratedDemo` scene; name the `flow` since the
  showcase's `GameUI` flow may share `GeneratedRoot`). Other generated/bootstrapped
  assets (Starter kit, settings, fonts) live OUTSIDE `GeneratedRoot` and survive test runs.
- Settings/databases are created via `Tools → Neo UI → Create or Repair Settings`; the themed
  widget prefab library + Dark/Light palette + type scale via `Tools → Neo UI → Create or
  Repair Starter Kit`. TMP SDF font assets (Inter + the Lucide icon font, committed under
  `Neo UI Framework/Fonts`) regenerate via `Tools → Neo UI → Create or Repair Fonts`
  (`FontAssetBootstrap` — also wires `NeoUISettings.iconFont`). Curated theme bundles
  (CleanSlate/NeonArcade/SoftFantasy — complete token/type/shape/motion systems) apply via
  `Tools → Neo UI → Apply Theme Bundle` or spec `"theme": { "bundle": "..." }`
  (`Editor/ThemeBundles.cs`); the acceptance render (demo spec × every bundle) is
  `-executeMethod Neo.UI.Editor.BeautificationAcceptance.Run` (needs graphics).
- Build UI hierarchies in editor code through `UIWidgetFactory` (Editor/Agent) — it is the single
  source of widget structure; the spec generator AND exporter both rely on its child names.
- Agent workflow with the editor OPEN: toggle `Tools → Neo UI → Agent Bridge` once, then
  write `Temp/neo-request.json` and read `Temp/neo-result.json`. With the editor CLOSED, run the
  same requests headlessly: `Unity.exe -batchmode -projectPath . -executeMethod
  Neo.UI.Editor.AgentBridge.RunBatch -neoRequest req.json -neoResult res.json` (req.json =
  one request or an array, processed in order; omit `-nographics` when screenshots/previews are in
  it; exit code 1 when any request fails). Actions:
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
  `{"action":"buildScene","flow":"<name>"}` (playable scene from generated assets; also
  `Tools → Neo UI → Build Scene From Generated UI`. `GeneratedRoot` is a SHARED bucket — every spec
  generated since the last wipe accumulates there. The build is therefore flow-scoped: it builds ONE
  flow (= one app) and instances ONLY the views that flow references. `"flow"` is optional when a
  single flow exists; with several it is REQUIRED — `GeneratedSceneBuilder.SelectFlowGraph` throws
  rather than silently picking one, so a second spec's screens can't leak into the scene. Showcase →
  `GameUI` (`ShowcaseSceneBuilder.ShowcaseFlow`); the committed ColorACube demo → `ColorACube`.
  Regression: `Tests/EditMode/SceneBuilderFlowScopingTests.cs`) ·
  `{"action":"importSprites","folder":"Assets/..."}` (imports every texture under the folder as a
  Single sprite — run it BEFORE generating a spec whose image `src` points there; `textureType`
  alone leaves `spriteImportMode` None and no Sprite sub-asset exists) ·
  `{"action":"bindings","spec":"path.json","out":"GameUI.bindings.json","stub":"Assets/Scripts/Generated/GameUIBindings.g.cs","namespace":"Game.UI"}`
  (the developer wiring story: derives the binding contract — domain signals / data sources / settings /
  cheats / views — from the spec, then emits a `// <auto-generated>` partial-class C# stub the developer
  fills in. `spec` optional → falls back to the exported project; all of `out`/`stub`/`namespace`
  optional, `"manifestOnly":true` skips the stub; the manifest JSON is always inlined in the result. The
  stub lands OUTSIDE `GeneratedRoot` (default `Assets/Scripts/Generated`, also `Tools → Neo UI → Generate
  Binding Stub`) so a UI regenerate never wipes it, and regenerates idempotently — greppable `const` ids
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
  window's "Fold Edits". Human entry point: `Tools → Neo UI → Check For Drift` (`DriftWindow`) — green
  = round-trips, red = will be lost; "Fold Edits Into Spec" re-captures the baseline. The **policy
  layer** on top (`SpecBaseline`, the `sync` action) enforces the invariant that the live, merged spec
  — not whatever the agent last wrote — is always the canonical input to the next generate; agents
  call `sync` rather than `generate`. Human sync entry points: `Tools → Neo UI → Sync With Spec…`
  (merge an agent's incoming spec, surfacing conflicts/off-spec in a window) and `Tools → Neo UI →
  Capture My Edits` (fold the current project into the baseline, no regenerate). Tests: `SpecDiffTests`,
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
  `WidgetStyleTag`), `icon` + `badge` on button/tab, `gradient` `{from,to,angle}` on shape/image
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
  `BreakpointConditions` registries. Opt-in `Tools → Neo UI → Migrate Spec To Layout Model` rewrites
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
- The **Composer** (`Tools → Neo UI → Composer`, `Editor/Composer/NeoComposerWindow.cs`) is the
  from-scratch, no-agent authoring surface: it edits a `UISpec` in memory and regenerates the prefab
  as a live preview, so everything round-trips losslessly by construction (the spec stays the single
  source of truth — never a scene-first editor). Tree · canvas · inspector panes, plus: a **widget
  palette** with drag-to-create onto canvas/tree (`ComposerPalette` registry — auto-includes
  `NeoElementKinds`) and **layout templates** (`ComposerTemplates`, `Editor/Composer/Templates~/*`);
  a **free, draggable/resizable viewport** with device presets (`ComposerDevicePresets` registry),
  rotate, zoom, and a CanvasScaler-equivalent so content scales like a real device
  (`SpecPreviewPane`, gated `RenderOptions.deviceScale` keeps agent renders byte-stable); a
  **Figma-grade canvas** — constraint-aware drag/resize writeback (`ConstraintWriteback`),
  drag-to-reorder in layout groups, smart alignment guides + equal-spacing, multi-select
  align/distribute, keyboard nudge/duplicate/delete; a **constraint-widget inspector**
  (`NeoConstraintWidget` in the EditorUI kit, Neo.UI-free) with Fixed/Hug/Fill sizing + auto-layout
  panel; **breakpoint authoring** (`BreakpointBar` — scope inspector edits to a breakpoint's override
  delta); and **live preview** (theme recolor, sample rows in bound lists via `PreviewSampleData`,
  breakpoint-override preview via an effective-spec merge). Every new fixed set is an extension seam.
- Soft design lint (`AgentValidation.ValidateDesign`, surfaced as `designWarnings` by the bridge's
  validate action): WCAG contrast on theme token pairs (3:1 for button labels — large text),
  raw fontSize where text styles exist, off-scale container spacing (4/8/12/16/24/32/48/64).
  It stays OUT of `ValidateAll` so hard validation contracts don't break.
