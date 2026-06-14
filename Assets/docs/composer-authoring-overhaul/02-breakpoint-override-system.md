# Pillar B — Breakpoint / Orientation Override System

[← Master plan](00-master-plan.md) · Prev: [01 — Layout](01-responsive-layout-foundation.md) · Next: [03 — Viewport](03-free-viewport.md)

> The deep one. A view authors a **base** layout (Pillar A); a `breakpoints` section + per-element
> `overrides` store ONLY the deltas per condition; a runtime driver applies the active override.
> Depends on Pillar A merged. Touches the core quartet → serialize against any other quartet work in
> the same wave (none in Wave 2: C is Composer-only).

---

## B.1 Decision recap & rationale

- **Runtime, not bake-time.** One prefab adapts live as the viewport/device aspect changes — matching
  the user's literal "every possible aspect ratio" request and DevTools/Framer behavior. Bake-time
  (N prefab variants) was rejected: asset explosion, broken single-prefab WYSIWYG identity, no
  reaction to continuous resize. **Cost:** a runtime component + per-element delta applier; mitigated
  by applying only on breakpoint *change* and only to elements that declare an override.
- **Delta-override cascade (Webflow/Framer).** Base layout inherits; an override stores only changed
  fields. Active override merges OVER base, field-by-field on the `layout` object.
- **WYSIWYG = base.** The baked prefab equals the base breakpoint; the preview drives the runtime
  component to show any breakpoint (Pillar C ties viewport aspect → active breakpoint).

---

## B.2 Architecture — data model

### B.2.1 Top-level `breakpoints` section (`UISpec`)

Add to `UISpec` (alongside `views`, `popups`, `presets`):
```csharp
public List<BreakpointSpec> breakpoints = new List<BreakpointSpec>();
```
```csharp
[Serializable]
public class BreakpointSpec
{
    public string name;                 // "landscape", "wide", … (the override key)
    public BreakpointCondition when;    // exactly one condition kind set
    // Parse/ToJsonObject deterministic; ordered list, first match wins at runtime.
}
[Serializable]
public class BreakpointCondition
{
    public string orientation;  // "portrait" | "landscape"  (one kind)
    public float? minAspect;    // width/height >=         (another kind)
    public float? maxAspect;
    public float? minWidth;     // reference-px width <= / >=  (CanvasScaler reference space)
    public float? maxWidth;
    // EXTENSIBLE: see condition registry B.2.4 — these built-ins are sugar over registered evaluators.
}
```

### B.2.2 Per-element `overrides` (`ElementSpec`)

Add `public Dictionary<string, LayoutSpec> overrides;` (key = breakpoint name → delta `LayoutSpec`).
Reuse `LayoutSpec` from Pillar A so the override IS a partial layout (only set fields are deltas;
unset fields inherit base). JSON:

```jsonc
"vstack": {
  "layout": { "h": "leftRight", "v": "topBottom", "offset": { "left": 24, "right": 24, "top": 48, "bottom": 48 } },
  "overrides": {
    "landscape": { "h": "center", "size": { "w": 900 } },   // only these change in landscape
    "narrow":    { "offset": { "left": 8, "right": 8 } }
  },
  "children": [ /* … */ ]
}
```

> Decision: overrides only carry `layout` deltas in v1 (position/size/constraint/sizing). Other
> properties (color, text) are explicitly out of scope — they rarely need per-breakpoint values and
> would balloon the model. Documented limitation; the seam (`overrides` being a dict of partials) can
> grow later.

### B.2.3 Runtime driver `UIResponsiveRoot`

New `Runtime/Containers/UIResponsiveRoot.cs` (`UIBehaviour`), one per view root:
- Serializes the breakpoint table (force-text: arrays of name + condition) and, per affected child, a
  serialized list of `(breakpointName, targetPath, resolvedAnchorMin/Max/offsetMin/Max/sizeDelta)` —
  i.e. the generator **pre-resolves** each override's `layout` to concrete RectTransform values at
  bake time, so the runtime applier is a cheap "set these 5 vectors" with **no spec parsing at
  runtime**. (Keeps the runtime dependency-free of the editor spec types.)
- On `OnRectTransformDimensionsChange` / a canvas-size watch, computes current aspect+orientation,
  selects the first matching breakpoint (else base), and IF it changed, applies that breakpoint's
  pre-resolved values to each target RectTransform. No per-frame work; only on change.
- **No silent failure:** if a target path can't be resolved, `Debug.LogWarning` once.
- **WYSIWYG:** at `Start`, applies the breakpoint matching the *baked* state only if it differs (base
  is baked, so usually a no-op).

### B.2.4 Extensibility seam — condition evaluators (NOT an enum)

New `Editor/Agent/BreakpointConditions.cs`:
```csharp
public interface IBreakpointCondition { string Id { get; } bool Matches(BreakpointEnv env); }
public struct BreakpointEnv { public float width, height, aspect; public bool portrait; }
public static class BreakpointConditions { public static void Register(IBreakpointCondition c); … }
```
Built-ins (`orientation`,`minAspect`,`maxAspect`,`minWidth`,`maxWidth`) register themselves; the
runtime mirror lives in `UIResponsiveRoot` as a parallel evaluator over the pre-resolved table so
runtime stays editor-free. A project adds e.g. a `safeAreaInset` condition through the seam.

---

## B.3 Round-trip, diff, merge

- **Generator:** bakes base into the prefab (Pillar A path), then for each element with `overrides`
  resolves each override's effective `layout` (base merged with delta) to concrete RectTransform
  values and writes them + the breakpoint table into `UIResponsiveRoot`. WYSIWYG: base bakes.
- **Exporter:** reads `UIResponsiveRoot` back → reconstructs `breakpoints` + per-element `overrides`.
  Must reverse the pre-resolution: store the *original delta `LayoutSpec`* alongside the resolved
  values in `UIResponsiveRoot` (a serialized parallel array) so export is exact, not re-derived.
  **Byte-identity test** in `Tests/EditMode/BreakpointRoundTripTests.cs`.
- **Diff/merge — `SpecPath` ADDITION required.** `overrides` is a dict keyed by breakpoint *name*.
  `SpecDiff.DiffDict` already diffs dicts by key (`SpecDiff.cs:83-97`), so `overrides/landscape/...`
  addresses fine **without** changing `SpecPath.ListKey` (that's only for *lists*). The `breakpoints`
  **list** DOES need a key: add `case "breakpoints": return Identity(item, "name", index);` to
  `SpecPath.ListKey` (`SpecPath.cs:84-105`) and the matching `ChildPath` case
  (`SpecPath.cs:112-129`). This makes a renamed/reordered breakpoint diff cleanly. **This is the only
  `SpecPath` edit in the whole overhaul** — call it out for the merge-conflict audit.
- **OffSpecLint:** unaffected (overrides are spec-resident, fully round-trippable).

---

## B.4 Composer authoring/preview UI (Wave 2, parallel-safe with C)

- A breakpoint manager in the Composer toolbar: list/add/remove/rename breakpoints (view or global
  scope per decision below), set each one's condition.
- A **breakpoint selector** (segmented control) that sets the "edit scope": when a non-base
  breakpoint is active, inspector edits write into that breakpoint's `overrides` delta (Pillar F
  consumes this scope), and the preview drives `UIResponsiveRoot` to that condition.
- **Scope decision:** `breakpoints` are **global** (top-level `UISpec.breakpoints`) so all views
  share the same named conditions (consistent device story); `overrides` are per-element. This mirrors
  Framer (project breakpoints) and keeps the condition set DRY.

---

## B.5 Workstreams

### Workstream B-core — quartet + runtime (SERIAL within Wave 2 against any quartet work)
- **Owns (edit):** `Editor/Agent/UISpec.cs` (`breakpoints`, `BreakpointSpec`, `BreakpointCondition`,
  `ElementSpec.overrides`), `Editor/Agent/UISpecGenerator.cs` (bake `UIResponsiveRoot`),
  `Editor/Agent/UISpecExporter.cs` (read it back), `Editor/Agent/SpecPath.cs` (the single
  `breakpoints` list-key addition).
- **Owns (create):** `Runtime/Containers/UIResponsiveRoot.cs`, `Editor/Agent/BreakpointConditions.cs`,
  `Tests/EditMode/BreakpointRoundTripTests.cs`, `Tests/EditMode/BreakpointCascadeTests.cs`,
  `Tests/PlayMode/ResponsiveDriverTests.cs` (driver selects + applies on resize).
- **Non-overlap:** In Wave 2, C touches only Composer/preview files → no quartet overlap. If any
  future quartet workstream shares the wave, serialize.
- **Dependencies:** Pillar A fully merged (reuses `LayoutSpec`, `ConstraintLayout`).
- **Acceptance:** override JSON round-trips byte-identical; runtime driver switches layout on
  simulated resize/orientation; cascade merges base+delta correctly; renamed breakpoint diffs as a
  modify, not add+remove.
- **Verify:** Roslyn relay (Runtime → Editor); EditMode round-trip + cascade tests in per-wave check;
  PlayMode driver test gated.
- **Define for C:** the `IActiveBreakpoint` hook — a method the preview calls to force
  `UIResponsiveRoot` to a chosen breakpoint name (preview-only). Ship this interface in B-core so C
  compiles against it.

### Workstream B-ui — Composer breakpoint authoring (Composer-only, parallel after B-core merges)
- **Owns (edit):** `Editor/Composer/NeoComposerWindow.cs` (toolbar breakpoint manager + selector),
  `Editor/Composer/SpecDocument.cs` (track "active edit breakpoint" state — additive field + getter).
- **Owns (create):** `Editor/Composer/BreakpointBar.cs` (the manager/selector control via EditorUI
  kit).
- **Dependencies:** B-core merged.
- **Acceptance:** can add/rename/delete breakpoints + conditions; selecting a breakpoint scopes
  inspector edits (verified jointly with Pillar F) and switches the preview's active condition.
- **Verify:** Roslyn Editor compile; manual smoke in the open editor (no batch).
- **Seam consumed:** `BreakpointConditions.All` populates the condition picker.

## B.6 Definition of done for Pillar B
- [ ] `breakpoints` + `overrides` round-trip byte-identically.
- [ ] Runtime driver selects the first matching breakpoint and applies deltas on resize/orientation,
      only on change.
- [ ] Cascade (base + delta) is correct and tested.
- [ ] `SpecPath` keys breakpoints by name; rename diffs cleanly; merge handles override conflicts.
- [ ] Condition kinds are a registry seam; global breakpoints / per-element overrides scope decided
      and implemented.
- [ ] Composer can author + preview breakpoints; WYSIWYG bakes the base.
