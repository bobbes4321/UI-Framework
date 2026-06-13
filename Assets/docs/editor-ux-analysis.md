# Editor UX Analysis & Tooling Suite

Deep analysis of every user-editable surface in the Neo UI Framework, what Doozy did well (and badly),
and the system built to support it. Companion to `neo-ui-package-feature-spec.md`.

## 1. What we learned from Doozy's EditorUI

Doozy's editor UX wins came from a small number of repeatable patterns:

- **One accent color per component family** (containers, interactive, animators, …) so you always
  know what you're looking at.
- **A component header** on every inspector: name + context (the id) at a glance.
- **Database-driven category/name dropdowns** everywhere a string id appears — never raw text fields.
- **Conditional fields**: only show what the current enum/toggle state actually uses.
- **Summaries on collapsed items**: a list element reads "Click — 2 listeners", not "Element 0".

Its slowness came from the delivery mechanism, not the patterns: UIToolkit FluidAnimatedContainers
(a ~200ms eased reaction per expand/collapse), sprite-sheet icon animations on hover, an editor
heartbeat ticking `SetDirty`/`RepaintAll` per active reaction, pooled UXML/USS template cloning, and
generated style/color/font databases loaded from ScriptableObjects.

**Decision: keep the patterns, drop the machinery.** The AE suite is flat IMGUI with cached styles:
no animation, no heartbeat, no asset/template loading, no codegen, no reflection scans on selection.
IMGUI also means the same components work in PropertyDrawers (so they apply inside *any* list,
including the flow node inspector) and in the graph window's IMGUIContainer.

## 2. The suite: `Neo.EditorUI` (standalone)

`Assets/Neo UI Framework/Editor/EditorUI/`, own asmdef, **zero references to Neo.UI** — it can be
lifted into any project/package as-is.

| API | Purpose |
| --- | --- |
| `NeoColors` | Skin-aware semantic palette: family accents (Interactive/Containers/Animation/Flow/Theming/Signals/Data), intents (Add/Remove/Warning), chrome (text, separators, row highlight). Plain constants, no load cost. |
| `NeoStyles` | Lazily built, cached GUIStyles (header title/subtitle, section title, popup rows, badges). Built once, ever. |
| `NeoGUI` | Layout blocks: `ComponentHeader` (accent strip + title + subtitle), `SectionScope`, persistent `BeginFoldoutSection`/`EndFoldoutSection` (SessionState), persistent `Tabs`, `Splitter`, `Badge`, `AccentButton`, `DrawProperties` (scriptless + exclusions), rect helpers. |
| `NeoSearchablePopup` | The workhorse dropdown: search field, filtered scroll list, keyboard nav (↑/↓/Enter/Esc), and an inline **"+ Add 'text'"** row — type the new entry in the search box, no modal dialog. Options are gathered only when the popup opens, never per frame. |
| `NeoDropdown` | `StringPopup` (SerializedProperty-bound, survives the popup outliving the IMGUI pass by re-resolving from path) and `ValuePopup` (plain value, for windows/toolbars). |
| `NeoListView` | Cached `ReorderableList` per (SerializedObject, propertyPath) via ConditionalWeakTable — never rebuilt per frame. Elements render through their PropertyDrawers, so dropdown-enhanced types work inside lists automatically. |

Performance rules encoded in the suite (and to follow when extending it):

1. Never create GUIStyles, ReorderableLists, or SerializedObjects per OnGUI pass — cache them.
2. Never poll databases per frame — fetch option lists when a dropdown opens.
3. No animated editor chrome, no editor-tick subscriptions for visuals.
4. Conditional display = draw or don't draw; no transitions.

## 3. Field-by-field findings and what was done

### String ids (the big one)
Every category/name pair now goes through the same searchable, database-backed picker:

| Surface | Before | Now |
| --- | --- | --- |
| `ViewId`/`ButtonId`/`ToggleId`/`SliderId`/`TagId`/`StreamId` (CategoryNameId) | plain `EditorGUI.Popup` + modal "add new" dialog | searchable popup + inline add (`CategoryNameIdDrawer`) |
| `UINode.ViewRef` (show/hide view lists in flow nodes) | raw text fields | view-database dropdowns (`ViewRefDrawer`) — same picker as UIView's id |
| `FlowTrigger.category/name` | raw text fields | dropdowns from the database matching the trigger type (buttons → buttonIds, toggles → toggleIds, views → viewIds, signal → streamIds) |
| `FlowEdge.toNode` | raw text field | dropdown of the graph's executable nodes |
| `FlowGraph.startNode` | raw text field | dropdown of the graph's executable nodes |
| `SignalNode.streamCategory/Name` | raw text fields | stream-database dropdown pair (special-cased in the node inspector) |
| `ThemeColorRef.token`, `ThemeColorTarget.token` | raw text fields | theme-token dropdown + live color swatch |

### Conditional display
- `FlowTrigger`: only the fields the trigger type uses (timer duration for Timer, nothing for Back/None).
- `FlowEdge`: `weight` only shown on RandomNode outputs (the only consumer).
- `UIActionBehaviour`: `signalStream` only when `sendSignal` is on.
- `TweenSettings`: ease enum vs. curve by ease mode; spring/shake fields only for those play modes;
  random-range fields swap in via a compact "~" toggle per timing row; loop delay only when looping.

### Collapsed summaries
- `FlowEdge` → "Next → MainMenu [ButtonClick]"
- `UIActionBehaviour` → "Click — 2 listeners"
- `TweenSettings` → "0.3s OutQuad · PingPong"

### Component inspectors (consistent headers + grouped sections)
All through `NeoUIEditor` base (accent header → fields → ApplyModifiedProperties):
containers (UIContainer/UIView/UIPopup/UITooltip, callbacks in a persistent foldout), interactive
(UIButton/UIToggle/UITab/UISlider with Selectable/Navigation tucked into a foldout over the stock
`SelectableEditor`/`SliderEditor`, UIToggleGroup, UIStepper, UITag), animators (UI + color animators,
Progressor; preview toolbars kept), theming (Theme, ThemeColorTarget), data (NeoUISettings, all
IdDatabases), flow (FlowController with runtime controls, FlowGraph asset).

### Flow graph editor fixes
- **Typing in the name field deselected the node after one letter** — every value change triggered a
  full `Populate()` which destroyed all node views (and the selection). Now: value edits refresh the
  title/port labels in place and rebuild only edges; the name is a *delayed* field whose commit
  propagates the rename to every edge targeting the node and to the graph's start node, with
  unique-name enforcement.
- **Show/Hide views foldouts wouldn't open** — a fresh `SerializedObject` was created every IMGUI
  pass, resetting `isExpanded` each frame. Now: one cached SerializedObject per graph (disposed and
  recreated on graph change/undo).
- The inspector is *sticky*: it keeps showing the last selected node, so clicking into the inspector
  can never blank it mid-edit.
- Structural changes (output add/remove) and undo/redo rebuild the view but restore selection by
  node name. Dropdown edits applied outside the IMGUI change-check are caught by a per-frame
  signature of the inspected node only.
- FlowController and the FlowGraph asset both got "Open Flow Graph Editor" buttons; the toolbar
  gained "Frame All".

## 4. Future opportunities (not done)

- **Theme editor**: variant/token matrix grid with color swatches per variant; rename-token
  refactoring across ThemeColorRef users.
- **Animation preset picker**: dropdown of `AnimationPresetDatabase` entries on UIAnimation fields
  ("apply preset…"), reusing `NeoSearchablePopup`.
- **UIAnimation channel drawer**: per-channel enable pills (M/R/S/F) on one row, Doozy-style.
- **Popup name dropdown** for `UIPopup.popupName` / `ShowPopupOnClick.popupName` backed by
  `PopupDatabase` (popup database is name→prefab, slightly different shape than IdDatabase).
- **Database health window**: list unused ids, ids referenced but missing from databases.
- **Multi-edit mixed-value support** in `NeoDropdown` (show "—" when values differ).
