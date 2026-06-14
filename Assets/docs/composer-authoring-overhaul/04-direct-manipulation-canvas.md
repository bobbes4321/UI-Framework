# Pillar D — Direct-Manipulation Canvas Upgrades (Figma-grade)

[← Master plan](00-master-plan.md) · Prev: [03 — Viewport](03-free-viewport.md) · Next: [05 — Palette](05-palette-and-templates.md)

> Makes the canvas feel like Figma: constraint-aware writeback, drag-to-reorder in layout groups,
> smart guides + equal-spacing, multi-select align/distribute, keyboard nudge/duplicate/delete.
> **Owns `ComposerCanvas.cs` exclusively + new helper files — NO core quartet.** Parallel with
> Pillar F in Wave 3. Depends on Pillar A (constraint model) merged.

---

## D.1 What exists today (verified, `ComposerCanvas.cs`)

- Click-select / shift-additive / marquee (`:191-253`, `:225-266`).
- Drag-move with ghost outline; `CommitMove` (`:383-413`) writes **absolute** `el.position = [x,y]`.
- Resize with 8 handles (`HandleSize=8`, `:41`); `CommitResize` (`:445-470`) computes a pivot-aware
  result and writes `el.size` + `el.position` (absolute).
- Snapping: 8px grid (`GridCanvas=8`, `:43`) + sibling-edge (6px threshold, `:42`), guides drawn
  (`:572-575`). `SnapMove`/`SnapAxis`/`SnapGrid` (`:474-535`).
- Reparent via drop (`FindDropTarget`/`ReparentSelection`, `:326-339`,`:415-441`); layout-owned
  children **lose position** on reparent (`:433`). `FreeParents={overlay,safearea}` (`:87`),
  `LayoutKinds={vstack,hstack,grid,scroll,list,panel}` (`:90-91`).
- **Absent:** constraint-aware writeback, live viewport-resize response, drag-to-reorder within a
  layout group (only reparent), smart alignment guides beyond snapping, multi-select align/distribute.

---

## D.2 Architecture

### D.2.1 Constraint-aware writeback (the correctness upgrade)

`CommitMove`/`CommitResize` must write into the element's `layout` (Pillar A), honoring its current
constraint, instead of absolute `position`/`size`:
- Compute the element's new rect in **device space** (as today, via `_scale`/`_boxes`).
- Convert to the element's **constraint offsets** using the inverse of the §A.2.3 table and the
  *current parent rect*: e.g. for `h:"right"`, `offset.right = parentRight - elementRight`; for
  `h:"leftRight"`, recompute `left`/`right` insets; for `h:"center"`, signed center delta; for
  `scale`, recompute fractions. Write via `SpecDocument.ApplyEdit`.
- This is what makes a dragged element stay glued when the viewport changes aspect — the offset is
  stored against the constraint, not as an absolute pixel.
- **Changing the constraint** (Pillar F's constraint widget, but also a canvas affordance): when the
  user drags a corner handle while holding a modifier, or via the inspector, switching `h`/`v`
  recomputes offsets so the on-screen rect is preserved at the moment of switch (Figma behavior).

New helper `Editor/Composer/ConstraintWriteback.cs` (pure math: device-rect + parent-rect +
constraint → `LayoutSpec` offsets, and back). Shared with Pillar F's widget. **Owner: Pillar D
creates it; Pillar F consumes read-only** (declare ownership to avoid a merge collision — see Wave 3
non-overlap note).

### D.2.2 Drag-to-reorder within layout groups (insertion indicator)

For a child of a `LayoutKinds` parent, dragging now supports **reordering among siblings** (not just
reparenting):
- While dragging over the parent, compute the insertion index from the cursor against sibling box
  midpoints (vertical for vstack, horizontal for hstack, row-major for grid).
- Draw a 2px **insertion line** (blue, EditorUI accent) between the two siblings.
- On drop, reorder `children` list via `SpecDocument.ApplyEdit` (a list move = remove + insert at
  index). Since `SpecPath` keys id-less elements by position (`SpecPath.cs:99-101`,`:142-150`), a
  reorder reads as modify-pairs in diff — documented v1 limitation already noted in `SpecPath`.
- Reparent (cross-container) still works; reorder is the in-container case.

### D.2.3 Smart alignment guides + equal-spacing (Figma/Sketch)

Extend the existing snap system (`SnapMove`/guides) into a richer guide layer
`Editor/Composer/AlignmentGuides.cs`:
- **Edge/center guides:** while dragging, compare the moving rect's left/right/top/bottom/centerX/
  centerY against every sibling's same anchors; within threshold, draw a red guide spanning both and
  snap.
- **Equal-spacing pips:** detect equal gaps among three+ siblings and draw the distribution pips
  (the "║ ║" marks), snapping to maintain equal spacing.
- Keep the 8px grid as a fallback snap; guides take priority. All guide drawing is in `OnGUI` (no
  editor-tick animation, per CLAUDE.md).

### D.2.4 Multi-select align/distribute toolbar

A small overlay toolbar (top-left of the canvas) active when 2+ elements selected:
- Align left/centerX/right/top/centerY/bottom; distribute horizontal/vertical (equal gaps).
- Operates on the selection's `layout` offsets (constraint-aware via `ConstraintWriteback`), batched
  in ONE `SpecDocument.ApplyEdit` (single undo).
- Drawn via EditorUI kit (`NeoGUI`/`NeoStyles`), styles cached.

### D.2.5 Keyboard affordances

In `OnGUI` key handling: arrow = nudge 1px (shift = 10px), updating `layout` offsets; `Ctrl/Cmd+D` =
duplicate (deep-clone the `ElementSpec`, offset slightly, insert as sibling); `Delete` = remove;
`Ctrl/Cmd+G`? (group into a vstack) — optional stretch goal, gate it. All via `SpecDocument.ApplyEdit`.

### D.2.6 Live viewport-resize response

When Pillar C resizes the viewport, `ComposerCanvas` already re-reads `_boxes` after the rebuild
(`CaptureBoxes`, `SpecPreviewPane.cs:319-339`). Ensure handles/guides recompute from the fresh boxes
(no stale cached device-px). This is mostly a "don't cache across rebuild" verification, plus making
the selection survive a viewport resize (keep selection by `ElementSpec` reference, re-resolve boxes).

---

## D.3 Workstream D (single workstream, canvas-only)

- **Owns (edit):** `Editor/Composer/ComposerCanvas.cs` (writeback, reorder, guides hook, align
  toolbar, keyboard, resize response).
- **Owns (create):** `Editor/Composer/ConstraintWriteback.cs`, `Editor/Composer/AlignmentGuides.cs`,
  `Tests/EditMode/ConstraintWritebackTests.cs` (pure math: rect+parent+constraint ⇄ offsets),
  `Tests/EditMode/AlignDistributeTests.cs` (operate on specs, assert resulting `layout`).
- **Dependencies:** Pillar A merged (writes `layout`). Reads Pillar C's `_scale`/`_boxes` (already in
  `SpecPreviewPane`/`ComposerCanvas`); if C not merged, the existing fixed-resolution path still
  feeds boxes, so D can develop against current preview and gain live-resize once C merges.
- **Non-overlap with Pillar F (Wave 3):** D owns `ComposerCanvas.cs`; F owns `SpecInspector.cs` +
  `SpecFieldCatalog.cs` + the EditorUI constraint *control*. The shared `ConstraintWriteback.cs` is
  **created and owned by D**; F consumes it read-only. The EditorUI **constraint-widget control** is
  **created and owned by F**; D may invoke it but does not edit it. Declare both ownerships here and
  in `06-…` so neither agent edits the other's file. → file-disjoint, true parallel.
- **Acceptance:**
  - Dragging a `right`-constrained element keeps it glued to the right edge across a viewport resize
    (the regression that motivated the overhaul).
  - Drag-to-reorder shows an insertion line and reorders `children`.
  - Edge/center guides + equal-spacing pips appear and snap.
  - Align/distribute on a multi-select produces correct, single-undo `layout` edits.
  - Arrow-nudge / duplicate / delete work and are undoable.
- **Verify:** Roslyn Editor compile; `ConstraintWritebackTests` + `AlignDistributeTests` (pure, run in
  per-wave check); manual smoke in the open editor for drag feel.
- **Seam introduced:** none required (these are editor interactions); align/distribute ops could be a
  small registry if a project wants custom ops — optional, document as a future seam.

## D.4 Definition of done for Pillar D
- [ ] All move/resize writeback is constraint-aware (writes `layout` offsets, survives viewport
      resize).
- [ ] Drag-to-reorder within layout groups with insertion indicator.
- [ ] Smart edge/center guides + equal-spacing snapping.
- [ ] Multi-select align/distribute toolbar (single-undo).
- [ ] Keyboard nudge/duplicate/delete.
- [ ] Selection + handles survive a live viewport resize.
