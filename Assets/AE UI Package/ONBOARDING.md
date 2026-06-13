# AlterEyes UI Package — Onboarding Guide

A clean-room rebuild of Doozy UI Manager 4 for **Unity 6 (6000.4.x)**, delivered as a reusable,
self-contained package. It gives you a complete UI toolkit: containers and views, interactive
widgets, an in-house tween engine with edit-mode preview, a visual navigation flow graph, a
decoupled signal bus, central theming, and — its two defining pillars — **AI-agent-first authoring**
and **a vector graphics system that batches every visual into one draw material**.

This document is the map. Read it top to bottom once, then keep it open as a reference.

---

## 1. Why this package exists

Doozy UI Manager 4 is deprecated and carries real day-to-day costs: multi-second editor lag when
selecting Doozy GameObjects (animated inspector chrome, reflection-heavy database scans), shipped
sprite art for every editor-animation frame, and codegen/dashboard machinery nobody used.

We kept the three parts that were genuinely good and rebuilt them clean:

1. **The animation system** — supports **previewing animations in edit mode** without entering play.
2. **UI Views** — containers addressed by `category/name` strings, shown/hidden via buttons or signals.
3. **The flow graph** — a graph view that visualizes UI navigation and **highlights the active node
   at runtime** for live UI-state debugging.

And added two pillars Doozy never had:

- **AI-agent-first authoring** — an agent can take a design (written as JSON) and produce an entire
  working UI: views, animations, transitions, navigation graph, and gameplay hooks — by writing text.
- **Central theming** — one place controls the color scheme, type scale, and shape language of all UIs.

The full feature specification lives in `Assets/docs/altereyes-ui-package-feature-spec.md`.

---

## 2. The three hard constraints

Every line of code in this package obeys these. If you contribute, you must too.

### Agent-first
- Everything is addressable by **category/name strings**, never GUIDs.
- ScriptableObjects are **flat and force-text-serialized** so they diff cleanly and an agent can read/write them.
- Prefer **signals** (`Signals.On<T>` / `Signals.Send`) over serialized `UnityEvent`s for decoupling.

### Editor performance is a first-class requirement
- No animated inspector chrome.
- No editor-tick subscriptions just to drive visuals.
- No reflection scans triggered by selection.
- Exactly **one settings asset** (`Resources/AEUISettings`).

### New Input System only
- `activeInputHandler = 1`. Never use `UnityEngine.Input` — always the new Input System.

---

## 3. Package layout & assemblies

```
Assets/AE UI Package/
├── Runtime/   → asmdef AlterEyes.UI         (all gameplay-facing runtime code)
├── Editor/    → asmdef AlterEyes.UI.Editor  (inspectors, drawers, flow window, agent tooling)
│   └── EditorUI/ → asmdef AlterEyes.EditorUI (standalone editor-UI kit — zero deps on AlterEyes.UI)
├── Tests/     → EditMode + PlayMode test asmdefs
├── Resources/ → AEUISettings asset, ID databases, the AEShape shader
├── Fonts/     → committed TMP SDF font assets (Inter + Lucide icon font)
├── Demo/, Starter/ → generated demo content and the starter widget library
└── docs/ (under Assets/docs) → spec, plans, spec-reference, schema
```

Two boundaries matter:

- **`AlterEyes.EditorUI` is liftable.** It is a standalone editor-tooling kit (AEGUI, AEColors,
  AEStyles, AESearchablePopup, AEDropdown, AEListView). It must keep **zero references** to
  `AlterEyes.UI` so it can be dropped into any other project. Don't add a dependency back.
- **`Assets/References/Doozy~`** is the original Doozy 4 source. The trailing `~` means Unity never
  imports it. **Port math/behavior from it; never import it, never copy its editor machinery.**

---

## 4. Core runtime subsystems

### 4.1 Containers (`Runtime/Containers`)

The base is **`UIContainer`** — a `MonoBehaviour` that manages a `CanvasGroup`'s visibility with a
proper state machine: `Visible → IsHiding → Hidden → IsShowing → Visible`. It coordinates registered
animators and progressors, exposes `Show()` / `Hide()` / `Toggle()`, fires both `UnityEvent`
callbacks (`OnShowCallback`, `OnVisibleCallback`, `OnHideCallback`, `OnHiddenCallback`) and mirrored
C# events, and can disable the canvas / graphic raycaster / GameObject when hidden for performance.

`onStartBehaviour` (`ContainerStartBehaviour`) decides whether a container begins shown, hidden, or
disabled — and it must be **baked WYSIWYG**: the prefab's saved state equals its runtime start state.

Subclasses and siblings:

- **`UIView : UIContainer`** — the primary screen/panel unit, addressed by a `ViewId` (category/name).
  Static API drives the whole screen system:
  ```csharp
  UIView.Show("Menu", "Main");      // show one view
  UIView.Hide("Menu", "Main");
  UIView.ShowCategory("HUD");       // show all views in a category
  UIView.HideAllViews();
  ```
  Views also respond to **`ViewCommand`** signals on the `"UIView/Command"` stream and publish
  `ViewVisibilityData` on `"UIView/VisibilityChanged"`.
- **`UIPanel : UIContainer`** — a content surface a tab shows/hides (see tabs below).
- **`UIPopup`** + `PopupDatabase`, `ShowPopupOnClick`, `PopupClickCatcher`, `HideContainerOnClick` —
  the popup system.
- **`UITooltip`** / `UITooltipTrigger` — hover/press tooltips.
- **`SafeAreaFitter`** — notch/safe-area-aware container.
- **`UICascadeChildren`** — staggered reveal of child elements.

### 4.2 Interactive widgets (`Runtime/Interactive`)

Built on **`UISelectable`** (the package's Selectable equivalent, with per-state visuals):

- **`UIButton`** — the workhorse. Click/long-press/double-click, sends signals or view commands.
- **`UIToggle`** + **`UIToggleGroup`** — on/off and radio-group behavior.
- **`UITab`** — a toggle that `controls` a sibling `UIPanel` (shows it, hides the others). The
  generator wires `UITab.containerReference` and bakes the selected tab's panel visible.
- **`UISlider`**, **`UIScrollbar`**, **`UIStepper`** (+ `UIStepperValueLabel`), **`UICounter`**,
  **`UIDropdown`** (with commit events), **`UIBadge`**, **`UITag`**.
- **`BackButton`** / `BackButtonInput` — hardware/escape back navigation.
- Wiring helpers: `ViewCommandOnClick`, `UIActionBehaviour`, `SelectionStateRelay`, `UISoundRelay`,
  `WidgetStyleTag` (records the chosen `variant`/`size` so the exporter can read it back).

### 4.3 Signals (`Runtime/Signals`)

A decoupled message bus keyed by `category/name` strings. This is the preferred way to connect UI to
gameplay.

```csharp
// Subscribe
Signals.On("Game", "Paused", () => Pause());                 // parameterless
Signals.On("Game", "Paused", signal => Log(signal.message)); // full signal (sender/message/payload)
Signals.On<int>("Score", "Changed", value => SetScore(value)); // typed payload

// Send
Signals.Send("Game", "Paused");
Signals.Send("Score", "Changed", 42);

// Unsubscribe
Signals.Off("Game", "Paused", handler);
```

`SignalStream`s are created on demand. `StreamId` databases give agents/inspectors a picklist.
`SignalReceiverComponent` / `SignalSender` expose this to non-coders in the inspector. `SignalLogger`
traces traffic. **Important runtime rule:** name-addressed lookups that match nothing must
`Debug.LogWarning` — no silent failures.

### 4.4 Flow graph (`Runtime/Flow`)

A node-graph that models UI navigation. **`FlowGraph`** is a ScriptableObject of `FlowNode`s and
`FlowEdge`s; **`FlowController`** executes a runtime clone of it.

- A node declares which `view` / `views` to show and which to `hide`.
- Edges are traversed via `Advance(edge)`; `GoBack()` walks a history stack.
- `SetActiveNodeByName(name)` is the cheat/shortcut entry point.
- `OnActiveNodeChanged` is what the editor graph window hooks to **highlight the live node**.
- **Lifecycle rule:** a `FlowController` defers its first action to `Start()` (not `OnEnable`),
  because OnEnable order across a loading scene is arbitrary and registries fill during OnEnable.
  Never auto-start cross-object behavior from `OnEnable`.

### 4.5 Animation, animators & tweens

- **`Runtime/Tweens`** — the **in-house tween engine** (no DOTween). `Tween`, `TweenOfT<T>`, `Ease`/
  `Easing`, `TweenPool`, `TweenSettings`. It is driven by `RuntimeHeartbeat` at runtime and by
  `EditorHeartbeat` in the editor — this dual heartbeat is what enables **edit-mode animation preview**.
- **`Runtime/Animation`** — `UIAnimation`, `ColorAnimation`, `MoveDirection`, `UIAnimationPreset` +
  `AnimationPresetDatabase` (named, reusable animation presets).
- **`Runtime/Animators`** — components that bind animations to containers/selectables:
  `UIContainerUIAnimator`, `UIContainerColorAnimator`, `UISelectableUIAnimator`,
  `UISelectableColorAnimator`, `UIToggleUIAnimator`, `UIToggleColorAnimator`, and standalone
  `UIAnimator`.
- **`Progressor`** + `ProgressTarget`s (`ImageProgressTarget`, `RectFillProgressTarget`,
  `ShapeProgressTarget`, `TextProgressTarget`, `UnityEventProgressTarget`) — drives any 0..1 visual
  (bars, radial dials, counters). **WYSIWYG rule:** a Progressor's baked `startValue` must equal its
  runtime start state.

### 4.6 Theming (`Runtime/Theming`)

One **`Theme`** (ScriptableObject) holds color **tokens**, **TextStyle**s (named type scale), and
**ShapeStyle**s. **`ThemeService`** is the static hub:

```csharp
ThemeService.activeTheme = myTheme;
ThemeService.SetVariant("Dark");                 // or "Light"
ThemeService.TryGetColor("surface", out var c);
```

Components reference theme tokens live via `ThemeColorTarget`, `ThemeTextStyleTarget`,
`ThemeShapeStyleTarget`, and `ThemeColorRef`. `ThemeVariantCycler` flips variants at runtime. Changing
a token re-pushes to every target — change it in one place, the whole UI updates.

> For the full workflow — how to see which token an element uses, how to recolor, how to author a
> theme, and how to switch Dark/Light with no code — see **§4.10 Theming in depth**.

### 4.7 IDs & data

- **`Runtime/Ids`** — `IdDatabase` ScriptableObjects per addressable kind (`ButtonIdDatabase`,
  `ViewIdDatabase`, `PanelIdDatabase`, `SliderIdDatabase`, `ToggleIdDatabase`, `DropdownIdDatabase`,
  `StreamIdDatabase`, `TagIdDatabase`). These power the searchable category/name dropdowns. The
  databases live on `AEUISettings`. `CategoryNameId` is the shared id struct.
- **`Runtime/Data`** — `UIData` + `UIBoundList`: data binding. `UIData.Set(category, name, rows)`
  clones a list/grid row template per row at runtime, filling `{key}` tokens.

### 4.8 Graphics (`Runtime/Graphics`) — the AEShape system

**This is the visual foundation of the entire package.** `AEShape` is an SDF (signed-distance-field)
vector graphic — rectangles, rounded rects, circles, rings, arcs — and **every visual is built from
it**. Its shader lives at `Resources/AEShape.shader`. Shape parameters (corner radius, thickness, arc
sweep, gradients) ride **vertex channels (UV1–3 + tangent)**, so **one shared material batches
everything**.

> ⚠️ **Never give an AEShape its own material.** Doing so breaks batching. Drive its look through its
> serialized shape parameters and the shared material.

`AEGradient` (live theme-token gradients) and `ElevationRecipe` (shadow/depth presets) round out the
layer.

### 4.9 Menus subsystem (`Runtime/Menus`)

A data-driven settings/cheats system (built per `Assets/docs/settings-cheats-menu-plan.md`):
`SettingsCatalog` / `CheatCatalog` (definitions) → `UserSettingsService` + `IUserSettingsStore`
(persistence) → `MenuControlBinder` + `MenuPresenter` (UI generation) → `MenuWidgetLibrary` (widgets).
`Runtime/Menus/Rebinding` (`InputRebindService`, `UIRebindControl`) handles input rebinding on the
new Input System.

### 4.10 Theming in depth

#### The mental model: components don't store colors — they point at tokens

A `Theme` asset holds **named color tokens** (`Primary`, `Background`, `TextDefault`, …) organized
into **variants** (`Dark`, `Light`, seasonal). Exactly one variant is active at a time, named by the
theme's `ActiveVariantName`.

Nothing in your scene hardcodes a color. Each element instead carries a small **target** component
that says *"my color is token X"* and resolves it against the active theme every time the theme
changes. There are three target components plus one inline ref:

| What it colors / styles | Component | Field that names the token/style |
|---|---|---|
| Any UGUI `Graphic`, TMP text, or `SpriteRenderer` | `ThemeColorTarget` | `token` (e.g. `Primary`) + optional `tint` multiplier |
| An `AEShape` surface (radius, border, fill, gradient, elevation) | `ThemeShapeStyleTarget` | `style` → a `ShapeStyle` on the theme (its `fillColor` is a token ref) |
| A TMP text's typography (font, size, spacing, color) | `ThemeTextStyleTarget` | `style` → a `TextStyle` on the theme (its `color` is a token ref) |
| Per-state button/toggle colors | inline `SelectableColorSet` on the `UISelectable` | each state is a `ThemeColorRef` (`useToken` + `token`) |

Every target is `[ExecuteAlways]` and subscribes to `ThemeService.OnThemeChanged`. The theme's
`OnValidate → RaiseChanged → ThemeService.NotifyThemeChanged` chain means **editing the theme asset
recolors every bound element live in edit mode — no play mode required.**

The active theme is resolved lazily: `ThemeService.activeTheme` returns `AEUISettings.theme` (the
single asset at `Resources/AEUISettings`) unless overridden in code. A missing token resolves to
**white plus a `Debug.LogWarning`** — that warning is your signal that a variant is missing a token.

#### Seeing which color an element will have

Work backwards from the GameObject to the token, then look up the token's value:

1. **Select the GameObject** and read its theming component:
   - `ThemeColorTarget` → its **`token`** field (and `tint`, a multiplier — use alpha to fade).
   - `ThemeShapeStyleTarget` → its **`style`** name (e.g. `Card`).
   - `ThemeTextStyleTarget` → its **`style`** name (e.g. `Title`).
   - A button/toggle → expand the `UISelectable`'s `SelectableColorSet`: each state
     (Normal/Highlighted/Pressed/Selected/Disabled) shows whether it `useToken` and which one.
2. **Open the Theme asset** (the one in `AEUISettings.theme`). Select the **active variant**, find
   that token in its color list — that's the real `Color`. For a shape/text style, find the style by
   name; its `fillColor`/`color` is itself a token ref that points back into the variant.

Fastest "what will it look like" check: just change the token in the theme and watch the Scene view
update live, or flip `ActiveVariantName` to preview the other variant.

#### Changing a color — four levers, smallest blast radius first

- **Per-instance** — change one `ThemeColorTarget`'s `tint`, or repoint its `token`. Affects only that element.
- **Theme-wide token edit** — change a token's `Color` in a variant on the Theme asset. Every element
  bound to that token updates at once. *This is the intended workflow.*
- **Variant switch** — change `ActiveVariantName` (inspector) or call `ThemeService.SetVariant("Light")`.
- **Whole-theme swap** — `ThemeService.activeTheme = otherTheme;` or assign a different asset to `AEUISettings.theme`.

In code:
```csharp
theme.SetToken("Primary", new Color(0.2f, 0.6f, 1f));  // set a token (pass a variantName to target one variant)
Color c = theme.GetColor("Primary");                   // resolve in the active variant (warns + white if missing)
ThemeService.SetVariant("Light");                       // switch variant on the active theme
```

#### Switching Dark ↔ Light with no code

This works **today, in edit mode, without writing code** — provided the active theme actually has
both variants:

- The **Starter Kit** theme ships `Dark` + `Light` (defaults to `Dark`).
- The **CleanSlate** and **SoftFantasy** bundles ship `Dark` + `Light`.
- ⚠️ **NeonArcade** ships **only `Dark`** — its other variants inherit the dark values, so there is no
  real light mode there until you author one.

Two no-code paths:

1. **Edit-mode toggle:** select the Theme asset → set **Active Variant Name** to `Light`/`Dark`. Every
   bound element recolors instantly. Note: there is no custom Theme inspector yet, so this is a
   **plain text field** — type the variant name exactly (case-sensitive), it isn't a dropdown.
2. **Runtime button:** add a `ThemeVariantCycler` component to any `UIButton`. Clicking it at runtime
   cycles Dark → Light → … Just a component add, still zero lines of code.

#### Creating a new theme

**Option A — curated bundle (fastest, recommended).** `Tools → AlterEyes UI → Apply Theme Bundle`
applies a complete, coherent system (CleanSlate / NeonArcade / SoftFantasy: tokens + type scale +
shape language + motion). Also drivable from a spec: `"theme": { "bundle": "CleanSlate" }`. Start from
one of these and edit.

**Option B — author a Theme asset by hand.**
1. **Create it:** Project window → `Create → AlterEyes → UI → Theme` (or duplicate the starter theme
   to inherit its tokens/styles).
2. **Add variants:** add `Dark`, `Light`, etc. Keep token names **identical across variants** — that
   is what makes switching work. In code, `theme.AddVariant("Light")` seeds the new variant with the
   first variant's tokens so none are missing.
3. **Add color tokens:** for each variant, add `TokenColor` entries (`token` name + `Color`).
4. **Add text & shape styles (optional):** populate `textStyles` (`name`, `font`, `size`, style flags,
   spacing, a `color` token ref) and `shapeStyles` (`name`, corner radius, border, `fillColor`/
   `borderColor` token refs, softness, gradient, elevation). These are what the style targets address
   by name.
5. **Make it active:** assign it to `AEUISettings.theme` (run `Tools → AlterEyes UI → Create or Repair
   Settings` first if you have no settings asset), or at runtime `ThemeService.activeTheme = myTheme;`.

After that, point elements at its tokens/styles via the target components and every recolor is a
single edit on the asset.

---

## 5. Editor tooling

### 5.1 The EditorUI kit (`Editor/EditorUI`)

The standalone look-and-feel kit every inspector/drawer routes through so everything is consistent:

- **`AEGUI`** — layout primitives (`BeginFoldoutSection`, headers, rows).
- **`AEColors`** — category accent colors: Interactive=blue, Containers=cyan, Animation=orange,
  Flow=purple, Theming=pink, Signals=teal, Data=yellow.
- **`AEStyles`** — cached `GUIStyle`s.
- **`AESearchablePopup`**, **`AEDropdown`**, **`AEListView`** — searchable/cached IMGUI controls.

### 5.2 Inspectors & drawers

- Inspectors subclass **`AEUIEditor`** (`Editor/Inspectors/ComponentEditors.cs`). Selectable-based
  components subclass `SelectableEditor`/`SliderEditor` and tuck the base GUI inside
  `AEGUI.BeginFoldoutSection`.
- Any category/name string field uses **`IdDatabaseOptions.DrawCategoryNamePair`** (a searchable
  dropdown backed by the databases on `AEUISettings`). Plain string pickers use
  `AEDropdown.StringPopup/ValuePopup` with an inline **"+ Add"** row — **never modal dialogs**.

**IMGUI performance rules** (these are why selection stays snappy):
- Never create `GUIStyle`s / `ReorderableList`s / `SerializedObject`s per `OnGUI` pass — cache them.
- Fetch dropdown options only when the dropdown opens.
- Conditional display = draw or don't draw (no animated reveal).
- Guard `enumValueIndex < 0` (multi-edit mixed values).
- Wrap manual `GUI.Toggle`-style writes in `BeginChangeCheck` so multi-edit drawing never stomps data.

### 5.3 The flow graph window (`Editor/Flow`)

A GraphView window with live runtime node highlighting. It keeps **one cached `SerializedObject`**
(foldout state lives there — recreating it per frame breaks expansion) and **never fully repopulates
the view on value edits** (that destroys the GraphView selection). Renames must propagate to
`FlowEdge.toNode` and `FlowGraph.startNode`.

---

## 6. The AI-agent authoring pipeline (`Editor/Agent`)

This is the headline feature. A UI is described as **JSON spec** and the pipeline turns it into real
prefabs, views, and a flow graph — round-trippable.

### 6.1 Key pieces

- **`UISpec`** — the spec data model.
- **`UIWidgetFactory`** — **the single source of truth for widget structure.** Both the generator and
  the exporter rely on its child names. Build all hierarchies through it.
- **`UISpecGenerator`** — spec JSON → prefabs/views/flow.
- **`UISpecExporter`** — scene/prefabs → spec JSON (the reverse). Export → generate → export must stay
  **byte-identical** (enforced by tests). **Never let the exporter fall back to scanning all of Assets**
  when a generated subfolder is missing (`UISpecExporter.FindGenerated`) — it would hijack committed
  demo/starter popups.
- **`UISpecPreview`** / **`UIScreenshotter`** — render views to PNGs in-memory across a resolution
  matrix; this is the agent's render-and-critique loop.
- **`AgentValidation`** — hard validation (`ValidateAll`) plus soft design lint (`ValidateDesign`:
  WCAG contrast, off-scale spacing, raw fontSize where text styles exist). Includes the
  **dead-interaction lint** (`ValidateInteractivity`) that flags buttons/tabs that do nothing.
- **`IconMap`** — Lucide icon-name → glyph mapping. **`SpecReference`** writes the spec docs/schema.

### 6.2 Spec element kinds

`button, toggle, switch, tab, slider, progress, tabbar, list/scroll, vstack, hstack, grid, panel,
spacer, text, image, shape (Ring/Arc + thickness/arcStart/arcSweep), icon (Lucide name), counter,
input, stepper, safearea`.

Styling fields include: `textStyle` (named theme TextStyle — owns the size; raw `fontSize` is the
styleless fallback), button `variant` (primary/secondary/ghost/danger) + `size` (sm/md/lg or `[w,h]`),
`icon` + `badge`, shape/image `gradient {from,to,angle}`, progress `"style":"radial"`, list/grid
`bind`+`item` (data-bound rows), `cascade` (staggered reveal), and a tab's `controls` (id of the
sibling panel it shows/hides). Flow nodes take `view`, `views`, and `hide` lists.

The full field catalog is generated to **`Assets/docs/spec-reference.md`** + **`aeui-spec.schema.json`**.

### 6.3 Driving the agent bridge (editor open)

Toggle **`Tools → AlterEyes UI → Agent Bridge`** once, then write `Temp/aeui-request.json` and read
`Temp/aeui-result.json`. Actions:

| Action | Effect |
|---|---|
| `{"action":"generate","spec":"path.json"}` | Build views/prefabs/flow from a spec |
| `{"action":"export","out":"path.json"}` | Reverse: scene → spec |
| `{"action":"validate"}` | Run hard validation; returns `designWarnings` too |
| `{"action":"screenshot","prefab":"Assets/...","out":"shot.png"}` | Render a prefab |
| `{"action":"preview","spec":"path.json","out":"dir"}` | In-memory render of a spec's views (no commits) |
| `{"action":"specReference"}` | Regenerate spec docs + schema |
| `{"action":"buildScene"}` | Assemble a playable scene from generated assets |

> Screenshot output paths must live **outside `Temp/`** if they need to survive an editor exit. The
> screenshotter needs a graphics device — batch runs must omit `-nographics`. **Run
> `{"action":"validate"}` after every generate.**

---

## 7. Setup & tooling menus

Under **`Tools → AlterEyes UI`**:

- **Create or Repair Settings** — creates `Resources/AEUISettings` + the ID databases (one settings asset max).
- **Create or Repair Starter Kit** — the themed widget prefab library + Dark/Light palette + type scale.
- **Create or Repair Fonts** — regenerates the Inter + Lucide TMP SDF font assets (`FontAssetBootstrap`;
  also wires `AEUISettings.iconFont`).
- **Apply Theme Bundle** — applies a curated bundle (CleanSlate / NeonArcade / SoftFantasy: complete
  token/type/shape/motion systems). Also available via a spec's `"theme": { "bundle": "..." }`.
- **Build Scene From Generated UI** — same as the `buildScene` bridge action.

---

## 8. Build & test workflows

### While the Unity editor is open on this project
Check `Temp/UnityLockfile` + `tasklist` for `Unity.exe`. **Don't batch-compile and never kill the
editor.** Compile-check editor code with Unity's bundled Roslyn:

```
dotnet "<UnityDir>/Editor/Data/DotNetSdkRoslyn/csc.dll" -nologo -target:library -langversion:9.0 \
  -define:UNITY_EDITOR \
  -r:<Data/Managed/UnityEngine/*.dll> -r:<Data/NetStandard/ref/2.1.0/netstandard.dll> \
  -r:<Library/ScriptAssemblies/{AlterEyes.UI,UnityEngine.UI,UnityEditor.UI,Unity.TextMeshPro,Unity.InputSystem}.dll> \
  <sources>
```

Use forward-slash output paths. Compile `Editor/EditorUI` **alone** (engine refs only) to verify the
kit stays dependency-free.

### When no editor holds the lock
```
Unity.exe -batchmode -nographics -runTests -testPlatform EditMode|PlayMode -projectPath .
```

### The acceptance render (needs graphics)
```
Unity.exe -executeMethod AlterEyes.UI.Editor.BeautificationAcceptance.Run   # omit -nographics
```

---

## 9. Runtime robustness rules (hard-won — keep them)

These were learned from the first playable generated scene. Honor them whenever you add features.

1. **No silent failures.** Registry/name-addressed commands that match nothing must `Debug.LogWarning`
   (see `UIView.ProcessCommand`, `FlowController.StartFlow`).
2. **Lifecycle.** Components that command others on startup defer their first action to `Start()`.
   Never auto-start cross-object behavior from `OnEnable`.
3. **WYSIWYG.** A widget's baked prefab state must equal its runtime start state (Progressor
   `SetCustomValue` + `startValue`; baked toggle/tab colors + serialized `isOnValue`; container
   `onStartBehaviour`). Any new runtime-driven visual needs the same treatment.
4. **Dead-interaction lint.** Keep `AgentValidation.ValidateInteractivity` in sync when you add
   interaction-wiring kinds.
5. **Behavior tests, not just screenshots.** Add a PlayMode test whenever a "renders fine but does
   nothing" bug is fixed. The nets are:
   - `Tests/PlayMode/RuntimeBehaviourRegressionTests.cs` — enable-order race, stepper labels,
     progressor start state.
   - `Tests/EditMode/GeneratedFlowPlaythroughTests.cs` — generates the demo spec, instantiates
     views + flow graph in-memory, and clicks through every flow edge + tab.

Export/generate determinism is guarded by `SpecLayoutAndWidgetTests`, `TypographyTests`,
`IconAndVariantTests`, `DepthAndShapeTests`, `JuiceTests`. **Any new spec field needs deterministic
export.**

---

## 10. Where to read next

| Topic | File |
|---|---|
| Full feature specification | `Assets/docs/altereyes-ui-package-feature-spec.md` |
| Editor UX rationale + field catalog | `Assets/docs/editor-ux-analysis.md` |
| Visual-polish roadmap (typography, icons, variants, themes, juice) | `Assets/docs/ui-beautification-plan.md` |
| Settings/cheats menu design | `Assets/docs/settings-cheats-menu-plan.md` |
| Spec field reference (generated) | `Assets/docs/spec-reference.md` |
| Spec JSON schema (generated) | `Assets/docs/aeui-spec.schema.json` |
| Project conventions (authoritative, terse) | `CLAUDE.md` (repo root) |

---

## 11. Quick start checklist for a new user

1. Open the project in Unity 6 (6000.4.x). Confirm `activeInputHandler = 1` (new Input System).
2. Run **`Tools → AlterEyes UI → Create or Repair Settings`**, then **Create or Repair Fonts**, then
   **Create or Repair Starter Kit**.
3. Optionally apply a theme bundle: **`Tools → AlterEyes UI → Apply Theme Bundle`**.
4. Drop a `UIView` into a canvas, add a `UIButton`, and wire it to `Signals.Send(...)` or a view command.
5. To author by spec: enable **Agent Bridge**, write a spec JSON, `generate`, then `validate`, then
   `preview` to render-and-critique.
6. Read `CLAUDE.md` and the feature spec before contributing code — the three hard constraints and the
   robustness rules are non-negotiable.
