# Pillar A — Responsive Layout Foundation (the keystone)

> Status: retired 2026-07 — the Composer was removed; native authoring supersedes it; see CLAUDE.md.

[← Master plan](00-master-plan.md) · Next: [02 — Breakpoints](02-breakpoint-override-system.md)

> **This phase blocks everything else.** It replaces absolute-px positioning with a Figma-style
> per-axis constraint + offset model and adds per-child sizing modes. It must land, compile, and
> round-trip byte-identically before any Wave 2 work begins.

---

## A.1 Why (root-cause recap, verified)

The generator writes free elements as absolute canvas pixels:

```csharp
// UISpecGenerator.cs:942-951  (ApplyCommonOverrides, !inLayout branch)
if (!string.IsNullOrEmpty(element.anchor)) UIWidgetFactory.TryApplyAnchor(rect, element.anchor);
if (element.size != null && element.size.Length >= 2) { rect.sizeDelta = …; }
if (element.position != null && element.position.Length >= 2)
    rect.anchoredPosition = new Vector2(element.position[0], element.position[1]);
```

`TryApplyAnchor` (`UIWidgetFactory.cs:131-144`) only sets `anchorMin/Max/pivot` and zeroes the
*stretch* axis; the fixed-axis offset is carried entirely by `anchoredPosition`, which is measured
from the anchor in absolute px. With a `Center` anchor, `[100,200]` is "+100px right of center" — at
1080 wide that's x=640, at 1920 wide it's x=1060: the element walks across the screen as the canvas
resizes, and the lack of any per-axis "stick to the right edge / stretch both edges" intent means it
clips off-screen on a different aspect. **Fix: express the offset relative to a per-axis constraint,
written into `offsetMin`/`offsetMax`, not `anchoredPosition`.**

---

## A.2 Architecture — the `layout` model

### A.2.1 New types in `UISpec.cs`

Add a `LayoutSpec` class (sibling of `GradientSpec`, same Parse/ToJsonObject discipline) and a
nullable `layout` field on `ElementSpec`:

```csharp
[Serializable]
public class LayoutSpec
{
    public string h;                 // horizontal constraint: left|right|leftRight|center|scale  (default "left")
    public string v;                 // vertical   constraint: top|bottom|topBottom|center|scale  (default "top")
    public LayoutOffset offset;       // per-constraint offsets (see table)
    public LayoutSize size;           // { w, h } fixed sizes; ignored on a stretched axis
    public LayoutSizing sizing;       // { w, h } in {fixed, hug, fill}  (per-child mode in a stack/grid)
    // NOTE: offset semantics depend on h/v (see A.2.3). Stored as a small dict to stay forward-compatible.

    public static LayoutSpec Parse(Dictionary<string, object> obj) { … }   // null-safe
    public Dictionary<string, object> ToJsonObject() { … }                 // deterministic key order: h,v,offset,size,sizing
    public bool IsEmpty => /* all fields null/default */;                   // empty -> don't emit
}
```

`offset`, `size`, `sizing` are tiny value holders. Keep them as nested dicts in JSON so a stretched
axis can carry `[start,end]` while a fixed axis carries a scalar:

```jsonc
"layout": {
  "h": "leftRight",
  "v": "center",
  "offset": { "left": 24, "right": 24, "v": 0 },   // h stretched -> left/right insets; v centered -> signed center offset
  "size":   { "h": 96 },                            // only the fixed (v? no—h is stretched) axis carries size; here height
  "sizing": { "w": "fill", "h": "fixed" }
}
```

> Design choice: `offset` keys are **named by constraint**, not by axis, so the JSON is
> self-documenting and the exporter can validate. Allowed keys: `left`,`right`,`top`,`bottom`
> (edge constraints), `h`,`v` (center constraints, signed), and for stretch the same edge keys act as
> insets. This is unambiguous because a given axis has exactly one constraint.

### A.2.2 The constraint registry (extensibility seam — NOT an enum)

Per CLAUDE.md, constraint kinds are a **registry**, not a sealed enum. New file
`Editor/Agent/LayoutConstraints.cs`:

```csharp
public interface ILayoutConstraint
{
    string Id { get; }                 // "left","right","leftRight","center","scale"
    bool Stretches { get; }            // true for leftRight/topBottom/scale
    // Apply to one axis of a RectTransform given the offset value(s) and the element's authored size.
    void Apply(RectTransform rect, Axis axis, LayoutOffsetValue offset, float? size);
    // Reverse: read the axis back into an offset value; return false if this constraint doesn't match.
    bool TryDetect(RectTransform rect, Axis axis, out LayoutOffsetValue offset, out float? size);
}

public static class LayoutConstraints
{
    public static void Register(ILayoutConstraint c);          // replace-by-Id, like NeoElementKinds.Register
    public static IReadOnlyList<ILayoutConstraint> All { get; }
    public static ILayoutConstraint Get(string id);            // null + Debug.LogWarning when missing
}
```

Built-ins (`left`,`right`,`leftRight`,`center`,`scale` ×2 axes) register themselves in a static
ctor, the same pattern as `AnchorPresets` but open. **`TryDetect` order matters for round-trip
determinism** — register in a fixed order and have the exporter try them in `All` order, first match
wins; the *generator* always picks the constraint the spec names, so the only determinism risk is the
exporter, addressed in A.3.

### A.2.3 Constraint → Unity mapping (authoritative table)

`Axis = X (horizontal)` shown; Y is symmetric (`top`↔`right`, `bottom`↔`left` inverted, etc.).
Let `P` = parent size on the axis (only needed for scale/center math at *author* time; at runtime
Unity does the work via anchors).

| `h` value | anchorMin.x | anchorMax.x | pivot.x | offset interpretation | RectTransform writes |
|-----------|-------------|-------------|---------|------------------------|----------------------|
| `left`    | 0 | 0 | 0 | `offset.left` = px from parent left to element left | `offsetMin.x = left`; `sizeDelta.x = size.w` (fixed axis) |
| `right`   | 1 | 1 | 1 | `offset.right` = px from parent right to element right | `offsetMax.x = -right`; `sizeDelta.x = size.w` |
| `center`  | 0.5 | 0.5 | 0.5 | `offset.h` = signed px of element center from parent center | `anchoredPosition.x = offset.h`; `sizeDelta.x = size.w` |
| `leftRight` | 0 | 1 | 0.5 | `offset.left`,`offset.right` = insets | `offsetMin.x = left`; `offsetMax.x = -right` (size IGNORED) |
| `scale`   | `s0` | `s1` | 0.5 | `offset` = `[s0,s1]` fractions of parent (0..1) | anchors = `[s0,s1]`; `offsetMin.x=offsetMax.x=0` (size IGNORED) |

This maps **exactly** onto how Unity already keeps stretch elements responsive (zero offsets on a
stretched axis) — the win is that `left`/`right`/`center` now also use `offsetMin/Max` (or
`anchoredPosition` only for the genuinely centered case) with a **declared intent**, so a
right-anchored element stays glued to the right edge at any aspect. That is the structural fix for
"disappears in landscape."

### A.2.4 Per-child sizing modes (Fixed / Hug / Fill)

Replaces the hardcoded `ConfigureStackSizing` (`UIWidgetFactory.cs:1344-1354`). Sizing is a
per-child property applied when the element is a child of a layout-group parent (vstack/hstack/grid).
Mapping per axis:

| `sizing.w/h` | LayoutElement | ContentSizeFitter | Parent layout-group flag |
|--------------|---------------|-------------------|--------------------------|
| `fixed` | `min = preferred = size`; `flexible = 0` | (none on this axis) | `childControl<Axis> = true`, no force-expand |
| `hug`   | clear min/preferred (-1) | `ContentSizeFitter.<axis>Fit = PreferredSize` | `childControl<Axis> = true` |
| `fill`  | `flexible = 1` | (none) | `childControl<Axis> = true`, `childForceExpand<Axis> = true` |

**Seam:** sizing modes are a registry too (`LayoutSizingModes.Register`, defaults `fixed`/`hug`/
`fill`) so a project can add e.g. `clamp` (min/max). Keep it small but open.

> Decision: the *parent* stack still needs `childControlWidth/Height = true` for per-child sizing to
> work at all. So `ConfigureStackSizing` becomes `ConfigureStack(layout, vertical)` that sets
> `childControlWidth = childControlHeight = true` and `childForceExpand = false` as the **base**, and
> the per-child pass turns on `childForceExpand<Axis>` only if ANY child requests `fill` on that axis
> (Unity's force-expand is a group-level flag, so we OR it across children — documented limitation;
> per-child fill within a non-expanding group still works via `flexibleWidth=1`).

---

## A.3 Round-trip & exporter

`UISpecExporter.ExportGeometry` (`UISpecExporter.cs:795-831`) currently emits `anchor`+`position`+
`size` (free) or `LayoutElement` size+flex (in-layout). New behavior:

- **When the prefab carries the new model** (detected by a marker — see below), emit `layout`
  instead of `anchor`/`position`/`size`/`flex`. Reverse-map:
  - For each axis, iterate `LayoutConstraints.All`, call `TryDetect`; first match → `h`/`v` + offset.
  - Read `LayoutElement`/`ContentSizeFitter` → `sizing`.
- **When the prefab carries a legacy preset** (no marker, anchors match a named preset), keep
  emitting `anchor`/`position`/`size` exactly as today → **un-migrated specs stay byte-identical.**

**Marker decision:** add a tiny serialized `NeoLayoutTag` MonoBehaviour (runtime,
`Runtime/Containers/NeoLayoutTag.cs`) that the generator attaches when it applies a `layout` (storing
the resolved `h`/`v` and offsets). This is the unambiguous round-trip signal and avoids
floating-point anchor reverse-lookup ambiguity (e.g. `center` and a custom 0.5 anchor look identical
in raw anchors). The exporter reads the tag; absence ⇒ legacy path. The tag is force-text and
trivial to diff. *(Alternative considered: pure anchor reverse-lookup with a tolerance — rejected
because `center` vs `leftRight`-with-equal-insets can alias and break byte-identity.)*

**Test:** new `Tests/EditMode/ConstraintLayoutRoundTripTests.cs` — for each constraint × axis,
generate from a `layout` spec, export, assert JSON equal; plus a legacy-spec test asserting the 16
presets still export as `anchor` strings. Add cases to `SpecLayoutAndWidgetTests` for the migration
equivalence (preset spec and its `layout` equivalent produce identical RectTransforms).

**Diff/merge:** no `SpecPath` change needed for `layout` (it's a nested body dict; `SpecDiff.DiffDict`
walks it, `SpecPath` addresses `…/layout/h` etc. via `Combine`). Verified against `SpecDiff.cs:83-97`
and `SpecPath.cs`. `OffSpecLint` is unaffected (layout lives in the spec, fully round-trippable).

---

## A.4 Workstreams (Wave 1 — SERIAL relay A1→A2→A3, then A4 parallel)

> **CRITICAL non-overlap note:** A1, A2, A3 all edit the core quartet. They MUST be a relay: each
> starts from the prior's merged commit. Do NOT run them in three simultaneous worktrees editing the
> same files — guaranteed conflicts. Either one worktree with three sequential commits, or three
> worktrees merged strictly in order with the next rebasing on the prior.

### Workstream A1 — Data model (`UISpec.cs` + new constraint/sizing registries)
- **Owns (edit):** `Editor/Agent/UISpec.cs` (add `LayoutSpec`, value holders, `ElementSpec.layout`
  field, Parse at ~`UISpec.cs:434`, ToJsonObject at ~`UISpec.cs:536` — insert `layout` in a fixed
  position in key order, e.g. right after `anchor`).
- **Owns (create):** `Editor/Agent/LayoutConstraints.cs`, `Editor/Agent/LayoutSizingModes.cs`,
  `Runtime/Containers/NeoLayoutTag.cs` (+ `.meta` via Unity later — agents create the `.cs` only).
- **Deliverables:** types compile; `LayoutSpec.Parse`/`ToJsonObject` deterministic; registries with
  built-in registrations; `IsEmpty` guards emission.
- **Dependencies:** none.
- **Acceptance:** `LayoutSpec` parses the §A.2.1 JSON and re-emits byte-identical; registries return
  built-ins; `ElementSpec.ToJsonObject` emits `layout` only when non-empty and legacy fields when
  `layout` is null. Unit test `LayoutSpecParseTests` (pure, no Unity scene).
- **Verify:** Roslyn relay compile of Runtime + Editor (see [08 §Verification](08-orchestration-and-testing.md)).
- **Seam introduced:** `LayoutConstraints.Register`, `LayoutSizingModes.Register`.

### Workstream A2 — Generator + factory mapping (`UISpecGenerator.cs` + `UIWidgetFactory.cs`)
- **Depends on:** A1 merged.
- **Owns (edit):** `Editor/Agent/UISpecGenerator.cs` — `ApplyCommonOverrides`
  (`UISpecGenerator.cs:891-958`): when `element.layout != null`, route to a new
  `ConstraintLayout.Apply(rect, layout, parentIsLayoutGroup)` and attach `NeoLayoutTag`; else keep
  legacy path. Per-child sizing applied for layout-group children.
  `Editor/Agent/UIWidgetFactory.cs` — refactor `ConfigureStackSizing` →
  `ConfigureStack`+per-child `ApplySizing`; add `ConstraintLayout` static (Apply/Detect using
  `LayoutConstraints`).
- **Non-overlap with A3:** A2 owns the **generator+factory write path**; A3 owns the **exporter read
  path**. They edit different files except both reference the shared `ConstraintLayout` static, which
  A2 creates and A3 only calls (read-only) — declare `ConstraintLayout` in `UIWidgetFactory.cs`
  (A2's file) so there is exactly one owner.
- **Acceptance:** generating a `layout` spec produces RectTransforms matching the §A.2.3 table at
  multiple parent sizes (test resizes the parent canvas and asserts the element stays glued per
  constraint — this is the "doesn't disappear in landscape" proof). Legacy specs unchanged.
- **Verify:** Roslyn compile; new `Tests/PlayMode/ConstraintResponsivenessTests.cs` (resize canvas,
  assert element rect) — note PlayMode full run is gated; EditMode equivalent using
  `LayoutRebuilder.ForceRebuildLayoutImmediate` is preferred and runs in the per-wave check.
- **Seam:** consumes A1's registries.

### Workstream A3 — Exporter reverse-map + round-trip (`UISpecExporter.cs`)
- **Depends on:** A2 merged.
- **Owns (edit):** `Editor/Agent/UISpecExporter.cs` — `ExportGeometry` (`UISpecExporter.cs:795-831`):
  read `NeoLayoutTag` → emit `layout`; absent → legacy emit unchanged.
- **Owns (create):** `Tests/EditMode/ConstraintLayoutRoundTripTests.cs`.
- **Acceptance:** export→generate→export byte-identical for layout specs AND legacy specs; the
  committed ColorACube demo (legacy) round-trips with zero diff (run `{"action":"diff"}` mentally /
  via test fixture).
- **Verify:** Roslyn compile of Editor + EditMode tests; targeted run of
  `ConstraintLayoutRoundTripTests` + `SpecLayoutAndWidgetTests` when editor is closed (gated).
- **Seam:** consumes `LayoutConstraints.All` ordering for deterministic detection.

### Workstream A4 — Legacy migration pass (parallel tail, new files only)
- **Depends on:** A3 merged.
- **Owns (create):** `Editor/Composer/SpecMigration.cs` (pure function
  `MigrateLegacyToLayout(UISpec) → UISpec`, converting each element's `anchor`+`position`+`size` to
  the equivalent `layout` per the preset→constraint map in §2.A) + a menu item `Tools → Neo UI →
  Migrate Spec To Layout Model` and an opt-in flag. **Never runs silently** (CLAUDE.md). Round-trips
  to the same RectTransforms (asserted).
- **Owns (create):** `Tests/EditMode/SpecMigrationTests.cs` — for each of the 16 presets, migrate and
  assert the generated RectTransform equals the legacy-generated one.
- **Acceptance:** migrating ColorACube yields a spec that generates pixel-identical prefabs; migration
  is idempotent.
- **Non-overlap:** new files only; no quartet edits → fully parallel with anything once A3 is in.

---

## A.5 Risks specific to A (see [08 risk register](08-orchestration-and-testing.md) for mitigations)
- Floating-point anchor aliasing in reverse-detect → mitigated by `NeoLayoutTag` marker.
- Force-expand being a group-level (not per-child) flag → documented OR-across-children behavior.
- Breaking the committed demo → A3/A4 tests assert byte-identity on the legacy path; migration is
  opt-in.

## A.6 Definition of done for Pillar A
- [ ] `layout` parses, emits, round-trips byte-identically (layout specs AND legacy specs).
- [ ] A `layout` element stays correctly positioned across canvas resizes (responsiveness test green).
- [ ] Per-child Fixed/Hug/Fill works in vstack/hstack/grid.
- [ ] 16 legacy presets re-expressible as constraints; migration pass proven pixel-identical.
- [ ] Constraint + sizing registries are the documented seams; no new sealed enum/switch.
- [ ] Branch compiles via the Roslyn relay; EditMode round-trip tests green.
