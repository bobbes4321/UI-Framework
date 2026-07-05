# Design-System Cohesion Plan

Status: PROPOSED (2026-07-05). Scope: the editor authoring surface — `NeoDesignSystemWindow`,
`NeoSetupWizard`, `NeoUIHubWindow`, `NeoGalleryWindow`, the right-click asset types and their
registries, the scene-view overlay preset workflow, and the animator preset pickers.

## Diagnosis

The package has grown four disconnected front doors, each covering an overlapping slice of the
design system with no cross-linking and different levels of fidelity:

1. **Hub** (`Tools → Neo UI → Hub`) — claims to be "every tool one click away" but its
   `HubToolRegistry` doesn't register the Design System window or the Gallery.
2. **New Project Setup wizard** — a one-shot seeding flow that doesn't reflect current project
   state when reopened (custom colors reseed to defaults every time), so it reads as "already done,
   nothing for me here".
3. **Design System window** — positioned as the ongoing authoring surface, but three of five tabs
   are shells: Presets lists assets and bounces to the Inspector, Motion only assigns defaults
   (can't create or edit a preset), and there is **no Typography tab** at all despite
   `Theme.textStyles` being a fully modeled, factory-consumed structure.
4. **Right-click asset creation + scene overlay + component inspectors** — the most capable path
   (full Inspector fidelity, `NeoWidgetPresetEditor`, `AnimationPresetBrowserPopup` with hover
   preview) but invisible from any window, and two central asset types (`UIAnimationPreset`,
   `ThemeBundleDefinition`) ship with **no custom inspector** — raw default-inspector editing of a
   five-channel animation.

Net effect: there is no single place that answers "what does my design system currently consist
of, where did each piece come from, and how do I add/change one?" — the exact new-user confusion
this plan fixes.

## Confirmed bugs (Phase 0 fixes)

All in `Editor/NeoDesignSystemWindow.cs` unless noted.

- **B1 — Variant dropdown silently flips the live theme and isn't persisted.** Colors-tab variant
  picker (`:100`) sets `theme.ActiveVariantName` (fires `RaiseChanged`, recolors the whole project)
  with no `Undo.RecordObject`/`SetDirty` — surprising side effect and the change can be lost on
  domain reload. Fix: either an explicit "browse variant" (local view state) separate from an
  explicit "Set Active" button, both undo-recorded + dirtied.
- **B2 — "Re-derive hover/pressed" cross-contaminates variants.** `DerivePair` (`:165`) reads the
  ACTIVE variant's base color but writes hover/pressed with `variantName = null` →
  `Theme.SetToken` stamps them into **every** variant (Dark-derived states destroy Light's). Fix:
  derive per-variant from each variant's own base.
- **B3 — Token ✕ removes from ALL variants.** The list shows the selected variant's tokens but
  `theme.RemoveToken(tc.token)` (`:120`) is theme-wide. Fix: confirm dialog stating scope (tokens
  are theme-wide by design — the UI must say so).
- **B4 — Shape-style edits don't fire `RaiseChanged`.** Shapes tab mutates `ShapeStyle` in place
  (`:389-395`); live `ThemeShapeStyleTarget`s don't refresh until an unrelated token edit. Fix:
  route through `theme.SetShapeStyle` or call `RaiseChanged` after edit.
- **B5 — Radius slider wipes per-corner radii.** `:392` force-overwrites `radiusPerCorner` with a
  uniform Vector4 — silently destroys per-corner setups made in the Theme inspector. Fix: only
  write `radiusPerCorner` when the style is uniform; expose the uniform toggle + per-corner fields
  (see Phase 2 Shapes fidelity).
- **B6 — Theme-bundle re-apply stomps window edits with no warning.** `ThemeBundles.ApplyBundle`
  and the window both write the same tokens; the window has zero bundle awareness. Fix: Phase 2
  bundle integration (provenance hint + apply-diff preview).
- **B7 — Buttons tab misc.** Built-in variants invisible (see Phase 2 — root cause is the factory
  fallback switch); `buttonVariants`/`buttonSizes` unguarded against null lists from pre-migration
  settings assets; "Add size" always creates a duplicate-named `xl`.

## Phase 1 — One front door, honest wizard (small, high leverage)

- **1.1** Register the missing tools in `HubToolRegistryDefaults`: Design System window, Gallery
  window, `Create or Repair Widget Presets`. Give `NeoGalleryWindow` a `[MenuItem]`
  (`Tools → Neo UI → Gallery`) — it is currently unreachable by a user. Acceptance:
  `HubToolCoverageTests` extended to require every `Tools/Neo UI` window in the Hub.
- **1.2** Setup wizard reflects reality: on open, load the CURRENT palette into the custom-color
  fields, the current motion defaults into the role dropdowns, and show per-include-toggle
  "already installed ✓" state (reuse the Hub's Tri-state probes). Rename result-panel buttons to a
  clear next-step path ("Continue in Design System").
- **1.3** Menu hygiene: dedupe `[MenuItem]` priorities (Fonts vs Widget Presets both 102, Animation
  Library vs Menu Widget Library both 103); unify `CreateAssetMenu` roots (`Neo/UI/...` vs
  `Neo UI/...` — pick `Neo UI/...` everywhere).
- **1.4** Purge stale Composer vestiges in user-facing text/docstrings (`PresetPickerPopup`,
  `NeoWidgetPresetEditor`, `NeoUISettings` "Composer Preview" header → "Preview").

## Phase 2 — Design System window becomes the real overview + editor

- **2.1 Overview tab (new, default).** A dashboard answering "what is my design system": active
  theme bundle + variant, token/text-style/shape-style/button-variant counts, preset count by
  category, motion defaults summary, per-section jump buttons and "＋ New" actions. Each card shows
  provenance where known (e.g. "seeded by CleanSlate bundle"). This is the missing overview.
- **2.2 Typography tab (new).** CRUD over `Theme.textStyles` (`SetTextStyle`/`RemoveTextStyle`
  already exist): name, font, size, style, spacing, color — with a rendered sample line per style
  (actual TMP font). Same undo/dirty discipline as Colors.
- **2.3 Presets tab → real editor.** Replace the list-and-ping with: thumbnail card grid (reuse
  `PresetThumbnailCache`/`PresetThumbnailRenderer` — the same visuals as `PresetPickerPopup`),
  kind filter, and an embedded editor pane that renders the SAME form as `NeoWidgetPresetEditor`
  (extract its body into a shared drawer so window + inspector stay one implementation). Create
  flow: name + targetKind picker sourced from `NeoWidgetPalette`/`NeoElementKinds` (not hardcoded
  "button") + optional "start from current selection" (routes `NeoSceneAuthoring.CreatePresetFromWidget`).
  Duplicate/delete actions.
- **2.4 Motion tab → browse + author.** Top: role-default rows (as today). Below: the full preset
  library as a grouped browser (reuse `AnimationPresetBrowserPopup`'s grouping/search machinery,
  hoisted into a reusable drawer) with **New Preset** (category + name → creates the asset via the
  same path the bootstrap uses) and an embedded channel editor for the selected preset — which
  requires **2.5**.
- **2.5 `UIAnimationPresetEditor` custom inspector (new).** Move/Rotate/Scale/Fade/Color channel
  sections using the existing `AnimatorEditorGUI` drawers + a Preview button (reuse
  `AnimationPreview` snapshot/restore on the current selection, like the browser popup's
  hover-preview). Also a `ThemeBundleDefinitionEditor` with an "Apply Bundle" button and variant
  token tables. Both windows and inspectors share the drawers — one implementation.
- **2.6 Buttons tab: make built-ins first-class data.** Root cause of "partially editable":
  primary/secondary/ghost/danger live in `UIWidgetFactory.VariantColors`' fallback switch, so a
  fresh project shows "No custom variants" and the canonical four can't be edited. Fix: settings
  bootstrap seeds the four built-ins into `settings.buttonVariants` (token-bound, matching the
  switch), the switch stays only as a last-resort fallback for old assets, and the tab lists ALL
  variants uniformly. Add `success` to the seeded set (its tokens already exist). Acceptance: a
  fresh project shows five editable variants; `variant: success` renders green out of the box.
- **2.7 Shapes tab: full fidelity.** Uniform toggle + per-corner radii, radius unit, fill mode /
  gradient (second color + angle), elevation — i.e. the whole `ShapeStyle`. Preview via a real
  `NeoShape` render (`UIScreenshotter.RenderToTexture`, like the button preview) instead of the
  faux `DrawRect`.
- **2.8 Bundles section (Overview or its own tab).** List `ThemeBundleRegistry` bundles, Apply
  (with a diff preview: which tokens/styles will change), and **"Save current look as bundle"**
  (rehome the wizard's `SaveCustomBundle` so the window can round-trip a hand-tuned look into a
  reusable `ThemeBundleDefinition`). Token rows in Colors get a subtle "bundle: X" provenance tag
  when they match the active bundle, and Apply warns when it would overwrite dirty edits (fixes B6).
- **2.9 Extensibility seam for the window itself.** Tabs become a registry
  (`NeoDesignSystemTabs.Register(id, title, drawer)`) seeded with the built-ins — a consuming
  project adds its own design-system tab without forking, per the package's hard constraint.

## Phase 3 — One consistent creation story everywhere

Rule to converge on: **every design-system asset is creatable three ways** — (a) right-click in
Project (`Neo UI/...`), (b) from the Design System window section that surfaces it, (c) in-context
where you already are (scene overlay, animator inspector) — and all three land on the same asset
type, discovered by the same registry, with the window refreshing via the existing
`NeoAssetRegistry` postprocessor invalidation.

- **3.1** Layout templates get the missing asset seam: `NeoLayoutTemplateDefinition`
  ScriptableObject (name, category, spec JSON TextAsset or inline) discovered by a
  `NeoAssetRegistry`-based registry, merged with the built-in `Templates~` JSON — today templates
  are the ONLY extension point that requires C# (`NeoLayoutTemplates.Register`). Surface them in
  the Design System Overview and keep `GameObject → Neo UI → Insert Template…` fed from the union.
- **3.2** Preset workflow escapes the overlay: Apply/Create/Update/Reset-Preset buttons on the
  widget component inspectors (`NeoUIEditor` foldout section) so a user who never noticed the
  scene-view overlay can still reach them; both routes call the same `NeoSceneAuthoring` methods.
- **3.3** Design System window sections gain "＋ New" for every asset-backed type (widget preset ✓
  2.3, animation preset ✓ 2.4, theme bundle ✓ 2.8, showcase → button that routes to the Hub).
- **3.4** Naming/API cleanup: rename `ButtonVariantAsset`/`ButtonSizeAsset` (plain serializable
  classes, not assets) to `ButtonVariantDef`/`ButtonSizeDef` with `[FormerlySerializedAs]`-safe
  migration, or document why they stay inline on settings rather than being SO-per-variant like
  every other extensible type.

## Phase 4 — Learnability

- **4.1** Empty states teach: every list-empty state names the three creation routes ("Create here,
  right-click in Project, or run Setup → Widget Presets").
- **4.2** Hub Showcases tab links each showcase to the features it demos; Design System Overview
  links to the `presets`/`animations`/`buttons` showcases as living examples.
- **4.3** A short `Assets/docs/authoring-guide.md`: the mental model (Hub = launch, Setup = seed,
  Design System = author the look, GameObject menu + overlay = build screens, right-click assets =
  extend), one page, linked from the Hub.

## Test coverage to add

- `HubToolCoverageTests`: every `Tools/Neo UI` window has a Hub tool (extends existing).
- Design System window smoke tests (EditMode): per-tab draw with fresh/legacy settings assets (null
  lists — B7), derive-states per-variant correctness (B2), token remove scope (B3),
  `RaiseChanged` fired on shape edit (B4), per-corner preservation (B5).
- Built-in variant seeding round-trip: fresh settings → five variants; `variant: success` resolves
  from `buttonVariants` not the fallback switch.
- Template-definition discovery test mirroring `ShowcaseRegistry`'s.

## Sequencing

Phase 0 (bugs) and Phase 1 are independent and small — do first, in one wave. Phase 2 is the bulk;
2.5 (shared drawers/inspectors) precedes 2.3/2.4 which embed them. Phase 3 rides on 2.x seams.
Phase 4 last, once the surfaces it documents are stable.
