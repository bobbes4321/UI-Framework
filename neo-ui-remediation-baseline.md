# Neo UI Remediation — Wave 0 Baseline

Recorded 2026-07-04. Unity editor was closed for this run (verified: no `Temp/UnityLockfile`,
no `Unity.exe` process before starting). Unity install:
`C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor/Unity.exe`.

## Test results

**EditMode**: 579 total, 576 passed, 2 failed, 1 skipped (duration ~63s).
Failures (both pre-documented in plan §0.7 — NOT regressions):
- `Neo.UI.Tests.ComposerProbeTests.RunSession_DrivenScenario_MutatesDocument_AndRecordsEveryStep`
  (headless — needs a live focused editor window)
- `Neo.UI.Tests.WidgetAttributeRegistryTests.OptionSets_SeedExactlyTheBuiltIns` (committed
  "Important" variant asset feeds the live seam)

**PlayMode**: 56 total, 56 passed, 0 failed.

Raw results: `Temp/results-edit.xml`, `Temp/results-play.xml`. Logs: `Temp/editmode-baseline.log`,
`Temp/playmode-baseline.log`.

## Working-tree state at baseline (do NOT revert — pre-existing human WIP)

Retirement decision: **Composer retired per owner decision 2026-07; native authoring
(`Editor/Authoring/`) is the authoring surface.**

`git status` is clean relative to `HEAD` except:
- `Assets/Neo UI Framework/Presets/Surface/Panel.asset` — modified (in-progress radius/design
  tuning, predates this session)
- `Assets/Neo UI Framework/Resources/Databases/ButtonIdDatabase.asset` — modified
- `Assets/Neo UI Framework/Resources/Databases/ViewIdDatabase.asset` — modified
- `Assets/Neo UI Framework/Resources/DefaultTheme.asset` — modified
- `Assets/Neo UI Framework/Starter/Showcase.prefab` — modified
- `Assets/Neo UI Framework/Starter/TabBar.prefab` — modified

Note: during the two baseline batchmode runs, several OTHER preset assets (Card.asset, every
`Presets/Button/*.asset`, `Dropdown/DefaultDropdown.asset`, `Input/TextInput.asset`,
`Tab/*.asset`) transiently showed as modified (a `radius` value shift) after the EditMode run,
then reverted to match `HEAD` again after the PlayMode run — this looks like non-deterministic
serialization-order churn from Unity re-touching these ScriptableObjects on import, not a real
edit. If a subagent sees these flip to "modified" again with only value-order-style diffs and no
task claims them, do not treat it as a regression; leave it and re-check `git diff` for the file
before assuming a task caused it.

`Editor/Composer/PresetPickerPopup.cs` — this file is NOW COMMITTED (in `ffd7404 "Added
PresetPickerPopup for widget presets, test suite for motion presets, and new animation
showcases"`), not new/uncommitted as the plan's Wave-0 §2 anticipated. Task 2.2 still applies:
rehome it from `Editor/Composer/` to `Editor/Authoring/` via `git mv` (a tracked-file move, not a
new-file add).

## Commit graph at baseline

HEAD = `ffd7404` "Added PresetPickerPopup for widget presets, test suite for motion presets, and
new animation showcases" (this commit landed between the plan being written and Wave 0 running —
it already contains what the plan's Wave-0 §2 described as uncommitted new files).

## Plan adjustments carried forward from baseline

1. Wave 2 Task 2.2's file list (`Editor/Composer/PresetPickerPopup.cs` "uncommitted, new") should
   be treated as a tracked-file `git mv`, since it is committed as of baseline.
