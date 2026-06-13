# Plan — Composer Catalog Unification (de-opinionated, extensible menu authoring)

> A worked example of the package's **extensible-by-design** tenet (see `CLAUDE.md` →
> *Hard constraints*). Follow-up to Plan 2 — the Neo UI Composer
> (`human-workflow-plans/02-spec-authoring-window.md`). Related: `settings-cheats-menu-plan.md`
> (the runtime menu system this authors).

## The problem

The Composer surfaces **two co-equal, always-present, top-level tree sections — "Settings" and
"Cheats"** — with two toolbar buttons (`+Settings` / `+Cheats`), two `SpecNodeKind`s
(`SettingsHeader` / `CheatsHeader`) and two add-paths. But under the hood they are the **same**
`MenuCatalogSpec` type, edited by the **same** `MenuCatalogEditor`, differing only by which spec
list they live in (`spec.settings` vs `spec.cheats`) and a single `favourites` bit (`UISpec.cs:1067`).

Two costs fall out of that:

1. **It bakes a product opinion into the chrome.** "Settings" is near-universal; **"Cheats" is
   not.** A project with no cheat menu still gets a permanent empty `Cheats (0)` header and a
   `+Cheats` button — the tool quietly asserts "your game should have cheats."
2. **It doesn't scale.** The moment a project wants a *third* catalog (debug, accessibility,
   key-bindings…) the established pattern says "add another hardcoded section + button + node kind +
   spec list." That is exactly the 90%-then-stuck failure mode the extensibility tenet exists to
   prevent.

The kind-aware **editor** (`MenuCatalogEditor` — slider ranges, dropdown options, rebind fields) is
*not* the problem; that is the right amount of opinion and stays. The problem is the **chrome** that
hardcodes the *set* of catalog kinds.

## The fix, in one line

Collapse "Settings" and "Cheats" into one neutral **"Menus"** section whose children are tagged by
`kind`, driven by a single catalog-kind list — so the package ships sensible defaults (settings,
cheats) while a consuming project can add a kind without forking the Composer.

The spec model, generator and runtime are **not touched in Phase 1** — the settings/cheats
distinction there is real (different runtime behaviour). Only the Composer's presentation changes.

## Current state (what to change)

- `SpecTreeView.cs`
  - `SpecNodeKind` enum: `SettingsHeader`, `CheatsHeader`, `Catalog`, `MenuItem` (`:9-13`).
  - `AddCatalogSection(spec.settings, "settings", …)` + same for cheats, in `RebuildIfNeeded` (`:108-109`,
    helper `:119`).
  - Header context menu "Add Settings Catalog" / "Add Cheats Catalog" (`ShowContextMenu`, `:272-277`).
  - `AddCatalog(kind)` routes to the right list + section (`:373-383`); `DuplicateCatalog` likewise.
  - Toolbar entry points `AddSettingsFromToolbar` / `AddCheatsFromToolbar` (end of file).
- `NeoComposerWindow.cs` → `BuildTreeToolbar` `+Settings` / `+Cheats` buttons (`:115-123`).
- `MenuCatalogEditor.cs` → `catalog.kind == MenuCatalogSpec.CheatKind` branch for `favourites` (`:25`).
- Model (reference only, untouched in Phase 1): `MenuCatalogSpec.SettingsKind`/`CheatKind`
  (`UISpec.cs:1059-1060`), `kind` field (`:1062`), the two lists parsed by section name in
  `UISpec.FromJson` (`:60`, `:65`). Note there is **no** `MenuCatalogSpec.Kinds` array today — the
  kind set is implied by those two constants + two fields. That missing seam is Phase 2.

**Path / selection invariant:** a catalog's stable path is `SpecPath.Catalog(section, id)` where
`section` is `"settings"`/`"cheats"`. The unified header is cosmetic only — each catalog *row* keeps
its real section in its path, so selection, the inspector and the baseline addressing are unchanged.

## Design

### One source of truth for catalog kinds

Introduce a tiny descriptor the Composer reads instead of hardcoding two of everything:

```csharp
// Editor/Composer/ComposerCatalogKinds.cs (NEW) — the package's default kinds + the seam to add more
public readonly struct CatalogKind {
    public readonly string id;        // MenuCatalogSpec.SettingsKind / CheatKind / a project's own
    public readonly string label;     // "Settings", "Cheats", …
    public readonly System.Func<UISpec, List<MenuCatalogSpec>> list; // where catalogs of this kind live
}
public static class ComposerCatalogKinds {
    // built-in defaults
    private static readonly List<CatalogKind> _kinds = new() {
        new(MenuCatalogSpec.SettingsKind, "Settings", s => s.settings),
        new(MenuCatalogSpec.CheatKind,    "Cheats",   s => s.cheats),
    };
    public static IReadOnlyList<CatalogKind> All => _kinds;
    // the extension seam: a consuming project registers its kind once (e.g. from an [InitializeOnLoad])
    public static void Register(CatalogKind kind) { /* replace-by-id or append */ }
}
```

The tree, toolbar and context menu all iterate `ComposerCatalogKinds.All`. Adding a kind in a
project becomes one `Register(…)` call — no Composer edit, no fork. (Phase 1 ships the two built-ins
and routes everything through this list; the `list` accessor keeps Phase-1 storage on the existing
two fields, so no model change yet.)

### Tree

- One `SpecNodeKind.MenusHeader` replaces `SettingsHeader` + `CheatsHeader`.
- `RebuildIfNeeded` emits a single `Menus (N)` section, then iterates `ComposerCatalogKinds.All`,
  appending each kind's catalogs as `Catalog` rows. Each row's `path` still uses its kind's section,
  so nothing downstream changes.
- Catalog rows show a **kind tag**: `Settings · Audio`, `Cheats · Debug` (label = `"{kind} · {name}"`,
  or a small coloured chip via `NeoColors.Data`). The tag is what makes one neutral section legible.
- Empty state reads as neutral: an empty `Menus (0)` section asserts nothing about cheats.

### Toolbar + context menu

- Replace the `+Settings` / `+Cheats` buttons with one **`+ Menu ▾`** `NeoDropdown` whose options
  come from `ComposerCatalogKinds.All` (Settings / Cheats / …). Selecting a kind calls the existing
  `AddCatalog(kindId)`.
- `MenusHeader` context menu offers the same kind picker ("Add Menu/Settings", "Add Menu/Cheats", …),
  mirroring how `AddElementCreateItems` enumerates `ElementSpec.Kinds`.

### Editor

`MenuCatalogEditor` stays. The only generalization: it shows `favourites` when the kind declares it
(today: `kind == CheatKind`). Keep that gated on kind id; a future kind can opt in via its descriptor
if needed. No behavioural change for settings/cheats.

## Phases

1. **Unify the two built-in kinds in the Composer (no model change).** Add
   `ComposerCatalogKinds`, the `MenusHeader` node, the kind tag, the single `+ Menu` picker, and route
   `AddCatalog`/`DuplicateCatalog`/context menus through the kind list. Drop `SettingsHeader`/
   `CheatsHeader` and the two toolbar buttons. Result: identical capability, neutral chrome,
   `Register(…)` seam in place. **This is the shippable deliverable.**

2. **(Future / optional) Generalize the model to N catalog kinds — the deeper seam.** Today the
   *model* bakes the kind set shut into two named fields. To let a project's registered kind actually
   round-trip (export → spec JSON → generate), generalize storage: either add `MenuCatalogSpec.Kinds`
   + a single `List<MenuCatalogSpec> catalogs` (kind on each), or a `Dictionary<string,
   List<MenuCatalogSpec>>`, and teach `UISpec.FromJson`/`ToJson`, `UISpecGenerator.BuildMenuElement`,
   the exporter and the runtime `MenuCatalog` to key by `kind`. Migrate `settings`/`cheats` as
   reserved built-in kinds for back-compat. This is a cross-cutting change and a textbook example of
   the extensibility tenet applied to the model itself — scope it as its own plan when a real third
   kind is needed.

## New & modified files (Phase 1)

| File | Action |
|------|--------|
| `Editor/Composer/ComposerCatalogKinds.cs` | NEW — default kinds + `Register` seam; single source the chrome iterates |
| `Editor/Composer/SpecTreeView.cs` | EDIT — `MenusHeader` replaces the two headers; iterate kinds; kind tag on rows; route `AddCatalog`/`DuplicateCatalog`/context menu through the kind list |
| `Editor/Composer/NeoComposerWindow.cs` | EDIT — `+ Menu ▾` kind picker replaces `+Settings`/`+Cheats` |
| `Editor/Composer/MenuCatalogEditor.cs` | EDIT (minimal) — `favourites` gated via the kind, not a hardcoded `== CheatKind` literal scattered around |
| `UISpec.cs`, `UISpecGenerator.cs`, runtime `MenuCatalog` | UNTOUCHED in Phase 1 (Phase 2 only) |

All chrome stays on the EditorUI kit (`NeoDropdown` for the picker, `NeoColors.Data` accent) per
`CLAUDE.md`.

## Testing

- `SpecTreeViewTests` (or extend the Composer tests): with one settings + one cheats catalog in the
  spec, the tree produces a single `Menus` section containing both, each row tagged with its kind,
  and each row's `path` still equals `SpecPath.Catalog("settings"/"cheats", id)` (selection +
  baseline addressing unchanged).
- `ComposerCatalogKindsTests`: `All` contains the two built-ins; `Register` adds/replaces a kind by
  id; the add-picker options equal `All` (mirrors `SpecFieldCatalogTests.AddPicker_IsExactlyTheSpecKindList`).
- Round-trip unchanged: `Open Project` → no edits → Save is byte-identical (Phase 1 changes only
  presentation, so the existing export/generate idempotency tests must still pass).

## Acceptance criteria

1. A project with no cheats sees a neutral `Menus` section and no `+Cheats` affordance anywhere.
2. Adding a Settings catalog and a Cheats catalog through the one `+ Menu` picker produces exactly
   the same spec/assets as before.
3. A consuming project can register a new catalog kind with a single `ComposerCatalogKinds.Register`
   call and see it in the picker + tree — without editing any package file (Phase 1: it appears in the
   chrome; full round-trip of a *novel* kind awaits Phase 2's model seam).
4. No regression in catalog/menu-item editing, selection, drift/sync, or round-trip idempotency.
