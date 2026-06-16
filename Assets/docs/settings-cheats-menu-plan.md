# Settings & Cheats Menu Subsystem — Implementation Plan

A data-driven, agent-authorable, pluggable subsystem for **settings menus**, **cheats menus**, and
**input rebinding**, coherent with the Neo UI package's spec pipeline, signal model, theming,
and editor-perf constraints. Reference implementations studied: `color_by_numbers_4`
(`SettingsHolder`, `ColorByNumbersCheatHolder`) and `com.neo.common`
(`UserSettingsHolderBase`, `CheatHolderBase`, `CheatMenuGameWindow`, `ReflectedValue`,
`ServiceLocator`, `PlayerPrefsHelper`).

---

## 1. Goal & the central design decision

The Doozy-era Neo systems (CBN / common) declare settings with a **fluent C# builder**
(`CreateSetting(_toggle).SetToggleAction(...).SetValueFunction(...)`). Each control is bound to game
state with live `Func<T>` getters and `Action<T>` setters, and the holder instantiates prefabs into
a panel at runtime.

That pattern is **incompatible with this package's hard constraints**: delegates can't live in a
flat force-text ScriptableObject or a JSON spec, and the package prefers `Signals.On/Send` over
serialized callbacks. So we **don't port the builder** — we split it into two layers that the rest
of the package already implies:

1. **Declarative layer** — *what controls exist*: id (`Category/Name`), control kind, label,
   default, range, options, persistence flag. Lives in a flat `ScriptableObject` catalog and is
   round-trippable through the JSON spec. Fully agent-authorable.
2. **Binding layer** — *how a control reaches game state*: a runtime `UserSettingsService` that
   persists values and emits a change **signal** per control. Game code reacts with
   `Signals.On<T>("Settings", "Audio/Master", ...)` — zero serialized references. A second
   **code-binding** path (`Bind(id, getter, setter)`) gives CBN-style direct binding for values the
   game owns, preserving feature parity for power users.

This keeps everything addressable by category/name, signal-first, flat-SO, and testable without play
mode — exactly the package's grain.

> **Naming note.** The existing `NeoUISettings` (in `Runtime/Settings/`) is the *editor/package*
> config asset. The new *user-facing* preferences live under a distinct folder/namespace to avoid
> confusion: proposed `Runtime/Menus/` → `namespace Neo.UI.Menus`, runtime value store named
> `UserSettingsService` (nods to common's `UserSettings`). Adjust if you prefer `Options`.

---

## 2. Core architecture

```
                 authored by agent / hand                          consumed at runtime
  ┌──────────────────────────────────┐         generate        ┌───────────────────────────┐
  │  UISpec  "settings"/"cheats"      │  ───────────────────▶   │  SettingsCatalog (asset)  │
  │  section (JSON)                   │  ◀───────────────────   │  CheatCatalog   (asset)   │
  └──────────────────────────────────┘         export          └───────────┬───────────────┘
                                                                            │ feeds
                                                                            ▼
  ┌───────────────────────────────────────────────────────────────────────────────────────┐
  │  SettingsMenu / CheatMenu presenter (MonoBehaviour)                                      │
  │   • builds rows from catalog via the widget prefab library (UIWidgetFactory contract)   │
  │   • wires each widget's value-change  ──▶  UserSettingsService.Set(id, value)            │
  │   • bakes initial value from service   ◀── UserSettingsService.Get(id)   (WYSIWYG)       │
  │   • category navigation via UITab/UIPanel (settings) or back-stack nav (cheats)          │
  └───────────────────────────────────────────────┬───────────────────────────────────────┘
                                                   │
        ┌──────────────────────────────────────────┴───────────────────────────────────────┐
        ▼                                                                                    ▼
  UserSettingsService (static, like Signals/ThemeService)                          Signals (existing)
   • Get<T>/Set<T> by Category/Name                                          Signals.Send("Settings",
   • default resolution from catalog                                          "<id>/Changed", value)
   • IUserSettingsStore persistence (PlayerPrefs default, swappable)         Signals.Send("Cheat", id)
   • optional code Bind(id, getter, setter)  ← CBN parity
```

---

## 3. Data model (`Runtime/Menus/`)

All flat, force-text serializable, `[SerializeReference]`-free where possible (use a `kind` enum +
optional payload fields, mirroring how `ElementSpec` carries every field flatly).

### 3.1 `MenuControlKind` (enum)
`Toggle, Switch, Slider, Stepper, Dropdown, Button, Label, KeyRebind` — plus reserved
`Header, Color, TextInput` for later. Cheats reuse the same enum (a cheat is just a `Button`,
`Toggle`, or numeric `Slider/Stepper` with an action binding).

### 3.2 `MenuItemDefinition` (serializable struct/class — one control)
```
string   category, name           // CategoryNameId addressing → the control's id
MenuControlKind kind
string   label, tooltip
string   group                    // category-within-catalog (tab / sidebar section)
bool     persisted = true         // false ⇒ value owned by game/save system, store doesn't keep it
// value config (only the relevant ones used per kind):
float    min, max, step
bool     wholeNumbers
string   defaultValue             // stringified default (parsed per kind), keeps the SO flat
List<string> options              // Dropdown items (or enum type name → expanded by service)
// binding:
SignalRefSpec changeSignal        // override; default is ("Settings"/"Cheat", "<category>/<name>")
bool     emitOnDrag, emitOnRelease // slider: continuous preview vs committed value (CBN parity)
// rebind:
string   inputActionRef           // serialized InputActionReference id (KeyRebind only)
int      bindingIndex
// platform gating (mirrors common's PlatformSpecificValue intent, simplified):
RuntimePlatformMask platforms
```

### 3.3 Catalog assets
- `MenuCatalog : ScriptableObject` (base) — `List<MenuItemDefinition> items`, `List<string> groups`,
  `string startGroup`. Flat, force-text.
- `SettingsCatalog : MenuCatalog` — settings semantics (persisted, change signals).
- `CheatCatalog : MenuCatalog` — cheat semantics: adds `bool favouritesEnabled`, per-item
  `bool isParametric` + parameter descriptors for `AddCheat<T>` parity, and the cheat namespace
  defaults to `Signals.Send("Cheat", id)`.

Catalogs are referenced by **category/name strings** and registered in an
`IdDatabase`-style picker on `NeoUISettings` (a `MenuCatalogDatabase`) so inspectors/drawers get
searchable dropdowns, consistent with `IdDatabaseOptions.DrawCategoryNamePair`.

---

## 4. Runtime service, store & binding (`Runtime/Menus/`)

### 4.1 `UserSettingsService` (static — same shape as `Signals` / `ThemeService`)
```
T    Get<T>(string category, string name)         // store value, else catalog default
void Set<T>(string category, string name, T value, bool commit = true)
bool TryGet<T>(..., out T value)
void Bind<T>(id, Func<T> getter, Action<T> setter, bool persist = false)  // CBN-parity direct binding
void RegisterCatalog(MenuCatalog catalog)         // for default resolution & type info
event Action<string,string,object> OnChanged
```
- On `Set(commit:true)`: persist via store → invoke binding setter (if any) →
  `Signals.Send("Settings", "<category>/<name>", value)`. On `commit:false` (slider drag): emit a
  **preview** signal (`"<id>/Preview"`) without persisting — this is the `OnSliderValueChanged` vs
  `OnSliderRelease` distinction CBN relies on.
- **No silent failures**: `Get/Set` on an id absent from every registered catalog logs
  `Debug.LogWarning` (package invariant — matches `UIView.ProcessCommand`).
- `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` resets statics (like `UISlider`,
  `Signals`).

### 4.2 `IUserSettingsStore` + `PlayerPrefsUserSettingsStore` (default)
- Keys are `"<product>.<category>/<name>"`. bool↔int, float, int, string, and JSON for compound
  (rebind overrides). Mirrors common's `PlayerPrefsHelper` abstraction so a game can swap in a
  cloud/save-file store (`ServiceLocator`-style) without touching the UI.
- Store is injectable: `UserSettingsService.Store = new MyCloudStore();`

### 4.3 Two integration modes (documented, both first-class)
- **Signal mode (default, agent-friendly):** game subscribes to `Signals.On<float>("Settings", ...)`
  and seeds from `UserSettingsService.Get<float>(...)` at startup. No package coupling.
- **Binding mode (CBN parity):** `UserSettingsService.Bind("Audio/Master", () => mixer.Volume,
  v => mixer.Volume = v)` for values the game already owns; `persisted:false` defers persistence to
  the game's save system (matches CBN settings that write to `PlayerData`).

---

## 5. Presenters (`Runtime/Menus/`)

### 5.1 `SettingsMenu : MonoBehaviour`
- Serialized: `MenuCatalogId catalog`, content `RectTransform`, optional category-nav root,
  and a `WidgetLibrary` reference (the starter-kit prefab set — see §7).
- On `Start()` (not `OnEnable` — registries fill in OnEnable; defer cross-object work per the
  lifecycle rule): build rows from the catalog, group by `group` into `UIPanel`s shown/hidden by a
  generated `UITab` bar (reuses the existing tab↔panel `controls` wiring), wire each control ↔
  service, and **bake** initial values WYSIWYG.
- Rebuild is idempotent and editor-bakeable so the prefab's serialized state equals runtime start
  state (Progressor/toggle WYSIWYG rule).

### 5.2 `CheatMenu : MonoBehaviour`
- Same builder, plus: search/filter field, favourites (persist a `List<string>` of ids in the store;
  port common's `FavouritesHolder` idea but store-backed, not EditorPrefs), category back-stack
  navigation (port `CheatMenuGameWindow` nav), numeric-param entry for `AddNumericCheat`, and toggle
  state display. A cheat button click → `Signals.Send("Cheat", id)`; parametric cheats carry the
  slider/stepper value in the signal payload.

### 5.3 `MenuRowBuilder` (shared, internal)
One method per `MenuControlKind` that calls the corresponding `UIWidgetFactory`-contract child names
so editor generation and runtime building stay structurally identical (the package's single-source
rule). Each row = label + control, themed via tokens/text-styles.

---

## 6. Widget & factory additions (`Editor/Agent/UIWidgetFactory.cs`, starter kit)

Confirmed gaps to fill (the factory has `CreateButton/Toggle/Switch/Slider/Stepper/InputField/
ListView/Panel/Stack/Grid` but **no dropdown**, and `UISlider` emits only Unity's `onValueChanged`):

1. **`UIDropdown` runtime widget** + `CreateDropdown` factory method + `"dropdown"` spec element
   kind. A themed `TMP_Dropdown` equivalent built from `NeoShape` (one shared material), publishing a
   `Signals.Send("UIDropdown","Behaviour", DropdownSignalData)` on the existing behaviour-stream
   pattern (so flow triggers can react too). Add `DropdownId` + `DropdownIdDatabase`.
2. **`UISlider` commit/preview events.** Add an `onRelease`/commit signal (pointer-up + keyboard
   release) distinct from continuous `onValueChanged`, and emit on the behaviour stream like
   `UIButton`/`UIToggle` do. Needed for `emitOnDrag`/`emitOnRelease`.
3. **`CreateSettingRow`, `CreateCheatRow`, `CreateKeyRebindRow`, `CreateCategorySidebar`** factory
   helpers (label + control compositions) with stable child-name constants for the exporter.
4. **Starter kit** (`StarterKitBootstrap`): generate default row prefabs (setting row, dropdown,
   key-rebind row, cheat row, category sidebar) into the widget prefab library so presenters have
   defaults — the equivalent of CBN's `_button/_slider/_toggle/_enum/_category` prefab slots, but
   theme-driven NeoShape prefabs.
5. **`NeoUISettings`** gains references to the default row/widget library + the `MenuCatalogDatabase`
   and `DropdownIdDatabase` (still one settings asset — just more fields).

All new inspectors/drawers (catalog editor, control-kind dropdown, catalog picker) go through the
**EditorUI kit** (`NeoGUI`/`NeoDropdown`/`NeoListView`), header accent for the new "Menus" category
(pick an unused accent), cached styles/lists, no per-OnGUI allocations.

---

## 7. Spec & agent integration (`Editor/Agent/`)

### 7.1 Spec extensions (`UISpec.cs`)
Add two optional top-level sections + one embeddable element kind, all deterministically
round-tripped (the export=import contract):

```jsonc
"settings": [
  { "id": "Settings/Audio",
    "groups": ["Audio","Video","Controls"], "start": "Audio",
    "items": [
      { "slider":   { "id": "Audio/Master", "group": "Audio", "label": "Master Volume",
                      "min": 0, "max": 1, "value": 0.8, "emitOnRelease": true } },
      { "dropdown": { "id": "Video/Quality", "group": "Video", "label": "Quality",
                      "options": ["Low","Medium","High"], "value": 2 } },
      { "toggle":   { "id": "Video/VSync", "group": "Video", "label": "VSync", "value": true } },
      { "rebind":   { "id": "Controls/Jump", "group": "Controls", "label": "Jump",
                      "action": "Gameplay/Jump", "bindingIndex": 0 } },
      { "button":   { "id": "Settings/Reset", "group": "Controls", "label": "Reset Defaults",
                      "onClick": { "signal": "Settings/ResetAll" } } }
    ] } ],
"cheats": [
  { "id": "Cheats/Main", "favourites": true,
    "items": [
      { "button":  { "id": "Player/GiveGold", "group": "Economy", "label": "Give 100 Gold" } },
      { "toggle":  { "id": "Player/God",      "group": "Player",  "label": "God Mode" } },
      { "slider":  { "id": "Time/Scale", "group": "World", "label": "Time Scale",
                     "min": 0, "max": 4, "value": 1 } }
    ] } ]
```

Plus an element kind so a view can embed a built menu:
`{ "settings": { "catalog": "Settings/Audio" } }` and `{ "cheats": { "catalog": "Cheats/Main" } }`.

Reuses existing `SignalRefSpec`, `CategoryNameId.Parse`, and the `onClick` block — no new trigger
machinery in the flow graph.

### 7.2 Generator (`UISpecGenerator.cs`)
- Emit a `SettingsCatalog`/`CheatCatalog` asset per `settings`/`cheats` entry into
  `Assets/Neo UI Generated/`, carry the `GeneratedMarker` (idempotent, collision-aware), register ids
  into the databases.
- When a view embeds a menu element, build the menu view prefab via `MenuRowBuilder` and add the
  presenter component pointing at the catalog. Bake WYSIWYG values.
- **Never fall back to scanning all of Assets** when a generated subfolder is missing
  (`FindGenerated` invariant) — would hijack committed catalogs.

### 7.3 Exporter (`UISpecExporter.cs`)
- Reverse: read catalog assets → `settings`/`cheats` sections; read embedded presenters → menu
  elements. Must be byte-identical on export→generate→export (new
  `SettingsAndCheatsSpecTests` joins `SpecLayoutAndWidgetTests` et al.).

### 7.4 Validation (`AgentValidation`)
- **Hard (`ValidateInteractivity`):** a settings/cheat control whose id resolves to no catalog
  entry, or a `Button` cheat/setting with no `onClick`/changeSignal binding → dead-interaction
  warning (extends the existing dead-button lint).
- **Soft (`ValidateDesign`):** controls without labels, off-scale row spacing, missing text styles,
  a `KeyRebind` referencing an absent `InputActionReference`. Stays out of `ValidateAll`.
- Run `{"action":"validate"}` after every generate (existing workflow).

---

## 8. Input rebinding (`Runtime/Menus/Rebinding/`)

New Input System only (package already depends on `com.unity.inputsystem`).

- `InputRebindService` wraps `InputActionRebindingExtensions.PerformInteractiveRebinding`:
  start/cancel/complete, conflict detection, reset-to-default, and per-binding **display string**
  (`action.GetBindingDisplayString(index)`).
- Persists overrides via the store as JSON (`action.SaveBindingOverridesAsJson` /
  `LoadBindingOverridesFromJson`) keyed under the `Controls` category, and emits
  `Signals.Send("Settings","<id>/Rebound", displayString)`.
- `UIRebindControl` widget (the `KeyRebind` row): a button showing the current binding; click →
  "Press a key…" capture state → commit/cancel; a small "reset" affordance.
- Catalog `KeyRebind` items carry `inputActionRef` + `bindingIndex`. The demo wires an
  `InputActionAsset` with a couple of gameplay actions.

---

## 9. Testing strategy

Follows the package's "behavior tests, not just screenshots" rule. **EditMode-first** because
signal dispatch and service Set/Get are synchronous.

**EditMode** (`Tests/EditMode/`):
- `UserSettingsServiceTests` — Get default resolution, Set persists + emits `"Changed"` signal,
  `commit:false` emits `"Preview"` and does **not** persist, missing-id warns, store round-trips all
  types, code `Bind` getter/setter path, store swap.
- `MenuBuildTests` — build a catalog into an in-memory canvas; assert one row per item, correct
  widget kind, labels, grouping into panels, **baked initial values == service values** (WYSIWYG),
  category start-group visibility.
- `MenuInteractionTests` — drive a built slider/toggle/dropdown widget's value-change → assert
  `UserSettingsService` value + store + outgoing signal; cheat button click → assert `"Cheat"`
  signal; parametric cheat carries payload.
- `SettingsAndCheatsSpecTests` — export→generate→export byte-identical; dead-binding lint fires;
  design lint warnings.

**PlayMode** (`Tests/PlayMode/`):
- Rebinding flow with simulated input (`InputTestFixture`): perform rebind → display string updates
  → override persists → reload store → still applied → reset restores default.
- Slider drag-vs-release signal timing; category nav show/hide; persistence across a simulated
  store reload.

**Pipeline net:** extend `GeneratedFlowPlaythroughTests` / `FullStackEndToEndTest` so the canonical
demo spec includes a settings view and a cheat menu; click through tabs and toggles asserting
service state — catching "renders fine but does nothing" regressions end to end.

---

## 10. Demo scene (final deliverable)

A `buildScene`-generated playable scene from a `Assets/Showcases/Specs/settings-menu.json` spec showcasing:
- **Settings menu** — tabbed Audio / Video / Controls with slider (master volume, live preview +
  commit), dropdown (quality), toggles (vsync, fullscreen), and a Reset Defaults button. A tiny demo
  listener (`Signals.On`) shows values applying live (e.g., a label echoing volume, a theme-variant
  toggle flipping Dark/Light via `ThemeService`).
- **Cheats menu** — categories, favourites, a numeric cheat (time scale slider), and toggles
  (god mode), each logging via the `"Cheat"` signal.
- **Input rebinding** — two gameplay actions rebindable, persisted across runs.

---

## 11. Phased rollout & acceptance criteria

Each phase compiles against the open editor via the csc trick (kit stays dependency-free), runs its
tests, and ends with `{"action":"validate"}` clean.

**P1 — Service & store (no UI).** `UserSettingsService`, `IUserSettingsStore` +
`PlayerPrefsUserSettingsStore`, missing-id warnings, signal emission, code-binding path.
*Accept:* `UserSettingsServiceTests` green; statics reset on subsystem registration.

**P2 — Catalog model & databases.** `MenuControlKind`, `MenuItemDefinition`, `MenuCatalog`/
`SettingsCatalog`/`CheatCatalog`, `MenuCatalogDatabase` on `NeoUISettings`, catalog inspector via the
EditorUI kit. *Accept:* flat force-text round-trip; searchable catalog/id pickers; no per-OnGUI
allocations.

**P3 — Dropdown widget + slider commit events.** `UIDropdown` + `DropdownId(Database)` +
`CreateDropdown` + `"dropdown"` spec kind; `UISlider` release/commit signal. *Accept:* dropdown
spec round-trips; slider preview vs commit covered by tests; one shared material preserved.

**P4 — Presenters & row builder.** `SettingsMenu`, `CheatMenu`, `MenuRowBuilder`; starter-kit row
prefabs; WYSIWYG baking; category nav. *Accept:* `MenuBuildTests` + `MenuInteractionTests` green;
baked == runtime start state.

**P5 — Spec generate/export/validate.** `settings`/`cheats` sections + embeddable element kinds;
generator (idempotent, no Assets-scan fallback); exporter byte-identical; validation lints.
*Accept:* `SettingsAndCheatsSpecTests` green; dead-binding lint fires; `validate` clean.

**P6 — Input rebinding.** `InputRebindService`, `UIRebindControl`, `KeyRebind` catalog/spec support,
JSON override persistence. *Accept:* PlayMode rebind test green (rebind → persist → reload → reset).

**P7 — Demo scene & pipeline net.** `Assets/Showcases/Specs/settings-menu.json`, `buildScene`, extended
`GeneratedFlowPlaythroughTests`. *Accept:* generated scene is interactive; pipeline test clicks
through settings + cheats asserting service state.

---

## 12. Open decisions (recommend, easy to flip)

- **Naming:** `Runtime/Menus/` + `Neo.UI.Menus` + `UserSettingsService` (recommended) vs
  `Options`. Keeps clear distance from editor `NeoUISettings`.
- **Settings vs Cheats unification:** shared `MenuCatalog` base, separate subclasses (recommended) —
  same builder/service, distinct semantics (persistence, favourites, signal namespace).
- **Dropdown options source:** explicit `options` list (recommended, agent-friendly + flat) with an
  optional `enum:"Namespace.Type"` expansion handled by the service (CBN's `SetOptions<T>` parity).
- **Persistence default:** PlayerPrefs store shipped; cloud/save-file store left to the game via the
  injectable `IUserSettingsStore` (matches common's `PlayerPrefsHelper`/`ServiceLocator` separation).
```
