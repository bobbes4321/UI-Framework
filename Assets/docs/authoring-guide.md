# Authoring Guide

A one-page mental model for building UI with Neo UI. Practical, not exhaustive — each surface links
to the deeper doc/code that owns it. Start at the Hub; everything else is one click from there.

## The mental model

| Surface | What it's for |
| --- | --- |
| **Hub** (`Tools → Neo UI → Hub`) | Launch and browse. Every showcase and every tool, one click away. |
| **New Project Setup** (`Tools → Neo UI → Setup → New Project Setup…`) | Seed a fresh project with a look and a starter library. Re-run any time. |
| **Design System window** (`Tools → Neo UI → Design System`) | Author the look, ongoing — the tokens/type/shapes/buttons/presets/motion/bundles a generated or hand-built widget actually reads. |
| **GameObject → Neo UI** + scene overlay + widget inspectors | Build actual screens, natively, in the scene. |
| **Right-click → Create → Neo UI** | Extend the design system with a new asset — no C# required. |
| **Agent Bridge / spec JSON** | The machine-addressable path — same widgets, same data, driven by JSON instead of clicks. |

## Hub — launch and browse

`Tools → Neo UI → Hub` is the front door. The **Showcases** tab is a searchable, categorized gallery
of every demo scene the package ships (and any you add) — thumbnail, Open / Regenerate / Check Drift
per row, plus a setup-status strip (Settings / Starter Kit / Fonts) with a one-click Repair All. The
**Tools** tab is every other window/wizard/menu action in one grid, grouped by category
(`HubToolRegistry` — a project registers its own tool with one call, so this list never goes stale).
If you don't know where something lives, it's here.

## New Project Setup — seed a fresh project

`Tools → Neo UI → Setup → New Project Setup…` is the guided first run: pick a starting look (a theme
bundle, or your own custom colors — one color per intent, hover/pressed derived automatically), tick
what to include (Starter Kit / Fonts / Widget Presets / Animation Library / Effect Assets), and
optionally pick motion defaults per animator role. One click orchestrates the existing idempotent
bootstraps in order.

It's **safe to re-run**: every step is create-or-repair (never destructive), and reopening the wizard
reads the *current* project state back into its fields and shows an installed-✓ indicator per
include-toggle, so it never lies about what's already there. Use it again whenever you want to change
the starting look or top up a library that's fallen behind.

## Design System window — author the look

`Tools → Neo UI → Design System` is where you keep editing the look after setup. Nine tabs, each its
own file under `Editor/DesignSystem/`, registered through `NeoDesignSystemTabs` (a project adds its
own tab via `NeoDesignSystemTabs.Register` without forking the window):

- **Overview** — a dashboard: what your design system currently consists of (token/style/variant/
  preset/motion counts), with jump buttons into the tab that owns each piece and "See it live" links
  to the showcase that demos it.
- **Colors** — browse and edit theme token colors per variant (Dark/Light/…), add tokens/variants,
  re-derive hover/pressed states.
- **Typography** — CRUD over named text styles (font, size, style, spacing, color) with a rendered
  sample line.
- **Buttons** — the five seeded variants (primary/secondary/ghost/danger/success) as editable
  per-state color data, plus named sizes, with a real rendered preview.
- **Shapes** — full `ShapeStyle` fidelity: radius (uniform or per-corner), outline, softness, fill
  mode/gradient, elevation, with a real `NeoShape` render.
- **Icons** — browse/search the resolvable icon set (185 featured Lucide names, plus every other
  Lucide 1.17.0 name and any project-defined `IconMapOverlay` entry), add a glyph or texture-backed
  entry without touching code.
- **Presets** — the `NeoWidgetPreset` library as a thumbnail grid with an embedded editor; create,
  duplicate, delete, or capture the current scene selection as a new preset.
- **Motion** — default animation preset per animator role, plus the full preset library as a
  searchable browser with an embedded channel editor and live preview.
- **Bundles** — apply a complete look (tokens + shapes + type + motion) with a diff preview before it
  overwrites anything, or save the current look as a reusable bundle.

Every edit here flows into both generated (spec) and natively-authored UI — there's one set of
structures, and this window is the shared place to edit them.

## Build screens natively

You don't need to know the spec format to build a screen. `GameObject → Neo UI → …` drops a widget
into the selection through the same build path the spec generator uses, so a hand-created widget is
identical to a generated one. The scene-view overlay (visible whenever a `UIView` is selected) gives
you Capture-to-Spec / Validate / Check-Drift / Add-Widget and the preset workflow (Apply / Create /
Update / Reset) without leaving the Scene view; the same preset actions also live directly on a
widget's component inspector, for when you never noticed the overlay. "Insert Template…" on the same
menu stamps a curated screen scaffold (main menu, HUD, settings, popup, …) into the selection.

When you're happy with a hand-built view, **Capture to Spec** folds it back into its showcase's spec
+ baseline — so native edits and spec-driven regeneration stay reconciled instead of fighting each
other.

## Extend with assets — no C# required

Right-click in the Project window → **Create → Neo UI** → … creates any of the package's extensible
asset types. Each is discovered automatically (a lazy registry + an `AssetPostprocessor` that
invalidates on import) — drop the asset and it shows up everywhere that type is used, no code, no
registration call:

- **Widget Preset** — a named component style (e.g. "Primary Button") an element references via
  `"preset"`.
- **Animation Preset** — a five-channel (Move/Rotate/Scale/Fade/Color) motion preset.
- **Theme Bundle Definition** — a complete token/shape/type/motion look.
- **Layout Template Definition** — a screen scaffold for "Insert Template…".
- **Showcase Definition** — a self-contained demo scene entry for the Hub.

This is the same seam pattern everywhere: if you can imagine "a project adds one of these," there's a
`Register`/discovery path for it rather than a closed list.

## Agents and the spec — the sync workflow

Everything above has a JSON-addressable twin: a `UISpec` describes views/popups/flows/menus by
category+name strings, and `UIWidgetFactory` builds the exact same widgets from it that native
authoring builds by hand. An agent (or a script) drives this through the Agent Bridge
(`Tools → Neo UI → Advanced → Agent Bridge`, or headless via `AgentBridge.RunBatch`) — write a request
JSON, get a result JSON back. The one rule that matters: use the **`sync`** action to push spec
changes into a project, not raw `generate` — `sync` exports the live project, diffs it against the
last-known baseline, refuses (rather than silently discarding) when it would lose off-spec human
edits, three-way merges, then regenerates and rewrites the baseline. `generate` is the raw primitive
for first generation or scratch/test roots only.

Full field-by-field vocabulary, every bridge action, and the JSON schema:
`Tools → Neo UI → Advanced → Agent Bridge` → `{"action":"specReference"}`, which writes
`Assets/docs/spec-reference.md` + `Assets/docs/neo-spec.schema.json`.
