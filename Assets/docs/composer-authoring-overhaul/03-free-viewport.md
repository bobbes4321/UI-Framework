# Pillar C — Free, Draggable, Resizable Viewport

> Status: retired 2026-07 — the Composer was removed; native authoring supersedes it; see CLAUDE.md.

[← Master plan](00-master-plan.md) · Prev: [02 — Breakpoints](02-breakpoint-override-system.md) · Next: [04 — Canvas](04-direct-manipulation-canvas.md)

> Solves pain point #1 ("only a fixed amount of aspect ratios"). **Composer/preview-only — touches
> NO core quartet file** → runs fully parallel with Pillar B in Wave 2. The only coupling is the
> `IActiveBreakpoint` hook that B-core ships first.

---

## C.1 What exists today (verified)

- `UISpecPreview.DefaultResolutions` — hardcoded 3-tuple (`UISpecPreview.cs:17-22`).
- `SpecPreviewPane` picks one via `NeoDropdown.ValuePopup` (`SpecPreviewPane.cs:125-130`), defaults
  to `[0]` (`SpecPreviewPane.cs:33`).
- Render is **letterboxed** into the pane preserving aspect (`SpecPreviewPane.cs:108-113`).
- Render path: `SpecPreviewPane.RenderRoot` (`SpecPreviewPane.cs:223-283`) builds a preview scene,
  a **WorldSpace** canvas at `sizeDelta=(w,h)`, an ortho camera, renders to a `RenderTexture`. **No
  `CanvasScaler`** — content is pixel-for-pixel at the chosen resolution, so it does NOT scale like a
  real device. Mirrored by `UIScreenshotter` for the agent `preview` action.
- **Absent:** custom W/H, free drag, rotate, zoom/pan, CanvasScaler.

---

## C.2 Architecture

### C.2.1 Device-preset registry (seam, replaces the hardcoded tuple)

New `Editor/Composer/ComposerDevicePresets.cs`:
```csharp
public readonly struct DevicePreset {
    public readonly string id, label;     // "iphone-15", "iPhone 15"
    public readonly int width, height;    // portrait px
    public readonly float dpiScale;       // optional reference-resolution hint
}
public static class ComposerDevicePresets {
    public static void Register(DevicePreset p);          // replace-by-id (like ComposerCatalogKinds.Register)
    public static IReadOnlyList<DevicePreset> All { get; }
}
```
Built-ins seed the current three plus a useful spread (phone S/M/L portrait+landscape, tablet,
desktop, ultrawide, square) — all **through `Register`**, never a hardcoded switch. `UISpecPreview.
DefaultResolutions` stays for the headless agent `preview`/`screenshot` matrix but is **re-sourced
from the registry** (the registry is the single truth; `DefaultResolutions` becomes a derived view) —
keeps the agent path and the Composer consistent.

### C.2.2 The viewport model in `SpecPreviewPane`

Add a `ViewportState`:
```csharp
class ViewportState {
    public int width, height;          // current device px (the canvas render size)
    public bool freeMode;              // "Responsive": drag handles drive width/height directly
    public string presetId;            // selected preset (null in free mode)
    public bool landscape;             // rotate toggle -> swaps w/h on apply
    public float zoom;                 // 1 = fit; user can pin a zoom
    public bool fitToWindow;           // auto-zoom to pane
}
```

Toolbar (built with EditorUI kit controls, cached styles):
- **Preset dropdown** (`NeoDropdown.ValuePopup` over `ComposerDevicePresets.All` labels).
- **Custom W / H** int fields (selecting them sets `freeMode`/clears preset).
- **Rotate** button (swap w/h, set `landscape`).
- **Zoom**: fit button + zoom field/slider; scroll-wheel over the pane zooms (Pillar D owns
  pan/marquee; coordinate via the shared `_scale` already in `ComposerCanvas`).
- **Dimension readout** while dragging ("412 × 915").
- **Breakpoint indicator**: shows which Pillar B breakpoint the current aspect/orientation activates
  (drives `IActiveBreakpoint`).

### C.2.3 Free drag-resize (DevTools "Responsive" mode)

In `SpecPreviewPane.OnGUI`, when `freeMode`, draw **resize handles on the right edge, bottom edge,
and bottom-right corner** of the letterboxed device rect (NOT on the element — this resizes the
*device*, distinct from `ComposerCanvas` element handles). Dragging updates `width`/`height` (in
device px, accounting for `_scale`), debounced re-render via the existing `RequestRebuild`
(`SpecPreviewPane.cs:70-85`, 0.15s debounce). Live dimension readout follows the drag.

> Coordination with Pillar D: element-resize handles (Pillar D, drawn by `ComposerCanvas`) and
> device-resize handles (Pillar C, drawn by `SpecPreviewPane`) must not fight. Decision: device
> handles live in the **letterbox margin** (outside the rendered device rect), element handles inside
> — spatially disjoint, so no hit-test conflict. Document the handle-zone split in both pillars.

### C.2.4 CanvasScaler-equivalent (scale like a device)

In `RenderRoot` (`SpecPreviewPane.cs:223-283`) and the shared `UIScreenshotter`/`UISpecPreview` path,
add a `CanvasScaler` to the preview canvas:
- `uiScaleMode = ScaleWithScreenSize`, `referenceResolution` from `NeoUISettings` (single settings
  asset, per CLAUDE.md), `screenMatchMode = MatchWidthOrHeight`, `matchWidthOrHeight` from settings.
- Set the canvas/scaler "screen size" to the viewport `width×height` so a 320-wide and 1920-wide
  render show the SAME UI proportionally — i.e. content scales like the shipped game, which is the
  whole point of testing aspect ratios.
- **Shared-path caution:** `UISpecPreview`/`UIScreenshotter` also feed the agent `preview`/
  `screenshot` actions and `BeautificationAcceptance`. Adding a CanvasScaler changes those renders.
  Decision: gate the scaler behind a parameter (`bool deviceScale`) defaulting to the **current**
  (no-scaler) behavior for the agent matrix to keep acceptance renders stable, and `true` for the
  Composer preview. Surfaces as a `RenderOptions` struct threaded through the render call.

### C.2.5 Live viewport resize → re-render → breakpoint

On any `width`/`height`/`landscape` change: `RequestRebuild`; after build, call
`IActiveBreakpoint.Apply(activeBreakpointName)` (Pillar B hook) so the preview shows the override
that a real device of that aspect would. The breakpoint indicator updates from the same computation.

---

## C.3 Workstream C (single workstream, Composer-only)

- **Owns (edit):** `Editor/Composer/SpecPreviewPane.cs` (viewport state, toolbar, free-drag handles,
  CanvasScaler in `RenderRoot`, readout, zoom/fit), `Editor/Agent/UISpecPreview.cs` (re-source
  `DefaultResolutions` from the registry; add `RenderOptions`/`deviceScale` param),
  `Editor/Agent/UIScreenshotter.cs` (thread `RenderOptions` through; default = current behavior).
- **Owns (create):** `Editor/Composer/ComposerDevicePresets.cs`,
  `Tests/EditMode/DevicePresetRegistryTests.cs`.
- **Dependencies:** Pillar A merged (preview renders the new layout model correctly); Pillar B's
  `IActiveBreakpoint` interface defined (consume; if B not yet merged, stub the hook behind a
  null-check and wire after B merges — document the temporary stub).
- **Non-overlap:** `UISpecPreview.cs` and `UIScreenshotter.cs` are in `Editor/Agent/` but are NOT the
  core quartet (`UISpec`/`UISpecGenerator`/`UISpecExporter`/`UIWidgetFactory`). Confirm B-core does
  NOT touch these two files (it doesn't — B touches `UISpec`/generator/exporter/`SpecPath` + new
  runtime). → C and B are file-disjoint, true parallel. **Audit point:** if B-core needs a
  preview change, route it through C.
- **Acceptance:**
  - Pick any device preset; enter custom W/H; toggle rotate (w/h swap); free-drag the device rect to
    arbitrary aspect with a live readout; zoom-to-fit and manual zoom.
  - Content scales like a device (CanvasScaler): the same view at 320×690 and 1280×2760 shows the
    same layout proportionally, not clipped/tiny.
  - Resizing the viewport re-renders and (with B merged) shows the active breakpoint's overrides.
  - Agent `preview`/`screenshot`/`BeautificationAcceptance` renders are byte-stable (no-scaler
    default preserved) — verified by the existing acceptance path.
- **Verify:** Roslyn Editor compile; `DevicePresetRegistryTests` (pure); manual smoke in the open
  editor; confirm acceptance render unchanged when editor is closed (gated).
- **Seam introduced:** `ComposerDevicePresets.Register`.

## C.4 Definition of done for Pillar C
- [ ] Hardcoded resolution list replaced by an extensible registry; defaults flow through it.
- [ ] Device presets + custom W/H + rotate + free-drag resize + zoom/fit + live readout all work.
- [ ] Preview uses a CanvasScaler so content scales like a real device; agent render paths unchanged.
- [ ] Viewport resize re-renders and ties into the active breakpoint.
