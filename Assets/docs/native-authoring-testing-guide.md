# Native-Unity Authoring — Testing Guide

This guide walks through everything added in the native-authoring work (Phases 1–4): building Neo UI
the familiar Unity way (GameObject menu + scene view + inspectors), capturing it back to the JSON spec
with one click, and the designer-friendly ScriptableObject seams. Work top to bottom — later sections
build on earlier ones. Each feature has **what it is**, **how to test**, and **expected result**.

> The whole point: the JSON spec stays the source of truth, but you no longer have to touch JSON (or
> the Composer) to author UI. You build with native Unity tools; a one-click **Capture to Spec** folds
> your work back into the spec losslessly.

---

## 0. Prerequisites — the New Project Setup wizard (do this once)

The fastest way to get a project ready: `Tools → Neo UI → Setup → New Project Setup…`.

- **What it is:** a guided window that lets you pick a **starting look** — a prebuilt theme bundle OR
  your **own colors** — and tick **what to include** (Starter Kit / Fonts / Widget Presets / Animation
  Library / Effect Assets), then sets the whole project up in one click. Every step is idempotent
  (create-or-repair), so it's safe to re-run.
- **How to test (bundle look):**
  1. Open `Tools → Neo UI → Setup → New Project Setup…`.
  2. Confirm the **Detected** strip shows which pieces already exist (Settings / Starter Kit / Fonts /
     Presets / Anims) with filled vs. hollow dots.
  3. With the **Theme bundle** tab selected, pick a bundle (e.g. NeonArcade) — its description shows beneath.
  4. Leave Starter Kit / Fonts / Widget Presets / Animation Library ticked; tick Effect Assets for Tier-2.
  5. Click **Set Up Project**.
- **How to test (custom colors):** switch the look toggle to **Custom colors** → pick a Primary/Background/
  Surface/Text (and more under the foldout), optionally tick **Save as reusable Theme Bundle Definition**
  and name it → **Set Up Project**. Expected: the theme reflects your colors (hover/pressed derived
  automatically), and if you saved it, the named bundle now appears in the bundle dropdown and under
  `Assets/Neo UI Themes/`.
- **Animation Library:** after setup with that box ticked, confirm `Assets/Neo UI Framework/Animations/`
  contains presets (Show_FadeIn, Show_SlideInLeft, Hide_*, Button_Press, Loop_Pulse, …) and that a spec's
  `"showAnimation": "SlideInLeft"` resolves without wiring (they auto-discover).
- **Expected:** a summary appears listing what ran (e.g. "Settings + databases, Fonts, Starter Kit,
  Widget presets, Theme bundle 'NeonArcade'"), the assets exist, and the theme reflects the chosen
  bundle. **Open Hub** / **Done** buttons appear, plus a hint to use `GameObject → Neo UI → View`.
  Re-running only repairs/fills gaps (the Detected dots should all be filled the second time).
- **Equivalent manual path** (if you prefer the individual steps): `Create or Repair Settings` →
  `Create or Repair Starter Kit` → `Create or Repair Fonts`, then optionally `Apply Theme Bundle…`.

Then open or create a scene to work in. (A throwaway scene is fine — capture targets a *showcase*, not
the scene file.)

**If anything looks unstyled** (white boxes, no rounded corners), re-run step 2 — the create flow
auto-fills missing tokens, but a totally empty theme can still look flat until the Starter Kit runs.

---

## 1. Native creation — `GameObject → Neo UI → …`

**What it is:** the familiar Unity create menu, now with a Neo UI submenu. Every entry builds a real
widget through the *same* code path the spec generator uses, so a hand-created widget is byte-for-byte
what a generated one would be.

### 1.1 Create a View (the container everything lives in)

- **Test:** `GameObject → Neo UI → View`.
- **Expected:**
  - If the scene has no Canvas, a `Canvas` (+ `CanvasScaler` set to *Scale With Screen Size*,
    1920×1080) and an `EventSystem` (with the **Input System UI Input Module** — *not* the old
    `StandaloneInputModule`) are created automatically.
  - A stretched `UIView` GameObject named `Main_View` appears under the Canvas, selected and pinged in
    the Hierarchy.
  - **Undo (Ctrl+Z) once** removes the whole thing cleanly (view, and the Canvas/EventSystem if they
    were created in the same action).

### 1.2 Create widgets into the View

- **Test:** select the `Main_View`, then create each of these and confirm they drop *inside* the
  selected object:
  - `GameObject → Neo UI → Input → Button` (also Toggle, Switch, Slider, Stepper, Input Field, Dropdown)
  - `GameObject → Neo UI → Layout → Vertical Stack` (also Horizontal Stack, Grid, Panel, Scroll)
  - `GameObject → Neo UI → Display → Text` (also Image, Icon, Shape, Progress)
- **Expected:**
  - The widget appears as a child of the current selection (select a Vertical Stack, create a Button →
    the button lands inside the stack and is laid out by it).
  - It renders themed (rounded, colored), matching what the same widget looks like in a generated showcase.
  - Each create is a single Undo step.
- **Edge cases to try:**
  - Create a widget with **nothing selected** → it parents under the Canvas (creating one if needed).
  - Create a widget with a **non-UI object selected** → same fallback to the Canvas.
  - **Right-click a specific object in the Hierarchy → Neo UI → Button** → it parents into that
    object (not into whatever else was selected).

### 1.3 "More Widgets…" + custom kinds

- **Test:** `GameObject → Neo UI → More Widgets…`.
- **Expected:** a popup lists *every* widget kind grouped by category (Layout / Input / Display / Data /
  Menus), including ones without a dedicated menu entry (tabbar, overlay, safearea, spacer, counter,
  list, settings, cheats). Picking one creates it under the selection.
- **Custom-kind check (optional, advanced):** if your project registers a custom `INeoElementKind` via
  `NeoElementKinds.Register`, it appears here automatically with no extra wiring.

### 1.4 The parity guarantee (why this is safe)

This is enforced by an automated test (`NativeAuthoringRoundTripTests`): for every built-in kind, a
widget built via the menu exports to the **identical** spec as one produced by the generator. You don't
need to test this by hand, but it's why a captured native widget round-trips perfectly.

---

## 2. Capture back to Spec — the scene-view overlay

**What it is:** an Odin-Validator-style overlay in the Scene view. When a Neo `UIView` is selected it
shows a drift dot and one-click actions, including **Capture to Spec**, which folds your hand-built view
into a showcase's JSON spec + baseline.

### 2.1 Find the overlay

- **Test:** open the **Scene** view (not Game). Select the `Main_View` (or any object under it).
- **Expected:** a panel titled **"Neo UI"** appears in the Scene view showing:
  - a colored **status dot** + the view id (e.g. `Main/View`),
  - a line like `no showcase yet · …`,
  - buttons: **Capture to Spec · Validate · Check Drift**, and **+ Add Widget · Assign Showcase**.
- **If you don't see it:** open the Scene view's **Overlays menu** (the `⋮`/`≡` button top-right of the
  Scene view, or press `` ` ``) and ensure **Neo UI** is checked. It only renders content while a
  `UIView` is selected; with nothing selected it shows "Select a Neo UI view to author."

### 2.2 Add a widget / apply a preset from the overlay

- **Add Widget:** with a view (or a container inside it) selected, click **+ Add Widget** and pick a kind.
  Identical to the GameObject menu — the widget drops into the selected element.
- **Apply Preset:** select a widget (e.g. a button) inside the view, click **Apply Preset**, pick a
  `NeoWidgetPreset` (create some in the Design System window first). Expected: the widget is rebuilt under
  that preset — its look changes to the preset's variant/size/shape/text while its place, id and label are
  kept; one Undo reverts it. (If the menu is empty, create a preset in the Design System → Presets tab.)

### 2.3 Capture a brand-new view → new showcase

- **Test:** with your `Main_View` selected, click **Capture to Spec**. Since it isn't part of any
  showcase, click **Assign Showcase → New showcase from this view…** first (or Capture will prompt you).
- **Expected:**
  - A new showcase is created under `Assets/Showcases/<id>/` with a `ShowcaseDefinition` asset, and a
    spec file at `Assets/Showcases/Specs/<id>.json`.
  - After Capture: the spec JSON file contains your view, the status dot turns **green** ("in sync"),
    and the view's `GeneratedMarker` is stamped with the showcase id.
- **Verify:** open `Assets/Showcases/Specs/<id>.json` — it should describe your view and its widgets.

### 2.4 Capture again (idempotency)

- **Test:** without changing anything, click **Capture to Spec** again.
- **Expected:** succeeds, dot stays green, no drift reported.

### 2.5 The drift dot + off-spec refusal (the safety net)

- **Green** = the project matches its spec/baseline. **Yellow** = you made edits that *will* round-trip
  safely. **Red** = you made edits that **cannot** round-trip and would be lost on regenerate.
- **Test the red/refusal path:** select a widget *inside* a button (e.g. its `Label` or `Box` child) and
  change a raw color directly (not via a theme token). Re-select the view → the dot should go **red**.
  Click **Capture to Spec**.
- **Expected:** capture **refuses** with a message naming the off-spec edit and a fix ("bind a theme
  token / fold the change into the spec"). A **Force Capture (accept the loss)** button appears — using
  it captures anyway and records the dropped edit (it doesn't silently lose it).
- **This is the core guarantee:** the overlay never silently destroys edits the spec can't represent.

### 2.6 Validate / Check Drift

- **Validate:** runs hard validation + soft design lint (WCAG contrast, off-scale spacing) on the
  showcase, listing issues inline. Empty list = clean.
- **Check Drift:** forces a fresh drift scan and opens the full **Drift** window for detail.

### 2.7 Re-open and confirm persistence

- **Test:** after a green capture, note the showcase id, then `Tools → Neo UI → Hub`, find your
  showcase, and **Open** it.
- **Expected:** your view generates/opens from the captured spec, unchanged. Round-trip complete.

---

## 3. Designer-extensible ScriptableObjects (drop-an-asset, no C#)

The unifying idea: **dropping an asset is enough** — no manual registration, no code.

### 3.1 Animation presets auto-discover

- **What changed:** you no longer have to add a `UIAnimationPreset` to the
  `AnimationPresetDatabase.presets` list for it to resolve. Dropping the asset is enough.
- **Test:**
  1. `Assets → Create → Neo UI/Animation Preset`. Set **Preset Name** to e.g. `SlideInLeft`, configure
     a move/fade, and save it anywhere under `Assets/`.
  2. In a spec (or a showcase spec), set a view's `"showAnimation": "SlideInLeft"` — **without** adding
     the preset to any database — and generate/regenerate.
- **Expected:** the animation resolves and bakes; **no** "preset not found" warning. (An entry
  explicitly wired into `NeoUISettings.animationPresets` still wins over a discovered one of the same
  name.)
- **Inspector check:** select your `AnimationPresetDatabase` asset → the new inspector explains the
  auto-discovery and shows the explicit-override list.

### 3.2 Theme bundles as an asset — `ThemeBundleDefinition`

- **What changed:** a complete theme personality (token palettes + radius/gradient/shadow/motion) can be
  authored as an asset instead of C#.
- **Test:**
  1. `Assets → Create → Neo UI/Theme Bundle Definition`. Set **Bundle Name** to e.g. `MyBrand`, add a
     **Variant** named `Dark` with a few token colors (e.g. `Primary`, `Surface`, `TextDefault`), and
     set the shape/motion fields.
  2. `Tools → Neo UI → Setup → Apply Theme Bundle` → **MyBrand** should appear in the list. Apply it.
- **Expected:** your bundle appears alongside the built-ins (CleanSlate / NeonArcade / SoftFantasy) and
  applies its palette/personality to the theme. It also resolves from a spec via
  `"theme": { "bundle": "MyBrand" }`.

### 3.3 Polished inspectors

- **Test:** select each of these assets and confirm a sectioned, accent-headed inspector (not Unity's
  raw default):
  - a `ShowcaseDefinition` (Identity / Content / Media sections),
  - an `AnimationPresetDatabase` (auto-discovery note + list),
  - your `Theme` asset (Color variants / Shape styles / Text styles foldout sections).

---

## 4. Design System editor — author your look (`Tools → Neo UI → Design System`)

The ongoing home for your design system, editing the live `NeoUISettings` + `Theme`. Nine tabs, plus a
**Save Assets** button at the bottom. (Edits mark the assets dirty; Save Assets writes them to disk.)
This guide only carries test steps for the five tabs below (the parity-confirmation pass predates
**Overview**, **Typography**, **Icons**, and **Bundles** — see [authoring-guide.md](./authoring-guide.md#design-system-window--author-the-look)
for what those cover; add test steps here if you touch them).

- **Colors:** pick the active variant; edit each token's color; **Add token** / **New variant**;
  **Re-derive hover/pressed states** recomputes Primary/Success/Danger hover+pressed from their base.
  - *Test:* change `Primary`, hit Re-derive → `PrimaryHover`/`PrimaryPressed` shift accordingly.
- **Buttons:** pick or **Add** a variant (e.g. type `success` → Add), set its per-state colors (each
  with a **T** toggle to bind a theme token vs. a raw color), choose the **Content** token. A **real
  rendered sample button** updates as you edit (re-rendered on change), plus N/H/P/D swatches. Edit the
  **Sizes** list below.
  - *Test:* create a `primary`-style variant, watch the preview update; a generated/native button using
    that `variant` should match.
- **Shapes:** pick or add a `ShapeStyle`; set **Corner radius**, **Outline width**, **Softness**, fill
  and outline colors; the preview shows fill + outline (radius/outline values labelled).
- **Presets:** lists `NeoWidgetPreset`s; **Select** pings one (edit it in the Inspector). **New "Primary
  Button" preset** creates a ready preset wired to `variant=primary, size=md`; **Create** makes a named one.
  - *Test:* create a "Primary Button" preset, then reference it from an element (`"preset":"Primary Button"`)
    or via the scene overlay's preset flow.
- **Motion:** pick a default animation preset per animator role (View Show/Hide, Button Hover/Press,
  Toggle On/Off, Loop, OneShot — `NeoUISettings.animatorDefaults`), the same data the Setup wizard's
  Motion Defaults step seeds and an animator's `Reset()`/the factory's hover-and-press feel consume.
  - *Test:* set a Button/Hover default, then create a native button — its hover slot should be seeded
    from that default.

Expected throughout: changes persist after **Save Assets**, and a created-then-generated widget reflects
them (the window edits the same structures the factory reads).

## 5. Known limitations / deferred (so you don't file these as bugs)

- **Design System Shapes preview** is a faux fill/outline swatch (the Buttons tab renders a real sample
  button). A real render for shapes is a possible future polish.
- **`CustomElementKindDefinition`** (authoring a brand-new widget *kind* as an asset) is deferred —
  custom kinds still require implementing `INeoElementKind` in C#.
- **Capture makes a snapshot, not a live prefab link.** After Capture, the scene instance stays a plain
  GameObject (it is not converted into a prefab instance of the generated view). Re-capturing picks up
  your latest scene edits; that's the intended v1 flow.
- **The Composer is gone.** It was retired 2026-07 once this native flow reached parity (`Editor/Composer/`
  removed; surviving pieces rehomed — see CLAUDE.md's "Native-Unity authoring" entry for the full list
  of where each moved). This guide's testing pass was the parity confirmation that green-lit the removal.

---

## 6. Quick test checklist

- [ ] **New Project Setup** wizard: bundle dropdown lists built-ins + dropped definitions; "Set Up
      Project" runs the chosen steps, shows a summary, and is safe to re-run.
- [ ] Setup wizards run; native widgets render themed.
- [ ] `GameObject → Neo UI → View` bootstraps Canvas + EventSystem (New Input System), single Undo.
- [ ] Widgets from Input/Layout/Display submenus drop into the selected container, render themed.
- [ ] Create with nothing / non-UI selected falls back to the Canvas; right-click parents into that object.
- [ ] "More Widgets…" lists every kind; picking one creates it.
- [ ] Scene-view **Neo UI** overlay appears when a `UIView` is selected.
- [ ] **+ Add Widget** from the overlay works.
- [ ] **Apply Preset** from the overlay rebuilds the selected widget under the chosen preset (place/id kept).
- [ ] **Assign Showcase → New showcase…** creates the showcase + spec file.
- [ ] **Capture to Spec** writes the spec JSON, dot goes green; re-capture stays green (idempotent).
- [ ] Raw-color edit below a widget root turns the dot **red**; Capture **refuses** with a fix; **Force**
      captures and records the loss.
- [ ] **Validate** / **Check Drift** report sensibly.
- [ ] Hub → Open the captured showcase reproduces the view.
- [ ] A dropped `UIAnimationPreset` resolves by name without database wiring.
- [ ] A `ThemeBundleDefinition` appears in Apply Theme Bundle and applies.
- [ ] `ShowcaseDefinition` / `AnimationPresetDatabase` / `Theme` show sectioned custom inspectors.
- [ ] Wizard **Custom colors** mode builds a theme from your colors; "Save as bundle" makes it reusable.
- [ ] Wizard **Animation Library** seeds presets under `Assets/Neo UI Framework/Animations/`.
- [ ] **Design System** window: Colors derive states; Buttons variant per-state colors + live preview;
      Shapes radius/outline; Presets create a "Primary Button"; **Save Assets** persists.

---

## Reference — what was added

| Area | Entry point | Code |
|---|---|---|
| Project setup | `Tools → Neo UI → Setup → New Project Setup…` | `Editor/NeoSetupWizard.cs` |
| Design system | `Tools → Neo UI → Design System` | `Editor/NeoDesignSystemWindow.cs` |
| Animation library | `Tools → Neo UI → Setup → Create or Repair Animation Library` | `Editor/AnimationLibraryBootstrap.cs` |
| Native create | `GameObject → Neo UI → …` | `Editor/Authoring/NeoCreateMenu.cs`, `NeoSceneAuthoring.cs` |
| Capture to spec | Scene overlay button | `Editor/Authoring/NeoCapture.cs` |
| Scene overlay | Scene view "Neo UI" | `Editor/Authoring/NeoSceneOverlay.cs` |
| Drift status (shared) | overlay dot + Drift window | `Editor/DriftStatus.cs` |
| Live build seam | (internal) | `UISpecGenerator.BuildElementLive` / `ViewPrefabPath` |
| Animation auto-discovery | drop a `UIAnimationPreset` | `Editor/AnimationPresetRegistry.cs` |
| Theme bundle asset | `Assets → Create → Neo UI/Theme Bundle Definition` | `Editor/ThemeBundleDefinition.cs` |
| Custom inspectors | select the asset | `Editor/Inspectors/AuthoringInspectors.cs`, `ComponentEditors.cs` |

Automated coverage: `NativeAuthoringRoundTripTests`, `NativeCaptureTests`, `Phase4ExtensibilityTests`,
`NativePresetWorkflowTests` (all green; see CLAUDE.md's known pre-existing-failures note for the
current unrelated red in CI — the Composer and its probe tests are gone, not just headless-flaky).
