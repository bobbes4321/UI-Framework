# UI Beautification Plan

> **Status (2026-06-11): P1–P6 implemented.** TextStyles + Inter (P1), Lucide icons + IconMap
> (P3), button variants/sizes + WidgetStyleTag (P2), Ring/Arc + gradients + elevation + radial
> progress (P4), CleanSlate/NeonArcade/SoftFantasy bundles + design lint (P5), press-scale /
> spring knobs / cascade / counter / badge / UISoundRelay (P6). Acceptance renders:
> `neo-screenshots/beautification/` (demo spec × starter + 3 bundles), regenerate via
> `-executeMethod Neo.UI.Editor.BeautificationAcceptance.Run`. Still open from P6:
> segmented progress, rarity frame, real-art support (sprites/9-slice); tab panels remain
> explicitly deferred.

Goal: agent-generated UI that looks professionally designed — web-app-grade polish first, then
game-grade "juice." Companion to `neo-ui-package-feature-spec.md` and
`editor-ux-analysis.md`. Written 2026-06-11, after the agent loop e2e test (see
`neo-demo-game-ui.json` + `neo-screenshots/`): layout/widgets/flow all work; what's missing is
the curated design system the way Tailwind/shadcn give it to web LLMs.

**Core principle: don't make agents better designers — give them a designer-made system to lean
on.** Beauty comes from curated tokens/styles/motion an agent *selects*, never values it invents.

**Cross-cutting rules (every phase):**
- Every new spec field gets deterministic export and an extension to the fixed-point tests
  (`Export_Generate_Export_IsFixedPoint` style — byte-identical round trips).
- One shared NeoShape material stays sacred; new rendering features ride vertex channels.
- No editor-side animation; new serializable types get EditorUI drawers (searchable dropdowns for
  any style/token name reference).
- Each phase ends with: tests green + regenerate `neo-demo-game-ui.json` + screenshot comparison.

---

## Phase 1 — Typography system (biggest single lever)

The #1 "programmer art" tell in the current renders is TMP's default LiberationSans at uniform
weight. Fix:

1. **Assets**: commit Inter (SIL OFL — include the license file) as TTFs (Regular, SemiBold, Bold)
   under `Neo UI Framework/Fonts/`, plus pre-generated TMP SDF font assets (commit the .asset files;
   don't generate at runtime/bootstrap — generation is slow and version-sensitive).
2. **Data model**: `TextStyle` ([Serializable], `Runtime/Theming/TextStyle.cs`): name, TMP_FontAsset,
   size, FontStyles, characterSpacing, lineSpacing, default colorToken. `Theme` gets
   `List<TextStyle> textStyles` + Get/Set/Remove/Names API (exact mirror of shape styles).
3. **Binder**: `ThemeTextStyleTarget : MonoBehaviour` (mirror of ThemeShapeStyleTarget): binds a
   TMP_Text by style name, `applyColor` flag, live-reapplies on theme change.
4. **Starter styles**: Display (Bold 72), Title (SemiBold 44), Heading (SemiBold 30), Body
   (Regular 24), Caption (Regular 18 + TextMuted), ButtonLabel (SemiBold 24 + TextOnPrimary).
5. **Spec**: `"textStyle": "Title"` on text/button/toggle/tab labels. `UIWidgetFactory.CreateLabel`
   takes an optional style name and applies the binder. Export: when a ThemeTextStyleTarget is
   present, export `textStyle` and DON'T export fontSize (style owns it); raw fontSize stays the
   styleless fallback.
6. **Editor**: style-name dropdown drawer for ThemeTextStyleTarget; Theme inspector gets an
   NeoListView section for text styles.

Acceptance: settings screen rendered with Display/Title/Body hierarchy looks like a designed page.

## Phase 2 — Widget variants & hierarchy (depends on P1's ButtonLabel style)

Every button today is the same full-width blue slab. Fix:

1. **Tokens**: add Danger, Success (+Hover/Pressed for each) to the starter theme. Add
   `ColorUtils.DeriveHover/DerivePressed(Color)` (HSL shift, mirrors how Doozy generated state
   colors) so bundles define one base per intent and derive the rest.
2. **Variants**: factory `CreateButton(..., variant, size)`: `primary` (filled), `secondary`
   (Surface fill + 1px Outline border), `ghost` (transparent, Primary text), `danger` (Danger
   fill). Sizes sm/md/lg = height 40/56/72 + matching label style.
3. **Round-trip**: tiny `WidgetStyleTag : MonoBehaviour { string variant; string size; }` runtime
   component stamped by the factory; exporter reads it back. (Inferring variant from tokens is too
   fragile.)
4. **Spec**: `"variant"` and `"size"` on button (toggle/tab get variant later if needed).

Acceptance: a screen with one primary CTA + quiet secondary/ghost buttons reads as hierarchy.

## Phase 3 — Icons (independent; can run before/parallel to P2)

1. **Assets**: Lucide icon font (ISC license — commit license) TTF + TMP font asset + a generated
   name→codepoint map (`IconMap.cs` or a JSON the editor parses — pick the C# constant map for
   zero load cost; ~100-icon curated subset: play, pause, settings, x, check, chevron-*, arrow-*,
   heart, star, coin, shield, sword, bag, lock, volume-*, music, user, trash, plus, minus, info,
   alert-*, home, search, refresh, share, trophy, gift, map, flag, clock, zap…).
2. **Settings**: `NeoUISettings.iconFont` (TMP_FontAsset reference).
3. **Factory**: `CreateIcon(parent, iconName, size, colorToken)` = TMP_Text using the icon font +
   the mapped glyph. Button/tab factories get an optional icon slot (`Icon` child-name const,
   horizontal icon+label arrangement; icon-only buttons when label empty).
4. **Spec**: `"icon": "play"` on button/tab; standalone `{ "icon": { "name": "...", "size": 32,
   "color": "TextDefault" } }` element kind. Export: detect by font asset == iconFont, reverse-map
   glyph→name.

Acceptance: HUD pause button shows a real pause icon; settings rows have leading icons.

## Phase 4 — Depth, gradients, new SDF shapes

The pieces half-exist: `NeoGradient` (theme-riding two-stop vertex gradient on any Graphic) isn't
reachable from styles/spec; the SDF shader does soft shadows only via the Card recipe.

1. **ShapeStyle extensions**: gradient (bool + tokenB + angle — applied via NeoGradient so the
   shared material survives), border color token, and an `elevation` int (0–3) mapping to a
   standardized drop-shadow recipe (offset/softness/alpha per level — extract from CreateCard).
2. **Factory**: a `WithElevation(go, level)` helper that builds the shadow sibling the Card way;
   Card/Popup refactored onto it.
3. **New shape types**: `Ring` (annulus, thickness param) and `Arc` (start/sweep angles) in
   NeoShape + shader. Channel budget: ring thickness and arc angles pack into the radii channel
   (UV1) — those params are meaningless for rounded-rect corners, so the channel is free per-mode.
4. **Radial progress**: `ShapeProgressTarget : ProgressTarget` driving an Arc's sweep — radial
   cooldowns/dials for free. Spec: `"progress": { "style": "radial", ... }`.
5. **Spec**: `"gradient": { "from": "PrimaryHover", "to": "Primary", "angle": 90 }` on shapes/
   buttons/backgrounds; shape kinds gain "ring"/"arc".

Acceptance: main-menu CTA with subtle gradient + elevation; a radial cooldown demo renders.

## Phase 5 — Curated theme bundles (the shadcn move; depends on P1/P2/P4 definitions)

1. **Bundles as code** (`Editor/ThemeBundles.cs`), each defining the complete system — token set
   (bases + derived hover/pressed/subtle via ColorUtils), text styles, shape styles (radius
   personality, gradients, elevation), and a motion personality (preset durations/eases):
   - **CleanSlate** — SaaS-neutral, light+dark variants, radius 12, fast subtle motion.
   - **NeonArcade** — dark, saturated gradients, glow accents, radius 8, snappy springs.
   - **SoftFantasy** — warm parchment/deep-forest, radius 20, slower eased motion.
2. **Spec**: `"theme": { "bundle": "NeonArcade", "tokens": { overrides... } }` — bundle applies
   first, explicit tokens override. Menu: `Tools → Neo UI → Apply Theme Bundle…`.
3. **Design lint** (extend AgentValidation): contrast check (WCAG relative luminance) for text
   tokens vs surface tokens; warnings for raw fontSize where a textStyle exists and for off-scale
   spacing/radius values (scale: 4/8/12/16/24/32/48/64).

Acceptance: the SAME `neo-demo-game-ui.json` rendered under each bundle — three distinct,
professional looks from one spec. This pair of before/after screenshots is the proof the whole
plan works.

## Phase 6 — Game-feel & juice (last; independent pieces, pick freely)

1. **Micro-interactions by default**: factory buttons get a pressed scale-to-0.96 state animation
   (UISelectableUIAnimator, State purpose); switch knobs move with Spring play mode (TweenSettings
   already supports Spring/Shake — currently unused).
2. **Cascade**: `UICascadeChildren : MonoBehaviour` on a container — on Show, offsets child
   animators' start delays (default 40ms stagger). Spec: `"cascade": true` on vstack/grid.
3. **Juice widgets** (factory + spec kinds): `counter` (rolling number via FloatTween + TMP),
   `badge` (notification dot/count on any widget corner), segmented progress (tick marks),
   rarity frame (gradient border + glow preset).
4. **Sound hooks**: `UISoundRelay` (runtime, optional) — listens to the existing UIButton/UIToggle
   signal streams, plays AudioClips referenced from NeoUISettings (click/hover/toggle). Zero
   per-widget wiring, signals already carry everything.
5. **Real art support** (when CBN needs it): sprite path references in spec for image elements,
   9-slice support, rounded-corner sprite masking (NeoShape as mask), background art layers.

## Done after the plan

- **Tab panels** (2026-06-11): `"controls"` on a tab names a sibling `panel` element; the generator
  wires it to that tab's `containerReference` and bakes WYSIWYG start visibility (selected tab's
  panel shown, the rest hidden). Panels are `UIPanel : UIContainer` carrying a `PanelId`
  (category/name, registry + `PanelIdDatabase`), so the tab→panel link round-trips byte-identically
  through export. The dead-interaction lint now passes for wired tabs.

## Explicitly deferred / out of scope for now

- `dropdown` widget (TMP_Dropdown template hierarchy — revisit when a real need appears).
- Backdrop blur / glassmorphism (grab-pass cost on mobile; decide per-project).
- Particle/FX element kind; localization-aware text styles; reference-image diff loop.

## Suggested execution order

P1 → P3 → P2 → P4 → P5 (the payoff render) → P6 incrementally. P1+P3+P2 together are roughly one
focused session and transform the look; P4 is the riskiest (shader work — test on device); P5 is
where "agent makes it pretty by default" lands.
