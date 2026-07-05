# Neo UI — Spec Reference

> Generated from code by `Tools → Neo UI → Generate Spec Reference` (or bridge `{"action":"specReference"}`). Do not edit by hand — regenerate.

A spec is JSON with optional top-level sections: `theme`, `presets`, `views`, `popups`, `settings`, `cheats`, `breakpoints`, `flow`. Run one through `{"action":"generate","spec":"path.json"}`, round-trip with `{"action":"export"}`, lint with `{"action":"validate"}`.

## Element kinds

`button`, `cheats`, `counter`, `dropdown`, `grid`, `hstack`, `icon`, `image`, `input`, `list`, `overlay`, `panel`, `progress`, `safearea`, `scroll`, `settings`, `shape`, `slider`, `spacer`, `stepper`, `switch`, `tab`, `tabbar`, `text`, `toggle`, `vstack`

`scroll` is a forgiving alias for `list` — accepted on parse, but always normalized to `list` immediately, so an authored `scroll` element always exports back as `list` (byte-stable; there is no separate `scroll` kind to round-trip to).

## Element fields

Per-element JSON keys (an element is `{ "<kind>": { ...fields } }`). Fields apply only to the kinds that use them.

| Field | Type |
|---|---|
| `align` | string |
| `anchor` | string |
| `animations` | { hover, press, selected, disabled, loop } |
| `arcStart` | number |
| `arcSweep` | number |
| `background` | string |
| `badge` | number |
| `bind` | string |
| `cascade` | bool |
| `catalog` | string |
| `cellSize` | number[] |
| `children` | element[] |
| `columns` | int |
| `controls` | string |
| `effect` | { id, params } |
| `fit` | string |
| `flex` | number |
| `fontSize` | number |
| `gradient` | { from, to, angle } |
| `group` | string |
| `icon` | string |
| `id` | string |
| `item` | element |
| `label` | string |
| `labelColor` | string |
| `layout` | { h, v, offset, size, sizing } |
| `max` | number |
| `min` | number |
| `onClickClose` | bool |
| `onClickHideView` | string |
| `onClickPopup` | string |
| `onClickShowView` | string |
| `onClickSignal` | { category, name } |
| `options` | string[] |
| `outlineColor` | string |
| `outlineWidth` | number |
| `overrides` | { [breakpointName]: layout delta } |
| `padding` | number |
| `padding4` | number[] |
| `particles` | { ...scalars, modules[], signal, atPointer } |
| `pointerGlow` | { color, size, softness } |
| `position` | number[] |
| `preset` | string |
| `radius` | number |
| `rotation` | number |
| `shape` | string |
| `signal` | { category, name } |
| `size` | number[] |
| `sizeVariant` | string |
| `spacing` | number |
| `src` | string |
| `step` | number |
| `style` | string |
| `textStyle` | string |
| `thickness` | number |
| `value` | number |
| `variant` | string |

## Anchor presets

`Bottom`, `BottomLeft`, `BottomRight`, `Center`, `Left`, `Right`, `Stretch`, `StretchBottom`, `StretchHorizontal`, `StretchLeft`, `StretchRight`, `StretchTop`, `StretchVertical`, `Top`, `TopLeft`, `TopRight`

## Shapes (kind `shape`)

`RoundedRect`, `Circle`, `Pill`, `Checkmark`, `Chevron`, `Cross`, `Ring`, `Arc`

## Button variants

`danger`, `ghost`, `light`, `primary`, `secondary`

## Button sizes

`lg`, `md`, `sm`

## Theme color tokens (factory)

`Background`, `Danger`, `DangerHover`, `DangerPressed`, `Outline`, `Primary`, `PrimaryHover`, `PrimaryPressed`, `Shadow`, `Success`, `SuccessHover`, `SuccessPressed`, `Surface`, `SurfaceElevated`, `TextDefault`, `TextMuted`, `TextOnPrimary`, `TextStrong`

## Shape styles

`Card`, `Control`, `ControlPill`, `Panel`, `ShadowSoft`

`progress` also accepts `"style": "radial"` — not a `ShapeStyle` asset, it switches the widget to an arc dial (`ShapeProgressTarget`).

## Text styles

`Body`, `ButtonLabel`, `ButtonLabelLarge`, `ButtonLabelSmall`, `Caption`, `Display`, `Heading`, `Title`

## Theme bundles

`CleanSlate`, `NeonArcade`, `SoftFantasy`

## Theme color tokens (current project theme)

`Accent`, `Background`, `Danger`, `DangerHover`, `DangerPressed`, `Error`, `Outline`, `Panel`, `Primary`, `PrimaryHover`, `PrimaryPressed`, `Shadow`, `Success`, `SuccessHover`, `SuccessPressed`, `Surface`, `SurfaceElevated`, `TextDark`, `TextDefault`, `TextMuted`, `TextOnPrimary`, `TextStrong`, `Warning`

## Responsive layout

Top-level `breakpoints` is an ordered list of `{ "name", "when": { ... } }` — the FIRST whose condition matches the viewport wins at runtime. `when` sets exactly one of `maxAspect`, `maxWidth`, `minAspect`, `minWidth`, `orientation` (a project can register more via `BreakpointConditions`). An element's per-breakpoint `overrides` dict keys by breakpoint `name` and cascades a delta `layout` object over the element's base `layout`.

An element's `layout` object takes `h` (left/right/center/leftRight/scale), `v` (top/bottom/center/topBottom/scale), `offset` (per-constraint, keyed by constraint name), `size` (`{w,h}`, ignored on a stretched axis) and `sizing` (`{w,h}` each fixed/hug/fill in a layout-group parent). `padding4` `[l,t,r,b]` wins over uniform `padding` on a container.

## Shape effects & particles

An element's `effect` object: `{ "id", "params": { ... } }`. Built-in ids: `glowPulse`, `sheenSweep`, `gradientCycle`, `variant`, `arcSpinner`, `cornerMorph`, `borderPulse`, `hueShift`, `transformJuice` (ShapeEffectRegistry — a project registers its own). Every descriptor's `params` shares the timeline keys `duration`/`loop`/`pingPong`/`ease`/`restingPhase`; an optional pointer gate `trigger` (`hover`/`press`/`always`) + `triggerMode` (`hold`/`playOnce`); and an optional live `bindings` array `[{ "signal":"Cat/Name", "param":..., "min":..., "max":... }]` (param `"enabled"` toggles the whole effect). The Tier-2 `variant` id additionally takes `definition` (a ShapeEffectDefinition id) and optionally `animate`/`from`/`to` (a material float driven over the same timeline).

An element's `particles` object is a UI particle emitter (scalars + an open `modules` array — built-in module ids: `gravity`, `drag`, `colorOverLife`, `sizeOverLife`). `atPointer: true` bursts at the click point; an optional `signal` adds a burst-on-signal trigger.

An element's `pointerGlow` object (`{ "color", "size", "softness" }`) follows the cursor while the element is hovered.

## Popups

`{ "name": ..., "title": ..., "message": ... }` builds the canonical card (title/message/OK). Add `"elements": [...]` (same vocabulary as views, stacked in the card) for custom content, `"size": [w,h]` for the card size and `"close": true` for an X dismiss button. A button element with `"onClick": { "close": true }` hides the popup. Open one from any button via `"onClick": { "popup": "Name" }`.

## Flow triggers (`on` in a flow edge)

| Trigger | Form |
|---|---|
| Button click | `{ "button": "Cat/Name" }` |
| Signal | `{ "signal": "Cat/Name" }` or `{ "signal": { "category":..., "name":... } }` |
| Toggle on/off | `{ "toggleOn": "Cat/Name" }` / `{ "toggleOff": "Cat/Name" }` |
| View shown/hidden | `{ "viewShown": "Cat/Name" }` / `{ "viewHidden": "Cat/Name" }` |
| Back button | `{ "back": true }` |
| Timer | `{ "timer": seconds }` |

## Icons (185 Lucide names)

Use on `icon` elements (`"name"`) or button/tab `"icon"` slots.

`anchor`, `arrow-down`, `arrow-left`, `arrow-right`, `arrow-up`, `arrow-up-right`, `award`, `axe`, `backpack`, `banknote`, `battery`, `battery-charging`, `battery-low`, `bell`, `bell-off`, `bike`, `bluetooth`, `book`, `book-open`, `bookmark`, `bot`, `brush`, `calendar`, `camera`, `car`, `castle`, `check`, `check-check`, `chevron-down`, `chevron-left`, `chevron-right`, `chevron-up`, `chevrons-down`, `chevrons-left`, `chevrons-right`, `chevrons-up`, `circle-alert`, `circle-check`, `circle-help`, `circle-minus`, `circle-plus`, `circle-user`, `circle-x`, `clipboard`, `clock`, `cloud`, `coins`, `compass`, `copy`, `credit-card`, `crosshair`, `crown`, `diamond`, `dice-5`, `download`, `droplet`, `ellipsis`, `ellipsis-vertical`, `external-link`, `eye`, `eye-off`, `fast-forward`, `file`, `file-text`, `film`, `filter`, `flag`, `flame`, `folder`, `footprints`, `frown`, `gamepad-2`, `gem`, `ghost`, `gift`, `hammer`, `hand`, `heart`, `heart-crack`, `hourglass`, `house`, `image`, `info`, `key`, `layout-grid`, `leaf`, `lightbulb`, `link`, `list`, `lock`, `lock-open`, `log-in`, `log-out`, `mail`, `map`, `map-pin`, `maximize`, `medal`, `menu`, `message-circle`, `message-square`, `mic`, `mic-off`, `minimize`, `minus`, `moon`, `mountain`, `move`, `music`, `octagon-alert`, `package`, `palette`, `paperclip`, `pause`, `pencil`, `phone`, `pin`, `pipette`, `plane`, `play`, `plus`, `pointer`, `power`, `puzzle`, `redo-2`, `refresh-ccw`, `refresh-cw`, `rewind`, `rocket`, `rotate-ccw`, `rotate-cw`, `save`, `scale`, `search`, `send`, `settings`, `settings-2`, `share-2`, `shield`, `shield-alert`, `shield-check`, `shopping-bag`, `shopping-cart`, `skip-back`, `skip-forward`, `skull`, `sliders-horizontal`, `smile`, `snowflake`, `sparkles`, `square`, `star`, `star-half`, `sun`, `sword`, `swords`, `tag`, `target`, `thumbs-down`, `thumbs-up`, `timer`, `trash`, `trash-2`, `triangle-alert`, `trophy`, `undo-2`, `upload`, `user`, `user-plus`, `users`, `video`, `volume`, `volume-1`, `volume-2`, `volume-off`, `volume-x`, `wallet`, `wand-sparkles`, `wifi`, `wifi-off`, `wrench`, `x`, `zap`, `zoom-in`, `zoom-out`
