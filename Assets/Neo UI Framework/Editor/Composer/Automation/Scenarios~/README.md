# Composer Probe scenarios

Scripted user sessions an agent (or `composerSession` bridge action) replays against the live
Composer window to capture a filmstrip + per-step interaction telemetry. The folder is named
`Scenarios~` (trailing tilde) so Unity does **not** import these as assets — they're plain JSON
inputs to the probe, and double as regression fixtures.

## Run one

Editor open, with the Agent Bridge toggled on (`Tools → Neo UI → Agent Bridge`), write to
`Temp/neo-request.json`:

```json
{ "action": "composerSession",
  "scenarioPath": "Assets/Neo UI Framework/Editor/Composer/Automation/Scenarios~/drag-and-snap.json",
  "out": "Temp/neo-composer-session/drag-and-snap" }
```

Read `Temp/neo-result.json` for the inlined report; the frames land in `<out>/filmstrip/*.png`
and the full record in `<out>/session.json`. A scenario can also be passed inline as a `"scenario"`
object instead of `"scenarioPath"`.

## Scenario shape

```jsonc
{
  "name": "...",
  "open": "new" | "project",        // or "spec": "path.json" to open a spec file
  "width": 1080, "height": 1920,    // optional initial device size (device px)
  "steps": [ { "action": "...", ... } ]
}
```

## Step actions

Injected (synthesized input through the real ComposerCanvas — the "feel" surface being measured):

- `select`  `{ path }`
- `drag`    `{ path, dx, dy }`            — device px, dy is +up
- `resize`  `{ path, handle, dx, dy }`    — handle ∈ tl|tr|bl|br|l|r|t|b
- `nudge`   `{ path, dir, count, shift }` — dir ∈ left|right|up|down

Driven (the same code path the UI control invokes — DragAndDrop / per-frame toolbars can't be
faithfully synthesized):

- `addWidget`    `{ kind, target? }`
- `setDevice`    `{ preset }` or `{ width, height }`
- `resizeDevice` `{ dw, dh }`
- `setBreakpoint``{ name }`
- `undo` / `redo`

Harness: `settle`, `capture` (a labelled beat — the probe captures after every step anyway).

`path` is a SpecPath, e.g. `views/Menu/Main/elements[0]/children[0]`. A brand-new doc starts with
one empty view `Menu/Main`.

Add a step kind without forking the package by registering it on `ComposerProbeActions`.
