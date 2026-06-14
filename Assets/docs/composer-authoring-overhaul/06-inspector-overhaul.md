# Pillar F — Inspector / Properties Panel Overhaul

[← Master plan](00-master-plan.md) · Prev: [05 — Palette](05-palette-and-templates.md) · Next: [07 — Fidelity](07-preview-fidelity.md)

> A Figma-like properties panel: constraint widget, auto-layout panel, per-child sizing dropdowns,
> breakpoint override editing — all through the EditorUI kit, cached per IMGUI rules. **Owns
> `SpecInspector.cs` + `SpecFieldCatalog.cs` + a new EditorUI control — NO core quartet.** Parallel
> with Pillar D in Wave 3. Depends on Pillar A (model) merged; reads Pillar B's edit-scope.

---

## F.1 What exists today (verified)

- `SpecInspector.DrawField` (`SpecInspector.cs:163-229`) renders each `SpecField` from
  `SpecFieldCatalog.For(kind)` (`SpecInspector.cs:122-128`).
- `SpecFieldCatalog` = built-in `All` table + `Registered` (seam `RegisterField`,
  `SpecFieldCatalog.cs:164-167`) + per-kind `INeoElementKind.Fields`, unioned in `For()`
  (`SpecFieldCatalog.cs:175-188`). `SpecField` = key/label/FieldKind/get/set/kinds
  (`SpecFieldCatalog.cs:35-56`).
- No constraint widget, no auto-layout panel, no sizing dropdowns, no breakpoint scoping.

---

## F.2 Architecture

### F.2.1 New EditorUI control: the constraint widget

New `Editor/EditorUI/NeoConstraintWidget.cs` (in the **standalone EditorUI kit** — must keep ZERO
references to `Neo.UI`, per CLAUDE.md; it operates on plain values + callbacks, not `ElementSpec`):
- Draws the Figma-style control: a 3×3 grid for the two axes with the "stretch" bars
  (Left/Right/Both/Center for H; Top/Bottom/Both/Center for V), plus a Scale toggle per axis.
- API: `Draw(Rect, ConstraintModel current, Action<ConstraintModel> onChange)` where
  `ConstraintModel` is a small POD (`h`,`v` strings + offsets) defined IN the kit (no Neo.UI types).
- Cached `GUIStyle`s and textures (built once, reused) — no per-OnGUI allocation.
- **Ownership note (Wave 3 non-overlap):** Pillar F **creates and owns** this control; Pillar D may
  *invoke* it from the canvas but must not edit it. Reciprocally, Pillar D owns
  `ConstraintWriteback.cs`; F consumes it read-only. → file-disjoint.

### F.2.2 New field kinds in `SpecFieldCatalog`

Extend `FieldKind` handling (the catalog is the seam; add via the registered/built-in tables, not a
sealed switch where avoidable). New logical fields surfaced for every element:
- **Constraint** (renders `NeoConstraintWidget`, reads/writes `element.layout.h/v/offset`).
- **Sizing W / Sizing H** dropdowns (Fixed/Hug/Fill from `LayoutSizingModes.All` — the seam, so a
  project's custom mode appears) — shown when the element's parent is a layout group.
- **Auto-layout panel** for vstack/hstack/grid: direction (vstack/hstack is the kind; for grid,
  columns), gap (`spacing`), per-side padding (expand `padding` to a `RectOffset`-style 4-field
  editor — **decision below**), child alignment (`align`).

> **Padding decision:** today `padding` is a single `float?` (`UISpec.cs:388`). Per-side padding
> needs a richer shape. Decision: keep `padding` (uniform) for backward compat AND add an optional
> `padding4: [l,t,r,b]` that, when present, wins. This is a **Pillar A-adjacent quartet change** —
> but A is already merged, and the field is small. To avoid re-opening the quartet in Wave 3, **fold
> `padding4` into Pillar A's A1 workstream** (add the field with `layout`), so F only *draws* it.
> If A has already merged before this is noticed, `padding4` becomes a tiny serialized addition that
> must go through a dedicated mini-quartet task gated before Wave 3 — flagged in the risk register.

### F.2.3 Breakpoint override editing (ties to Pillar B)

- The inspector reads the **active edit breakpoint** from `SpecDocument` (Pillar B's `BreakpointBar`
  sets it). When a non-base breakpoint is active:
  - Editable fields that participate in overrides (the `layout` fields) write into
    `element.overrides[breakpoint]` (the delta) instead of base, via `SpecDocument.ApplyEdit`.
  - Show an **override indicator** (a dot / "overridden" badge, Framer-style) on fields that differ
    from base in this breakpoint, with a "reset to base" affordance (removes the delta key).
- When base is active, edits write base as today.

### F.2.4 Polish

- Group fields into collapsible sections via `NeoGUI.BeginFoldoutSection` (consistent with the
  inspector conventions in CLAUDE.md): Layout (constraint + sizing + auto-layout), Appearance,
  Behavior, Data. Section accents from `NeoColors`.
- All styles/ReorderableLists cached; dropdown options fetched on open.

---

## F.3 Workstream F (single workstream)

- **Owns (create):** `Editor/EditorUI/NeoConstraintWidget.cs` (kit, Neo.UI-free),
  `Tests/EditMode/InspectorFieldTests.cs` (catalog returns the new fields per kind; constraint
  read/write round-trips through the catalog get/set).
- **Owns (edit):** `Editor/Composer/SpecInspector.cs` (draw constraint widget, sizing dropdowns,
  auto-layout panel, breakpoint scoping + override indicators, foldout sections),
  `Editor/Composer/SpecFieldCatalog.cs` (register the new fields; consume `LayoutSizingModes.All`).
- **Dependencies:** Pillar A merged (`layout`/sizing model + `padding4` if folded in A1). Pillar B
  merged (active edit breakpoint + `overrides`). If B not yet merged, build the panel against base
  only behind a feature flag and wire breakpoint scoping after B merges (document the flag).
- **Non-overlap with Pillar D (Wave 3):** F owns inspector + catalog + the EditorUI widget; D owns
  canvas + writeback math. Shared types: `ConstraintWriteback` (D owns, F reads),
  `NeoConstraintWidget` (F owns, D reads). No file is edited by both. → true parallel.
- **EditorUI dependency rule:** `NeoConstraintWidget` must compile in the standalone
  `Editor/EditorUI` compile pass (engine refs only, no Neo.UI) — verify with the EditorUI-alone
  Roslyn compile (`neo-compilecheck.ps1` "EditorUI kit" step).
- **Acceptance:**
  - The constraint widget edits `h`/`v` and the offsets; switching a constraint preserves the
    on-screen rect (uses `ConstraintWriteback`).
  - Sizing dropdowns expose Fixed/Hug/Fill (+ any registered mode) and apply correctly.
  - Auto-layout panel edits direction/gap/per-side padding/align.
  - With a non-base breakpoint active, layout edits write the override delta; override badges +
    reset-to-base work.
  - Catalog stays extensible (`RegisterField` still works; project `INeoElementKind.Fields` still
    appear).
- **Verify:** Roslyn EditorUI-alone compile (for the widget), Editor compile, `InspectorFieldTests`
  in per-wave check; manual smoke in the open editor.
- **Seam introduced/consumed:** consumes `LayoutSizingModes.Register` (A) for the sizing dropdown;
  extends `SpecFieldCatalog` via the existing `RegisterField` seam.

## F.4 Definition of done for Pillar F
- [ ] Figma-style constraint widget in the EditorUI kit (Neo.UI-free, cached styles).
- [ ] Per-child Fixed/Hug/Fill dropdowns sourced from the sizing-mode registry.
- [ ] Auto-layout panel (direction/gap/per-side padding/align).
- [ ] Breakpoint override editing with override badges + reset-to-base.
- [ ] Foldout-sectioned, cached, no per-OnGUI allocation; existing field seams intact.
