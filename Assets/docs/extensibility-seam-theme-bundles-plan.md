# Plan — Theme Bundle Registry

> Member of `extensibility-seams-master-plan.md`. Pattern **R** (Kinds Registry). **Wave 1** —
> fully independent (disjoint files). The smallest, lowest-risk seam — a good place to prove the
> convention.

## The problem

The three curated bundles (CleanSlate / NeonArcade / SoftFantasy — complete token/type/shape/motion
systems) are a **sealed array**, even though the runtime `Theme` they configure is fully open. A
project cannot ship its own coherent "Corporate" / "HighContrast" / "Mobile" bundle without forking.

## Current state (what to change)

- `Editor/ThemeBundles.cs:83-136` — three `private static readonly Bundle` instances + a
  `private static readonly Bundle[] All`; `TryGet(name, out Bundle)` (`:138`+) looks up by name;
  `Names` projects `All`.
- `Editor/ThemeBundles.cs:308-315` — three hardcoded `[MenuItem("Tools/Neo UI/Apply Theme
  Bundle/…")]` methods, one per bundle.
- `ThemePaletteEditor.cs:18` — `private static readonly string[] Bundles = { "CleanSlate",
  "NeonArcade", "SoftFantasy" }` (the inspector dropdown — drifts from `All` today).
- The spec path `"theme": { "bundle": "…" }` resolves through `ThemeBundles.TryGet`.

## The fix, in one line

Turn `All` into a seeded registry (`ThemeBundleRegistry` with `Register`/`TryGet`/`Names`), drive the
menu and the inspector dropdown off `Names`, so a project registers a `Bundle` once and it appears
everywhere a bundle can be picked.

## Design

- Keep the existing `Bundle` struct/class (its fields already capture a full token/type/shape/motion
  system — don't churn it). Move the three built-ins into the registry's seed.
  ```csharp
  public static class ThemeBundleRegistry {                 // Pattern R
      private static readonly List<Bundle> _bundles = new() { CleanSlate, NeonArcade, SoftFantasy };
      public static IReadOnlyList<Bundle> All => _bundles;
      public static IEnumerable<string> Names => _bundles.Select(b => b.name);
      public static bool TryGet(string name, out Bundle b) { /* case-insensitive */ }
      public static void Register(Bundle b) { /* replace-by-name, else append */ }
  }
  ```
  `ThemeBundles.TryGet`/`Names` become thin forwarders (keep them so the spec path + callers don't
  change).
- **Menu**: replace the three fixed `[MenuItem]`s with a dynamic submenu. Since `[MenuItem]` is
  attribute-driven (can't enumerate at compile time), use a single
  `[MenuItem("Tools/Neo UI/Apply Theme Bundle…")]` that opens a tiny `NeoDropdown`/picker over
  `ThemeBundleRegistry.Names`, then calls the existing `ApplyFromMenu`. (Keeps the chrome on the
  EditorUI kit per `CLAUDE.md`; avoids a fork point per bundle.)
- **Inspector**: delete `ThemePaletteEditor.Bundles` and bind the dropdown to
  `ThemeBundleRegistry.Names` — kills the drift.
- A project registers from `[InitializeOnLoad]`: `ThemeBundleRegistry.Register(myBundle)`.

## New & modified files

| File | Action |
|------|--------|
| `Editor/ThemeBundles.cs` | EDIT — add `ThemeBundleRegistry`; seed built-ins; `TryGet`/`Names` forward; replace 3 `[MenuItem]`s with one picker |
| `ThemePaletteEditor.cs` | EDIT — dropdown reads `ThemeBundleRegistry.Names`, delete local array |

## Testing

- `ThemeBundleRegistryTests`: `Names` equals the three built-ins by default; `Register` adds a new
  bundle and replaces by name; `TryGet` is case-insensitive.
- The beautification acceptance render (`BeautificationAcceptance.Run`, demo × every bundle) still
  enumerates exactly the registry — extend it to iterate `Names` rather than a literal list.

## Acceptance criteria

1. A project calls `ThemeBundleRegistry.Register(new Bundle { name = "Corporate", … })` from
   `[InitializeOnLoad]`; "Corporate" appears in the Apply-Theme-Bundle picker and the inspector
   dropdown, and `"theme":{"bundle":"Corporate"}` in a spec applies it — no package file edited.
2. The three built-ins apply identically to before; acceptance render unchanged for them.
3. Inspector dropdown and menu can no longer drift from the registry (single source).
