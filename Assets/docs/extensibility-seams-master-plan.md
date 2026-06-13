# Master Plan — Extensibility Seams

> The umbrella for the package's **extensible-by-design** tenet (see `CLAUDE.md` → *Hard
> constraints*). This doc defines the **shared convention** every seam plan follows, the
> **parallelization map** (so agents can run concurrently without colliding), and the **global
> acceptance criteria** for "the tenet is met." Each gap has its own executable sub-plan, linked
> below. The first worked example — `composer-catalog-unification-plan.md` — is folded in as one
> member of this family and is the reference implementation of the convention.

## The problem, in one line

Across the package, ~6 places define a **fixed set** — element/widget kinds, menu-item kinds, flow
trigger kinds, button variants/sizes/shapes, theme bundles, validation rules — as a sealed
`enum`/`switch`/hardcoded array a consuming project cannot extend without forking. The tenet says
each must ship its defaults *through* a documented seam. This plan family installs that seam in each
place, consistently.

## What is already right (the gold standard — do not touch, copy it)

Three subsystems were built correctly and are the templates the rest must match:

- **Flow nodes** — `FlowNode` abstract + `[SerializeReference]` on `FlowGraph.nodes`
  (`Runtime/Flow/FlowNode.cs:36`). A project writes `class FooNode : FlowNode`; it serializes and
  runs with zero package edits.
- **Progress targets** — `ProgressTarget` abstract + component dispatch
  (`Runtime/Animators/ProgressTarget.cs:6`).
- **Theme tokens / TextStyles / ShapeStyles** — string-keyed `List<>` registries with
  `SetToken`/`GetTextStyle`/`SetShapeStyle` (`Runtime/Theming/Theme.cs`).

Every seam below should end up looking like one of these.

## The shared convention (read this before any sub-plan)

### Two canonical seam shapes

**Pattern R — the Kinds Registry** (the default for "a set of named cases").
A `static` class holding the descriptors, seeded with the package built-ins in a static initializer,
exposing `All` / `TryGet(id)` / `Register(descriptor)` (replace-by-id-or-append). The **reference
implementation is `ComposerCatalogKinds`** from the composer-catalog plan — every new registry in
this family mirrors its shape:

```csharp
public static class NeoXxxKinds {
    private static readonly List<XxxKind> _kinds = new() { /* built-in defaults */ };
    public static IReadOnlyList<XxxKind> All => _kinds;
    public static bool TryGet(string id, out XxxKind kind) { /* … */ }
    public static void Register(XxxKind kind) { /* replace-by-id, else append */ }
}
```

- **Editor-side** registries (most of them) may be plain `static` — the editor is a single domain.
  Built-in seeding goes in the static initializer; a project registers from `[InitializeOnLoad]`.
- **Runtime-side** registries (flow triggers, anything evaluated at play time) must survive domain
  reload: seed lazily on first access and let a project register from `[RuntimeInitializeOnLoadMethod]`
  or a `MonoBehaviour`/SO it ships. Never rely on editor-only init for runtime behavior.
- A descriptor that needs designer-tunable data (colors, glyphs) carries a **ScriptableObject** the
  project authors in the inspector (Pattern A below), not raw literals.

**Pattern A — the Asset List** (for designer-tuned data behind a registry).
A `List<SomeAsset>` the project populates — e.g. button-variant color sets, an icon-map overlay —
referenced from `NeoUISettings`. Mirrors how the `Theme` asset already holds tokens/styles.

### Seam-first, migrate-later (the staging rule that makes this safe AND parallel)

Every sub-plan is staged the same way:

- **Phase 1 — install the seam as a pre-check.** Insert the registry *ahead of* the existing
  hardcoded path. The 25 built-in element kinds, the 8 triggers, etc. keep their proven code path
  untouched; only a *project's novel* case flows through the new path. Expose `Register`. **The
  tenet is satisfied at Phase 1** — and because built-ins are unchanged, the existing round-trip /
  golden tests stay green, so Phase 1 is low-risk and the file edits are small and localized.
- **Phase 2 (optional, later) — migrate built-ins through the seam.** Move each built-in case into a
  descriptor so the original `switch` shrinks toward nothing. Pure internal cleanup, no behavior
  change, can be done lazily and does not block the tenet.

This staging is what keeps the shared-file edits tiny (a pre-check at one anchor) so multiple agents
can touch the same big file with minimal conflict.

### Invariants every sub-plan must preserve

- **Round-trip:** export → generate → export stays byte-identical for built-ins (the existing
  `SpecLayoutAndWidgetTests`, `TypographyTests`, `IconAndVariantTests`, etc. must still pass). A
  registered *novel* kind must round-trip through its own provider.
- **No silent failures:** an unknown id that matches no registered kind must `Debug.LogWarning`
  (per `CLAUDE.md` → runtime robustness), never no-op silently.
- **Editor kit:** all new chrome uses `NeoDropdown` / `NeoColors` (per `CLAUDE.md` → editor tooling).
- **EditorUI stays dependency-free:** nothing in `Editor/EditorUI` may gain a `Neo.UI` reference.

## The sub-plans

| # | Plan | Pattern | Touches | Wave |
|---|------|---------|---------|------|
| 1 | `composer-catalog-unification-plan.md` (folded in) | R | `SpecTreeView`, `NeoComposerWindow`, `MenuCatalogEditor`, new `ComposerCatalogKinds` | 1 |
| 2 | `extensibility-seam-theme-bundles-plan.md` | R | `Editor/ThemeBundles.cs`, `ThemePaletteEditor.cs` | 1 |
| 3 | `extensibility-seam-validation-rules-plan.md` | R | `Editor/Agent/AgentValidation.cs`, `NeoUISettings` (+read `ComposerOptions.SpacingScale`) | 1 |
| 4 | `extensibility-seam-widget-attributes-plan.md` | R + A | `Editor/Composer/ComposerOptions.cs`, `UIWidgetFactory.cs` (VariantColors), `IconMap.cs`, `NeoUISettings` | 1 |
| 5 | `extensibility-seam-flow-triggers-plan.md` | R (runtime) | `Runtime/Flow/FlowTrigger.cs`, `UISpec.cs` (Parse/ToJson trigger region), `Drawers/IdDatabaseOptions.cs` | 1 |
| 6 | `extensibility-seam-element-kinds-plan.md` (keystone) | R | `UISpec.cs` (`ElementSpec.Kinds`), `UISpecGenerator.cs` (switch), `UISpecExporter.cs` (`ExportElementBody`), `SpecFieldCatalog.cs` | 2 |

## Parallelization map (for running agents concurrently)

The only files touched by more than one plan:

| Shared file | Plans | How to keep them parallel |
|-------------|-------|---------------------------|
| `UISpec.cs` | 5 (flow triggers, region ~700–805) and 6 (element kinds, region ~335) | **Disjoint regions** — flow-trigger edits live in `FlowEdgeSpec.ParseTrigger`/`TriggerToJson`; element-kind edits are the `ElementSpec.Kinds` array + a generator hook. Different methods, far apart → git merges cleanly. Safe to run together; if you'd rather not, put 6 in Wave 2 (it already is). |
| `NeoUISettings` | 3 (lint scale/threshold config) and 4 (variant + icon asset lists) | Both **append** new serialized fields. Each agent adds its field under a clearly-labeled region at the end of the class; no logic overlap. |
| `ComposerOptions.cs` | 4 (owns it — turns the arrays into registry reads) and 3 (only *reads* `SpacingScale`) | Plan 4 **owns** all edits; Plan 3 only consumes `ComposerOptions.SpacingScale` and must not edit the file. No write conflict. |

**Recommended execution:**

- **Wave 1 (run all five concurrently):** plans 1, 2, 3, 4, 5. They touch mutually disjoint files
  except the additive `NeoUISettings` appends (3 & 4) and the disjoint `UISpec.cs` regions (5 vs the
  keystone) — both handled above.
- **Wave 2 (the keystone, after Wave 1 merges):** plan 6. It's the largest and benefits from the
  registry convention being already proven by Wave 1. It absorbs the menu-item-kind set (sibling of
  element kinds) and the Composer accent-color gap (folds into the element-kind descriptor's
  `Accent`). Sequencing it second keeps `UISpec.cs` to one writer at a time and lets plan 6 reuse
  the exact registry shape the Wave-1 agents validated.

Each Wave-1 plan is self-contained: an agent can open its sub-plan doc and execute it end to end.
Compile-check editor code with Unity's Roslyn per `CLAUDE.md` (the editor is open — never batch-
compile), and run that plan's named tests before merging.

## Global acceptance criteria (the tenet is met when…)

1. For **each** of the six fixed sets, a consuming project can add a new case with a single
   `Register(…)` call (or one Asset added to a `NeoUISettings` list) from its own assembly —
   **without editing any package file** — and see it: in the relevant chrome (Composer picker /
   inspector), in generation, and (where applicable) in export round-trip.
2. All existing tests stay green (round-trip idempotency, golden specs, validation contracts).
3. Each registry has a test mirroring `SpecFieldCatalogTests.AddPicker_IsExactlyTheSpecKindList`:
   `All` contains exactly the built-ins; `Register` adds/replaces by id; the chrome's option list
   equals `All`.
4. `CLAUDE.md` → *Hard constraints* gains a one-line pointer to this master plan as the canonical
   catalog of seams, the way it already points at the composer-catalog plan.
5. No regression in drift/sync, the EditorUI dependency boundary, or runtime robustness
   (unknown-id warnings present for every new string-addressed lookup).
