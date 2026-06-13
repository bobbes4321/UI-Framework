# Neo UI Package — Feature Specification (Doozy Replacement)

**Status:** Draft 1 — 2026-06-10
**Working name:** `com.neo.ui`
**Replaces:** Doozy UI Manager 4 (`Assets/ThirdParty/Doozy` in the CBN repo, ~1,036 C# files)

---

## 1. Why

Doozy UI Manager 4 is deprecated — no future Unity compatibility is guaranteed. Beyond that, its day-to-day costs are real:

- **Editor lag:** selecting a GameObject with a Doozy component can take multiple seconds (animated inspector chrome, reflection-heavy database scans on selection).
- **Bloat:** the package ships sprite art for every frame of every editor animation, plus dashboard/theming/codegen machinery nobody uses.
- **Fancy for no reason:** animated editor headers and transitions that add nothing.

The parts worth keeping — and the reason we're rebuilding rather than discarding:

1. **The animation system (Reactor):** genuinely good, and it supports **previewing animations in edit mode without entering play mode**.
2. **UI Views:** containers addressed by category/name, transitioned via UI buttons or signals.
3. **The flow graph (Nody):** a graph view that visualizes UI navigation and **highlights the active node at runtime** — live UI-state debugging.

This document is the comprehensive feature list for the replacement package. Scope = everything the CBN project actually uses + the liked subsystems + requested extras, with an explicit cut list (§15) so nothing is silently dropped.

Two new pillars that Doozy never had:

- **AI-agent-first authoring (§13):** an agent should be able to take a design (from Claude Design, or simply written out) and produce an entire working UI — views, animations, transitions, navigation graph, gameplay hooks — by writing text.
- **Central theming (§11):** one place to control the color scheme of all UIs.

### Engineering ground rules (decided)

- **Own tween engine** — no DOTween dependency. Edit-mode preview requires an editor-ticked engine; DOTween can't do this well.
- **Editor performance is a first-class requirement** (§12), not a nice-to-have.
- Development happens in a **separate project on the latest Unity LTS**. Do **not** import Doozy there — copy its source into a Unity-ignored folder (e.g. `Reference/Doozy~/`; trailing-`~` folders are never imported) so it stays greppable as a reference implementation (easing/spring/shake math, defaults, transition arbitration). The CBN project remains the live behavioral reference for animation feel.

---

## 2. Ground-truth usage in CBN

Measured by script-GUID scan over 3,030 game prefabs/scenes (excluding `Assets/ThirdParty`). Code greps alone badly undercount Doozy usage — most wiring is serialized.

| Component | Instances | Assets | Notes |
|---|---:|---:|---|
| UIButton | 102 | 46 | the workhorse |
| UISelectableUIAnimator | 95 | 41 | per-state button animations |
| UISelectableColorAnimator | 43 | 12 | per-state button colors |
| UIContainerUIAnimator | 42 | 31 | view show/hide animations |
| UIAnimator | 39 | 23 | standalone animations (spinners, hints) |
| UIView | 31 | 19 | |
| SignalSender | 20 | 18 | |
| UIPopup | 14 | 14 | one prefab per popup type |
| UIToggle | 13 | 9 | |
| UIToggleUIAnimator | 10 | 6 | |
| UIContainerColorAnimator | 6 | 2 | |
| UITab | 6 | 2 | main-menu tabs |
| UIToggleGroup | 5 | 4 | |
| Progressor | 4 | 4 | store-card completion bars |
| UIStepper | 3 | 1 | cheat canvas |
| FlowController | 2 | 2 | FigmaCanvas.prefab + PaintingTestScene |
| UISlider | 1 | 1 | Settings_Slider.prefab |

Zero serialized usage: UITooltip, standalone ColorAnimator/SpriteAnimator, ReactorController, ProgressorGroup, SignalToAudioSource, InputToSignal, BackButton component, reflected animators (Float/Int/Vector2/3), Mody modules, UIDesigner, SceneManagement nodes. (UITooltip, UIStepper, UIScrollbar, UITag are nonetheless **in scope** by request.)

### Game-code API touchpoints (compatibility targets)

The new package should keep these API shapes close enough that migration is mechanical:

| CBN code | Doozy API used |
|---|---|
| `Runtime/UI/UIStates/Panel.cs` | `UIContainer` + `OnShowCallback`/`OnHideCallback` event hookup |
| `Runtime/UI/UIStates/UIShortCutManager.cs`, `Runtime/Cheats/ColorByNumbersCheatHolder.cs` | `FlowController.SetActiveNodeByName(string)` |
| `Runtime/UI/UIStates/PopupUtils.cs`, `ShopPanel.cs`, `NewsPanel.cs`, `Store/StoreFronts/StoreFrontUI.cs`, most `Store/Item/*ItemUI.cs`, `SettingsHolder.cs`, `ColorACubeLogIn.cs` | fluent `UIPopup.Get("Popup_Name")…Show()` + `OnHideCallback` on popup instances — the single most-called Doozy API in game code |
| `Runtime/PlaySet/Dioramas/DioramaManager.cs`, `Runtime/UI/ErrorCanvasManager.cs` + all of the above | project-owned `UIPopupDatabase` wrapper |
| `Runtime/Store/Item/ModelItemUI.cs`, `PaintingPanel.cs`, `PausePanel.cs` | `Signal.Send(category, name, payload)`, `SignalSender` |
| `HintButton.cs`, `DioramaPanel.cs`, `HideUIAnimatorAfterDelay.cs` | `UIAnimator.Play()/Play(reverse)` |
| Navigation graph | one FlowGraph asset: `Assets/ColorACube/Prefabs/UI/UI.asset`, run by `FigmaCanvas.prefab` |

---

## 3. Tween core (Reactor replacement) — foundation for everything

Pooled, allocation-free tween objects ("reactions") driven by a tick service that works identically in play mode and edit mode.

**Engine**
- State machine per tween: `Pooled / Idle / StartDelay / Playing / LoopDelay / Paused`.
- Object pool: tweens are recycled, zero allocation during playback.
- Settings: duration, start delay, loop count (−1 = infinite), loop delay — **each with an optional random-range variant** (random duration/delay/loop count) for organic-feeling loop animations.
- Easing: Linear + Sine/Quad/Cubic/Quart/Quint/Expo/Circ/Back/Elastic/Bounce × In/Out/InOut, Spring — **plus AnimationCurve mode** as an alternative to the enum.
- Play modes: `Normal`, `PingPong`, `Spring` (elasticity/vibration), `Shake` (strength/vibration/fade-out). Spring/Shake provide the button-press feel.
- Directions: Forward / Reverse, reversible mid-flight.

**API**
- `Play(direction)`, `Stop()`, `Pause()`, `Resume()`, `Reverse()`, `Rewind()`, `SetProgressAt(float)`, `SetProgressAtZero/One()`, `PlayToProgress/FromProgress(float)`, `SetFrom/SetTo(value)`, `PlayToValue(value)`.
- Callbacks: `OnPlay`, `OnStop`, `OnFinish`, `OnLoop`, `OnPause`, `OnResume`, `OnUpdate`, plus a typed value-changed callback. UnityEvent variants at the component level.
- From/To **reference value modes**: `StartValue` (captured at play time), `CurrentValue` (relative to wherever the target is now), `CustomValue` (absolute), plus offset variants. Load-bearing for "slide in from current position" transitions.

**Ticker (the edit-mode preview enabler)**
- `Heartbeat` abstraction with two implementations:
  - **Runtime ticker:** singleton MonoBehaviour, configurable FPS cap.
  - **Editor ticker:** driven by `EditorApplication.update`, fully functional outside play mode, with SceneView repaint while previewing.

**Acceptance:** an animation configured on a component can be played, reversed, and scrubbed in the inspector *without entering play mode*, and the object's state reverts cleanly when preview stops.

---

## 4. Animation layer

- **Composite UIAnimation** — Move + Rotate + Scale + Fade channels animating RectTransform/CanvasGroup; per-channel enable and settings; animation purposes: `Show / Hide / Loop / Button / State / Custom` (purpose drives sensible defaults).
- **Move direction presets** — 12 directions (Left/Right/Top/Bottom + corners/centers) + custom position/offset, for slide-in/slide-out without hand-typing vectors.
- **ColorAnimation** — animates a color over time against a **color-target abstraction** (UGUI `Image`, TMP text; extensible). Heavily used via the selectable/container color animators.
- **Animation presets** — save/load named animation configurations (ScriptableObject database, category/name addressing) so one "Show: SlideInLeft" is reused across every view. Presets are also the unit agents reference in the UI spec (§13).

Cut from this layer: sprite-frame animation, reflected float/int/vector property animation, Texture2D reactions (zero usage).

---

## 5. Animator components (MonoBehaviour glue)

Highest-usage part of the whole package — these wire animations to lifecycle events.

- **`UIAnimator`** — standalone; plays its UIAnimation on demand from code (`Play()`, `Play(reverse)`). CBN uses it for hint buttons, panel expand/collapse, loading/saving spinners.
- **`UIContainerUIAnimator` / `UIContainerColorAnimator`** — bind Show/Hide animations to a container's visibility lifecycle. The container's transition is complete only when **all** registered animators have finished (registration model, not polling).
- **`UISelectableUIAnimator` / `UISelectableColorAnimator`** — per selection-state animations (Normal/Highlighted/Pressed/Selected/Disabled) on buttons and other selectables.
- **`UIToggleUIAnimator` / `UIToggleColorAnimator`** — on/off state animations for toggles.
- Common behavior across animators: OnStart/OnEnable behaviour (`Disabled / PlayForward / PlayReverse / SetFromValue / SetToValue`), automatic target discovery on the same GameObject, manual override.
- **`Progressor`** — float 0→1 driver with its own tween, value events (changed/incremented/decremented/reached-end), and **progress targets**: Image fill amount, TMP text, UnityEvent. CBN store cards use this for completion bars.

Cut: `ReactorController` (multi-animator orchestrator), `ProgressorGroup`, Animator-parameter and AudioMixer progress targets (zero usage).

---

## 6. Signals — the event bus

- **`SignalStream`** registry addressed by **category + name** strings; get-or-create semantics through a static service.
- **`Signal`** (payload-less) and **`MetaSignal<T>`** (typed payload). Metadata: sender object/GameObject, timestamp, optional message string.
- **Static send API:** `Signal.Send(category, name)` / `Signal.Send(category, name, payload)` — CBN code calls this directly today.
- **`SignalReceiver`** — serializable class embeddable in any component (with stream picker UI) + standalone receiver component; connect/disconnect lifecycle.
- **`SignalSender`** — MonoBehaviour that fires a configured signal on demand (20 uses in CBN, including on the XR rig and pause button).
- **Code-first typed subscribe API** (new, agent-pillar): `Signals.On<T>("category", "name", handler)` / `Signals.Off(...)` extension surface so UI→gameplay wiring lives in greppable C#. Prefer signals over serialized UnityEvents for cross-feature wiring (matches Neo messaging conventions).
- Stream ID databases (ScriptableObject) back the editor dropdown pickers. Categories to migrate from CBN: `Input`, `Music`, `Mute`, `Navigate`, `UIContainer`, `UISelectable`.
- Integration points: every UIButton behaviour publishes a signal; the UIView static show/hide API rides a signal stream; flow-graph nodes (UINode/SignalNode/PortalNode) listen to streams.

Cut: signal provider cooldown plumbing beyond per-behaviour cooldowns; `SignalToAudioSource` (zero usage).

---

## 7. UIContainer / UIView

The container base (shared by UIView, UIPopup, UITooltip) and the view system.

**Lifecycle**
- Four states: `Visible / Hidden / IsShowing / IsHiding`, with **interruption handling** — a Hide issued mid-Show reverses the running animations rather than snapping.
- Commands: `Show / Hide / Toggle` + `InstantShow / InstantHide`.
- Transition completion = all registered animator reactions + progressors finished; expose total show/hide duration.

**Addressing**
- Views carry a category/name `ViewId`; static registry of live views.
- Static API: `UIView.Show(category, name)`, `Hide(category, name)`, `ShowCategory/HideCategory(category)`, `HideAllViews()` — globally addressable without references.

**Callbacks** (plain UnityEvents + C# events — *not* Doozy's ModyEvents)
- `OnShow` (transition starts), `OnVisible` (fully shown), `OnHide`, `OnHidden`, `OnVisibilityChanged(state)`. CBN's `Panel.cs` hooks these.

**Behaviors**
- OnStart behaviour: `InstantHide / InstantShow / Hide / Show / Disabled` (first-frame state).
- Auto-hide after show + delay.
- When hidden: optionally disable GameObject, Canvas, and/or GraphicRaycaster.
- CanvasGroup `interactable`/`blocksRaycasts` management during transitions.
- Custom start position (snap RectTransform to an off-screen staging position on Awake).
- EventSystem selection: clear-selected on show/hide; auto-select a target after show (gamepad/keyboard nav).

Cut: device-orientation detection, multiplayer playerIndex plumbing.

---

## 8. UIPopup

Confirmed heavily used in CBN (14 popup prefabs, fluent API calls in code, a project-owned `UIPopupDatabase` wrapper).

- Popup prefab lookup **by name** from a popup database (link-asset per popup, like Doozy's `UIPopupLink`).
- Fluent API: `UIPopup.Get("Popup_FontSelection")` returning an instance to configure (`SetTexts`, callbacks, …) then `.Show()` — CBN's `SettingsHolder`, `ColorACubeLogIn`, `DioramaManager` depend on this shape.
- Parenting modes: dedicated popups canvas / a `UITag` target.
- **Queue:** one visible popup at a time, FIFO behind it; named queues optional.
- Automatic sorting-order management.
- Hide-on options: back button / click anywhere / click on target.
- Full container lifecycle + callbacks (UIPopup extends the container base).

---

## 9. Interactive components

- **`UISelectable`** (base, extends Unity `Selectable`): five selection states (`Normal/Highlighted/Pressed/Selected/Disabled`), state-changed event, per-state animator registration, cooldown support.
- **`UIButton`**: category/name `ButtonId` + static registry. **Behaviours** — `PointerEnter/Exit/Down/Up/Click/DoubleClick/LongClick/RightClick`, `Selected/Deselected`, `Submit` — each carrying: UnityEvent, signal emission, optional cooldown. `onClickEvent` shortcut + `ISubmitHandler` (gamepad/keyboard).
- **`UIToggle` + `UIToggleGroup`**: `isOn`, value-changed events (with instant variants that skip animation), group exclusivity + allow-none option, lookup by ToggleId.
- **`UITab`**: toggle whose `isOn` syncs with a target container's visibility (CBN main-menu tabs).
- **`UISlider`**: min/max/wholeNumbers, value events (changed/incremented/decremented/min-reached/max-reached), drag + keyboard/gamepad input.
- **`UIStepper`**: plus/minus stepper with step size, min/max, value events.
- **`UIScrollbar`**: scrollbar with the same behaviour/animator integration as other selectables.
- **`UITooltip`**: container subclass; parenting modes (tooltips canvas / trigger / tag target); tracking modes (`Disabled / FollowPointer / FollowTrigger / FollowTarget`); show/hide hover delays; size constraints.
- **`UITag`**: category/name `TagId` component marking GameObjects as parent/positioning targets for popups and tooltips.
- **Back-button system**: a lightweight static back-button signal (enable/disable/force-fire) feeding flow-graph back-navigation. One entry point the game calls (VR has no escape key — CBN triggers "back" from controller input). No InputToSignal/legacy-input machinery.

---

## 10. Flow graph (Nody replacement) — with runtime debugging

### Runtime

- **`FlowGraph`** ScriptableObject: nodes, edges, persisted editor position/zoom; graph state `Idle / Playing / Paused / Stopped`.
- **`FlowController`** MonoBehaviour: runs a graph; **Global** (cross-scene, static lookup) or **Local**; on-enable/disable behaviour (start/stop/pause/resume); `Start/Stop/Pause/Resume` API; **`SetActiveNodeByName(string)`** — CBN's `UIShortCutManager` and cheat system depend on it.
- **History stack + `GoBack()`** for back navigation; per-input-port "allows back" flag.

**Node set:**

| Node | Behavior |
|---|---|
| **Start** | entry point when the graph starts |
| **UINode** | the core node: shows/hides views on enter/exit; advances on UIButton click / signal / toggle / view event / back / optional timer; per-port back-allowed flag |
| **SignalNode** | sends a signal (with optional payload) and advances |
| **BackButtonNode** | enables/disables the back-button system; can clear history |
| **PortalNode** | *global* node — always listening while the graph runs; jumps the flow when triggered by a signal / button / toggle / view event; cross-flow shortcuts; optional history clear |
| **RandomNode** | weighted random output selection |
| **TimeScaleNode** | sets `Time.timeScale` instantly or animated (eased), optional wait-for-finish before advancing |
| **ApplicationQuitNode** | quits the app (exits play mode in editor) |
| **PivotNode** | visual flow routing (declutter), 4 orientations |
| **StickyNote** | editor-only documentation node |
| **DebugNode** | logs a message and passes through |

Cut: sub-graphs (Enter/Exit nodes), multiplayer per-player flows.

### Editor

- Graph window: pan/zoom, grid, rectangle select, **node search/create window**, **minimap**, side inspector for the selected node, undo/redo, copy/paste.
- **Runtime debugging — the killer feature:** in play mode the window highlights the active node and shows the traversal trail live; selecting a FlowController opens its running graph.
- Implementation note: Doozy builds on `UnityEditor.Experimental.GraphView`, which still works in Unity 6 but is unmaintained. **Evaluate Unity's newer graph-toolkit option on the target LTS before building; GraphView is the fallback.**

---

## 11. Theming — central UI color scheme

**New feature, not a Doozy port.** Doozy 4 only ships color utilities (`HSL/HSV/RGB` conversion), a per-selection-state `SelectableColor`, and a bare dark/light `ThemeColor` pair — the central theme manager was a Doozy 3 feature that never made it to 4. Requirement: **one central place to control the color scheme of all UIs.**

- **Theme asset** (ScriptableObject): named color tokens (`Primary`, `Background`, `Accent`, `TextDefault`, …) with multiple named **theme variants** (Dark/Light/seasonal/per-platform); one active theme, switchable at runtime and in the editor.
- **`ThemeColorTarget`** component: binds a Graphic/TMP/SpriteRenderer color to a token by name. **Live edit-mode updates** — change a token in the theme asset and every bound element across all open scenes/prefabs recolors instantly.
- **Per-selection-state color sets** (port of `SelectableColor`: Normal/Highlighted/Pressed/Selected/Disabled) whose entries can reference theme tokens; feeds the selectable/toggle color animators.
- Color animators can target "animate to theme token X" instead of only hardcoded colors.
- Agent integration: tokens addressable by name in the UI spec (§13); the theme asset itself is spec-representable — an agent can define or restyle a palette in text.
- Supporting math ported from Doozy `Runtime/Colors/`: HSL/HSV/RGB conversion, lighten/darken helpers.

---

## 12. Editor performance & UX requirements (anti-goals, first-class)

These are requirements, not aspirations — they are the reason this package exists:

- **Instant inspectors.** Plain UIToolkit inspectors; no animated headers/icons; no per-frame sprite art anywhere in the package; no reflection-heavy database scans triggered by selection. Target: selecting any component is imperceptible (<100 ms to fully drawn inspector).
- **Edit-mode preview controls on every animator inspector:** Play / Play Reverse / Stop / jump-to-From / jump-to-To, driven by the editor ticker, SceneView repaint while previewing, clean revert of object state on stop.
- **Fast category/name pickers** with inline "add new" — no codegen, no roaming-database scanning machinery (a major source of Doozy's selection lag).
- No dashboard window. No editor-chrome theming engine. No auto-generated styles. **One settings asset, max.**

---

## 13. AI-agent-first authoring

The package must be designed so an AI agent can author complete UIs by writing text — from a Claude Design output or a written description to a working UI with animations, transitions, and gameplay hooks.

**Chosen model: declarative text spec → generator produces real Unity assets.** After generation, prefabs/graphs are the source of truth and designers keep the native workflow (graph window, edit-mode preview); an exporter dumps assets back to spec text for agent inspection.

- **Declarative UI spec format** (YAML or JSON; schema shipped + documented in the package): views (category/name, layout hints, content elements), buttons/toggles/sliders with behaviours, animation assignments (preset name or inline settings), theme tokens, popups, and the navigation flow graph. One spec file can define an entire UI.
- **Generator** (editor tooling): consumes a spec → creates/updates prefabs, FlowGraph assets, ID database entries, animation presets, theme entries. **Idempotent re-generation:** re-running an edited spec updates generated assets without destroying manual tweaks where feasible (generated vs hand-edited regions flagged); collisions are reported, never silently overwritten.
- **Exporter:** dumps any existing view/graph/theme back to spec text — agents read current state without parsing Unity YAML.
- **Flow-graph text DSL:** the navigation graph (nodes, edges, triggers) is fully expressible in the spec; the graph window is the visual/debug view of the same data.
- **Code-first gameplay wiring:** the `Signals.On<T>(...)` API (§6) keeps UI→gameplay connections in greppable C# instead of serialized UnityEvent references.
- **Stable string addressing everywhere:** category/name IDs, node names, preset names, theme tokens — everything an agent references is a greppable string, never a GUID.
- **Agent-readable assets:** all package ScriptableObjects serialize force-text with flat, documented, deterministic layouts, so direct asset edits remain a fallback path.
- **Validation command:** editor menu item + static API callable from batch mode/CI that lints spec ↔ generated assets ↔ ID databases and reports broken references as text — actionable agent feedback without screenshots.
- *Stretch (not committed):* a Unity-editor automation/MCP endpoint so agents can trigger generate/export/validate without a human in the editor.

**Constraint that applies from day one:** every feature in §3–§11 must be spec-representable. Agent-friendliness (string addressing, force-text, signals-over-UnityEvents) is a design constraint on phase 1, even though the generator itself lands in phase 5.

### Worked example (the spec format must cover at least this)

```yaml
theme:
  tokens: { Primary: "#3A86FF", Background: "#14213D", TextDefault: "#FFFFFF" }

presets:
  - { name: SlideInLeft,  type: Show, move: { from: Left },  fade: { from: 0 }, duration: 0.3, ease: OutCubic }
  - { name: SlideOutLeft, type: Hide, move: { to: Left },    fade: { to: 0 },   duration: 0.2, ease: InCubic }

views:
  - id: Menu/Main
    showAnimation: SlideInLeft
    hideAnimation: SlideOutLeft
    elements:
      - button: { id: Action/Play, label: "Play", labelColor: TextDefault, background: Primary,
                  onClick: { signal: { category: Gameplay, name: StartPainting } } }
  - id: Menu/Settings
    showAnimation: SlideInLeft
    hideAnimation: SlideOutLeft

flow:
  start: MainMenu
  nodes:
    - { name: MainMenu, view: Menu/Main,
        next: [ { on: { button: Action/Settings }, to: Settings } ] }
    - { name: Settings, view: Menu/Settings, allowBack: true }
```

Gameplay hook, written by the same agent in C#:

```csharp
Signals.On("Gameplay", "StartPainting", () => coreLocator.Get<IGameStateService>().StartPainting());
```

Every element above maps to a feature in §3–§11; if the spec schema can't express something from those sections, the schema is incomplete.

---

## 14. ID / database system

- **`CategoryNameId`** base type + per-component ID types (`ViewId`, `ButtonId`, `ToggleId`, `SliderId`, `TagId`, `StreamId`) with value equality.
- ScriptableObject database per ID type; runtime static registries (hash set per component type) for category/name lookup.
- Editor: fast dropdown picker with inline "add new" (see §12 — no codegen, no scanning).
- **Migration note** — categories existing in CBN today: Views: `Menu`, `HUD` · Buttons: `Action`, `Direction`, `Generic`, `Media`, `Navigation`, `Shop` · Toggles: `Mute`, `Remember`, `Auto` · Streams: `Input`, `Music`, `Mute`, `Navigate`.

---

## 15. Explicitly NOT rebuilding (cut list)

| Cut | Rationale / replacement |
|---|---|
| Mody (modules / ModyActions / ModyEvent) | zero direct usage; plain UnityEvents + signals replace ModyEvents in callbacks |
| UIDesigner layout/alignment tools | unused; Unity's own RectTransform tooling suffices |
| SceneManagement (SceneDirector + scene load nodes) | unused; CBN has its own SceneSwapper |
| Multiplayer playerIndex plumbing | single-player VR titles |
| Orientation detection | VR/desktop only |
| Pooler service | unused; framework has its own patterns |
| Sprite-frame animation, reflected property animators, Texture2DReaction | zero usage |
| ReactorController, ProgressorGroup, Animator/AudioMixer progress targets | zero usage |
| SignalToAudioSource | zero usage; audio goes through the Neo audio service |
| InputToSignal / legacy input machinery | replaced by the single back-button entry point |
| Sub-graphs (Enter/Exit nodes) | unused; flat graphs suffice |
| Codegen windows, dashboard, editor-chrome theming | the bloat we're escaping |

---

## 16. Suggested build phasing

1. **Tween core** + tickers + edit-mode preview — everything depends on it.
2. **UIContainer/UIView** + animator components + **Signals** (incl. code-first API) + **theming core** (theme asset + ThemeColorTarget — early, so every later component binds tokens from day one).
3. **Interactive components** (selectable/button/toggle/group/tab/slider/stepper/scrollbar) + **UIPopup** + **UITooltip** + **UITag**.
4. **Flow graph** runtime + graph editor + runtime debugging.
5. **Agent authoring**: spec format + generator/exporter + validation.
6. **Progressor**, animation presets polish, back-button, migration tooling.

Agent-friendliness constraints (string addressing, force-text assets, signals-over-UnityEvents, spec-representable settings) apply from phase 1; only the generator tooling itself is phase 5.

---

*Source inventory: Doozy UI Manager 4 at `Assets/ThirdParty/Doozy` in the CBN repo (Runtime: Reactor, Signals, UIManager, Nody, Mody, Colors, Pooler, SceneManagement, UIDesigner; Editor: per-module editors + EditorUI). Usage data measured 2026-06-10 by script-GUID scan; see §2.*
