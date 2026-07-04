# Composer Authoring Overhaul — Master Plan

> Status: retired 2026-07 — the Composer was removed; native authoring supersedes it; see CLAUDE.md.

> Status: planning. Author: lead architect. Target branch: `composer-authoring-overhaul`.
> All file paths are relative to `Assets/Neo UI Framework/` unless absolute.
> Verified against the live codebase (file:line references throughout) — agents must re-verify before editing.

## 0. How to read this document set

This is the master plan. Each pillar has a detailed sub-plan; read the master for sequencing and
contracts, then the sub-plan for the workstream you own.

- [01 — Responsive Layout Foundation (Pillar A, the keystone)](01-responsive-layout-foundation.md)
- [02 — Breakpoint / Orientation Override System (Pillar B)](02-breakpoint-override-system.md)
- [03 — Free Draggable Resizable Viewport (Pillar C)](03-free-viewport.md)
- [04 — Direct-Manipulation Canvas Upgrades (Pillar D)](04-direct-manipulation-canvas.md)
- [05 — Widget Palette + Drag-to-Create + Templates (Pillar E)](05-palette-and-templates.md)
- [06 — Inspector / Properties Panel Overhaul (Pillar F)](06-inspector-overhaul.md)
- [07 — Preview Fidelity & Polish (Pillar G)](07-preview-fidelity.md)
- [08 — Orchestration: Waves, Dependencies, Verification, Migration, DoD](08-orchestration-and-testing.md)

---

## 1. Executive summary & design philosophy

### 1.1 The vision
The Composer (`NeoComposerWindow`) is, and remains, the from-scratch, no-agent authoring surface
for Neo UI. It edits a **spec in memory** and **regenerates a prefab as a live preview**. We are
**not** building a parallel scene-first authoring system: a scene-first editor would write directly
to RectTransforms and thereby break the round-trip / diff / merge / baseline / binding guarantees
that make the package agent-safe. The spec stays the single source of truth.

We are turning the Composer into a **Figma/Webflow-grade UI building tool** while keeping every
existing invariant: spec is canonical; export→generate→export is byte-identical; new fields flow
through generator + exporter + diff + merge + baseline; nothing is a sealed enum/switch — every new
fixed set ships as a documented extension seam.

### 1.2 The three pain points and their root causes (verified)
1. **"Only a fixed amount of aspect ratios to test."** — `UISpecPreview.DefaultResolutions`
   (`Editor/Agent/UISpecPreview.cs:17-22`) is a hardcoded 3-tuple list. `SpecPreviewPane`
   (`Editor/Composer/SpecPreviewPane.cs:125-130`) picks one via `NeoDropdown.ValuePopup` and
   letterboxes a fixed-resolution render into the pane (`SpecPreviewPane.cs:108-113`). There is **no**
   free drag, custom W/H, rotate, zoom, or CanvasScaler. → **Pillar C.**
2. **"Moved elements in portrait, they disappear in landscape."** — ROOT BUG. The generator writes
   `rect.anchoredPosition = new Vector2(position[0], position[1])` and `rect.sizeDelta` as **absolute
   canvas pixels** for free elements (`UISpecGenerator.cs:942-951`). Anchors only set
   pivot/anchorMin/Max and zero-out the *stretch* axis offset (`UIWidgetFactory.TryApplyAnchor`,
   `UIWidgetFactory.cs:131-144`); the **fixed-axis offset is never expressed relative to the anchor**.
   An element authored at `[100,200]` against a `Center` anchor sits 100px right / 200px up of canvas
   center regardless of canvas width — but because the preview never resized, the bug stayed hidden
   until a real device with a different aspect appeared. The fix is a **Figma-style per-axis
   constraint + offset model**. → **Pillar A (keystone).** A *base* layout that adapts is necessary
   but not sufficient; some designs genuinely need different layouts per orientation → **Pillar B.**
3. **"Layout tools are in their baby shoes."** — No per-child sizing modes (Fixed/Hug/Fill), no
   min/preferred/max exposure, no ContentSizeFitter control from spec, no drag-to-reorder within
   layout groups, no align/distribute, no smart guides beyond 8px grid + sibling-edge snap
   (`ComposerCanvas.cs:474-535`), no widget palette, no templates. → **Pillars A, D, E, F.**

### 1.3 Web-UX influences (mined and applied; cited where they motivate a feature)
- **Figma constraints** (per-axis: Left / Right / Left&Right / Center / Scale) → Pillar A. Maps
  exactly onto Unity `anchorMin`/`anchorMax`/`pivot`/offsets but with a vastly better mental model.
  This is the structural answer to "disappears in landscape": store an **offset relative to a
  per-axis constraint**, not absolute px.
- **Figma Auto Layout** (direction, gap, per-side padding, alignment, per-child Fixed/Hug/Fill) →
  Pillars A + F. Maps onto `LayoutGroup` + `ContentSizeFitter` + `LayoutElement`.
- **Webflow / Framer breakpoints** (author a base, override deltas per breakpoint; base inherits) →
  Pillar B. We choose a **runtime driver** model (see §2.B decision).
- **Browser DevTools responsive design mode** (draggable viewport, device presets + custom W/H +
  "Responsive" free mode, rotate, dimension readout, zoom-to-fit) → Pillar C.
- **CSS flexbox/grid** (justify/align/grow/shrink/basis; grid template + gap + auto-fit/minmax) →
  Pillars A + F (informs sizing + grid auto-columns; `UIResponsiveGridColumns` already exists,
  `Runtime/Containers/UIResponsiveGridColumns.cs`).
- **Figma/Sketch smart guides** (edge/center alignment guides, equal-spacing pips, snap-to-distribute,
  align/distribute toolbar, nudge, shift-resize, duplicate-drag) → Pillar D.
- **Design-tool palette + components/templates** (categorized searchable palette, drag-to-canvas,
  curated scaffolds) → Pillar E.

### 1.4 The non-negotiable principle
**The spec is the source of truth; the prefab is its materialization.** Every feature below either
(a) adds spec fields that round-trip byte-identically through generator+exporter+diff+merge+baseline,
or (b) is pure editor chrome that writes only through `SpecDocument.ApplyEdit` (the single undo-aware
mutation entry point, `Editor/Composer/SpecDocument.cs`). No feature writes RectTransforms as the
persisted truth.

---

## 2. Architecture decisions (per pillar)

This section is the binding contract for the data model. Sub-plans expand the implementation.

### 2.A Responsive layout foundation — the constraint+offset model

**Decision: add a `layout` sub-object to `ElementSpec`, additive and backward-compatible.** The
existing `anchor`/`position`/`size`/`flex` fields stay valid (and the 16 anchor presets keep
working); `layout` is the richer, preferred expression. When `layout` is present it wins; when absent
the legacy fields drive generation exactly as today (zero behavior change for un-migrated specs).

New JSON shape (all keys optional):
```jsonc
{
  "button": {
    "label": "Play",
    "layout": {
      // Per-axis constraint (Figma model). horizontal: left|right|leftRight|center|scale
      //                                     vertical:   top|bottom|topBottom|center|scale
      "h": "center",            // default "left"  (preserves legacy when omitted? see migration)
      "v": "bottom",            // default "top"
      // Offsets are interpreted PER CONSTRAINT (see table in 01-…):
      //   left/right/top/bottom  -> distance from that edge to the matching element edge (px)
      //   center                 -> signed offset of the element center from the parent center (px)
      //   leftRight/topBottom    -> [startMargin, endMargin] insets from both edges (px)
      //   scale                  -> [startFraction, endFraction] of parent (0..1), proportional
      "offset": { "left": 40, "bottom": 64 },
      "size":   { "w": 320, "h": 96 },         // ignored on a stretched (leftRight/scale) axis
      // Per-child sizing mode in an auto-layout parent: fixed | hug | fill
      "sizing": { "w": "fixed", "h": "hug" }
    }
  }
}
```
- **Mapping to Unity** (authoritative table lives in `01-…`): each constraint maps to a definite
  `anchorMin`/`anchorMax`/`pivot` and an offset interpretation in terms of `offsetMin`/`offsetMax`
  (NOT `anchoredPosition` — that is the source of the bug). E.g. `h:"left"` →
  `anchorMin.x=anchorMax.x=0`, `offsetMin.x = leftOffset`; `h:"right"` → `anchorMin.x=anchorMax.x=1`,
  `offsetMax.x = -rightOffset`; `h:"leftRight"` → `anchorMin.x=0,anchorMax.x=1`,
  `offsetMin.x=startMargin, offsetMax.x=-endMargin`; `h:"center"` →
  `anchorMin.x=anchorMax.x=0.5`, position from center; `h:"scale"` →
  `anchorMin.x=startFraction, anchorMax.x=endFraction`, zero offsets.
- **The 16 legacy presets become constraint pairs** (re-expressed, not deleted). `Center` →
  `{h:center, v:center}`; `TopLeft` → `{h:left, v:top}`; `Stretch` → `{h:leftRight, v:topBottom}`;
  `StretchTop` → `{h:leftRight, v:top}`; etc. The factory keeps `AnchorPresets`/`TryApplyAnchor`/
  `DetectAnchor` for legacy specs; a new `ConstraintLayout` static converts both directions.
- **Per-child sizing (Fixed/Hug/Fill)** maps to: `fixed` → `LayoutElement.min=preferred=size`,
  `flexible=0`; `hug` → `ContentSizeFitter` on that axis = `PreferredSize` (or, for a parent stack,
  `childControlX=true` + no forced expand) ; `fill` → `LayoutElement.flexible=1` + force-expand on
  that axis. This **replaces the hardcoded `ConfigureStackSizing`** (`UIWidgetFactory.cs:1344-1354`,
  childControl/childForceExpand baked) with a per-child-driven configuration.
- **Round-trip:** exporter reverse-maps anchors+offsets → constraint+offset, and
  `LayoutElement`/`ContentSizeFitter` → sizing modes. A new `LayoutSpec.ToJsonObject`/`Parse` plus
  exporter detection makes export→generate→export byte-identical. **Diff/merge cost nothing extra**:
  `layout` is a nested dict under the element body, so `SpecDiff.DiffDict` (`SpecDiff.cs:83-97`)
  walks it field-by-field for free; `SpecPath` already addresses nested dicts. No `SpecPath` change
  needed (verified: `SpecPath.WidgetKey` keys elements by id/position, `layout` is just another body
  field).
- **Tradeoff stated:** we keep `position`/`size`/`flex` as legacy rather than hard-migrating every
  committed spec, because (a) byte-identity must hold and (b) the ColorACube demo + starter kit are
  GUID-referenced committed assets. A **one-time opt-in migration pass** (Pillar A, workstream A4)
  rewrites a spec from legacy → `layout` and is run explicitly, never silently.

### 2.B Breakpoint / orientation override system

**Decision: RUNTIME driver, delta-override cascade.** A view authors a **base** layout (the element
fields above). A `breakpoints` section defines named conditions (orientation or width threshold). A
view/element carries optional **overrides** that store ONLY the changed fields per breakpoint. At
runtime a `UIResponsiveRoot` component on the view root watches canvas size/orientation, selects the
active breakpoint, and applies the override deltas to the affected elements.

**Tradeoff (bake-time vs runtime):** bake-time (generate N prefab variants) would be simpler to
render but (a) explodes the asset count, (b) breaks WYSIWYG single-prefab identity, (c) can't react
to a *continuous* resize at runtime (the user's literal request: "every possible aspect ratio"). We
choose **runtime** so one prefab adapts live, matching DevTools/Framer behavior. Cost: a runtime
component + a small per-element override applier; mitigated by applying only on breakpoint *change*
(not per frame) and only to elements that declare an override.

JSON shape (view-level + per-element overrides):
```jsonc
{
  "breakpoints": [                         // top-level UISpec section, ordered; first match wins
    { "name": "landscape", "when": { "orientation": "landscape" } },
    { "name": "wide",      "when": { "minAspect": 1.6 } },     // width/height >= 1.6
    { "name": "narrow",    "when": { "maxWidth": 600 } }       // reference-px width <= 600
  ],
  "views": [{
    "id": "Menu/Main",
    "elements": [{
      "vstack": {
        "layout": { "h": "leftRight", "v": "topBottom" },      // BASE
        "overrides": {                                          // delta per breakpoint
          "landscape": { "layout": { "h": "center", "size": { "w": 900 } } }
        },
        "children": [ /* … */ ]
      }
    }]
  }]
}
```
- **Cascade semantics:** active override merges OVER base (shallow-by-field at the `layout`/scalar
  level; nested `layout` merges field-by-field). Base inherits for anything not overridden. This is
  the Webflow/Framer model.
- **Round-trip:** `breakpoints` and `overrides` round-trip via new `UISpec`/`ElementSpec`
  serialization. The generator bakes the base into the prefab (WYSIWYG = base) AND writes the
  override table into the `UIResponsiveRoot` component (serialized, force-text). Exporter reads the
  component back. Diff/merge: `overrides` is a dict keyed by breakpoint name → `SpecPath` needs a
  small addition so override entries key by breakpoint name (not position) — see `02-…`.
- **WYSIWYG decision:** the baked prefab equals the **base** breakpoint. The preview can show any
  breakpoint by driving `UIResponsiveRoot` to that condition (Pillar C ties the viewport
  aspect/orientation to the active breakpoint).

### 2.C Free viewport
**Decision:** Replace the hardcoded resolution list with (a) an extensible **device-preset registry**
(`ComposerDevicePresets.Register`, defaults flow through it), (b) **custom W/H** entry, (c) **free
drag-resize** handles on the pane (DevTools "Responsive" mode), (d) **rotate** (swap W/H), (e)
**zoom / fit**, (f) a **CanvasScaler-equivalent** in the preview render so content scales like a
device (reference resolution + match policy taken from `NeoUISettings`). The preview canvas gains a
`CanvasScaler` configured to `ScaleWithScreenSize` so a 320-wide phone and a 1920-wide tablet render
the SAME UI at proportional scale — exactly how it ships. Live viewport resize re-renders and (via
Pillar B) re-selects the active breakpoint.

### 2.D Direct-manipulation canvas
**Decision:** All canvas writeback becomes **constraint-aware**: `CommitMove`/`CommitResize`
(`ComposerCanvas.cs:383-470`) write `layout.offset`/`layout.size` honoring the element's current
constraint (e.g. a right-anchored element stores a right offset), so dragging in one viewport stays
correct in another. Add: drag-to-reorder within layout groups (insertion indicator), smart alignment
guides + equal-spacing pips, multi-select align/distribute toolbar, keyboard nudge/duplicate/delete,
resize handles that surface the constraint widget. Live viewport resize updates element boxes
without a full rebuild where possible.

### 2.E Palette + templates
**Decision:** A categorized, searchable **widget palette** driven by a `ComposerPalette` registry
that enumerates built-in kinds + `NeoElementKinds.All` (so project kinds appear automatically). Drag
onto canvas (drops into hovered container at cursor, computing the right `layout` for the drop) or
onto the tree (insert as child). **Templates/scaffolds** (main menu, settings, HUD, pause, popup) are
curated `.json` specs shipped under `Editor/Composer/Templates~` and inserted via a "New from
template" picker, all through `SpecDocument.ApplyEdit`.

### 2.F Inspector overhaul
**Decision:** A Figma-like properties panel: a **constraint widget** (the 3×3 + stretch control), an
**auto-layout panel** (direction/gap/per-side padding/align), **per-child sizing dropdowns**
(Fixed/Hug/Fill), and **breakpoint override editing** (a breakpoint selector that scopes edits to the
active override). All drawn through the EditorUI kit, all caches per CLAUDE.md IMGUI rules, all
extend `SpecFieldCatalog` via the existing `RegisterField` seam (`SpecFieldCatalog.cs:164`).

### 2.G Preview fidelity
**Decision:** Live theme-token recolor (re-render on token edit), live sample data in bound lists
(`UIData.Set` with template rows during preview), optional live flow playback, general polish.

---

## 3. Phase & wave breakdown (summary; full detail per sub-plan)

The work is **7 pillars across 5 waves**. Pillar A is the keystone and is **Wave 1, mostly serial**
because it touches the core quartet (`UISpec.cs`, `UISpecGenerator.cs`, `UISpecExporter.cs`,
`UIWidgetFactory.cs`). The **iron rule**: any workstream touching the core quartet is **serialized**
within a wave (one quartet-owner per wave) or split so each agent owns disjoint *methods/regions* of
those files with a documented merge order. Composer/EditorUI files parallelize freely.

See [08-orchestration-and-testing.md](08-orchestration-and-testing.md) for the full wave table,
per-task verification commands, risk register, testing strategy, migration, and Definition of Done.

### Wave table (authoritative summary)

| Wave | Workstreams (parallel unless noted) | Touches core quartet? | Merges before next wave |
|------|-------------------------------------|-----------------------|-------------------------|
| **1 — Keystone** | **A1** constraint data model in `UISpec.cs` (+`LayoutSpec`) → **A2** generator+factory mapping → **A3** exporter reverse-map + round-trip tests → **A4** legacy migration pass. **A1→A2→A3 are SERIAL** (same quartet files). **A4** parallel-tail after A3. | YES (all of A) | ALL of A. The branch must compile + round-trip byte-identical before Wave 2 starts. Non-negotiable gate. |
| **2 — Breakpoints + Viewport** | **B** breakpoint override system (quartet + new runtime `UIResponsiveRoot`); **C** free viewport (Composer-only: `SpecPreviewPane`, `UISpecPreview`, new `ComposerDevicePresets`). | B: YES; C: NO | B and C both. B must merge before D (canvas reads breakpoint). C independent of B except a thin "active breakpoint" hook (define the interface in B, C consumes it). |
| **3 — Canvas + Inspector** | **D** canvas upgrades (`ComposerCanvas.cs` only + new guide/align helpers); **F** inspector overhaul (`SpecInspector.cs`, `SpecFieldCatalog.cs`, new constraint-widget control in EditorUI). | NO (both Composer/EditorUI) | D and F. |
| **4 — Palette + Templates** | **E** palette + drag-create + templates (new `ComposerPalette`, palette pane in `NeoComposerWindow`, templates folder). | NO | E. |
| **5 — Fidelity + Polish + Gate** | **G** preview fidelity; full-suite test run (gated, editor closed); docs/spec-reference regen. | NO | — (final) |

**Parallelism reality:**
- Wave 1: **A1, A2, A3 cannot run in parallel** — they edit overlapping regions of the same quartet
  files. Run them as a *relay* (one worktree, sequential commits, or three worktrees merged in
  strict order A1→A2→A3). A4 (a new file + a menu item) parallelizes after A3.
- Wave 2: **B (quartet) and C (Composer)** are file-disjoint → true parallel. The only coupling is a
  one-method interface (`IActiveBreakpoint`/preview hook) that B defines first; C is written against
  the interface and integration-tested after both merge.
- Wave 3: **D (`ComposerCanvas.cs`) and F (`SpecInspector.cs`+`SpecFieldCatalog.cs`+EditorUI control)**
  are file-disjoint → true parallel. Both depend on A (constraint model) and read B's preview hook.
- Wave 4: **E** is mostly new files + additive edits to `NeoComposerWindow.cs` and `SpecTreeView.cs`;
  depends on A (drop computes a `layout`) and benefits from D (drop targeting) but doesn't conflict.
- Wave 5: **G** + the gated full test run.

---

## 4. Cross-cutting contracts every workstream must honor

1. **Mutation only via `SpecDocument.ApplyEdit(Action, label)`** — never mutate `ElementSpec`
   in place outside it (it drives undo + dirtying + preview rebuild). Verified entry point used by
   `ComposerCanvas.CommitMove` (`ComposerCanvas.cs:401`) and `SpecTreeView.AddElementTo`
   (`SpecTreeView.cs:541`).
2. **Round-trip byte-identity** — any new spec field needs deterministic `ToJsonObject` ordering in
   `UISpec.cs` and a matching exporter read. Guard with a test in `Tests/EditMode` modeled on
   `SpecLayoutAndWidgetTests`.
3. **No silent failures** — unmatched name-addressed lookups `Debug.LogWarning` (breakpoint name not
   found, device preset id missing, etc.).
4. **WYSIWYG** — baked prefab start state == runtime start state. Base breakpoint is what bakes.
5. **Extensibility seams, not enums** — every new fixed set (sizing modes, constraint kinds, device
   presets, breakpoint condition kinds, palette categories) ships as a registry/interface with
   defaults registered through it. Pattern to follow: `NeoElementKinds.Register`
   (`NeoElementKinds.cs:109`), `ComposerCatalogKinds.Register` (`ComposerCatalogKinds.cs:82`),
   `ComposerOptions.RegisterVariant/Size/Align/Shape` (`ComposerOptions.cs:39-50`),
   `SpecFieldCatalog.RegisterField` (`SpecFieldCatalog.cs:164`).
6. **IMGUI performance** — cache GUIStyles/ReorderableLists/SerializedObjects; fetch dropdown options
   only on open; no editor-tick visual animation; no reflection scans on selection.
7. **Editor is OPEN on the main checkout** — agents NEVER batch-compile or kill it. Verify with the
   Roslyn relay in `08-…` §Verification (the existing `neo-compilecheck.ps1` is the template; each
   worktree must point `$Proj` at the OPEN checkout's `Library/ScriptAssemblies`, not its own
   worktree which has none).

---

## 5. Pointers to detail
- Data model exact shapes, mapping tables, code sketches: in each pillar's sub-plan §"Architecture".
- Per-task file ownership + non-overlap guarantees + verification + acceptance criteria: each
  sub-plan §"Workstreams".
- Wave schedule, risk register, testing strategy, migration, DoD: [08-…](08-orchestration-and-testing.md).
