# Plan 4 — Agent ↔ Human Collaboration Protocol

## Problem

The spec is the source of truth; prefabs are its materialization. The *intended* round-trip back to
agents is:

> human edits prefab → `{"action":"export"}` → that JSON becomes the new canonical spec the agent
> reads next time.

This backbone is correct but has three holes that lose human work:

1. **It's a discipline, not a guarantee.** Nothing forces export-before-generate. An agent handed the
   *stale original* spec regenerates and wipes the human's edits.
2. **Off-spec edits are silently dropped at export** — the human thinks their work was captured; it
   wasn't (see Plan 1).
3. **No merge.** Export is whole-project; if a human edited view A while the agent's new spec changes
   view B, there's no clean combine — someone's regenerate overwrites the other.

This plan turns the agent loop into a *collaborative* loop where human edits provably flow back
without loss. It is the **policy layer** built on Plan 1's merge/diff/lint **machinery**.

## Goal

Establish and enforce a protocol with one invariant:

> **The live, merged spec — not whatever the agent last wrote — is always the canonical input to the
> next generate.**

Concretely: a committed baseline spec, a mandatory export-and-merge step before any destructive
regenerate, and refusal to silently discard non-round-trippable human edits.

## Dependencies

- **Plan 1** provides `SpecDiff`, `OffSpecLint`, `SpecMerge`, and the `diff`/`merge` AgentBridge
  actions. This plan consumes them. If Plan 1 isn't built yet, build its merge engine first.

## Current-state references

- `AgentBridge.HandleRequest` dispatch — `AgentBridge.cs:80-122`; existing `generate`/`export`
  handlers.
- `UISpecGenerator` overwrites prefabs wholesale (`PrefabUtility.SaveAsPrefabAsset` ~`:288`);
  `GeneratedMarker` guard `:281-285`; `GeneratedRoot` `:42-52`.
- `UISpecExporter.ExportProject` — `UISpecExporter.cs:28-78`.
- Headless batch entry — `AgentBridge.RunBatch` (`CLAUDE.md` agent-workflow section).

## Design

### The baseline file

Introduce a committed baseline: `Assets/Neo UI Generated/.neo-baseline.json` (force-text, committed).
It records the exact spec the current generated assets were last produced from. It is written by
`generate` (after a successful generate) and by the Composer's Save and by `merge`. It is the `base`
input to three-way merges and the reference for drift detection.

> Note: `GeneratedRoot` is redirectable for tests (`NeoTestScratchRoot` etc., per `CLAUDE.md`). The
> baseline path must follow `GeneratedRoot`, so test runs write the baseline into scratch, never the
> committed one. Production code must never reassign `GeneratedRoot` (existing rule).

### The protocol (a `sync` macro action)

The core deliverable is a single safe-regenerate action that encodes the whole protocol so neither
agents nor humans have to remember the discipline:

`{"action":"sync","incoming":"new-spec.json"}` — does, in order:

1. **Export** the live project → `current` spec.
2. **Drift + lint** `current` vs `.neo-baseline.json` (Plan 1). Compute `humanChanges` and
   `offSpecWarnings`.
3. **If off-spec edits exist** → by default **refuse** and return them (the human edited something
   that can't round-trip; they must fix it per the lint's `fix` text, or pass
   `"force":true` to proceed and accept the loss — explicitly, never silently).
4. **Three-way merge** `base=.neo-baseline.json`, `ours=current`, `theirs=incoming` (Plan 1's
   `SpecMerge`). Conflicts default to the incoming spec but are returned in `conflicts`.
5. **Generate** from the merged spec.
6. **Write** the merged spec as the new `.neo-baseline.json`.
7. Return `{ ok, applied, conflicts, offSpecWarnings, dropped }`.

So an agent that wants to change the UI calls `sync` with its new spec instead of `generate`.
A human who just wants their editor edits captured calls `sync` with no `incoming` (steps 1-2-6:
export, fold drift into baseline, no regenerate needed). `generate` remains available as the raw,
unsafe primitive for first-time generation and tests.

### Making the unsafe path loud, not forbidden

Keep `generate` working (tests and first-generation need it), but when it runs against an existing
generated tree whose live state has drifted from `.neo-baseline.json`, have it **warn** in the result
(`"warning":"N human edits will be overwritten — use 'sync' to preserve them"`). Don't hard-block —
that would break batch/test flows — but make the loss impossible to miss.

### Agent-facing contract (documentation, not code)

Update `CLAUDE.md`'s agent-workflow section to state the protocol as the standing rule:

- To change generated UI, agents call `sync`, not `generate`.
- `generate` is for first generation, scratch/test roots, and explicit clean rebuilds only.
- After any human editing session, the human (or the Composer Save) leaves `.neo-baseline.json`
  current, so the next agent `sync` already has the human's work in `base`/`ours`.
- The agent reads `conflicts`/`offSpecWarnings` from `sync` and surfaces them rather than steamrolling.

### Human-facing entry points

- `Tools → Neo UI → Sync With Spec…` — file-picker for the incoming spec, runs the `sync` flow, shows
  applied/conflicts/off-spec in the Plan 1 drift window.
- The Composer (Plan 2) Save already updates the baseline; add a "Sync from agent spec…" menu there
  that runs `sync` and reloads `_doc` from the merged result, so a human can pull an agent's changes
  into their open session and see conflicts inline.
- `Tools → Neo UI → Capture My Edits` — the no-`incoming` form: export + fold drift into baseline, so
  a human can checkpoint their manual work as canonical at any time.

### Git as the backstop

The baseline + merged spec are force-text and committed, so every `sync` produces a reviewable diff.
Document the convention: commit after a `sync` so human↔agent handoffs are visible in history and
recoverable. This is the durable record that no work was silently lost.

## New & modified files

| File | Action |
|------|--------|
| `Editor/Agent/AgentBridge.cs` | EDIT — add `sync` action; add drift warning to `generate` |
| `Editor/Agent/SpecBaseline.cs` | NEW — read/write `.neo-baseline.json` under `GeneratedRoot` |
| `Editor/Agent/UISpecGenerator.cs` | EDIT — write baseline after successful generate; emit drift warning |
| `Editor/SyncWindow.cs` (or reuse Plan 1's `DriftWindow`) | EDIT/NEW — `Sync With Spec…`, `Capture My Edits` menus |
| `Editor/Composer/NeoComposerWindow.cs` | EDIT (if Plan 2 built) — "Sync from agent spec…" + baseline update on Save |
| `CLAUDE.md` | EDIT — codify the protocol as the standing agent rule |

## Edge cases

- **First generation (no baseline):** `sync` with no live assets == plain generate + write baseline.
- **Test/scratch roots:** baseline path follows `GeneratedRoot`; scratch runs never touch the
  committed baseline (relies on the existing `GeneratedRoot` redirection — `CLAUDE.md`).
- **Flow-scoped builds:** `GeneratedRoot` is a shared bucket across specs (`CLAUDE.md`). The baseline
  is whole-bucket; merging two specs that target the same bucket is exactly what three-way merge
  handles — but document that `sync`'s `incoming` should be the spec for the slice being changed, and
  conflicts across unrelated specs surface rather than silently interleave.
- **`force:true`** must echo the dropped off-spec edits in the result so the loss is recorded, never
  invisible.
- **Conflict storm:** if `conflicts` is large, `sync` still completes (incoming wins by default) but
  the result flags it prominently; the human resolves via the Composer/drift window.

## Testing

- `SyncProtocolTests.cs` (EditMode):
  - human edits exportable layer (in prefab) → `sync` with a stale incoming spec → human edit survives
    in the regenerated assets AND in the new baseline.
  - human edits view A, incoming changes view B → both present after `sync`, no conflict.
  - human and incoming both change the same field → reported in `conflicts`, incoming wins by default.
  - off-spec edit present → `sync` refuses without `force`; with `force` it proceeds and lists the
    dropped edit.
  - no-`incoming` `sync` → baseline updated to current, assets unchanged.
- `BaselineTests.cs` — baseline written after generate; path follows `GeneratedRoot`; scratch runs
  don't touch the committed baseline.
- Reuse Plan 1's `RoundTripSafetyTests` as the integration anchor.

Follow `CLAUDE.md` build rules (Roslyn compile-check while editor open; batch tests only when no
`Temp/UnityLockfile`).

## Acceptance criteria

1. An agent calling `sync` with a stale spec **cannot** wipe a human's exportable-layer edits — they
   are merged in and preserved.
2. Off-spec edits are never silently lost: `sync` refuses (or, with explicit `force`, records them as
   dropped).
3. `.neo-baseline.json` always reflects the spec the committed assets were last generated from, and
   is updated by generate / Composer Save / sync.
4. Conflicting human+agent edits are surfaced, not steamrolled.
5. `generate` against a drifted tree emits a visible warning pointing to `sync`.
6. `CLAUDE.md` documents `sync` as the standing way agents change generated UI.
