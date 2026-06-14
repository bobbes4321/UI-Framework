# Pillar E — Widget Palette + Drag-to-Create + Templates

[← Master plan](00-master-plan.md) · Prev: [04 — Canvas](04-direct-manipulation-canvas.md) · Next: [06 — Inspector](06-inspector-overhaul.md)

> Kills the blank page: a categorized searchable palette, drag-to-create onto canvas/tree, and curated
> templates. **Mostly new files + additive edits — NO core quartet.** Wave 4. Depends on Pillar A
> (drop computes a `layout`) and benefits from Pillar D (drop targeting) but conflicts with neither.

---

## E.1 What exists today (verified)

- Creation = right-click "Add Child" in `SpecTreeView` over a flat `ElementSpec.KnownKinds` list
  (`SpecTreeView.cs:410-416`, `AddElementTo` `:541-544`).
- Defaults per kind from `ComposerFactory.NewElement` (`ComposerFactory.cs:12-63`).
- No palette, no drag-to-create, no templates.
- `KnownKinds` already unions built-ins + `NeoElementKinds.All` (`UISpec.cs:345-355`) — so a
  registry-driven palette picks up project kinds for free.

---

## E.2 Architecture

### E.2.1 Palette registry (seam)

New `Editor/Composer/ComposerPalette.cs`:
```csharp
public readonly struct PaletteEntry {
    public readonly string kind;        // ElementSpec kind id
    public readonly string category;    // "Layout" | "Input" | "Display" | "Data" | "Custom" …
    public readonly string label, icon; // Lucide icon name for the palette tile
    public readonly int order;
}
public static class ComposerPalette {
    public static void Register(PaletteEntry e);                 // replace-by-kind
    public static IReadOnlyList<PaletteEntry> All { get; }       // built-ins + auto-entries for NeoElementKinds.All
    public static IEnumerable<string> Categories { get; }
}
```
- Built-ins register every `ElementSpec.Kinds` entry into a sensible category (Layout: vstack/hstack/
  grid/scroll/panel/overlay/safearea/spacer; Input: button/toggle/switch/slider/stepper/input/
  dropdown/tab/tabbar; Display: text/image/icon/shape/progress/counter; Data: list + bound; Menus:
  settings/cheats).
- **Auto-include project kinds:** for every `NeoElementKinds.All` not already registered, synthesize a
  `PaletteEntry` in category "Custom" using its `Accent` (`INeoElementKind.Accent`) — so a project's
  custom kind appears in the palette with zero extra work. This is the key extensibility win.

### E.2.2 Palette pane in `NeoComposerWindow`

- Add a collapsible **left-edge palette strip** (or a tab on the tree pane) built with the EditorUI
  kit: a search field (`NeoSearchablePopup`-style filtering) + category sections + tiles (icon +
  label). Cache styles; fetch entries once on open, not per OnGUI (CLAUDE.md).
- Layout: extend `NeoComposerWindow.BuildUI` (`NeoComposerWindow.cs:64-96`) — add the palette as a
  third split or a toggleable overlay so the existing tree/preview/inspector panes are untouched in
  spirit (additive).

### E.2.3 Drag-to-create

- **Onto the canvas:** begin a drag from a palette tile (`DragAndDrop` with a custom payload
  `kind`). `ComposerCanvas` (Pillar D) gains a drop handler: on drop, find the hovered container via
  the existing `FindDropTarget` (`ComposerCanvas.cs:326-339`), create `ComposerFactory.NewElement
  (kind)`, compute its **`layout`** so it lands at the cursor (free parent → center/left constraint
  with offset at cursor; layout-group parent → appended/inserted at the insertion index from Pillar
  D's reorder math), insert via `SpecDocument.ApplyEdit`. **Coordination:** the canvas drop handler is
  an additive method in `ComposerCanvas.cs` — since Pillar D merges in Wave 3 (before E in Wave 4),
  E edits the *already-merged* `ComposerCanvas.cs` to add `HandlePaletteDrop`. Declare this as the
  ONLY E edit to `ComposerCanvas.cs` (a new method, no conflict with D's regions).
- **Onto the tree:** drop a palette tile onto a tree node → insert as child of that node (reuse
  `SpecTreeView.AddElementTo`, `SpecTreeView.cs:541`). Additive method in `SpecTreeView.cs`.

### E.2.4 Templates / scaffolds

- Curated specs shipped under `Editor/Composer/Templates~/` (tilde = not imported as assets; loaded
  as `TextAsset`-free raw JSON via `File.ReadAllText` from the package path, like Doozy reference).
  Templates: `main-menu.json`, `settings-screen.json`, `hud.json`, `pause-menu.json`,
  `popup.json` — each a small valid `UISpec` fragment (one view or popup) authored with the new
  `layout` model + a sensible breakpoint or two (showcases Pillars A/B).
- New `Editor/Composer/ComposerTemplates.cs`: a registry (`Register(TemplateEntry)` seam) that lists
  templates (built-ins loaded from `Templates~`, projects add their own). A "**New from template**"
  picker (extend the tree toolbar's `+ View`/`+ Popup`, `NeoComposerWindow.cs:115-133`) inserts the
  template's views/popups/breakpoints into the current `SpecDocument` via `ApplyEdit`, with name
  collision handling (warn + suffix, no silent overwrite).
- Templates are **specs**, so they round-trip and merge like anything else.

---

## E.3 Workstream E (single workstream)

- **Owns (create):** `Editor/Composer/ComposerPalette.cs`, `Editor/Composer/ComposerTemplates.cs`,
  `Editor/Composer/PalettePane.cs`, `Editor/Composer/Templates~/*.json`,
  `Tests/EditMode/PaletteRegistryTests.cs`, `Tests/EditMode/TemplateInsertTests.cs` (insert a
  template into an empty spec, assert it generates + round-trips).
- **Owns (edit, additive only):** `Editor/Composer/NeoComposerWindow.cs` (palette pane + "New from
  template" picker), `Editor/Composer/ComposerCanvas.cs` (one new `HandlePaletteDrop` method),
  `Editor/Composer/SpecTreeView.cs` (one new tree-drop method).
- **Dependencies:** Pillar A merged (templates use `layout`; drop computes `layout`). Pillar D merged
  (drop targeting / insertion index reuse) — E is Wave 4, after D's Wave 3 merge.
- **Non-overlap:** E's edits to `ComposerCanvas.cs`/`SpecTreeView.cs` are single new methods appended
  after Pillar D/existing code merged. No other Wave-4 workstream touches Composer → no contention.
- **Acceptance:**
  - Palette lists all built-in kinds by category + any `NeoElementKinds`-registered project kind.
  - Drag a tile onto the canvas: element is created in the hovered container at the cursor with a
    correct `layout`; onto the tree: inserted as child.
  - "New from template" inserts a scaffold that generates a valid, responsive view and round-trips.
- **Verify:** Roslyn Editor compile; `PaletteRegistryTests` + `TemplateInsertTests` (run in per-wave
  check); manual smoke for drag feel.
- **Seam introduced:** `ComposerPalette.Register`, `ComposerTemplates.Register`.

## E.4 Definition of done for Pillar E
- [ ] Searchable categorized palette including project-registered kinds.
- [ ] Drag-to-create onto canvas (cursor/container-aware `layout`) and onto tree.
- [ ] Curated templates that generate + round-trip; "New from template" picker with collision warns.
- [ ] Palette + templates are registry seams.
