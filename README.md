# AlterEyes UI Package

A Unity 6 (6000.4.10f1) clean-room rebuild of Doozy UI Manager 4 into a fully fledged,
reusable, **agent-first** UI package.

Everything is addressable by category/name strings (never GUIDs), backed by flat
force-text ScriptableObjects, and driven by signals over serialized UnityEvents — so the
whole UI can be generated, exported, and validated programmatically.

## Repository layout

| Path | Assembly | What lives here |
| --- | --- | --- |
| `Assets/AE UI Package/Runtime` | `AlterEyes.UI` | Containers, interactive widgets, animation/tweens, flow graphs, signals, theming, id/databases, settings, and `Graphics/` (the `AEShape` SDF vector graphic all visuals are built from — one shared material batches everything). |
| `Assets/AE UI Package/Editor` | `AlterEyes.UI.Editor` | Inspectors, property drawers, the flow-graph window, and the agent spec tooling. |
| `Assets/AE UI Package/Editor/EditorUI` | `AlterEyes.EditorUI` | Standalone editor tooling kit (AEGUI, AEColors, AEStyles, dropdowns, list views) — kept dependency-free so it lifts into other projects. |
| `Assets/AE UI Package/Tests` | EditMode + PlayMode | Behaviour-regression and pipeline tests. |
| `Assets/docs` | — | Feature spec, editor-UX analysis, beautification roadmap, generated spec reference. |

## Documentation

- **Feature spec:** `Assets/docs/altereyes-ui-package-feature-spec.md`
- **Editor UX rationale + field catalog:** `Assets/docs/editor-ux-analysis.md`
- **Visual-polish roadmap:** `Assets/docs/ui-beautification-plan.md`
- **Spec reference + JSON schema:** `Assets/docs/spec-reference.md` (regenerate via the agent bridge `specReference` action)

## Getting started

This project uses **Git LFS** for binary assets (textures, fonts, audio). Install it once
before cloning:

```sh
git lfs install
git clone https://github.com/bobbes4321/UI-Framework.git
```

Open the folder in Unity 6 (6000.4.10f1). Then, from the Unity menu:

1. `Tools → AlterEyes UI → Create or Repair Settings` — settings asset + databases.
2. `Tools → AlterEyes UI → Create or Repair Fonts` — Inter + Lucide icon TMP SDF fonts.
3. `Tools → AlterEyes UI → Create or Repair Starter Kit` — themed widget prefab library + Dark/Light palette + type scale.

## Hard constraints

- **Agent-first** — category/name strings only, flat force-text ScriptableObjects, signals over UnityEvents.
- **Editor performance** — no animated inspector chrome, no editor-tick visual subscriptions, no reflection scans on selection, one settings asset max.
- **New Input System only** — never `UnityEngine.Input`.

See `CLAUDE.md` for the full set of build/test workflows and contribution conventions.

## Note on reference material

The Doozy 4 reference source (`Assets/References/Doozy~`) and the raw Color-A-Cube
screenshot dump are **not** tracked in this repository — they are reference-only inputs,
not part of the shipped package.
