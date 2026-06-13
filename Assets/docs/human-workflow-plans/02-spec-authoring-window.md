# Plan 2 — The Neo UI Composer: A Spec-Authoring Editor Window

## The core idea

Today humans edit the **prefab** (the disposable output) and we reverse-engineer it back to spec,
which is lossy. The Composer flips this: **the window's live document is a `UISpec` in memory.**
Every GUI edit mutates that spec; the prefab is regenerated as a *preview*, never the source of
truth. Because we never reverse-engineer the prefab, **everything done in the window round-trips
losslessly by construction.** This is the structural-editing path that returns the workflow to
artists and designers without the round-trip risk.

`UISpec` is already a clean, in-memory, deterministically-serializable model (`UISpec.cs:32`,
`FromJson` `:44`, `ToJson` `:83`) and the generator/exporter/preview all already operate on it — so
this window reuses the entire pipeline; it adds an authoring surface, not new materialization logic.

## Ambition ladder (build in this order; ship value at each rung)

- **Tier 2 (MVP, this plan's Phase 1–3): the Composer window.** Tree + live preview + property
  inspector. Edits mutate the in-memory spec. Save = write spec JSON + regenerate committed assets.
  Delivers: artists change spacing/colors, designers add tabs/menus/settings/cheats — all via GUI,
  no JSON, no lossy prefab edit.
- **Tier 3 (Phase 4): WYSIWYG canvas.** Make the preview interactive — click-select, drag-move,
  drag-resize, marquee, snapping, drag-to-reparent. Purely additive: same spec mutations, driven by
  direct manipulation. This is the "Figma-lite that emits spec JSON" headline.
- **Tier 4 (future, out of scope here): live flow playback inside the window, live theme-palette
  recolor, side-by-side responsive resolutions.**

## Current-state references

- Document model: `UISpec.cs` (whole file). Element vocabulary + every field: `ElementSpec`
  (`UISpec.cs:333-587`), `Kinds` list `:335-340`.
- Generator: `UISpecGenerator.cs` — `GeneratedRoot`, `GeneratedMarker` (`:281-285`), per-view build.
- In-memory preview (render a spec without committing assets): the `preview` action path
  (`UISpecPreview` / `UIScreenshotter.CaptureLive`), invoked from `AgentBridge` `HandlePreview`.
- Single source of widget structure: `UIWidgetFactory` (Editor/Agent) — child names are the contract.
- Editor kit to reuse for ALL chrome (per `CLAUDE.md` conventions): `Editor/EditorUI/` — `NeoGUI`,
  `NeoColors`, `NeoStyles`, `NeoDropdown`, `NeoSearchablePopup`, `NeoListView`.
- ID/database dropdowns: `IdDatabaseOptions.DrawCategoryNamePair` (Editor/Drawers).
- Existing UI-Toolkit editor-window precedent: the flow graph window (`Editor/Flow/FlowGraphWindow.cs`).

## Architecture

### Document model

The window owns one `UISpec` instance (`_doc`) plus an undo stack of serialized snapshots
(`UISpec.ToJson` → string → push; cheap and deterministic). Mutations go through a single
`ApplyEdit(Action mutate, string label)` choke point that: snapshots for undo, runs `mutate`, marks
dirty, and schedules a debounced preview rebuild. Never mutate `_doc` outside `ApplyEdit`.

**Selection** is a `SpecPath` string (reuse the addressing from Plan 1 `SpecPath.cs` if built; if
Plan 2 lands first, define `SpecPath` here and Plan 1 reuses it). The selected path drives both the
property inspector (right pane) and the highlight in the preview (center).

### Three panes (UI Toolkit `EditorWindow`, like the flow window)

```
┌──────────────┬───────────────────────────┬──────────────────┐
│  TREE         │  LIVE PREVIEW              │  INSPECTOR        │
│ (left)        │  (center)                 │  (right)          │
│               │                           │                   │
│ Theme         │  [rendered current view]  │  Properties of    │
│ Views         │   ← regenerated on edit   │  selected node    │
│  Menu/Main    │   (debounced 150ms)       │  (NeoDropdown +   │
│   ├ vstack    │                           │   NeoGUI fields)  │
│   │  ├ button │  resolution selector      │                   │
│   │  └ text   │  [1080×1920 ▾]            │  + Add child ▾    │
│ Popups        │  variant/theme selector   │  Delete / Move    │
│ Menus         │                           │                   │
│ Flow → opens FlowGraphWindow on the same spec               │
└──────────────┴───────────────────────────┴──────────────────┘
```

**Left — Spec tree.** A `TreeView` over `_doc`: Theme tokens, Views (each expands to its
`ElementSpec` tree via `children`), Popups, Settings/Cheats catalogs, Flow (a leaf that opens the
existing flow window bound to the same spec). Context menu: Add sibling, Add child (kind picker from
`ElementSpec.Kinds`), Duplicate, Delete, Move up/down, Reparent (drag). All mutate `_doc.elements`/
`children` lists through `ApplyEdit`.

**Center — Live preview.** Reuse the in-memory render pipeline (`UISpecPreview`/`CaptureLive`) the
`preview` action already uses: build the selected view's prefab subtree in memory, render to a
texture at the chosen resolution, draw it in the pane. Debounce rebuilds (150 ms) so dragging a
slider doesn't thrash. Show the selected node's bounds as an overlay rect (compute from the in-memory
RectTransform before discarding the temp object). Resolution + theme-variant selectors mirror the
`preview` action's resolution matrix.

**Right — Property inspector.** For the selected `ElementSpec`/`ViewSpec`/`MenuItemSpec`/token, draw
its spec fields with kind-appropriate controls, reusing the kit:
- Theme tokens / colors → `ThemeColorRef`-style swatch + token dropdown.
- Category/Name ids → `IdDatabaseOptions.DrawCategoryNamePair` (writes new ids into the database too).
- `variant`, `sizeVariant`, `shape`, `align`, `anchor` → `NeoDropdown.StringPopup` with the valid
  enums.
- `textStyle`/`style` → theme style dropdowns with "+Add".
- `icon` → Lucide name searchable popup (reuse `IconMap`).
- numeric fields (`padding`, `spacing`, `radius`, `min/max/value/step`, `size`, `position`) → number
  fields; spacing/padding offer the on-scale snap values (4/8/12/16/24/32/48/64) the design lint uses.
- `onClick*`, `controls`, `bind`, `catalog`, `group` → the relevant id/view/popup pickers.
- Conditional display: only draw fields valid for the node's `kind` (mirror what `ElementSpec.Parse`
  reads per kind, `UISpec.cs:398-491`).

This pane is essentially a data-driven re-projection of `ElementSpec`'s fields. Build a
`SpecFieldCatalog` mapping `kind → field descriptors` so the inspector and the "+Add" kind picker
stay in sync with `ElementSpec.Kinds` (one place to extend when a new field is added).

### Dedicated panels for the JSON-only islands

These get first-class authoring UI in the tree/inspector (today they are JSON-only):
- **Tabs + panels:** "Add tab" offers to also create the sibling `panel` it `controls` and wires the
  `controls` id automatically (mirrors the generator's deferred per-view wiring described in
  `CLAUDE.md`). Bakes selected-tab visibility into the preview.
- **Settings / cheats catalogs:** a `MenuCatalogSpec` editor — add/remove `MenuItemSpec` rows
  (`UISpec.cs:923-1050`), pick kind (toggle/switch/slider/stepper/dropdown/button/label/rebind),
  group, default value, range. This removes the biggest designer JSON island.
- **Theme/palette:** a token grid editor — add/rename tokens, edit per-variant colors with live
  preview recolor, optionally start from a bundle (expanded to tokens on apply).

### Save / load / sync

- **Open:** load a spec JSON file, OR "Open Current Project" = `UISpecExporter.ExportProject()` into
  `_doc` (so a human can start from whatever exists). If Plan 1 exists, offer to load the committed
  `.neo-baseline.json`.
- **Save:** write `_doc.ToJson()` to the spec file, then run the generator on it (regenerate the
  committed assets). This is the only moment assets are written. Update the baseline (Plan 4).
- **Live preview ≠ committed assets:** the center pane uses in-memory render; nothing touches
  `GeneratedRoot` until Save. So an unsaved session can't corrupt committed prefabs.
- **Conflict guard:** on Save, if Plan 1 is present, run drift detection first — if the on-disk
  prefabs were edited outside the window since load, warn and offer merge before overwriting.

## Phases

1. **Document + tree + inspector (read/edit existing spec, no preview).** Load spec JSON, render the
   tree, edit fields through `ApplyEdit`, Save back to JSON + regenerate. Undo/redo. This alone lets
   designers edit structure without touching JSON.
2. **Live preview pane.** Wire the in-memory render + selection highlight + resolution/variant
   selectors.
3. **Dedicated panels:** tabs+panels helper, menu-catalog editor, theme/palette editor. (Closes the
   JSON-only islands — this is the payoff for designers/artists.)
4. **WYSIWYG canvas (Tier 3):** interactive select/move/resize/reparent on the preview. Map pointer
   gestures to `ElementSpec.position`/`size`/parent-list mutations through `ApplyEdit`. Add snapping
   to the spacing scale and sibling edges. Marquee multi-select. This is independently shippable.

## New & modified files

| File | Action |
|------|--------|
| `Editor/Composer/NeoComposerWindow.cs` | NEW — `EditorWindow`, three-pane layout, `Tools → Neo UI → Composer` |
| `Editor/Composer/SpecDocument.cs` | NEW — `_doc` ownership, `ApplyEdit`, undo stack, dirty/save |
| `Editor/Composer/SpecTreeView.cs` | NEW — left pane tree + context menu mutations |
| `Editor/Composer/SpecInspector.cs` | NEW — right pane, data-driven field drawing |
| `Editor/Composer/SpecFieldCatalog.cs` | NEW — `kind → field descriptors` (single source for inspector + add-kind picker) |
| `Editor/Composer/SpecPreviewPane.cs` | NEW — center pane, in-memory render + highlight (Phase 2) |
| `Editor/Composer/MenuCatalogEditor.cs` | NEW — settings/cheats authoring (Phase 3) |
| `Editor/Composer/ThemePaletteEditor.cs` | NEW — token grid (Phase 3) |
| `Editor/Composer/ComposerCanvas.cs` | NEW — WYSIWYG manipulation (Phase 4) |
| `Editor/Agent/SpecPath.cs` | NEW or SHARED with Plan 1 — node addressing |
| `Editor/Flow/FlowGraphWindow.cs` | EDIT — accept an externally-supplied spec/flow so "Flow" tree leaf opens it on `_doc` |

All chrome MUST go through `Editor/EditorUI/` (NeoGUI/NeoColors/NeoStyles/NeoDropdown/NeoListView) per
`CLAUDE.md`. Header accents follow the family colors (Containers=cyan, Interactive=blue, etc.).

## IMGUI/UI-Toolkit performance rules (from `CLAUDE.md`)

- Never build GUIStyles/lists/SerializedObjects per frame — cache (see `NeoListView`/`NeoStyles`).
- Fetch dropdown options only when the dropdown opens.
- Debounce preview regeneration; never regenerate on every keystroke.
- The preview's temp GameObjects must be `DestroyImmediate`d every rebuild (no leaks); mirror the
  `preview` action's in-memory discipline (commits no prefabs/assets).

## Edge cases

- **Empty spec / new document:** start with one empty view; tree shows it; preview shows blank canvas.
- **Polymorphic `size`** (`UISpec.cs:446-448`): inspector must offer either a size-variant dropdown
  (buttons) or a `[w,h]` field, never both writing the same key.
- **id-less elements:** selection by structural path; renaming/reordering updates the path.
- **Flow lives in its own window:** don't duplicate flow editing here; bind the existing window to
  `_doc`'s `FlowSpec` and keep renames propagating (the flow window already handles
  `FlowEdge.toNode`/`FlowGraph.startNode` rename propagation per `CLAUDE.md`).
- **Bound lists (`bind`/`item`)** can't show live data in preview — render the template once with
  placeholder `{key}` tokens and label it "(template — populated at runtime)".

## Testing

- `SpecDocumentTests.cs` (EditMode) — `ApplyEdit` snapshots/undo/redo; mutating then `ToJson` equals
  hand-authored JSON for representative edits (add tab, add menu item, change spacing).
- `SpecFieldCatalogTests.cs` — every `ElementSpec.Kinds` entry has a field descriptor set; catalog
  covers every serialized field the inspector should expose (guards drift when `ElementSpec` grows).
- Round-trip: `Open Current Project` → no edits → Save → generated assets byte-identical to before
  (reuses the existing export/generate idempotency tests).
- Manual QA script in the plan's acceptance section (window can't be fully unit-tested for UX).

## Acceptance criteria

1. A designer can add a tab + its panel, a settings row, and a cheat entirely through the window —
   no JSON editing — and Save produces working generated assets.
2. An artist can change a view's spacing and a theme token through the window with live preview.
3. Nothing under `GeneratedRoot` changes until Save; an abandoned session leaves committed assets
   untouched.
4. `Open Current Project` → Save with no edits is a no-op at the asset level (byte-identical).
5. (Phase 4) Dragging an element in the preview updates `ElementSpec.position`/`size` and round-trips.
