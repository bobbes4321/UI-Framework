# Plan — Element-Kind Registry (keystone)

> Member of `extensibility-seams-master-plan.md`. Pattern **R** (Kinds Registry), staged
> **seam-first / migrate-later**. This is the largest seam and the one that actually unblocks a
> project from shipping its own widget. It also absorbs the **menu-item kind** set (its sibling) and
> the **Composer accent-color** gap. **Wave 2** — run after the Wave-1 plans merge.

## The problem

A custom widget (`carousel`, `data-table`, a game-specific HUD piece) cannot be added without editing
**six** package sites:

- `ElementSpec.Kinds` — the authoritative `string[]` of 25 kinds (`UISpec.cs:335`).
- `UISpecGenerator.BuildElementTree` — a ~290-line `static switch (element.kind)`
  (`UISpecGenerator.cs:493-784`), one case per kind, each calling a `UIWidgetFactory` method and
  wiring signals/view-commands/badges/id-registration.
- `UISpecExporter.ExportElementBody` — the reverse: a `GetComponent`-chain that sniffs the prefab
  back into a kind (`UISpecExporter.cs:270`+).
- `SpecFieldCatalog` — which inspector fields apply per kind (`SpecFieldCatalog.cs:76-150`);
  `ElementKinds => ElementSpec.Kinds` (`:179`).
- The Composer "+ Add child" picker (reads `SpecFieldCatalog.ElementKinds`).
- The Composer tree/inspector **accent color** switch on `element.kind` (the `NeoColors` gap).

That is the textbook "90%-then-stuck." The fix is one registry the generator, exporter, field
catalog and chrome all read — and a project registers a kind into it once.

## The fix, in one line

Introduce `NeoElementKinds` (Pattern R) holding an `INeoElementKind` per kind; in **Phase 1** the
generator/exporter consult the registry **first** for project kinds and fall through to the existing
built-in `switch`/`GetComponent`-chain unchanged, so built-ins keep their proven, byte-identical
path while a novel kind round-trips through its provider.

## Current state (what to change)

- `UISpec.cs:335-340` — `ElementSpec.Kinds` hardcoded array (also includes `"settings"`/`"cheats"`,
  the menu kinds — coordinate with `composer-catalog-unification-plan.md`).
- `UISpec.cs:925-928` — `MenuItemSpec.Kinds` (`label/button/toggle/switch/slider/stepper/dropdown/
  rebind`); generator switch `UISpecGenerator.cs:1072-1085`; exporter `UISpecExporter.cs:892-905`.
  Sibling problem; same seam shape (`NeoMenuItemKinds`).
- `UISpecGenerator.cs:493-784` — `switch (element.kind)`. Note the shared context each case uses:
  `parent`, `settings`, `report`, the `ViewBuild build` accumulator, and the deferred tab→panel
  wiring pass. Any seam interface MUST pass this context.
- `UISpecExporter.cs:262-280` — `ExportElement` → `ExportElementBody`, a chain of early-returning
  `GetComponent` checks. The pre-check hook goes at the **top** of `ExportElementBody`.
- `SpecFieldCatalog.cs` — `static` field table, private `Add`, `For(kind)` filter, `ElementKinds`.

## Design

### The descriptor + registry

```csharp
// Editor/Agent/NeoElementKinds.cs (NEW)
public interface INeoElementKind {
    string Kind { get; }                                   // "carousel"
    GameObject Build(ElementBuildContext ctx);             // ctx wraps parent, spec, settings, report, build
    bool TryExport(GameObject go, out ElementSpec spec);   // reverse; false if this go isn't ours
    IEnumerable<SpecField> Fields { get; }                 // inspector fields for this kind (Composer)
    UnityEngine.Color Accent { get; }                      // Composer tree/inspector accent (folds the NeoColors gap)
}
public static class NeoElementKinds {                       // Pattern R — mirror ComposerCatalogKinds exactly
    public static IReadOnlyList<INeoElementKind> All { get; }
    public static bool TryGet(string kind, out INeoElementKind k);
    public static void Register(INeoElementKind k);         // replace-by-Kind, else append
}
```

`ElementBuildContext` is a thin struct exposing everything the switch cases use today
(`RectTransform parent`, `ElementSpec element`, `NeoUISettings settings`, `GenerateReport report`,
`ViewBuild build`, plus the `RegisterId`/`AddViewCommand` helpers as instance methods or statics).
Define it by reading the locals the `switch` block touches — do not guess.

### Phase 1 — pre-check, built-ins unchanged

- **Generator** (`BuildElementTree`, top of the method, before the `switch`):
  ```csharp
  if (NeoElementKinds.TryGet(element.kind, out var ext))
      { go = ext.Build(ctx); /* common geometry/anchor pass still runs below */ }
  else switch (element.kind) { /* the existing 25 cases, untouched */ }
  ```
  Keep the post-switch geometry/anchor/child-recursion code shared for both paths.
- **Exporter** (top of `ExportElementBody`):
  ```csharp
  foreach (var k in NeoElementKinds.All)
      if (k.TryExport(go, out var spec)) return spec;
  // …existing GetComponent chain unchanged…
  ```
  Built-ins are NOT registered in Phase 1 (they stay in the switch/chain), so `All` is empty until a
  project registers — zero risk to the round-trip. A project's `TryExport` must be specific (match
  its own marker component) so it never hijacks a built-in.
- **Field catalog** (`SpecFieldCatalog`): expose `public static void RegisterField(SpecField)` and
  have `For(kind)` also union `NeoElementKinds.TryGet(kind, …).Fields`. Change
  `ElementKinds` to return `ElementSpec.Kinds` ∪ `NeoElementKinds.All.Select(k => k.Kind)` so the
  "+ Add child" picker shows project kinds.
- **`ElementSpec.Kinds`**: keep the array as the built-in list; add a `KnownKinds` accessor that
  unions the registry, and route validators/pickers through `KnownKinds` (so an unknown registered
  kind isn't flagged as invalid). Generation warns (not errors) on a kind that's neither built-in
  nor registered — preserve the existing "Unknown kind" `report.issues` path.
- **Accent color**: replace the Composer's `switch (element.kind)` accent lookup with
  `NeoElementKinds.TryGet(kind, out var k) ? k.Accent : <built-in default by category>`. Built-ins
  keep their current `NeoColors` accents via the default branch; only project kinds read `Accent`.

### Phase 2 (optional, later)

Migrate the 25 built-ins into `INeoElementKind` descriptors so the generator `switch` and the
exporter chain collapse into `NeoElementKinds.All` iteration. **This is itself parallelizable** —
one agent per kind-group (Containers / Interactive / Ranged / Shape&Image / Data). No behavior
change; the round-trip tests are the guardrail. Do the same for `MenuItemSpec.Kinds` →
`NeoMenuItemKinds`.

## New & modified files (Phase 1)

| File | Action |
|------|--------|
| `Editor/Agent/NeoElementKinds.cs` | NEW — `INeoElementKind` + `ElementBuildContext` + registry (Pattern R) |
| `Editor/Agent/UISpecGenerator.cs` | EDIT — registry pre-check atop `BuildElementTree`; keep shared geometry pass; warn on unknown kind |
| `Editor/Agent/UISpecExporter.cs` | EDIT — registry pre-check atop `ExportElementBody` |
| `Editor/Composer/SpecFieldCatalog.cs` | EDIT — `RegisterField`; `For` unions registry fields; `ElementKinds` unions registry |
| `Editor/Agent/UISpec.cs` | EDIT (minimal) — `ElementSpec.KnownKinds` accessor unioning the registry; validators/pickers read it |
| Composer accent lookup | EDIT — read `INeoElementKind.Accent`, built-in default branch unchanged |

## Testing

- `NeoElementKindsTests`: `All` empty by default (built-ins still in switch); `Register` adds/replaces
  by `Kind`; `KnownKinds` unions registry + `ElementSpec.Kinds`.
- Round-trip unchanged: every existing `SpecLayoutAndWidgetTests` / `*RoundTrip*` test passes (built-
  ins never entered the new path).
- New: register a throwaway `INeoElementKind` ("probe") in a test, generate a spec using it, export,
  assert the spec round-trips through its provider and the picker/field-catalog include it.
- Unknown-kind path still produces a `report.issues` warning (no silent failure).

## Acceptance criteria

1. A project registers `class CarouselKind : INeoElementKind` from `[InitializeOnLoad]`; the Composer
   "+ Add child" picker shows `carousel`, its inspector shows the kind's `Fields`, generation builds
   it, and export round-trips it — **no package file edited**.
2. The 25 built-ins are byte-identical through export→generate→export; all existing tests green.
3. The Composer accent for a project kind comes from its descriptor; built-in accents unchanged.
4. Menu-item kinds documented as the Phase-2 sibling (`NeoMenuItemKinds`), same shape.
