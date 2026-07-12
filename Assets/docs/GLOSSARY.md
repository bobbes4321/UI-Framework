# Glossary

One-page definitions for Neo UI's recurring jargon. See [00-START-HERE.md](./00-START-HERE.md) for
the full doc map.

- **Signal** — a category/name-addressed event (`Signals.Send`/`Signals.On`), used instead of
  UnityEvents so specs/flows can address behavior with strings an agent can read and write.
- **Registry / `NeoKeyedRegistry<T>`** — the central extensibility pattern: a keyed lookup seeded
  with built-ins that any project can extend with one `Register(...)` call, no forking. Examples:
  `FlowNodeKinds`, `AgentBridgeActions`, `NeoCommands`, `NeoDesignSystemTabs`.
- **Seam** — a general extension point, not necessarily a registry: a `NeoKeyedRegistry<T>`, a
  `ScriptableObject` discovered by an `AssetPostprocessor`, a `virtual`/`protected` hook, or a
  `partial` class. "Ship defaults through the seam, never around it."
- **`NeoAssetRegistry<TAsset,TEntry>`** — a registry variant that rebuilds its base layer from a
  fresh `AssetDatabase` scan for `TAsset` ScriptableObjects every call (so deleted assets drop out),
  layered under manually `Register`-ed built-ins. Backs `ThemeBundles`, `ShowcaseRegistry`,
  `AnimationPresetRegistry`, `ViewTransitionRegistry`.
- **Spec (`UISpec`)** — the JSON document describing views/popups/flows/menus by category+name
  strings. Source of truth for agent-driven generation; see `spec-reference.md`.
- **Baseline (`.neo-baseline.json`)** — the spec a project's generated assets were last produced
  from, stored in `GeneratedRoot`. Rewritten by every successful `generate`, `sync`, or "Fold Edits."
  Diffing against it is how round-trip merges detect human drift.
- **Sync** — the Agent Bridge `sync` action: export live project → diff vs. baseline → refuse if
  off-spec edits exist (unless `force`) → three-way merge → generate → rewrite baseline. The
  standing way to push spec changes without silently discarding hand edits.
- **Off-spec edit** — a human change to generated assets that has no representation in the spec and
  therefore can't survive a merge; `OffSpecLint` flags these before `sync` would drop them.
- **Native authoring** — building UI directly in the Scene view (`GameObject → Neo UI → …` + the
  scene-view overlay) through the same widget-build path the spec generator uses, so hand-built and
  generated widgets are structurally identical. Replaced the retired "Composer" window.
- **Flow graph** — the visual node graph (`FlowGraphWindow`, `UnityEditor.Experimental.GraphView`)
  describing view/popup navigation; nodes are registered via `FlowNodeKinds`.
- **Theme token** — a named color/size/shape value (e.g. `Primary`, `PrimaryHover`) that flows into
  every widget reading the active `Theme`, instead of a hardcoded color.
- **Theme bundle (`ThemeBundleDefinition`)** — a complete token + shape + type + motion look,
  applicable in one step via "Apply Theme Bundle…" (e.g. CleanSlate, NeonArcade, SoftFantasy).
- **Shape style (`ShapeStyle`)** — the radius/outline/softness/fill/gradient/elevation data a
  `NeoShape` renders from; edited in the Design System window's Shapes tab.
- **`NeoShape`** — the SDF-driven vector-graphics component. Every shape/button/panel batches on one
  shared material by packing its parameters into vertex UV channels (see `neoshape-channel-layout.md`)
  instead of using per-instance materials.
- **Widget preset (`NeoWidgetPreset`)** — a named, reusable component style (e.g. "Primary Button")
  an element references via `"preset"` instead of repeating raw style data.
- **Animation preset (`UIAnimationPreset`)** — a reusable five-channel (Move/Rotate/Scale/Fade/Color)
  motion definition, assignable as a default per animator role.
- **Animator role** — a named animation slot an animator plays into: `View/Show`, `View/Hide`,
  `Button/Hover`, `Button/Press`, `Selectable/Normal|Selected|Disabled`, `Toggle/On|Off`, `Loop`,
  `OneShot`. Registered via `NeoAnimatorRoles` (a runtime `NeoRuntimeKeyedRegistry<T>`).
- **Agent Bridge** — the file-based (`Temp/neo-request.json` / `Temp/neo-result.json`) or headless
  (`AgentBridge.RunBatch`) JSON RPC surface agents use to drive the package: generate, export,
  validate, sync, preview, buildScene, regenerateShowcase, bindings, specReference, screenshot,
  importSprites, diff, merge.
- **Showcase (`ShowcaseDefinition`)** — a self-contained demo living under `Assets/Showcases/{id}/`
  with its own isolated `Generated/` root and scene, discovered lazily by `ShowcaseRegistry` and
  listed in the Hub. Every user-visible feature is expected to have one.
- **Binding manifest / binding stub** — the derived contract (`BindingManifest`) and generated
  partial-class stub (`BindingStubGenerator`, via the `bindings` action) that connects a spec's
  signals/data/settings/cheats/views to hand-written game code without regeneration overwriting it.
