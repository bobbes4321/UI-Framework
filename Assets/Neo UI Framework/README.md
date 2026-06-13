# Neo UI Package (`com.neo.ui`)

Doozy UI Manager 4 replacement, implementing the
[feature spec](../docs/neo-ui-package-feature-spec.md). Runtime assembly `Neo.UI`,
editor assembly `Neo.UI.Editor`, tests under `Tests/`.

## Subsystem map

| Spec § | Folder | What's there |
|---|---|---|
| §3 Tween core | `Runtime/Tweens` | `Tween`/`Tween<T>` state machine (Pooled/Idle/StartDelay/Playing/LoopDelay/Paused), `Easing` (Penner set + Spring + AnimationCurve mode), play modes Normal/PingPong/Spring/Shake, random-range timing, `TweenPool`, `UITick` + `RuntimeHeartbeat` (edit-mode ticking comes from `Editor/EditorHeartbeat`) |
| §4 Animation layer | `Runtime/Animation` | Composite `UIAnimation` (Move/Rotate/Scale/Fade), `UIMoveDirection` presets + `MoveMath`, `ColorAnimation` + `IColorTarget`, `UIAnimationPreset` + database |
| §5 Animators | `Runtime/Animators` | `UIAnimator`, container UI/color animators, selectable UI/color animators, toggle UI/color animators, `Progressor` + progress targets (Image fill / TMP text / UnityEvent) |
| §6 Signals | `Runtime/Signals` | `SignalStream` registry (category+name, get-or-create), `Signal`/`MetaSignal<T>`, static `Signal.Send`, code-first `Signals.On<T>/Off`, `SignalReceiver` (serializable + component), `SignalSender` |
| §7 Containers | `Runtime/Containers` | `UIContainer` (4-state lifecycle, interruption reverses running animators, registration-based completion), `UIView` (static API riding the `UIView/Command` stream; visibility published on `UIView/VisibilityChanged`) |
| §8 UIPopup | `Runtime/Containers/UIPopup.cs` | `UIPopup.Get("name")…Show()` fluent API, named FIFO queues, sorting-order management, hide-on back/overlay/container, popup database |
| §9 Interactive | `Runtime/Interactive` | `UISelectable`, `UIButton` (trigger behaviours + `UIButton/Behaviour` signal), `UIToggle`/`UIToggleGroup`/`UITab`, `UISlider`, `UIStepper`, `UIScrollbar`, `UITooltip`(+trigger), `UITag`, static `BackButton` |
| §10 Flow graph | `Runtime/Flow`, `Editor/Flow` | `FlowGraph` (SerializeReference nodes, force-text), `FlowController` (global/local, `SetActiveNodeByName`, history + `GoBack`), node set (Start/UI/Signal/BackButton/Portal/Random/TimeScale/ApplicationQuit/Pivot/StickyNote/Debug), GraphView window with runtime active-node highlight |
| §11 Theming | `Runtime/Theming` | `Theme` asset (tokens × variants), `ThemeService`, `ThemeColorTarget` (live edit-mode recolor), `ThemeColorRef`/`SelectableColorSet`, `ColorUtils` |
| §12 Editor UX | `Editor/` | Editor heartbeat, edit-mode preview controls (Play/Reverse/From/To/Stop with clean state revert) on all animator inspectors, fast category/name pickers with inline add-new, one settings asset (`Resources/NeoUISettings`) |
| §13 Agent authoring | `Editor/Agent` | JSON spec (`UISpec`), generator (`UISpecGenerator`, idempotent, collision-reporting), exporter (`UISpecExporter`), validator (`AgentValidation`, menu + batch mode), widget factory (`UIWidgetFactory`), screenshotter (`UIScreenshotter`) |
| §14 IDs | `Runtime/Ids` | `CategoryNameId` + per-component id types, per-type ScriptableObject databases |
| Rendering | `Runtime/Graphics` | `NeoShape` — SDF vector graphic (rounded rect with per-corner radius, circle, pill, checkmark/chevron/cross glyphs; border, linear/radial gradient, soft shadow via `edgeSoftness`), one shared material (`Resources/NeoShape.shader`), params ride vertex channels so everything batches; `NeoGradient` vertex-gradient mesh effect (theme-token stops, works on TMP) |
| Shape styles | `Runtime/Theming` | `ShapeStyle` (named radius/border/softness + token colors) on the `Theme`; `ThemeShapeStyleTarget` binds an `NeoShape` to a style by name with live edit-mode restyle |
| Starter kit | `Editor/StarterKitBootstrap.cs` | `Tools → Neo UI → Create or Repair Starter Kit`: full Dark/Light palette + shape styles on the theme, and a sprite-free prefab library (Button, Toggle, Switch, Slider, ProgressBar, Card, TabBar, ListView, Tooltip, Popup, Showcase view) under `Starter/` |

## Quick start

1. `Tools → Neo UI → Create or Repair Settings` creates `Resources/NeoUISettings.asset`,
   the ID/popup/preset databases and a default theme.
2. Add a `UIView` to a RectTransform, give it a `ViewId`, add a `UIContainerUIAnimator`,
   pick Show/Hide animations — preview them from the inspector without entering play mode.
3. Drive it from code: `UIView.Show("Menu", "Main")`, `UIPopup.Get("Popup_Info").SetTexts("…").Show()`,
   `Signals.On<int>("Shop", "ItemBought", id => …)`.
4. Create a Flow Graph (`Create → Neo → UI → Flow Graph`), open it (double-click), add a
   `FlowController` to a scene object. In play mode the window highlights the active node live.

## Agent workflow (spec → assets)

- **Generate:** `Tools → Neo UI → Generate From Spec…` (or
  `UISpecGenerator.GenerateFromSpecFile(path)` from batch mode). Re-running an edited spec updates
  generated assets in place; anything at a generated path that the generator didn't create is
  reported as a collision and never overwritten.
- **Export:** `Tools → Neo UI → Export Spec…` dumps theme/presets/views/flow back to JSON.
- **Validate:** `Tools → Neo UI → Validate`, or in CI:
  `Unity -batchmode -projectPath <proj> -executeMethod Neo.UI.Editor.AgentValidation.ValidateFromBatchMode`
  (exit code 0 = clean).
- **See your output:** `UIScreenshotter` renders any UI prefab to PNG without play mode —
  menu (`Screenshot Selected Prefab`), batch
  (`-executeMethod Neo.UI.Editor.UIScreenshotter.CaptureFromCommandLine -neoPrefab <assetPath> -neoOut <png>`),
  or — while the editor is open — enable `Tools → Neo UI → Screenshot Watcher` once and drop
  `{"prefab":"Assets/...","out":"shot.png","width":1080,"height":1920}` at
  `Temp/neo-screenshot-request.json`; the result report appears at `Temp/neo-screenshot-result.json`.

Spec elements cover widgets (`button`, `toggle`, `switch`, `tab`, `slider`, `progress`, `tabbar`,
`list`, `text`, `image`, `shape`) and layout containers (`vstack`, `hstack`, `grid`, `scroll`,
`spacer` — `padding`/`spacing`/`columns`/`cellSize`, nested via `children`). Any element takes
`anchor` (preset: `TopLeft…BottomRight`, `Stretch`, `StretchTop`, …), `size [w,h]`,
`position [x,y]`, `style` (theme shape style) and `background` (color token). A `popups` section
generates ready-made `UIPopup` prefabs. Widgets are built by `UIWidgetFactory` (NeoShape-based,
theme-bound, fully wired), and the exporter reads them — including hand tweaks — back into spec
form; `export → generate → export` is byte-stable (see `SpecLayoutAndWidgetTests`).

The JSON schema is documented on `UISpec` (Editor/Agent/UISpec.cs); the worked example from the
feature spec lives in `Tests/EditMode/SpecTests.cs` and is exercised end-to-end by
`GeneratorEndToEndTests` (generate → validate → export → re-parse).

## Conventions

- Everything is addressed by category/name **strings** (greppable, agent-friendly); no GUID lookups.
- All package ScriptableObjects are flat and serialized as text.
- Signal streams used by the package: `UIButton/Behaviour`, `UIToggle/Behaviour`,
  `UIView/Command`, `UIView/VisibilityChanged`, `Input/BackButton`.
- Prefer `Signals.On(...)` in C# over serialized UnityEvents for UI → gameplay wiring.

## Tests

- `Tests/EditMode` — tween state machine + easing math, signals, ids, theming, JSON/spec
  round-trip, generator/exporter/validator end-to-end.
- `Tests/PlayMode` — container lifecycle incl. interruption, UIView static API, buttons/toggles/
  tabs/steppers, popup queueing, flow navigation (button/portal/timer/back), progressor, live theming.
