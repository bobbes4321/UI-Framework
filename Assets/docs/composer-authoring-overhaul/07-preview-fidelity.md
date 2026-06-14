# Pillar G — Preview Fidelity & Polish

[← Master plan](00-master-plan.md) · Prev: [06 — Inspector](06-inspector-overhaul.md) · Next: [08 — Orchestration](08-orchestration-and-testing.md)

> The final polish wave. Live theme recolor, live sample data in bound lists, optional live flow
> playback, general polish. **Composer/preview-only — NO core quartet.** Wave 5.

---

## G.1 What exists today (verified)

- `SpecPreviewPane.RenderRoot` (`SpecPreviewPane.cs:223-283`) builds the view in a preview scene and
  renders to a texture; rebuild is debounced 0.15s (`SpecPreviewPane.cs:70-85`).
- Bound lists bake the row template inactive (per CLAUDE.md); the preview shows the empty template,
  not sample rows.
- No live flow playback in the preview.

---

## G.2 Architecture

### G.2.1 Live theme-token recolor
- The preview already re-renders on edits. Ensure a theme **token** edit (in the spec's `theme`
  section, or via a future theme panel) triggers `RequestRebuild` so colors update live. Hook the
  theme edit path in `SpecDocument`/`SpecInspector` to call the pane's `RequestRebuild`.
- Optional: a theme/variant switcher in the preview toolbar (re-source from the spec's `theme.variants`)
  so the author can flip Dark/Light without leaving the Composer. Drives the same render with the
  selected variant's tokens (`ThemeColorTarget` resolution).

### G.2.2 Live sample data in bound lists
- For an element with `bind` + `item` (`UISpec.cs:404-405`), the preview should show N sample rows so
  the author sees a populated list, not an empty template.
- Decision: synthesize sample rows from the `item` template's `{key}` tokens (fill with placeholder
  values: "Item 1", "Item 2", …) and call `UIData.Set(category, name, rows)` against the **preview
  scene's** data context before the layout rebuild in `RenderRoot`. This is preview-only (does not
  touch the baked prefab; the template still bakes inactive). Configurable row count in the toolbar.
- **No silent failure:** if `bind` references an unknown source, the preview shows the empty template
  (current behavior) — no warning needed (it's a preview convenience), but log at verbose level.

### G.2.3 Optional live flow playback
- A "Play" affordance in the preview that, for a view referenced by a flow, lets the author click
  interactive elements and watch flow transitions IN the preview scene (signals dispatch
  synchronously — see CLAUDE.md `GeneratedFlowPlaythroughTests`). This reuses the in-memory flow
  instantiation pattern from `Tests/EditMode/GeneratedFlowPlaythroughTests.cs`.
- Gate behind a toggle; it's the most complex and least essential — ship it last, fall back to static
  preview if time-constrained.

### G.2.4 General polish
- Empty-state guidance ("drag a widget from the palette" when a view is empty).
- Selection outline polish, hover affordances, status-bar dimension/zoom readout consistency.
- Performance: ensure no per-OnGUI allocations crept in across the overhaul; verify debounce still
  coalesces drag storms.

---

## G.3 Workstream G (single workstream)

- **Owns (edit):** `Editor/Composer/SpecPreviewPane.cs` (recolor hook, sample data injection, optional
  playback, polish), `Editor/Composer/SpecDocument.cs` (additive: theme-edit → rebuild signal),
  `Editor/Composer/SpecInspector.cs` (additive: trigger rebuild on theme edits — coordinate if any
  Pillar F follow-up is still open).
- **Owns (create):** `Editor/Composer/PreviewSampleData.cs` (sample-row synthesis),
  `Tests/EditMode/PreviewSampleDataTests.cs`.
- **Dependencies:** Pillars A–C merged (renders the new model + viewport). Independent of D/E/F
  except shared Composer files — Wave 5 is single-workstream so no contention.
- **Acceptance:** theme token edit recolors live; bound lists show sample rows in preview; (optional)
  flow playback advances on click; no perf regressions.
- **Verify:** Roslyn Editor compile; `PreviewSampleDataTests`; manual smoke.

## G.4 Definition of done for Pillar G
- [ ] Live theme recolor + variant switch in preview.
- [ ] Bound lists show sample rows in preview (prefab still bakes empty template).
- [ ] (Optional) live flow playback.
- [ ] Polish + no per-OnGUI allocation regressions.
