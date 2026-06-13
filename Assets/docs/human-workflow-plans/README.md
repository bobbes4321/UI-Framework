# Human Workflow Plans

These four plans rebalance Neo UI from an agent-only loop back toward a workflow where
artists, designers, and developers can iterate on agent-generated UI **without losing work**,
while keeping the agent self-iteration loop intact.

They share one foundational principle, stated once here and assumed by all four:

> **The JSON spec is the single source of truth. The prefab/scene is a disposable
> materialization of it.** Anything that wants to survive must end up in the spec.

The plans are independent enough to be implemented by separate agents, but they have a
dependency order:

| # | File | What it delivers | Depends on |
|---|------|------------------|-----------|
| 1 | [`01-roundtrip-safety.md`](01-roundtrip-safety.md) | The merge engine, drift detection, and off-spec lint that make prefab edits safe to round-trip | — (foundation) |
| 2 | [`02-spec-authoring-window.md`](02-spec-authoring-window.md) | The **Neo UI Composer** — a GUI editor whose document is a `UISpec` (lossless by construction) | optional reuse of 1's diff |
| 3 | [`03-developer-binding-ergonomics.md`](03-developer-binding-ergonomics.md) | Generated binding stubs, domain signals, typed data sources — closes the "menu does nothing" gap | — |
| 4 | [`04-agent-human-collaboration-protocol.md`](04-agent-human-collaboration-protocol.md) | The session protocol + AgentBridge changes that route human edits back to agents without loss | **1** (uses its merge engine) |

**Recommended sequencing:** 1 → 4 (they pair: 1 is the mechanism, 4 is the policy), then 2,
then 3. 2 and 3 can proceed in parallel with the 1+4 pair.

## Key files every plan references

- `Assets/Neo UI Framework/Editor/Agent/UISpec.cs` — the in-memory document model + `FromJson`/`ToJson`.
- `Assets/Neo UI Framework/Editor/Agent/UISpecGenerator.cs` — spec → prefabs/assets (`GeneratedRoot`, `GeneratedMarker`).
- `Assets/Neo UI Framework/Editor/Agent/UISpecExporter.cs` — prefabs/assets → spec.
- `Assets/Neo UI Framework/Editor/Agent/AgentBridge.cs` — the file-based + headless action dispatch.
- `Assets/Neo UI Framework/Editor/Agent/UIWidgetFactory.cs` — single source of widget structure.
- `Assets/Neo UI Framework/Editor/EditorUI/` — the standalone editor kit (NeoGUI, NeoDropdown, NeoListView, etc.).
