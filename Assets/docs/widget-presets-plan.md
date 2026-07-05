# Plan — Widget Presets (reusable component styles, à la Figma)

> **Status (2026-07): Waves 1–2 implemented and shipped; this doc is historical design record, not
> current architecture.** `NeoWidgetPreset` + the `NeoWidgetPresets` registry, `ElementSpec.preset`
> resolve/override, `WidgetPresetTag` + override-delta export, `PresetLibraryBootstrap`, and the §6
> motion hookup all exist. What changed since this plan was written: the Composer (its palette/
> inspector CRUD, thumbnail cache, visual picker) was retired — that authoring surface is now native
> scene-view (`Editor/Authoring/PresetPickerPopup.cs`, `NeoSceneAuthoring.CreatePresetFromWidget`/
> `UpdatePresetFromWidget`/`ResetWidgetToPreset`/`ApplyPreset`), and the registry lives at
> `Editor/Agent/NeoWidgetPresets.cs`, not `Editor/Composer/`. See CLAUDE.md's "Spec element kinds"
> (`preset` field) and "Native-Unity authoring" entries for the current source of truth; Wave 5's
> `Editor/Agent/PresetFields.cs` is the current seam for which fields a preset governs (superseding
> this doc's per-call-site field lists).
>
> A new **top layer** of the design system: named, reusable styling bundles ("Primary Button",
> "Section Header") that a widget references **by name** and that resolve at generate time. Pattern
> **A** (a `NeoWidgetPreset` ScriptableObject the project authors) + Pattern **R** (a code-seeded +
> lazily-discovered registry, mirroring `ShowcaseRegistry`). Linked, not copied — change the preset,
> regenerate, every instance updates; per-element fields still override. Two non-negotiable UX goals:
> **(1) dead-simple add/change/remove of presets with a generous prebuilt library**, and **(2) a
> visual thumbnail of each preset in the Composer**, not a text list.

## The problem

The package already ships the *bottom* layers of a design system — and they're all extensible:

- **Color tokens** + variants/modes (`Theme.variants`/`TokenColor`, `ThemeColorRef`) — Figma's
  Variables/Color styles. ✅
- **Text styles** (`Theme.textStyles`, `TextStyle`, `ThemeTextStyleTarget`) — Figma's Text styles. ✅
- **Shape styles** (`Theme.GetShapeStyleNames`/`ShapeStyle`) — partial Effect styles. ◐
- **Animation presets** (`AnimationPresetDatabase`) — but applied per **view** (show/hide), never as a
  reusable widget-level "motion" you attach. ◐
- **Button variant/size** (`ButtonVariantAsset`/`ButtonSizeAsset` on `NeoUISettings`,
  `WidgetStyleTag`) — extensible, but set field-by-field, one widget at a time. ◐
- **Theme bundles** (`ThemeBundleRegistry`: CleanSlate/NeonArcade/SoftFantasy) — a whole token/
  shape/motion personality in one entry. ✅

What's missing is the **one Figma feature that makes a system feel like a system**: the *Component*
layer. There is no "Primary Button" object you define once and reuse. Today "primary button" is an
ad-hoc tuple (`variant:"primary"` + `size:"lg"` + `textStyle:"ButtonLabel"` + maybe an icon) that the
author re-specifies on every button. Decide later that primary buttons should be a touch more rounded
and you're editing every button. That is exactly the pain this plan removes.

A secondary, smaller problem surfaced while scoping: the Composer keeps a **second, parallel** list of
button variants/sizes (`ComposerOptions.cs:25-26`) separate from `NeoUISettings.buttonVariants`, so a
project variant must be registered *twice* (author a `ButtonVariantAsset` **and** call
`RegisterVariant`). We collapse that here.

## The fix, in one line

Add a `NeoWidgetPreset` ScriptableObject (a named bundle of styling references) discovered through a
`NeoWidgetPresets` registry; add a single `preset` field to `ElementSpec` that the generator resolves
**before** element-level fields (which override); preserve the link on export with a
`WidgetPresetTag` marker + an **override-delta** (write `preset:"name"` + only the fields that differ
from the preset's resolved values); surface presets in the Composer as a **visual card grid** backed
by an in-memory thumbnail render cache; and ship a generous prebuilt library seeded the same way the
Starter Kit is.

## Design

### 1. The preset asset (Pattern A) — `NeoWidgetPreset`

New runtime SO `Runtime/Settings/NeoWidgetPreset.cs` (runtime so it stays consistent with `Theme`/
databases and could feed runtime instantiation later; the *registry* below is editor-side). Flat,
force-text, addressed by name — agent-first. Every styling field is **optional** (`null` / `-1`
sentinel = "this preset doesn't set it"), so a preset only governs the fields it cares about:

```csharp
[CreateAssetMenu(menuName = "Neo/UI/Widget Preset")]
public class NeoWidgetPreset : ScriptableObject
{
    public string presetName;        // "Primary Button" — the id used in specs (unique)
    public string category = "Button"; // grouping for the palette/picker ("Button","Text",…)
    public string targetKind = "button"; // which element kind this styles (seam: allow an array later)
    [TextArea] public string description;

    // References INTO the layers below (resolved at generate; null = not set)
    public string variant;           // → ButtonVariantAsset / built-in
    public string sizeVariant;       // → ButtonSizeAsset / built-in
    public string textStyle;         // → Theme.textStyles
    public string shapeStyle;        // → Theme shape styles
    public string motion;            // → AnimationPresetDatabase (default show/hide; see §6)
    public string background;        // → theme token
    public string labelColor;        // → theme token
    public string icon;              // Lucide name (default icon slot)

    // Direct property defaults (nullable = not set)
    public float? radius;
    public float? padding;
    public float[] padding4;
    public float? spacing;
}
```

> **Naming.** Field is `preset` (string) on the spec; concept is "Widget Preset". We deliberately do
> **not** call it "component" to avoid colliding with Unity's `Component`.

### 2. The registry (Pattern R) — mirror `ShowcaseRegistry` exactly

`Editor/Agent/NeoWidgetPresets.cs` (rehomed off the Composer in Wave 2; editor — uses `AssetDatabase`): code-seeded built-ins **plus**
lazy discovery of every `NeoWidgetPreset` asset, with an `AssetPostprocessor` invalidating discovery
on import. Same `All` / `TryGet` / `Register` / `EnsureDiscovered` / `InvalidateDiscovery` shape as
`ShowcaseRegistry.cs:20-109`; a discovered asset overrides a built-in of the same name; a project adds
a preset by **dropping one asset — no fork, no C#** (exactly the `ShowcaseDefinition` story).

`TryGet` is case-sensitive ordinal on `presetName`. `ResetForTests` re-seeds and forces rediscovery.
We do **not** add a `List<NeoWidgetPreset>` to `NeoUISettings` (discovery already finds project
assets); we *do* let built-ins live under a known root (§5) so seeding/repair has one obvious target.

### 3. The spec field + generate-time resolution (linked, override-on-top)

- `ElementSpec.preset` (string), parsed in `ElementSpec.Parse`, emitted in `ToJsonObject` only when
  non-empty, placed in the existing deterministic key order next to `textStyle` (so pre-existing
  specs stay **byte-identical** — `UISpec.cs`).
- In `UISpecGenerator`, before each element is handed to `UIWidgetFactory`, run
  `ResolvePresetAndOverrides(element)`: if `element.preset` resolves, build an **effective** spec =
  preset fields as base, then any non-null element field wins (`element.variant ?? preset.variant`,
  etc.). The factory only ever sees the merged effective spec — it never learns about presets.
- **Missing preset = graceful, loud.** Per the "no silent failures" rule, `Debug.LogWarning` and fall
  back to the element's own fields (mirrors `UIView.ProcessCommand`). Never throw, never silently drop.

This is the *same shape* as the existing breakpoint cascade (`LayoutSpec.MergedWith`): a base layered
under a delta. We are generalizing a proven pattern, and `textStyle` already proves "store a name,
resolve at generate."

### 4. Export with override-delta (the load-bearing round-trip mechanic)

The exporter reads baked prefab state. If we let it, it would flatten a presetted button back into
inline `variant`/`size`/… and **lose the link**. To preserve it:

- New marker `Runtime/Interactive/WidgetPresetTag.cs` (sibling to `WidgetStyleTag`), stamped by the
  factory at generate with the preset name, `HideFlags.HideInInspector`.
- In `UISpecExporter.ExportElementBody`, when a `WidgetPresetTag` is present and its preset still
  resolves: write `preset:"name"` and emit **only the fields that differ** from the preset's resolved
  values (`ComputeOverrideDelta` — field-by-field, with array/nullable-aware compares, same spirit as
  `FindMatchingPresetName`'s reverse lookup for animations). If the preset is **gone**, warn and
  export the element fully (link lost but values preserved — never silently drop data).

Round-trip guarantee: `generate(exported + preset) ≡ generate(effective)`, because generate re-applies
the preset then layers the delta. Deterministic because the preset is immutable-by-name and the delta
is an exact field compare.

### 5. CRUD — make add/change/remove trivial (UX goal #1)

| Action | Entry point | Notes |
|---|---|---|
| **Create (blank)** | `Create → Neo UI → Widget Preset` (the `[CreateAssetMenu]`) | discovered immediately |
| **Create from selection** | Composer inspector button "Create Preset from This…" | captures the selected element's styling into a new asset, then sets `element.preset` so it's linked (Figma "Create component") |
| **Edit** | Select the asset → `NeoWidgetPresetEditor` (subclass `NeoUIEditor`, kit-styled, kind-aware field set) | live; thumbnail + theme refs validated |
| **Update from selection** | Inspector "Update Preset" (shown when linked + drifted) | diff dialog, then push overrides to the asset (Figma "push to main") |
| **Reset to preset** | Inspector "Reset to Preset" | clears element overrides (undoable) |
| **Delete** | Project delete | graceful: referencing widgets keep rendering off their baked/explicit fields; generate warns |

**Prebuilt library (seeded like the Starter Kit).** `Editor/PresetLibraryBootstrap.cs` mirrors
`StarterKitBootstrap` with a `Tools → Neo UI → Setup → Create or Repair Widget Presets` menu item,
idempotent create-or-repair into `Assets/Neo UI Framework/Presets/{Category}/{Name}.asset`. Ship a
generous set, e.g.: **Button** (Primary/Secondary/Ghost/Danger/Icon/FAB/Link + sm/lg sizes), **Text**
(Display/Title/Heading/Body/Caption/Label), **Dropdown**, **Toggle/Switch**, **Slider/Progress**
(linear + radial), **Tab** (default/filled), **Card/Panel** (default/elevated/bordered), **Input**,
**List Row** (compact/default/large). Each references the appropriate layers (variant/size/textStyle/
shapeStyle/motion) rather than baking literals.

**Bundles seed presets.** Extend `ThemeBundles.Bundle` with the preset ids it installs and have
`ThemeBundles.Apply` seed a matching set, so applying CleanSlate/NeonArcade/SoftFantasy delivers a
full *component library* in that personality — the closest analog to publishing a Figma library.

**Collapse the duplicate registry.** Make `ComposerOptions.ButtonVariants`/`ButtonSizes` lazily
auto-enumerate `NeoUISettings.buttonVariants`/`buttonSizes` (seed built-ins first, then append), so a
project variant appears in the picker with **no** separate `RegisterVariant` call. Byte-stable: the
built-in seed is unchanged; `RegisterVariant` stays for code-only additions.

### 6. Motion as a referenceable default

**Shipped, resolved differently than originally planned.** `NeoWidgetPreset.motion` references an
animation preset by name (via `AnimationPresetRegistry`, not the `AnimationPresetDatabase` lookup this
plan originally assumed). Rather than wiring only into a pre-existing view show/hide or button-press
attach point, it seeds the element's own **loop animation channel** — the same `"animations": {
"loop": "..." }` per-element spec field described in CLAUDE.md's "Animation presets" entry — so a
preset can give *any* target kind, not just views/buttons, a play-on-start `UIAnimator`. The applied
name is stripped back out on export like any other preset-governed field (delta compare against the
preset's resolved value), so it round-trips exactly like `variant`/`textStyle`/etc.

### 7. Visual previews in the Composer (UX goal #2)

Today the palette/pickers are text. Replace with rendered thumbnails, reusing the **existing in-memory
render path** (`UIScreenshotter.CaptureLive` / `UISpecPreview`) that already powers agent `preview` —
no new rendering tech.

- `Editor/Composer/PresetThumbnailRenderer.cs`: build a throwaway one-element `UISpec` (the preset
  applied to its `targetKind`), render to a small `Texture2D` (96px palette, ~180px picker) via the
  existing capture path; returns `null` headless (graphics-less batch) — callers fall back to a label.
- `Editor/Composer/PresetThumbnailCache.cs`: cache keyed by (preset name + active theme variant +
  content hash); **render once, reuse** (honors the "never render per OnGUI" rule). Invalidate on
  theme change / token edit / bundle apply / preset edit (hook `SpecPreviewPane.ApplyDocumentTheme`
  and `SpecDocument.Changed`); `Clear()` releases textures on window close. ~37 KB/thumb, ≈2 MB worst
  case — editor memory, fine.
- **Palette → card grid.** Refactor `PalettePane` to a grid of cards (thumbnail + label + category
  accent), drag-to-create unchanged (`DragAndDrop.SetGenericData(ComposerPalette.DragKey,…)`); keep a
  list-mode toggle for power users. A new **"Presets"/"Components" category** sources the registry, so
  you drag *"Primary Button"*, not generic *"button"* — the literal feature requested.
- **Inspector preset picker**: a visual popup (thumbnail cards) instead of `NeoDropdown.StringPopup`,
  with Figma-style override indicators (which fields differ) and the Reset/Update buttons from §5.
- **Probe coverage**: add a `composerSession` step (e.g. `takePaletteSnapshot`) so the agent loop can
  verify thumbnails render + grid layout via the filmstrip (per the live-editor probe constraint, this
  is validated when a focused editor is available; otherwise compile-check + EditMode tests).

### 8. Merge / diff / drift

`preset` is a scalar string → `SpecMerge.MergeScalar` and `SpecDiff` handle it for free; conflicts
surface (never swallowed) under the existing `SpecPath` element address. `WidgetPresetTag` is an
exporter-preserved marker (like `WidgetStyleTag`), not an off-spec edit, so `OffSpecLint` needs no new
rule — but add an advisory warning when a tag references a missing preset.

## Phasing (parallelizable waves)

- **Wave 1 — model + round-trip (keystone, do first).** `NeoWidgetPreset`, `NeoWidgetPresets`
  registry + postprocessor, `ElementSpec.preset` parse/emit, `ResolvePresetAndOverrides` in the
  generator, `WidgetPresetTag` + `ComputeOverrideDelta` export. Tests: byte-identical round-trip,
  override-delta export, missing-preset fallback, merge of `preset` + overrides. **Nothing ships until
  round-trip is green.**
- **Wave 2 — authoring + library (concurrent after Wave 1).** `PresetLibraryBootstrap` + the prebuilt
  set, `NeoWidgetPresetEditor`, Composer inspector Create/Update/Reset, collapse the variant/size
  registries, bundles-seed-presets. Tests: idempotent seeding, create-from-selection round-trip,
  deleted-preset fallback, bundle seeding, picker auto-enumerates settings.
- **Wave 3 — visual Composer (concurrent after Wave 1; independent of Wave 2).** Thumbnail renderer +
  cache, palette card grid + Presets category, inspector visual picker + override indicators, probe
  step. Tests: thumbnail renders to expected size, cache reuse + invalidation, headless null-safety.

## Acceptance criteria

1. A button referencing `preset:"Primary Button"` generates with the preset's styling; editing the
   preset asset + regenerate updates every instance; a per-element `variant` still overrides.
2. Generate → export → generate → export is **byte-identical**; export writes `preset` + only the
   override delta; a deleted preset degrades gracefully (warn, keep values).
3. A project adds a preset by dropping one `NeoWidgetPreset` asset — it appears in the Composer with no
   code. Create/Update/Reset-from-selection work from the inspector. The prebuilt library seeds
   idempotently via the Setup menu.
4. The Composer palette and preset picker show **rendered thumbnails** in a card grid; thumbnails are
   cached (no per-OnGUI render) and invalidate on theme/preset change; headless runs fall back to
   labels without erroring.
5. `ComposerOptions` button-variant/size pickers auto-include `NeoUISettings`-authored attributes with
   no separate `Register` call; defaults stay byte-stable.

## New / touched files

**New:** `Runtime/Settings/NeoWidgetPreset.cs`, `Runtime/Interactive/WidgetPresetTag.cs`,
`Editor/Composer/NeoWidgetPresets.cs` (+ postprocessor), `Editor/PresetLibraryBootstrap.cs`,
`Editor/Inspectors/NeoWidgetPresetEditor.cs`, `Editor/Composer/PresetThumbnailRenderer.cs`,
`Editor/Composer/PresetThumbnailCache.cs`, tests under `Tests/EditMode/` (round-trip, registry,
bootstrap, creation, merge, thumbnail).

**Touched:** `Editor/Agent/UISpec.cs` (field + parse/emit), `UISpecGenerator.cs` (resolution),
`UISpecExporter.cs` (delta export), `Editor/Composer/ComposerOptions.cs` (collapse + `PresetNames`),
`SpecInspector.cs` (preset picker + CRUD buttons), `PalettePane.cs` (card grid), `ThemeBundles.cs`
(seed presets), `SpecPreviewPane.cs` / `SpecDocument` (cache invalidation hooks).
