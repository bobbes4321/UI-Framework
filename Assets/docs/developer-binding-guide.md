# Wiring generated UI to your game (Developer Guide)

Neo UI generates menus, HUDs and screens from a JSON spec. This guide covers the three things you
do to make a generated UI *do something*: discover what it exposes, react to widget input, and feed
data into lists. None of it requires editing the generated prefabs — everything stays in your own
C# and in the spec.

> **Mental model.** The spec is the source of truth; the prefab is a disposable materialization of
> it. Your game code talks to the UI through **string-addressed signals and data ids**, never through
> direct object references — so regenerating the UI never breaks your wiring.

---

## 1. Discover what a UI exposes — the binding manifest + stub

Instead of grepping the spec to find which signals fire and which lists need data, generate a
**manifest** (a contract) and a **C# stub** (fill-in-the-blank wiring).

**Editor:** `Tools ▸ Neo UI ▸ Generate Binding Stub`. It writes:

- `Assets/Scripts/Generated/<Flow>UIBindings.g.cs` — the stub (auto-generated; safe to overwrite)
- `Assets/Scripts/Generated/<Flow>.bindings.json` — the manifest (human/agent-readable contract)

**Agent / CLI:** the `bindings` action does the same headlessly:

```json
{ "action": "bindings",
  "spec": "Assets/Mockups/MyGame/ui.json",
  "out":  "Assets/Scripts/Generated/MyGame.bindings.json",
  "stub": "Assets/Scripts/Generated/MyGameUIBindings.g.cs",
  "namespace": "MyGame.UI" }
```

All fields are optional: omit `spec` to derive from the current project, omit `out`/`stub`/`namespace`
for defaults, or pass `"manifestOnly": true` to skip the stub. The manifest JSON is always returned
inline in the result.

### The manifest

```json
{
  "flow": "MyGame",
  "signals":     [ { "category": "Shop", "name": "Buy", "payload": "none", "source": "button Shop/Buy", "domain": true } ],
  "dataSources": [ { "id": "Shop/Deals", "tokens": ["name", "price"], "source": "list in view Shop/Store" } ],
  "settings":    [ { "category": "Audio", "name": "MusicVolume", "kind": "slider", "type": "float", "default": "0.8" } ],
  "cheats":      [ { "category": "Player", "name": "GodMode", "kind": "toggle", "type": "bool" } ],
  "views":       [ { "id": "Shop/Store", "category": "Shop", "name": "Store" } ]
}
```

### The stub

The stub is a `partial class <Flow>UIBindings` with:

- **`const` ids** for every view / signal / data source / setting — greppable and refactor-safe.
- **`Wire()`** — subscribes each domain signal and binds each setting, each calling an empty
  `partial void` hook.
- **`Populate…`** helpers — one per data source, calling `UIData.Set`.
- **`partial void On…`** declarations — the hooks *you* implement.

You implement the hooks in your **own sibling partial** (e.g. `MyGameUIBindings.Handlers.cs`). The
generator never touches that file, so regenerating the stub never overwrites your logic.

```csharp
// MyGameUIBindings.Handlers.cs  (yours — never regenerated)
using UnityEngine;

namespace MyGame.UI
{
    public partial class MyGameUIBindings : MonoBehaviour
    {
        void Start() => Wire();   // call once, in Start (not OnEnable — registries fill in OnEnable)

        partial void OnShopBuy() => Debug.Log("Buy clicked");                       // domain signal
        partial void OnAudioMusicVolumeChanged(float v) => MyAudio.SetMusic(v);     // settings control
    }
}
```

> `Wire()` warns (it never fails silently) if a setting id resolves to no registered control —
> usually a sign you called `Wire()` before the menu's catalog presenter loaded, or the id is wrong.

---

## 2. React to widget input — domain signals

Every interactive widget publishes a **standard** signal on a shared stream
(`UIButton/Behaviour`, `UIToggle/Behaviour`, …). That's the firehose: you'd have to subscribe to it
and branch on the widget id by hand.

Instead, give a widget a **domain signal** in the spec and it publishes its typed value directly to
a stream *you* name — in addition to the standard one. The translation layer disappears.

**Spec** — buttons use `onClick.signal`; toggles, sliders and dropdowns use a top-level `signal`:

```json
{ "button":   { "id": "Shop/Buy", "label": "Buy", "onClick": { "signal": { "category": "Shop", "name": "Buy" } } } }
{ "toggle":   { "id": "Audio/Mute",  "label": "Mute", "signal": { "category": "Audio", "name": "Muted" } } }
{ "slider":   { "id": "Audio/Vol",   "min": 0, "max": 1, "signal": { "category": "Audio", "name": "MusicVolume" } } }
{ "dropdown": { "id": "Gfx/Quality", "options": ["Low","High"], "signal": { "category": "Graphics", "name": "Quality" } } }
```

**C#** — subscribe with the matching payload type:

```csharp
using Neo.UI;

Signals.On("Shop", "Buy", OnBuy);                       // button → no payload
Signals.On<bool>("Audio", "Muted", muted => …);         // toggle → bool
Signals.On<float>("Audio", "MusicVolume", v => …);      // slider → float (committed value)
Signals.On<int>("Graphics", "Quality", index => …);     // dropdown → selected index
```

Payload by widget: button → *none*, toggle/switch → `bool`, slider → `float`, dropdown → `int`.

> **Additive, always.** The standard `…/Behaviour` streams and any flow-graph triggers keep firing
> exactly as before — domain signals are an extra, cleaner channel, never a replacement. Use
> `Signals.Off(...)` to unsubscribe (e.g. in `OnDestroy`).

---

## 3. Feed lists — typed & incremental data

A `list`/`grid` with a `bind` id and an `item` row template renders one row per data row, filling
`{token}` placeholders in the template's text.

```json
{ "list": { "bind": "Shop/Deals", "item": {
    "hstack": { "children": [
      { "text": { "label": "{name}" } },
      { "text": { "label": "${price}" } }
] } } } }
```

### Push rows

The original string API still works:

```csharp
UIData.Set("Shop", "Deals", new[] {
    new Dictionary<string,string> { ["name"]="Sword", ["price"]="100" },
    new Dictionary<string,string> { ["name"]="Shield", ["price"]="80" },
});
```

…but the **typed** API lets you keep your own model type and supply the projection to row tokens
once (no dictionary building at every call site):

```csharp
UIData.Set<Deal>("Shop", "Deals", deals,
    d => new Dictionary<string,string> { ["name"] = d.Name, ["price"] = d.Price.ToString() });
```

The generated `Populate…` helper wraps exactly this:

```csharp
bindings.PopulateShopDeals(deals, d => new Dictionary<string,string> {
    ["name"] = d.Name, ["price"] = d.Price.ToString() });
```

### Patch one row (no full rebuild)

Once you've populated with `Set<T>`, the projection is remembered, so row-level edits re-token only
the affected row instead of rebuilding the whole list:

```csharp
UIData.Update("Shop", "Deals", 0, deals[0]);   // re-render row 0 only
UIData.Add("Shop", "Deals", newDeal);           // append one row
UIData.RemoveAt("Shop", "Deals", 2);            // remove one row
```

Out-of-range indices or a missing projection log a warning rather than failing silently.

---

## End-to-end, in four steps

1. Author or generate a UI spec; give the widgets you care about `signal` ids and the lists `bind` ids.
2. `Tools ▸ Neo UI ▸ Generate Binding Stub` → open `Assets/Scripts/Generated/<Flow>UIBindings.g.cs`.
3. Drop a `<Flow>UIBindings` component on a GameObject in your scene; add a sibling partial that
   `Start()`-calls `Wire()` and implements the `On…` hooks.
4. Call the `Populate…` helpers (or `UIData.Set<T>`) whenever your data changes.

Regenerate the UI as often as you like — your handler partial and your subscriptions are addressed
by string id, so they survive.

## Worked example: the showcase

The committed showcase wires its `GameUI` flow through exactly this path — read the two files end to end:

- **`Assets/Scripts/Generated/GameUIBindings.g.cs`** — the generated stub for the showcase spec
  (`Assets/Showcases/Specs/game-ui.json`). It surfaces the *whole* contract: every view / signal / setting / cheat
  id, a `Wire()` that subscribes `Shop/Buy` and binds ~20 settings and cheats, and ~30 `partial void`
  hooks — even though the example implements only a handful.
- **`Assets/Scripts/GameUIBindings.Handlers.cs`** — the hand-written sibling partial you own. It
  `Start()`-calls `Wire()`, fills the `Shop/Deals` list from a typed `Deal` model, reacts to the
  `Shop/Buy` domain signal, and implements one settings hook (`OnAudioMasterChanged`). The other ~29
  generated hooks stay empty no-ops — that's the point: you pay only for what you wire.

The sibling `ShowcaseDirector` (`Runtime/Demo/ShowcaseDirector.cs`) deliberately takes the *low-level*
road for the things the contract doesn't model (the live HUD simulation) — see the boundaries below.

### Two boundaries the contract does not cover

- **Cheat/settings *buttons* aren't auto-wired.** A button carries no value, so `Wire()` can't bind it
  — the manifest lists it but the stub generates no hook for it. React on the raw cheat stream instead:
  `Signals.On(UserSettingsService.CheatCategory, "Economy/Give1k", …)`. (Valued controls — toggle,
  switch, slider, dropdown, stepper — *are* auto-wired, through `UserSettingsService.Bind`.)
- **Scalar *output* widgets aren't string-addressable.** A `UICounter` / `Progressor` you drive from
  game state (a coin total, a health bar) has no "set this widget by id" command — the contract covers
  inputs and list data, not arbitrary output. Fetch the widget once by a generated **view-id** const
  and drive it directly: `UIView.GetFirstView(category, name).GetComponentInChildren<UICounter>()`.

## Reference

| You want to… | Use |
|---|---|
| List everything a UI exposes | `Tools ▸ Neo UI ▸ Generate Binding Stub` / `{"action":"bindings"}` |
| React to a click/toggle/slider/dropdown | spec `signal` (or button `onClick.signal`) + `Signals.On<T>` |
| Fill a list from your own type | `UIData.Set<T>(cat, name, rows, project)` or the `Populate…` helper |
| Change one row efficiently | `UIData.Update / Add / RemoveAt` |
| Stop listening | `Signals.Off(...)` |
