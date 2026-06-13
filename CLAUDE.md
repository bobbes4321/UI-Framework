# UI Package (AlterEyes UI)

Unity 6 (6000.4.10f1) clean-room rebuild of Doozy UI Manager 4 into a fully fledged, reusable UI
package. Full feature spec: `Assets/docs/altereyes-ui-package-feature-spec.md`. Editor UX design
rationale + field catalog: `Assets/docs/editor-ux-analysis.md`. Visual-polish roadmap (typography,
icons, variants, theme bundles, juice — phased, with acceptance criteria):
`Assets/docs/ui-beautification-plan.md`.

## Layout

- `Assets/AE UI Package/Runtime` — asmdef `AlterEyes.UI` (containers, interactive, animation/tweens,
  flow graphs, signals, theming, ids/databases, settings, and `Graphics/` — `AEShape`, the SDF
  vector graphic all visuals are built from; its shader lives at `Resources/AEShape.shader`, params
  ride vertex channels UV1-3+tangent so ONE shared material batches everything — never give an
  AEShape its own material).
- `Assets/AE UI Package/Editor` — asmdef `AlterEyes.UI.Editor` (inspectors in `Inspectors/`,
  property drawers in `Drawers/`, flow graph window in `Flow/`, agent spec tooling in `Agent/`).
- `Assets/AE UI Package/Editor/EditorUI` — asmdef `AlterEyes.EditorUI`: **standalone** editor
  tooling kit (AEGUI, AEColors, AEStyles, AESearchablePopup, AEDropdown, AEListView). It must keep
  zero references to AlterEyes.UI so it stays liftable into other projects.
- `Assets/AE UI Package/Tests` — EditMode + PlayMode test asmdefs.
- `Assets/References/Doozy~` — Doozy 4 reference source (tilde = not imported). Port math/behavior
  from here; never import it, never copy its editor machinery.

## Hard constraints (apply to ALL code)

- **Agent-first**: everything addressable by category/name strings (never GUIDs); flat force-text
  ScriptableObjects; prefer signals (`Signals.On<T>` / `Signals.Send`) over serialized UnityEvents.
- **Editor performance**: no animated inspector chrome, no editor-tick subscriptions for visuals,
  no reflection scans on selection, one settings asset max (`Resources/AEUISettings`).
- **New Input System only** (`activeInputHandler=1`) — never `UnityEngine.Input`.

## Editor tooling conventions

All inspectors/drawers go through the EditorUI kit so everything looks and behaves the same:

- Inspectors: subclass `AEUIEditor` (see `Editor/Inspectors/ComponentEditors.cs`); Selectable-based
  components subclass `SelectableEditor`/`SliderEditor` and tuck the base GUI into
  `AEGUI.BeginFoldoutSection`. Header accents come from `AEColors` (Interactive=blue,
  Containers=cyan, Animation=orange, Flow=purple, Theming=pink, Signals=teal, Data=yellow).
- Any category/name string gets a searchable dropdown: `IdDatabaseOptions.DrawCategoryNamePair`
  (databases live on `AEUISettings`). Plain string pickers use `AEDropdown.StringPopup/ValuePopup`
  with an inline "+ Add" row — never modal dialogs.
- IMGUI rules: never create GUIStyles/ReorderableLists/SerializedObjects per OnGUI pass (cache —
  see `AEListView`/`AEStyles`); fetch dropdown options only when the dropdown opens; conditional
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
  -r:<Library/ScriptAssemblies/{AlterEyes.UI,UnityEngine.UI,UnityEditor.UI,Unity.TextMeshPro,Unity.InputSystem}.dll>
  <sources>` — use forward-slash output paths. Compile `Editor/EditorUI` alone (engine refs only) to
  verify the kit stays dependency-free.
- **When no editor holds the lock**: batch tests via
  `Unity.exe -batchmode -nographics -runTests -testPlatform EditMode|PlayMode -projectPath .`.
- Settings/databases are created via `Tools → AlterEyes UI → Create or Repair Settings`; the themed
  widget prefab library + Dark/Light palette + type scale via `Tools → AlterEyes UI → Create or
  Repair Starter Kit`. TMP SDF font assets (Inter + the Lucide icon font, committed under
  `AE UI Package/Fonts`) regenerate via `Tools → AlterEyes UI → Create or Repair Fonts`
  (`FontAssetBootstrap` — also wires `AEUISettings.iconFont`). Curated theme bundles
  (CleanSlate/NeonArcade/SoftFantasy — complete token/type/shape/motion systems) apply via
  `Tools → AlterEyes UI → Apply Theme Bundle` or spec `"theme": { "bundle": "..." }`
  (`Editor/ThemeBundles.cs`); the acceptance render (demo spec × every bundle) is
  `-executeMethod AlterEyes.UI.Editor.BeautificationAcceptance.Run` (needs graphics).
- Build UI hierarchies in editor code through `UIWidgetFactory` (Editor/Agent) — it is the single
  source of widget structure; the spec generator AND exporter both rely on its child names.
- Agent workflow with the editor OPEN: toggle `Tools → AlterEyes UI → Agent Bridge` once, then
  write `Temp/aeui-request.json` and read `Temp/aeui-result.json`. With the editor CLOSED, run the
  same requests headlessly: `Unity.exe -batchmode -projectPath . -executeMethod
  AlterEyes.UI.Editor.AgentBridge.RunBatch -aeuiRequest req.json -aeuiResult res.json` (req.json =
  one request or an array, processed in order; omit `-nographics` when screenshots/previews are in
  it; exit code 1 when any request fails). Actions:
  `{"action":"generate","spec":"path.json"}` · `{"action":"export","out":"path.json"}` ·
  `{"action":"validate"}` · `{"action":"screenshot","prefab":"Assets/...","out":"shot.png"}` ·
  `{"action":"preview","spec":"path.json","out":"dir"}` (renders a spec's views to PNGs across the
  resolution matrix IN-MEMORY — commits no prefabs/assets; the agent render-and-critique loop;
  `UISpecPreview`/`UIScreenshotter.CaptureLive`) · `{"action":"specReference"}` (writes
  `Assets/docs/spec-reference.md` + `aeui-spec.schema.json`) ·
  `{"action":"buildScene"}` (playable scene from generated assets; also
  `Tools → AlterEyes UI → Build Scene From Generated UI`) ·
  `{"action":"importSprites","folder":"Assets/..."}` (imports every texture under the folder as a
  Single sprite — run it BEFORE generating a spec whose image `src` points there; `textureType`
  alone leaves `spriteImportMode` None and no Sprite sub-asset exists)
  (screenshot output paths must live OUTSIDE Temp/ if they need to survive an editor exit; the
  screenshotter needs a graphics device — batch runs must omit -nographics).
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
  (rides AEGradient, tokens stay live), `src` on image (sprite asset path — rides an AEShape
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
- Soft design lint (`AgentValidation.ValidateDesign`, surfaced as `designWarnings` by the bridge's
  validate action): WCAG contrast on theme token pairs (3:1 for button labels — large text),
  raw fontSize where text styles exist, off-scale container spacing (4/8/12/16/24/32/48/64).
  It stays OUT of `ValidateAll` so hard validation contracts don't break.
