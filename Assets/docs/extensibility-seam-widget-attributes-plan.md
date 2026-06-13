# Plan — Widget Attribute Registries (variants, sizes, shapes, align, icons)

> Member of `extensibility-seams-master-plan.md`. Pattern **R** for the option lists + Pattern **A**
> (ScriptableObject) for designer-tuned data (variant colors, icon glyphs). **Wave 1** — owns
> `ComposerOptions.cs`, `UIWidgetFactory.VariantColors`, `IconMap.cs`; adds additive `NeoUISettings`
> fields (coordinate with the validation plan per the master parallelization map). Does **not** touch
> the element-kind path, so it runs concurrently with the keystone.

## The problem

The per-attribute fixed sets are all baked in:

- Button **variants** `{primary,secondary,ghost,danger}` and **sizes** `{sm,md,lg}` —
  `ComposerOptions.cs:15-16` (picker) + `UIWidgetFactory.cs:56-64` (consts) + a private
  `VariantColors()` switch (`UIWidgetFactory.cs:450-495`) baking colors per variant. Runtime
  `WidgetStyleTag` already stores them as free strings, so the **only** thing sealing variants shut
  is the color switch.
- **Shape names** `{roundedRect,circle,pill,checkmark,chevron,cross,ring,arc}` and **align**
  `{left,center,right}` — `ComposerOptions.cs:17-19`.
- **Icons** — `IconMap.cs:15-202`, ~170 Lucide glyphs in a private static dict (+ aliases at `:205`).
  No way to add a brand glyph; `ComposerOptions.Icons()` (`:67`) reads `IconMap.Names` directly.

A project adding a `success` variant, an `xl` size, or a brand icon must fork.

> **Shape names caveat:** the *spec/picker* side is easy to open here, but a genuinely new primitive
> (e.g. `star`) also needs `NeoShape` mesh + shader work (`NeoShape.ShapeType`, hardcoded shader
> codes). That deeper graphics seam is **out of scope** for this plan — we open the variant/size/
> align/icon sets and the shape **name list** (so registered shapes round-trip), and leave a
> documented note that new primitives need the graphics seam. Don't try to open the shader here.

## The fix, in one line

Replace the `ComposerOptions` literal arrays with seeded registries (`ButtonVariants`, `ButtonSizes`,
`Aligns`, `ShapeNames` → `Register`/`All`), back variant **colors** with a `NeoUISettings` asset list
(Pattern A) that `VariantColors()` reads (falling back to built-ins), and give `IconMap` a settings-
referenced overlay so a project blends custom glyphs with the Lucide defaults.

## Design

### Option lists (Pattern R, editor-side)

In `ComposerOptions.cs`, convert each `static readonly string[]` to a small registry
(`NeoOptionSet` with seeded built-ins + `Register`/`All`), or — lighter and adequate here — a
`List<string>` seed + a `RegisterVariant/RegisterSize/RegisterShape/RegisterAlign` method each. The
picker code already calls `ComposerOptions.ButtonVariants` etc., so it just reads `.All` now.

### Variant colors (Pattern A)

- `UIWidgetFactory.VariantColors(variant)` currently `switch`es on the 4 names. Phase-1 seam-first:
  ```csharp
  if (NeoUISettings.instance.TryGetVariantColors(variant, out var set)) return set;
  switch (variant) { /* the 4 built-ins, untouched */ }
  ```
- Add `List<ButtonVariantAsset>` to `NeoUISettings` (additive, labeled region). `ButtonVariantAsset`
  is a `[Serializable]` holding `string name`, the `SelectableColorSet`, and the content token. A
  project authors one in the inspector → `success`/`warning` variants with no code.
- Sizes are pure dimensions (no color switch) — opening the `ButtonSizes` list + the factory's
  size→dimension lookup the same way is enough.

### Icon overlay (Pattern A)

- Add `IconMapOverlay` asset (name→glyph + name→alias) referenced from `NeoUISettings`.
- `IconMap.TryGetGlyph` / `Names` consult the overlay **first**, then the built-in dict. Keep the
  built-in dict and the forgiving alias table exactly as-is (seam-first). `ComposerOptions.Icons()`
  then transparently shows project glyphs in the searchable picker.

## New & modified files

| File | Action |
|------|--------|
| `Editor/Composer/ComposerOptions.cs` | EDIT — variants/sizes/aligns/shapeNames become seeded registries with `Register`/`All`; pickers read `.All` |
| `Editor/Agent/UIWidgetFactory.cs` | EDIT — `VariantColors` reads settings asset list first, built-in switch as fallback; size lookup reads the list |
| `Editor/Agent/IconMap.cs` | EDIT — `TryGetGlyph`/`Names` consult the settings overlay before the built-in dict |
| `NeoUISettings` (+ `ButtonVariantAsset`, `IconMapOverlay`) | NEW types + additive fields (labeled region; coordinate with validation plan) |

## Testing

- `WidgetAttributeRegistryTests`: each option set equals its built-ins by default; `Register` adds a
  value; the Composer picker reflects it.
- `IconAndVariantTests` (existing) stay green for built-ins; new: a registered `success` variant +
  asset colors generates a button with those colors and round-trips (`WidgetStyleTag` already stores
  the string); a custom icon name resolves through the overlay and survives export.
- Built-in `VariantColors` byte-identical when no asset overrides a built-in name.

## Acceptance criteria

1. A project adds a `success` `ButtonVariantAsset` and an `IconMapOverlay` glyph in the inspector;
   the variant appears in the Composer picker with its colors, the icon appears in the icon search,
   both generate and round-trip — no package file edited.
2. The 4 built-in variants / 3 sizes / 8 shapes / 3 aligns and all ~170 icons behave identically when
   nothing is registered/overlaid.
3. Documented note that a brand-new shape *primitive* (not just a name) needs the `NeoShape` graphics
   seam (out of scope here).
