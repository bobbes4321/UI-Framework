# Plan — Flow Trigger Kind Registry

> Member of `extensibility-seams-master-plan.md`. Pattern **R**, but **runtime-side** (triggers are
> evaluated at play time) — registration must survive domain reload. **Wave 1**; touches
> `Runtime/Flow/FlowTrigger.cs` + `Drawers/IdDatabaseOptions.cs` (disjoint) and a localized region of
> `UISpec.cs` (the `FlowEdgeSpec` parse/serialize methods ~700–805, far from the element-kind edits —
> see the master parallelization map).

## The problem

Flow *nodes* are the gold standard of extensibility (`FlowNode` abstract + `[SerializeReference]`),
yet flow *triggers* are sealed: `FlowTrigger.TriggerType` is an 8-value `enum` driving **four**
hardcoded sites. A project wanting a gesture / input-action / proximity / network trigger forks all
four. This is the most jarring inconsistency in the package — the seam already exists one layer over.

## Current state (what to change)

- `Runtime/Flow/FlowTrigger.cs:13-24` — `enum TriggerType { None, ButtonClick, Signal, ToggleOn,
  ToggleOff, ViewShown, ViewHidden, Back, Timer }`.
- `Runtime/Flow/FlowTrigger.cs:73-92` — `FlowTriggerListener.Connect` switch.
- `Runtime/Flow/FlowTrigger.cs:109-143` — `FlowTriggerListener.Matches` switch.
- `Editor/Agent/UISpec.cs:721-766` — `FlowEdgeSpec.ParseTrigger` (JSON key → `TriggerType`).
- `Editor/Agent/UISpec.cs:777-804` — `FlowEdgeSpec.TriggerToJson` (`TriggerType` → JSON key).
- `Editor/Drawers/IdDatabaseOptions.cs:22-36` — `ForTrigger` switch (trigger type → which id database
  the drawer offers).

## The fix, in one line

Introduce `NeoTriggerKinds` (runtime Pattern R) where each kind owns its JSON key + parse + serialize
+ connect + match (+ which id database it picks from); the four switches consult the registry first
and fall through to the built-in enum cases, so built-in triggers are unchanged and a project
registers a new trigger kind once.

## Design

```csharp
// Runtime/Flow/NeoTriggerKinds.cs (NEW) — runtime, survives domain reload
public interface INeoTriggerKind {
    string Id { get; }                                   // "gesture"
    string JsonKey { get; }                              // the "on" object key the spec uses
    void Connect(FlowTriggerListener listener, Action fire);
    bool Matches(FlowTriggerListener listener, Signal signal);
}
public static class NeoTriggerKinds {                    // Pattern R; lazy-seeded, reload-safe
    public static IReadOnlyList<INeoTriggerKind> All { get; }
    public static bool TryGetByKey(string jsonKey, out INeoTriggerKind k);
    public static void Register(INeoTriggerKind k);       // call from [RuntimeInitializeOnLoadMethod]
}
```

Because `TriggerType` is a serialized `enum`, Phase-1 seam-first keeps the enum for built-ins and adds
a `string customKind` field on `FlowTrigger` (used only when `type == TriggerType.Custom`, a new
sentinel value). A project trigger serializes as `type = Custom, customKind = "gesture"`.

- **Connect / Matches** (`FlowTrigger.cs`): `if (type == Custom && NeoTriggerKinds.TryGet(customKind,
  …)) k.Connect/Matches(...); else switch(type){ /* built-ins untouched */ }`.
- **Parse** (`UISpec.cs:721`): after the built-in `else if` chain, a final
  `else { foreach (var k in NeoTriggerKinds.All) if (on.ContainsKey(k.JsonKey)) { type=Custom;
  customKind=k.Id; /* read its payload */ } }`.
- **Serialize** (`UISpec.cs:777`): `case Custom: return new(){ [kind.JsonKey] = … }` via
  `TryGet(customKind)`.
- **`IdDatabaseOptions.ForTrigger`** (`IdDatabaseOptions.cs:22`): add an optional
  `INeoTriggerKind.PreferredIdDatabase` (or a small map) so a custom trigger's drawer offers the
  right id list; built-ins keep the switch.
- **Editor/runtime boundary:** the registry and `INeoTriggerKind` live in **Runtime** (Connect/Match
  are runtime). The editor (`UISpec.cs`, `IdDatabaseOptions`) references it — that direction is
  allowed (`Neo.UI.Editor` → `Neo.UI`). Do **not** add any editor reference into `Runtime`.
- **No silent failure:** an `on` object whose only key matches no built-in and no registered kind
  must `Debug.LogWarning` (per `CLAUDE.md`), as the built-in parser does for unknowns today.

## New & modified files

| File | Action |
|------|--------|
| `Runtime/Flow/NeoTriggerKinds.cs` | NEW — `INeoTriggerKind` + registry (runtime Pattern R) |
| `Runtime/Flow/FlowTrigger.cs` | EDIT — `Custom` sentinel + `customKind`; `Connect`/`Matches` consult registry first |
| `Editor/Agent/UISpec.cs` | EDIT (localized) — `ParseTrigger`/`TriggerToJson` fall through to the registry by JSON key |
| `Editor/Drawers/IdDatabaseOptions.cs` | EDIT — `ForTrigger` consults the kind's preferred id database |

## Testing

- `NeoTriggerKindsTests` (runtime): `All` has the built-ins exposed via the registry façade (or empty
  + enum fallback, matching seam-first choice); `Register` adds/replaces by `Id`; lookup by `JsonKey`.
- Existing flow tests (`GeneratedFlowPlaythroughTests`, `RuntimeBehaviourRegressionTests`) green —
  built-in triggers Connect/Match unchanged.
- New: register a "probe" trigger, author an edge `"on": { "probe": "X/Y" }`, assert it parses to
  `Custom/probe`, round-trips to identical JSON, Connects, and Matches its signal.
- Unknown trigger key logs a warning, doesn't crash.

## Acceptance criteria

1. A project registers `class GestureTrigger : INeoTriggerKind` from
   `[RuntimeInitializeOnLoadMethod]`; an `on: { gesture: … }` edge parses, serializes byte-identically,
   connects and fires at runtime — no package file edited, and the trigger's drawer offers the right
   id list.
2. The 8 built-in triggers behave and round-trip identically.
3. No editor→runtime dependency introduced; runtime registration survives domain reload.
