# Plan — Validation Rule Registry

> Member of `extensibility-seams-master-plan.md`. Pattern **R** (Kinds Registry) for rules + a small
> config seam for the scale/thresholds. **Wave 1** — `AgentValidation.cs` is disjoint; the only
> shared touch is an additive `NeoUISettings` field (coordinate with the widget-attributes plan per
> the master parallelization map).

## The problem

`AgentValidation` is a sealed `static` class — no interface, registry, or hook. Every rule and every
magic number is baked in, so a project on a different design system gets **false** warnings it cannot
fix and **cannot add** its own checks. `CLAUDE.md` itself mandates the dead-interaction lint "stay in
sync when adding interaction wiring kinds" — that mandate is unsustainable without a seam.

## Current state (what to change)

- `AgentValidation.cs:71-79` — hardcoded contrast `pairs` (5 token/surface/min triples).
- `AgentValidation.cs:94` — hardcoded spacing `float[] scale = { 0,4,8,12,16,24,32,48,64 }`
  (**duplicates** `ComposerOptions.SpacingScale`, `ComposerOptions.cs:22` — unify on one source).
- `AgentValidation.cs:164-165` — `TextContrastMinimum = 3f`, `AffordanceContrastMinimum = 2f` consts.
- `AgentValidation.cs:304-338` — `ValidateInteractivity`: the fixed set of "this button/tab does
  something" wiring kinds (FlowTrigger / ShowPopupOnClick / HideContainerOnClick / ViewCommandOnClick
  / signal / event). A project's custom wiring (e.g. "opens URL") is falsely flagged dead.
- `AgentValidation.cs:351-367` — `ValidateMenuBindings`, tied to the two catalog kinds (coordinate
  with `composer-catalog-unification-plan.md`).

Note the existing split (keep it): hard contracts in `ValidateAll`; soft `ValidateDesign` /
`ValidateInteractivity` surfaced as `designWarnings`. **The registry must respect that split** — a
custom rule declares which bucket it's in.

## The fix, in one line

Add `NeoValidationRules` (Pattern R) holding `INeoValidationRule` instances bucketed by severity
(hard / design / interactivity); `AgentValidation`'s existing methods run the built-ins then iterate
the registry; move the scale/thresholds to a config a project can override.

## Design

```csharp
// Editor/Agent/NeoValidationRules.cs (NEW)
public enum ValidationBucket { Hard, Design, Interactivity }
public interface INeoValidationRule {
    ValidationBucket Bucket { get; }
    void Validate(ValidationContext ctx);   // ctx: prefab/spec under test + Add(issue)/AddWarning(text)
}
public static class NeoValidationRules {     // Pattern R
    public static IReadOnlyList<INeoValidationRule> All { get; }
    public static void Register(INeoValidationRule rule);
}
```

- Each existing `Validate*` method, after its built-in checks, iterates
  `NeoValidationRules.All.Where(r => r.Bucket == thisBucket)` and feeds them the same context object
  it already builds. **Built-ins are not moved** in Phase 1 (seam-first) — they stay inline; the
  registry only adds project rules. So `ValidateAll`'s hard contracts can't be weakened by a missing
  registration.
- **Scale + thresholds → config.** Add to `NeoUISettings` (additive, labeled region):
  `float[] spacingScale` (default = the current values), `float textContrastMin`,
  `affordanceContrastMin`, and the contrast `pairs`. Replace the literals in `AgentValidation` with
  reads from settings; **delete the duplicate** in `ComposerOptions` and have it read the same
  settings field (single source of truth for "the blessed scale"). Both lint and the Composer snap
  then agree.
- **Interactivity wiring** is the subtle one: the "does this button do something?" check should ask
  the registry "does any rule claim this object is wired?" so a project's custom wiring component can
  declare itself live. Expose `INeoValidationRule`-style `bool ClaimsWired(GameObject)` providers, or
  simpler: a `NeoInteractivityProviders.Register(Func<GameObject,bool>)` the dead-interaction check
  ORs in. Pick the lighter of the two; document it where `CLAUDE.md` points.

## New & modified files

| File | Action |
|------|--------|
| `Editor/Agent/NeoValidationRules.cs` | NEW — bucketed rule registry + `ValidationContext` (Pattern R) |
| `Editor/Agent/AgentValidation.cs` | EDIT — run registry per bucket; read scale/thresholds/pairs from settings; OR-in interactivity providers |
| `NeoUISettings` | EDIT (additive) — `spacingScale`, contrast mins, contrast pairs (labeled region; coordinate with widget-attributes plan) |
| `Editor/Composer/ComposerOptions.cs` | EDIT — `SpacingScale` reads the settings field (kill the duplicate) |

## Testing

- `NeoValidationRulesTests`: registering a Design rule makes it fire in `ValidateDesign` and **not**
  in `ValidateAll`; a Hard rule fires in `ValidateAll`.
- A project rule that flags a custom condition appears in `designWarnings`; built-in warnings
  unchanged.
- Custom spacing scale on settings: a value off the default scale but on the project's scale no
  longer warns. Existing design-lint tests pass with default settings.
- Interactivity: a button wired only by a project's custom component is no longer flagged dead once
  its provider is registered.

## Acceptance criteria

1. A project registers a custom rule + a custom spacing scale from `[InitializeOnLoad]` / settings;
   its rule fires in the right bucket and its scale silences false spacing warnings — no package file
   edited.
2. Hard validation contracts cannot be weakened by registration (built-ins always run).
3. One source of truth for the blessed spacing scale (lint and Composer agree).
