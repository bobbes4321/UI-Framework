# Plan 3 — Developer Binding Ergonomics

## Problem

The runtime plumbing for wiring generated UI to game code already exists and is solid — `UIData`,
`Signals`, `UserSettingsService`, name-addressed registries. But it's **undiscoverable from the
generated output** and forces hand-written translation/scaffolding for the three most common tasks:

1. **Populating a list.** `UIData.Set(category, name, rows)` works, but rows are
   `Dictionary<string,string>` (stringly-typed, no nesting), the binding is read-only, and a full
   rebuild happens on any change.
2. **Handling a click.** A button emits a *generic* `ButtonSignalData` on `"UIButton/Behaviour"`
   (`UIButton.cs:209-213`), not a domain signal like `"Shop/Buy"`. The developer must subscribe to
   the firehose and branch on `data.category`/`data.buttonName` by hand.
3. **Knowing what to wire at all.** Nothing tells the developer which signals a generated menu emits,
   which `UserSettingsService.Bind` points exist, or which `UIData` categories feed which lists. It's
   archaeology through the spec or grep.

## Goal

Make the path from "agent generated a menu" to "the menu does something in my game" fill-in-the-blank
instead of reverse-engineering, **without** breaking the agent-first, signals-over-UnityEvents,
string-addressed conventions (`CLAUDE.md` hard constraints).

Three deliverables:

- **A. Generated binding manifest + C# stub** — per spec, emit a discoverable list of every signal,
  bind point, and data source, plus an optional partial-class stub the developer fills in.
- **B. Domain signals on widgets** — let the spec name a domain signal a widget emits, so the
  translation layer disappears.
- **C. Typed, incremental data sources** — a typed list-binding API alongside the stringly-typed one,
  with row-level updates.

## Current-state references

- Data binding: `Runtime/Data/UIData.cs:19-60` (`Set(category,name,rows)`), `Runtime/Data/UIBoundList.cs:14-85`
  (template clone + `{key}` token fill, `Rebuild()`).
- Signals: `Runtime/Signals/Signals.cs:16-122` (`On<T>`/`Send`), `Runtime/Signals/SignalStream.cs`.
- Button emission: `Runtime/Interactive/UIButton.cs:209-213` (`ButtonSignalData` on `"UIButton/Behaviour"`).
  Toggle `:138-139`, Slider `:111-112`, Dropdown `:67-68`.
- Click wiring in spec: `ElementSpec.onClickSignal` (`UISpec.cs:391`), parsed `UISpec.cs:465-474`.
- Settings binding: `Runtime/Menus/UserSettingsService.cs:16-299` (`Bind(category,name,getter,setter,persist)`,
  `Get/Set<T>`), `MenuControlBinder.cs`, `MenuPresenter.cs`. Catalog model `MenuItemDefinition.cs`.
- Working example to mirror: `Runtime/Demo/ShowcaseDirector.cs` (`UIData.Set`, `Signals.On`, cheat
  handlers, widget lookup).
- Validation hook for "dead interaction": `AgentValidation.ValidateInteractivity` (`CLAUDE.md`).

## Design

### A. Binding manifest + stub generator

New editor action + file. After generate, produce a **manifest** describing the contract the spec
exposes to game code, derived from the spec (not the prefabs — the spec is the truth):

`{"action":"bindings","out":"Assets/.../GameUI.bindings.json"}` in `AgentBridge` (new case in the
`AgentBridge.cs:100-114` switch), plus `Tools → Neo UI → Generate Binding Stub`.

Manifest shape:
```json
{
  "signals":   [ { "category":"Shop", "name":"Buy", "payload":"none|bool|float|int|string", "source":"button Shop/Buy" } ],
  "dataSources":[ { "id":"Shop/Deals", "tokens":["name","price"], "source":"list in view Shop/Store" } ],
  "settings":  [ { "category":"Audio", "name":"MusicVolume", "kind":"slider", "type":"float", "default":"0.8" } ],
  "cheats":    [ { "category":"Cheats", "name":"Player/GodMode", "kind":"toggle", "type":"bool" } ],
  "views":     [ { "id":"Shop/Store", "category":"Shop", "name":"Store" } ]
}
```

Derivation rules (all from the in-memory `UISpec`):
- **signals** — every `onClickSignal` (`ElementSpec.onClickSignal`), plus the standard widget signals
  for each interactive id, plus any domain signals from deliverable B.
- **dataSources** — every `list`/`grid` with a `bind` (`ElementSpec.bind`); `tokens` = the distinct
  `{key}` tokens found in the `item` template's text labels.
- **settings/cheats** — every `MenuItemSpec` in the catalogs (`UISpec.cs:923-1050`), with the C# type
  per kind (toggle/switch→bool, slider/stepper→float, dropdown→int, button→none).
- **views** — every `ViewSpec.id`.

**Stub generator** — emit a `partial class <FlowName>UIBindings` with:
- `const string` ids for every view, signal, data source, setting (greppable, refactor-safe).
- A `Wire()` method with `Signals.On(...)` subscriptions for each domain signal and
  `UserSettingsService.Bind(...)` calls for each setting, each calling an empty `partial void
  On<Thing>(...)` the developer implements in their own (non-generated) partial file.
- A `Populate<Source>(IEnumerable<...>)` helper per data source calling `UIData.Set`.

Emit the stub OUTSIDE `GeneratedRoot` (e.g. `Assets/Scripts/Generated/`) so it isn't wiped by spec
regen of UI assets, and regenerate it idempotently. Mark it `// <auto-generated>`; the developer's
implementations live in a sibling partial they own.

### B. Domain signals on widgets

Today `onClick.signal` already exists (`UISpec.cs:391,553`) and emits a domain signal — but the
*standard* widget signal still fires generically and the manifest/flow rely on `UIButton/Behaviour`.
Make domain signals first-class so developers never touch the firehose:

- Spec: `onClick.signal` already covers buttons. Add the equivalent for toggles/sliders/dropdowns:
  an optional `signal` field on the element that names the domain stream the widget publishes to (in
  addition to the standard stream, for back-compat). Parse in `ElementSpec.Parse`, export in
  `ToJsonObject`, generate the wiring in `UISpecGenerator`.
- Runtime: the interactive component, when given a domain signal id, calls
  `Signals.Send(category, name, payload, sender)` with the typed payload (bool for toggle, float for
  slider, int for dropdown) in addition to its standard signal. Keep the standard signal for
  back-compat and the flow system, which already listens on it.
- Result: `Signals.On<bool>("Audio","Muted", OnMuted)` works directly — no `ToggleSignalData`
  branching.

Keep this **additive** — the generic signals and existing flow triggers must keep working
(`FlowTrigger` listens on the standard streams — `Runtime/Flow/FlowTrigger.cs`).

### C. Typed, incremental data sources

Add a typed façade over `UIData`/`UIBoundList` without removing the string API:

- `UIData.Set<T>(category, name, IEnumerable<T> rows, Func<T,IReadOnlyDictionary<string,string>> project)`
  — caller supplies the projection from their domain type to row tokens once; no manual dictionary
  building at call sites.
- `UIData.Update<T>(category, name, int index, T row)` / `UIData.Add` / `UIData.RemoveAt` — row-level
  ops so `UIBoundList` can patch a single spawned row instead of full `Rebuild()`. Extend
  `UIBoundList` with `UpdateRow(index)`/`InsertRow`/`RemoveRow` that re-token only the affected row.
- Keep the existing `Set(category,name,IEnumerable<Dictionary<string,string>>)` untouched.

This is the lowest-priority deliverable; A and B remove the most friction.

## New & modified files

| File | Action |
|------|--------|
| `Editor/Agent/BindingManifest.cs` | NEW — derive manifest from `UISpec` |
| `Editor/Agent/BindingStubGenerator.cs` | NEW — emit `partial class` C# stub |
| `Editor/Agent/AgentBridge.cs` | EDIT — add `bindings` action |
| `Editor/Agent/UISpec.cs` | EDIT — `signal` field on toggle/slider/dropdown elements (B) |
| `Editor/Agent/UISpecGenerator.cs` | EDIT — wire domain signals on those widgets |
| `Editor/Agent/UISpecExporter.cs` | EDIT — export the new `signal` field (deterministic round-trip) |
| `Runtime/Interactive/UIToggle.cs`, `UISlider.cs`, `UIDropdown.cs` | EDIT — emit optional domain signal |
| `Runtime/Data/UIData.cs` | EDIT — typed `Set<T>`/`Update`/`Add`/`RemoveAt` (C) |
| `Runtime/Data/UIBoundList.cs` | EDIT — row-level patch methods (C) |
| `CLAUDE.md` | EDIT — document `bindings` action + the developer wiring story |

## Conventions to respect (`CLAUDE.md`)

- Agent-first, string-addressed: ids stay `Category/Name` strings, never GUIDs.
- Signals over serialized UnityEvents — the stub uses `Signals.On`, not inspector wiring.
- Deterministic export: any new spec field (the `signal` on toggle/slider/dropdown) needs symmetric
  `Parse`/`ToJsonObject` and a round-trip test, or it breaks `SpecLayoutAndWidgetTests` et al.
- No silent failures: stub `Wire()` should `Debug.LogWarning` if a bound view/signal id resolves to
  nothing at runtime (mirror `UIView.ProcessCommand`).

## Testing

- `BindingManifestTests.cs` (EditMode) — generate the canonical demo spec, assert the manifest lists
  every signal/dataSource/setting/cheat/view with correct payload/type.
- `BindingStubTests.cs` — emitted C# compiles (Roslyn compile-check per `CLAUDE.md`); regen is
  idempotent; the developer's partial is never overwritten.
- `DomainSignalTests.cs` (PlayMode) — a toggle with a `signal` id fires `Signals.On<bool>` on that
  id AND the standard stream; flow triggers on the standard stream still fire.
- `TypedDataTests.cs` (PlayMode) — `Set<T>` populates a `UIBoundList`; `UpdateRow` patches one row
  without rebuilding others.
- Round-trip: `IconAndVariantTests`-style test for the new `signal` field export.

## Acceptance criteria

1. After generate, a developer can open one manifest/stub file and see every signal, setting, data
   source, and view id the UI exposes — no grep, no spec reading.
2. `Signals.On<bool>("Audio","Muted", h)` works directly for a generated toggle with a domain signal;
   no `ToggleSignalData` branching needed.
3. Populating a list is `Populate(myItems)` / `UIData.Set<T>(...)` with a one-time projection.
4. Existing flow triggers and standard widget signals are unchanged (additive only).
5. All new spec fields round-trip byte-identically.
