# Neo UI Framework — Pre-Development Architecture Audit

**Date:** 2026-07-04 · **Scope:** full repository read (Runtime 153 files, Editor 126, Tests 105, Assets/docs 30) against CLAUDE.md's hard constraints and every design doc's stated claims. All findings were verified by reading actual code; file:line citations are to the current working tree. Load-bearing claims were independently spot-checked.

---

## 1. Executive summary

This codebase is in genuinely good shape to build on — better than its size and growth pattern would suggest. The four hard architectural boundaries all hold cleanly: `Editor/EditorUI` has zero `Neo.UI` references (asmdef `references: []`), Runtime is New-Input-System-only (one correctly-guarded `#elif ENABLE_LEGACY_INPUT_MANAGER` fallback), no GUID addressing exists anywhere cross-object commanding is string-addressed, and every UnityEvent in Runtime is the documented designer-hook surface paired with signals — the newer subsystems (Data, Menus, Effects, Particles) added zero. Even more unusually, the docs are honest: all six extensibility-seam plans' Phase-1 deliverables actually landed, every documented past-bug class (enable-order race, WYSIWYG bake, dead-interaction lint, byte-identical round-trip, sync refusal semantics) is enforced by a STRONG state-asserting test, and the six round-trip test files all assert a true `export→generate→export` fixed point.

The single biggest risk going forward is exactly what the audit brief predicted: **sanctioned copy-paste**. The codebase's growth idiom is a class comment saying "Mirrors X exactly" followed by a hand-maintained copy that then drifts. This has already shipped observable bugs: the preset-governed field list exists in five places and three of them disagree (`padding4` missing from the Composer's override indicator, `icon` wrongly retained by scene-authoring Apply-Preset); the `{token}` scanner exists twice with different trimming and template recursion; `ConstraintWriteback` re-implements the entire constraint math and silently degrades project-registered custom constraints; the runtime breakpoint matcher lost the "empty condition never matches" guard its two editor twins have; and the 18 registries — CLAUDE.md's "consistent seam" — have quietly forked into three lookup conventions, two duplicate policies, two error policies, and one real cache-eviction bug. None of these are individually severe; collectively they mean the (n+1)th feature copies whichever variant its author found first, and the drift compounds.

The second structural risk is the spec pipeline's **two-generation architecture**: a modern, genuinely open modifier layer (effect/particles/animations/layout ride marker components at O(1) cost per field) wrapped around a legacy per-kind core (a 295-line generator switch, a 420-line order-dependent exporter detection chain, and per-kind styling fields costing 14–16 scattered edit sites each). Four hard round-trip breaks currently live in that core, all invisible to the test suite. The recommended pre-feature-work program below is roughly two to three weeks of consolidation, and the registry base abstraction (§5: verdict **yes, build it now**) should come first because several other fixes land on top of it.

---

## 2. Findings

Paths abbreviated: `Runtime/` = `Assets/Neo UI Framework/Runtime/`, `Editor/` = `Assets/Neo UI Framework/Editor/`, `Tests/` = `Assets/Neo UI Framework/Tests/`.

### 2.1 Architecture / consistency violations

**A1 — Spec round-trip: four hard breaks, all untested.** The prime invariant ("export → generate → export byte-identical; spec is the source of truth") is violated by:
- `Editor/Agent/UISpecExporter.cs:666-671` — a button with both `onClickShowView` and `onClickHideView` gets two `ViewCommandOnClick` components at generate (`UISpecGenerator.cs:754-755`) but the exporter does one `GetComponent` with an if/else, so one command is silently lost. *Fix:* `GetComponents` loop, emit both.
- `UISpecExporter.cs:713` — the accepted kind alias `"scroll"` (`UISpecGenerator.cs:843-844`) always exports as `"list"`; an authored `scroll` spec is never byte-stable. *Fix:* stamp the authored kind on the marker tag, or document+normalize on parse instead.
- `UISpecGenerator.cs:1262` / `UISpecExporter.cs:1030` (pointerGlow) and `UISpecGenerator.cs:962` / `UISpecExporter.cs:860-862` (shape `outlineColor`) — theme-token refs are resolved to concrete colors at bake and exported back as hex, silently severing the token link ("tokens stay live" broken for these two fields). *Fix:* persist the token name on the component (both already store a color; add the ref) and export it.
- `UISpec.cs:1042` parses `labelColor` for every kind, but the toggle (`UISpecGenerator.cs:772-781`) and tab (`:788-804`) cases never apply it and their export branches never read it — authored data swallowed with no warning. *Fix:* apply + export, or reject with a validation issue.

**A2 — Nondeterministic export ordering.** `UISpecExporter.cs:55, 68, 76` export views, popups and flows in raw `AssetDatabase.FindAssets` order, while catalogs are explicitly sorted with a comment explaining why (`:40` — "sorted by id so export stays idempotent regardless of asset-scan order"). Worse, flow export `break`s on the first graph found (`:81`): in the documented multi-flow shared root, *which* flow exports is scan-order dependent and the rest are dropped silently. Baseline diffs and `sync` both ride on export stability — this is a latent source of phantom drift. *Fix:* sort all three collections by id/name; export all flow graphs (the spec model already holds a list).

**A3 — No-silent-failure gaps (the invariant is otherwise excellently held — 30+ compliant sites verified).**
- `Runtime/Graphics/Effects/NeoSignalParamBinding.cs:113` — `TrySetLiveParam`'s return is discarded; a misspelled `param` in a spec's effect `bindings` no-ops on every signal forever. The doc comment at `NeoShapeEffect.cs:274-275` even claims otherwise. *Fix:* one-time `LogWarning` on first `false`.
- `Runtime/Containers/UIView.cs:119-124` — `ShowCategory`/`HideCategory` never increment `matched`, so the zero-match warning (`:133-136`) only covers `Show`/`Hide`; a typo'd category black-screens silently — the exact scenario the comment at `:131` says must fail loudly. *Fix:* count category matches too.
- `Runtime/Animation/AnimationPresetDatabase.cs:18-22` — `Get(name)` returns silent null (public runtime API).
- `Editor/Agent/ParticleEffectRegistry.cs:80-87` — `GetForConfig` silent null while its sibling `Get` warns; `Editor/Agent/BreakpointConditions.cs:73-80` — silent `TryGet` despite copying from the warning `LayoutConstraints.Get`.
- `Runtime/Animation/NeoAnimatorRoles.cs:67-68` — `Register` silently drops null AND duplicate ids (see also A6).

**A4 — Editor-performance rule violations.**
- `Editor/NeoDesignSystemWindow.cs:280-281` — `new GUIStyle(EditorStyles.boldLabel)` inside OnGUI (preview-fallback path); a direct breach of the "never create GUIStyles per OnGUI pass" rule.
- `Editor/NeoDesignSystemWindow.cs:526-535, 81, 165, 324` — dropdown option arrays rebuilt every frame via plain `EditorGUILayout.Popup`, while the same file's Motion tab uses the mandated on-open `NeoDropdown.ValuePopup` pattern (`:480-487`). *Fix:* convert the four sites to `NeoDropdown`.
- `Editor/Composer/ComposerCanvas.cs:141, 164-189` — `BuildIndex()` rebuilds the full structural index (one `Node` alloc + concatenated path string per element) on **every IMGUI event**; the tree pane correctly rebuilds only on dirty (`SpecTreeView.cs:110-113`). *Fix:* invalidate on `SpecDocument.Changed`.
- `Editor/Agent/ShapeEffectRegistry.cs:343-355` — `VariantDescriptor.ResolveDefinition` runs `AssetDatabase.FindAssets("t:ShapeEffectDefinition")` + load-and-compare **per call**, invoked per element at generate time. The only per-call project scan found. (Also an A5 seam violation.)
- Minor/rule-tension: `Editor/Flow/FlowGraphWindow.cs:86` polls `EditorApplication.update` for the play-mode node highlight even in edit mode (cheap early-out at `:412-420`); attach on `playModeStateChanged` instead. Registry postprocessors invalidate on **any** `.asset` import (`ShowcaseRegistry.cs:257-262` et al.), so a generate burst rescans all four asset registries repeatedly.

**A5 — The "drop an asset, it's discovered" claim has two holes.** CLAUDE.md's claim is literally true for the four named registries (each has `EnsureDiscovered` + postprocessor), but:
- **Deleted/renamed `ShowcaseDefinition` / `ThemeBundleDefinition` assets are never evicted.** Discovery is fold-only (`ShowcaseRegistry.cs:96-108`, `ThemeBundles.cs:47-56`) and the registered entries are plain classes, not `UnityEngine.Object`s — after deletion the stale entry survives every re-discovery until domain reload, and renaming an id leaves the old entry registered *alongside* the new one. The postprocessors' delete-handling is dead weight for these two. (`NeoWidgetPresets` escapes functionally via Unity fake-null filtering at `:40, :56` — but `All` still exposes destroyed stubs raw at `:32-34`.)
- **`ShapeEffectDefinition` is asset-backed but has no registry at all** — no cache, no postprocessor, per-call scan (see A4). It's a designer-droppable SO exactly like `ThemeBundleDefinition` and should sit behind the same seam.
- `AnimationPresetRegistry` took the opposite trade-off: `_presets.Clear()` on rescan (`AnimationPresetRegistry.cs:99`) handles deletes but appends duplicates with no dedup or warning (`:100-104`) — two presets named "Pulse" both appear in dropdowns and `TryGet` picks nondeterministically by GUID order.

**A6 — Registry copy-paste drift (18 registries, 3 lineages, 10 concrete deviations).** Full lineage evidence in §5. The heavy hitters: `ComposerCatalogKinds.Register` and `ComposerDevicePresets.Register` **throw** `ArgumentException` (`ComposerCatalogKinds.cs:84-85`, `ComposerDevicePresets.cs:104-105, :39`) where every sibling warns-and-ignores — and both document registration from `[InitializeOnLoad]` static ctors, where a throw becomes a `TypeInitializationException` that poisons the registering type for the whole domain. `NeoAnimatorRoles` is first-wins (`:68`) where every other keyed registry replaces so projects can override built-ins. `ThemeBundleRegistry` is the lone `OrdinalIgnoreCase` registry (`ThemeBundles.cs:38, :70`), undocumented. `AnimationPresetRegistry` has no `Register` method at all — the only asset registry where a project can't register an in-memory instance.

**A7 — Runtime breakpoint matcher drift.** `Runtime/Containers/UIResponsiveRoot.cs:39-53` is a deliberate editor-free mirror of `BreakpointConditions.Evaluate`, but it lost the "empty condition never matches" guard both editor versions have (`BreakpointConditions.cs:102`, `SpecPreviewPane.cs:684`) — an all-unset baked condition matches every viewport at runtime. *Fix:* add the guard; add a PlayMode assertion.

**A8 — `UIWidgetFactory.ApplyTextOutline` hardcodes `"Assets/Neo UI Generated/Materials"`** (`UIWidgetFactory.cs:229-234`) instead of deriving from `GeneratedRoot`. Consequence: showcase and test-scratch generates leak outline materials into the committed shared root, and scratch teardown never deletes them. *Fix:* derive from `UISpecGenerator.GeneratedRoot` (or explicitly reference `DefaultGeneratedRoot` if the shared cache is intended — decide, don't repeat the literal three times).

**A9 — `SpecReference` contradicts its own "can't drift" claim.** The doc-comment says the reference reflects live types (`Editor/Agent/SpecReference.cs:13-17`), but `BuildSchema` is a hand-curated property list (`:63-136`) that was never taught the responsive/effects surface: `neo-spec.schema.json` is missing `breakpoints`, `layout`, `overrides`, `padding4`, `effect`, `particles`, `pointerGlow`, `atPointer`, `triggerMode`, and effect `bindings`; top-level `presets` is an untyped array (`:158`), so the color channel is invisible. Regenerating won't fix it — the generator itself is stale. This is also the one unfinished GATE item of `composer-authoring-overhaul/08-orchestration-and-testing.md` §3.4. `spec-reference.md`'s intro (`:5`) likewise omits `breakpoints` from the top-level section list, and complex fields leak raw .NET type names (`` Dictionary`2 ``, `LayoutSpec`) via `FriendlyType` (`SpecReference.cs:320-331`).

### 2.2 DRY / duplication

**D1 — Preset-governed field set: five copies, drift shipped (highest-risk duplication in the codebase).** The "which element fields does a `NeoWidgetPreset` govern" list is enumerated independently in:
1. `Editor/Agent/UISpecGenerator.cs:1141-1185` (`ResolvePresetAndOverrides`, merge at generate),
2. `Editor/Agent/UISpecExporter.cs:437-471` (`ApplyPresetDelta`, delta at export),
3. `Editor/Composer/SpecInspector.cs:252-269` (`PresetOverrides`, override indicator — **omits `padding4`**, which the exporter compares at `UISpecExporter.cs:459`, so the Figma-style indicator never reports a padding4 override),
4. `SpecInspector.cs:315-321` (`CapturePreset`) and `:301-312` (`ResetElementToPreset` — this one **does** include `padding4`, so the Composer's own three lists disagree with each other),
5. `Editor/Authoring/NeoSceneAuthoring.cs:92-96` (`ApplyPreset` encodes the set *by omission*, keeping `kind/id/label/icon` — but `icon` IS preset-governed per lists 1–4, so **an applied preset's icon is always clobbered by the old widget's icon**).
*Fix:* one `PresetFields` descriptor table (`{name, getElement, setElement, getPreset, setPreset, equals}`) next to `NeoWidgetPresets`; all five sites become foreach loops. This simultaneously fixes both shipped bugs, removes the "add a preset field = 5 synchronized edits" tax, and creates the missing extension seam (today a project extending `NeoWidgetPreset` forks four files).

**D2 — Constraint math re-implemented wholesale.** `Editor/Composer/ConstraintWriteback.cs` self-documents (`:11-14`) as mirroring `LayoutConstraints`/`ConstraintLayout` "exactly": `WriteAxis` (`:88-151`) re-encodes the four constraints' `TryDetect` math (`LayoutConstraints.cs:204-329`), `ResolveAxis` (`:162-213`) re-encodes their `Apply` halves, and the offset-key mapping exists **three** times (`ConstraintLayout.ResolveOffset` `UIWidgetFactory.cs:1624-1652`, `ConstraintWriteback`, `ConstraintLayout.WriteOffset`). Extensibility casualty: a project-registered `ILayoutConstraint` — the documented seam, honored by generator and exporter — falls into `ConstraintWriteback`'s generic default branch (`:138-150, :208-211`), so the Composer canvas silently degrades custom constraints. *Fix:* extract a pure per-axis core onto `ILayoutConstraint` (`Resolve`/`Writeback`) and make each constraint own its offset-key mapping; both the RectTransform adapter and device-rect adapter become thin callers. Related: `Editor/EditorUI/NeoConstraintWidget.cs:40-47, 191-199` redeclares the constraint-id list (justified by the asmdef wall) — inject a data-driven mode list from `SpecInspector` so custom constraints can appear there too.

**D3 — Breakpoint-condition matching × 3.** The registry (`BreakpointConditions.Evaluate`, `:100-111`), a hardcoded Composer mirror (`Editor/Composer/SpecPreviewPane.cs:682-693` — the comment admits it; project-registered conditions never match in preview), and the runtime mirror with the A7 drift. *Fix:* the preview one is a drop-in one-line replacement (`BreakpointConditions.Evaluate`, same assembly); the runtime one stays a mirror by design but needs the guard.

**D4 — `{token}` scanner × 2, disagreeing.** `Editor/Agent/BindingManifest.cs:247-277` (public static, recurses `item` templates, `Trim()`s tokens) vs `Editor/Composer/PreviewSampleData.cs:116-147` (private, no `item` recursion, no trim). Concrete symptom: a template label `"{ name }"` produces token `name` in the bindings manifest but a different key in preview sample rows; nested-template tokens appear in the manifest but not the preview. *Fix:* delete the private copy, call the public one — one line.

**D5 — ~9 private element-tree walkers, no shared visitor.** `IdRefSlots.cs:96-129`, `BindingManifest.cs:126-198` + `:255-261`, `SpecPreviewPane.cs:635-646` + `:786-788`, `PreviewSampleData.cs:48-73` + `:124-131`, `ComposerOptions.cs:224-232`, `SpecMigration.cs:60-61`, `ComposerCanvas.cs:186-187`, `SpecTreeView.cs:242-258`. Each re-decides whether bound-list `item` templates count, and about half decide differently — exactly the D4 bug class. *Fix:* one `SpecWalk.Elements(view, includeItemTemplates)` in `Editor/Agent/`, every walker becomes a flat lambda.

**D6 — Color-string parsing × 5.** Canonical hex parser `Runtime/Theming/ColorUtils.TryParseHex` (`ColorUtils.cs:108-114`) is hand-copied in `ThemePaletteEditor.cs:163-167`, `SpecInspector.cs:1000-1008`, `SpecPreviewPane.cs:733/743/766`. The "`#hex`-or-token → `ThemeColorRef`" decode exists twice: `UISpecGenerator.cs:1489-1499` (warns on bad hex) vs `ParticleEffectRegistry.cs:231-239` (**silent** — an A3-class inconsistency). The token→Color *resolution* chain itself is confirmed single-sourced (`Theme.TryGetColor` → `ThemeService` → `ThemeColorRef.Resolve`) — this is purely the string-parsing shell.

**D7 — Path/name literals.** The `"Views"/"Popups"/"Flow"/"Presets"` subfolder names are re-concatenated at ~20 sites across 9 files (`UISpecGenerator.cs:273-274, 384, 1819-1820, 1932-1933, 2064`; `UISpecExporter.cs:55, 68, 76`; `GeneratedSceneBuilder.cs` ×6; `OffSpecLint.cs:223`; `ThemeBundles.cs:444`; `AgentBridge.cs:552`; `NeoUIHubWindow.cs:466, 517`; `ShowcaseRunner.cs:58, 68`; `BeautificationAcceptance.cs:148`). The `$"Tab_{name}"` format string is derived by hand in three places (`UIWidgetFactory.cs:831`; `UISpecGenerator.cs:834, 1717`) unlike the proper shared child-name consts (`UIWidgetFactory.cs:66-92`). Popup chrome names `"Title"/"Message"/"Buttons"` are raw literals on both generate and export sides (`UISpecGenerator.cs:1873, 1876`; `UIWidgetFactory.cs:1048-1053`; `UISpecExporter.cs:116-117, 121`) — an element id sanitizing to `Title` is silently skipped as chrome. *Fix:* static folder properties on `UISpecGenerator` + consts in the factory.

**D8 — Generator/exporter hand-mirrors.** `ElementCarriesOwnId` admits it mirrors the exporter's id-bearing branch list (`UISpecGenerator.cs:2066-2075`); the exporter's spacer heuristic (`UISpecExporter.cs:801-804`) implicitly mirrors the generator's inline spacer recipe (`UISpecGenerator.cs:906-917`); switch-vs-toggle/tabbar/radial/stepper detection heuristics (`UISpecExporter.cs:626, 695, 685, 546-552`) re-encode factory recipes. Also: shape/image/spacer/overlay structure is built inline in the **generator** (`:982-1016, 906-917, 938-943`), contradicting the "UIWidgetFactory is the single source of widget structure" rule — it stays consistent only because native authoring routes through `BuildElementLive`. *Fix (incremental):* a factory-owned "widget fingerprint" table both sides consult; move the four inline recipes into the factory as they're next touched.

**D9 — Small duplications.** `AgentBridge.HandleSync` vs `HandleRegenerateShowcase` duplicate ~20 lines of `SyncResult` payload shaping (`AgentBridge.cs:380-407, 604-621`). `GetFloat`/`GetString` param-bag helpers copy-pasted between `ShapeEffectRegistry.cs:139-146` and `ParticleEffectRegistry.cs:111-115` (and `ShapeEffectRegistry` reaches into `ParticleEffectRegistry.ParseColorRef` at `:247-250` — shared code living in the "wrong" registry). Four near-identical `AssetPostprocessor` bodies (`ThemeBundles.cs:92-94` inline vs extracted helpers in the other three). "Flow references missing view" checked three ways (`AgentValidation.cs:655`, `GeneratedSceneBuilder.cs:267`, `PreviewFlowPlayback.cs:95`) — low priority, representations genuinely differ.

**Confirmed genuinely shared (no action, called out for fairness):** spec JSON codec (`UISpec.FromJson/ToJson` + `MiniJson` everywhere — zero hand-rolled parsers found); theme token→Color resolution; `LayoutSpec.MergedWith` cascade (one impl, three callers); anchor-preset map; missing-sprite check; Authoring validation/drift (calls `AgentValidation`/`DriftStatus` directly); `UIResponsiveRoot` applying pre-baked vectors rather than re-resolving; hover/pressed derivation (`ColorUtils.DeriveHover/DerivePressed`).

### 2.3 Extensibility gaps (sealed lists masquerading as — or missing — seams)

**E1 — `AgentBridge` actions: sealed switch + two parallel hand-lists.** `Editor/Agent/AgentBridge.cs:128-148` (action switch), `:116-120` (`mutatesAssets` guard list), `:146` (error string enumerating valid actions). A consuming project cannot add a bridge action without forking, on the package's flagship agent-first surface — while the Composer probe's step kinds already have exactly the right registry (`ComposerProbeActions.Register`). *Fix:* `AgentBridgeActions.Register(id, handler, mutatesAssets)`, built-ins seeded through it; all three lists collapse.

**E2 — Flow nodes: sealed 11-type creation menu.** `Editor/Flow/FlowGraphWindow.cs:616-626` hand-lists every node type; default-output seeding hardcodes type checks (`:640-642`). CLAUDE.md's own extensibility clause names "a flow node" as the canonical thing a project adds. *Fix:* `FlowNodeKinds` registry (id → factory + default-edge policy), seeded with built-ins.

**E3 — Menu kinds: the largest fully-sealed subsystem.** No registry anywhere in the settings/cheats pipeline: `MapKind` (`UISpecGenerator.cs:1657-1670`), `BuildMenuRow` (`:1743-1804`), `UnmapKind` (`UISpecExporter.cs:1274-1287`), `MenuItemSpec.Kinds` + `ValueToTyped` (`UISpec.cs:1635-1638, 1745-1758`), the closed `MenuControlKind` enum (`Runtime/Menus/MenuControlKind.cs:7`), plus the exporter's `is CheatCatalog ? "cheats" : "settings"` hardcode (`UISpecExporter.cs:503, 1232`). This is sanctioned Phase-2 debt (`BindingManifest.cs:229-232` carries the explicit `NeoMenuItemKinds` TODO), but it's the clearest live violation of the no-sealed-lists constitution and the natural companion to extracting the menus module (§2.2/F-list).

**E4 — Composer per-kind seams half-plumbed.** Verified end-to-end: a project CAN add an element kind (parse → build → export → palette) without touching package code — `NeoElementKinds` is genuinely wired (`UISpecGenerator.cs:716-737`, `UISpecExporter.cs:494-496`, `ComposerPalette.cs:139`). But the cross-cutting rules keyed on kind *strings* exclude custom kinds:
- `Editor/Composer/ComposerCanvas.cs:184` sets `container = ContainerKinds.Contains(element.kind)`, bypassing the `IsContainerKind`/`IElementKindContainer` seam that the tree (`SpecTreeView.cs:463`) and window (`NeoComposerWindow.cs:310`) use — **custom container kinds work everywhere except canvas drop-targeting.** One-line bug.
- `UISpecGenerator.IsPlainContainer` (`:1471-1472`) is a sealed 5-kind chain — a custom container never receives card decor (`:1051`) or gradients (`:1062`). Containment is a registry fact for the Composer but a string chain for the generator: same fact, two sources of truth.
- `ComposerCanvas.LayoutKinds`/`FreeParents`/axis detection (`:97, 114-115, 603, 875, 1089`) gate reorder/free-placement with no hook.
- `SpecInspector.DrawField` dispatches on a sealed `FieldKind` enum switch (`SpecInspector.cs:694-763`); `SpecFieldCatalog.RegisterField` exists (`SpecFieldCatalog.cs:196-201`) but a registered field cannot bring a custom drawer, `SectionOf` (`:328-350`) forces project fields into the Behavior section, and `HasOnClick` hardcodes `button|toggle|tab` (`:667`). *Fix:* optional drawer delegate + section on `SpecField`; an `IElementKindInteractive`-style capability alongside `IElementKindContainer`.
- `SpecPreviewPane.ConditionMatches` (`:682-693`) — see D3.

**E5 — Widget-attribute seam inconsistency.** `CreateButton` consults `NeoUISettings.TryGetVariantColors`/`TryGetButtonSize` first (`UIWidgetFactory.cs:494-496, 512-517`) but `CreateTab`'s variant switch is sealed (`:845-867`) — undocumented deviation, not a decision.

**E6 — Registry-level seams blocked by API gaps.** `NeoAnimatorRoles` first-wins means a project can never re-describe a built-in role (`NeoAnimatorRoles.cs:68`); `AnimationPresetRegistry` has no `Register` for in-memory/seed-then-query instances. Both are one-method fixes that fall out of §5.

**E7 — Hub/window soft gaps.** Setup strip + `RepairAll` cover 3 of 6 bootstraps (`NeoUIHubWindow.cs:79-99, 283-286` — Widget Presets, Animation Library, Effect Assets missing — a bootstrap-descriptor registry would make the strip complete and extensible); category order/accents sealed with graceful defaults (`:223-230, 248-263`); `NeoDesignSystemWindow.Tabs` sealed (`:21`); `IdDatabaseManagerWindow.AccentFor` sealed accent switch (`:146-158`); `AgentValidation`'s raw-fontSize lint has a sealed component exclusion list (`:114-116`) with no `ClaimsWired`-style opt-out. `NeoValidationRules` is live in all three buckets (`AgentValidation.cs:59, 198, 498`) but built-in *design* lints bypass it — defensible for Hard (documented at `:55-58`), weak for Design ("ship defaults through the seam").

### 2.4 Dead / vestigial code

Remarkably little — the retirements CLAUDE.md claims actually happened:
- **`DemoSceneBuilder` / `ShowcaseSceneBuilder`: confirmed gone.** No class, no `MenuItem` registrations; only historical comments (`ShowcaseAugment.cs:9`, `GeneratedSceneBuilder.cs:31`). `ONBOARDING.md:435` still documents the retired "Build Scene From Generated UI" menu item — stale doc, not dead code.
- `Editor/Showcases/NeoUIHubWindow.cs:331, 376` — `_selectedId`-driven `selected` computed then discarded (`_ = selected;`). Delete.
- `AgentBridge.cs:288` prints a pre-`Advanced` menu path in a hint string; several docs share the stale path (`DriftWindow.cs:28`, `BindingStubGenerator.cs:41`, `SpecMigration.cs:23` are the current registrations).
- **Strategic limbo, not dead code: Composer vs native authoring.** CLAUDE.md says the 4-file `Editor/Authoring/` overlay "is intended to supersede the Composer once at parity," while the current working tree actively invests in both (new `PresetPickerPopup.cs` + 6 Composer file edits AND both Authoring files). Overlap is mostly mediated through shared seams (`NeoCreateMenu` consumes `ComposerPalette.All`; both build via `BuildElementLive`), but Apply-Preset now exists in two chromes and no parity criteria are written anywhere. Decide and document the criteria before the Composer accretes more one-surface-only features (the new preset picker is exactly that).
- Stale docs (candidates for a one-hour refresh pass): `composer-authoring-overhaul/00-master-plan.md` still reads `Status: planning` and "we are **not** building a parallel scene-first authoring system"; `editor-ux-analysis.md` §4 lists the Design System window and animation preset picker as "not done" (both exist: `NeoDesignSystemWindow.cs:18`, `AnimationPreviewEditor.cs:171`) and line 22 still says "AE suite"; `ui-beautification-plan.md` header says tab panels "remain explicitly deferred" while its own line 145-149 records them done; `widget-presets-plan.md` §6 describes a motion design the code deviated from (code seeds the `loop` channel, `UISpecGenerator.cs:1170-1182`) and lacks a "Status: implemented" header; feature spec §12 still says "Plain UIToolkit inspectors" (reversal documented only in editor-ux-analysis); `native-authoring-testing-guide.md` says "four tabs" (now five); CLAUDE.md doesn't mention the new `presets` showcase seeded at `ShowcaseRegistry.cs:198`.

### 2.5 Test coverage gaps

The headline is positive: **every documented bug class is enforced by a STRONG state-asserting test** (verified by reading the assertions, e.g. enable-order race `RuntimeBehaviourRegressionTests.cs:60-63`, real `button.Click()` flow playthrough `GeneratedFlowPlaythroughTests.cs:205-218`, true export→generate→export fixed points in all six round-trip files, sync refusal + force + both conflict policies `SyncProtocolTests.cs:92-180`, factory-drift auto-rebuild vs human-drift refusal `ShowcaseRunnerTests.cs:103-137`). The overall suite is ~85-90% real behavioral assertions. Gaps:
- **`spacer` element kind: zero test coverage anywhere.** Doubly risky because its export detection is a fragile heuristic (D8).
- **`importSprites` bridge action: zero coverage at any level.**
- **8 of 14 bridge actions have no bridge-level (JSON `HandleRequest`) test** — `merge`, `screenshot`, `preview`, `specReference`, `buildScene`, `regenerateShowcase`, `bindings`, `composerSession` are tested only via underlying APIs; `validate` is shape-only; **`buildScene`'s actual scene construction never executes in any test** (`SceneBuilderScenePathTests.cs` is reflection-only, `:27-40, :71-72`).
- **Every A1/A2 round-trip break is untested** — the byte-identity tests all exercise specs that happen not to hit the broken fields (dual view commands, `scroll`, pointerGlow/outline tokens, toggle/tab `labelColor`, multi-flow export). Each A1 fix should land with its regression test.
- Composer interactive chrome is unguarded headless: `PresetPickerPopup` (new, uncommitted), `BreakpointBar` override editing, the entire `ComposerCanvas` gesture surface, `AlignmentGuides`, `SpecPreviewPane` — all rely solely on the `composerSession` probe, which needs a live focused editor. Model layer is well covered (17 direct Composer test files; `ConstraintWriteback` and undo/redo STRONG).
- Weakest files: `SceneBuilderScenePathTests.cs` (overload-exists reflection checks), `RecipeLibraryTests.cs` (absence-of-issues only — would pass on empty-but-clean views).

### 2.6 Naming / style inconsistencies

- Ensure-method naming: `EnsureDiscovered` (family standard) vs `ComposerTemplates.EnsureBuiltins` (`ComposerTemplates.cs:56`).
- Test-reset naming/visibility: `internal ResetForTests` (standard) vs **public** `NeoElementKinds.ClearForTests` (`NeoElementKinds.cs:148`); five registries have no reset at all (`ThemeBundleRegistry` — whose `Remove` also fails to reset `_discovered`, `ThemeBundles.cs:80-82` — `ComposerDevicePresets`, `ComposerCatalogKinds`, `ComposerTemplates`, `NeoAnimatorRoles`).
- Lookup convention split: `TryGet(out)` (12 registries) vs `Get()`-null-with-warning (4), with two silent outliers (A3).
- Register error policy: warn-and-ignore (15) vs throw (2) vs silent (1).
- Case-folding: `Ordinal` everywhere except `ThemeBundleRegistry` (`OrdinalIgnoreCase`), undocumented.
- Every registry `All` returns the live backing `List<T>` behind `IReadOnlyList` — consistent, but mutable-under-iteration and downcast-able; the base class (§5) should return a guarded view.
- Kind-key micro-special-casing in JSON: `icon`→`name`, text/icon `color` (`UISpec.cs:1158-1160`) — document or normalize.

---

## 3. Top 10 highest-leverage fixes (do these before resuming features)

| # | Fix | Effort | What it unblocks |
|---|-----|--------|------------------|
| 1 | **`PresetFields` descriptor table** replacing the five hand-mirrored preset field lists (D1); fixes the shipped `padding4` indicator and Apply-Preset `icon` bugs in the same stroke | **S–M** | Preset system becomes safely extensible (the working tree is actively building on presets *right now*); adding a preset field drops from 5 synchronized edits to 1 |
| 2 | **Fix the four hard round-trip breaks + deterministic export ordering** (A1, A2), each with a regression test | **M** | Protects the package's core invariant (spec = source of truth); eliminates a class of phantom drift/`sync` refusals that will otherwise surface as trust-destroying heisenbugs |
| 3 | **Build `NeoKeyedRegistry<T>` / `NeoAssetRegistry<TAsset,TEntry>`** and migrate the 15 conforming registries (§5); put `ShapeEffectDefinition` behind it (kills the per-call scan), give `AnimationPresetRegistry` a `Register` + dedup, fix eviction | **M** | Stops the (n+1)th registry reinvention permanently; structurally fixes A5's eviction bug, A4's scan, A6's policy drift, and deletes four postprocessor copies |
| 4 | **Extract the menus module (~530 lines out of the three god-files) and give it the missing `NeoMenuItemKinds` registry** (E3 — the sanctioned Phase-2 TODO at `BindingManifest.cs:229-232`) | **M–L** | Largest single cohesion win on `UISpecGenerator`/`UISpecExporter`/`UISpec`; closes the biggest sealed-list violation; menu rows become a project extension point |
| 5 | **Constraint/breakpoint math consolidation** (D2, D3, A7): per-axis core on `ILayoutConstraint`; `SpecPreviewPane.ConditionMatches` → `BreakpointConditions.Evaluate`; runtime empty-condition guard | **M** | Custom constraints/conditions — already-documented seams — actually work in the Composer; kills the largest math duplication before the Composer/native-authoring convergence builds more on top of it |
| 6 | **No-silent-failure sweep** (A3): warn in `NeoSignalParamBinding`, `UIView` category commands, `AnimationPresetDatabase.Get`, `ParticleEffectRegistry.GetForConfig`, `BreakpointConditions.TryGet`, `NeoAnimatorRoles.Register`; unify the `ParseColorRef` twins (D6) while there | **S** | Restores the project's most safety-critical invariant to 100%; each gap is a support-ticket generator (silent black screens, dead effect bindings) |
| 7 | **Rewrite `SpecReference.BuildSchema` to reflect `ElementSpec`** (or complete the hand list) and regenerate both artifacts (A9) | **S–M** | The agent-facing contract — the package's differentiator — currently lies about the entire responsive + effects surface; also closes the unfinished overhaul GATE |
| 8 | **`AgentBridgeActions` registry** (E1) + dedupe the `SyncResult` shaping (D9) | **S** | Agent-first surface becomes extensible; three parallel hand-lists collapse to one registration |
| 9 | **Shared `SpecWalk.Elements` visitor + delete the private `{token}` scanner** (D4, D5) | **S** | Fixes the manifest/preview token mismatch today; removes the "does `item` count?" bug class from all future walkers |
| 10 | **Composer canvas trio** (E4/A4): `ComposerCanvas.cs:184` container-seam bypass (one line), `BuildIndex` on document-change instead of per-event, `LayoutKinds`/`FreeParents` through the kind-capability seam | **S** | Custom kinds get full Composer parity; removes the canvas's per-frame GC churn |

**Worth doing opportunistically** (not top-10 but cheap): A8 `ApplyTextOutline` root (live scratch-leak); `spacer` + `importSprites` tests and a real `buildScene` execution test (§2.5); NeoDesignSystemWindow's two perf violations (A4); `Tab_{name}`/popup-chrome/folder-literal consts (D7); flow-node registry (E2 — do it with #3's base class in hand); the one-hour stale-docs refresh pass (§2.4).

---

## 4. Registry unification verdict

**Yes — build it now, before the next registry is written.** Three shapes exist, not eighteen; a two-class base covers 15 of 18 cleanly, and the audit found that every deviation among the copies is an *accident of copy-paste vintage*, not a requirement. Building it now is cheaper than after the next three registries appear, and several top-10 fixes (#3, #6, #8, E2's `FlowNodeKinds`, E3's `NeoMenuItemKinds`) want it as their foundation.

**Shape A — keyed code registry** (11 adopters: `HubToolRegistry`, `ShapeEffectRegistry`, `ParticleEffectRegistry`, `LayoutConstraints` [key = Id+Axis], `LayoutSizingModes`, `BreakpointConditions`, `NeoElementKinds`, `ComposerCatalogKinds`, `ComposerDevicePresets`, `ComposerTemplates`, `ComposerPalette`'s registered half):

```csharp
// Neo.UI.Editor
public class NeoKeyedRegistry<T>
{
    public NeoKeyedRegistry(Func<T, string> key,
        StringComparison comparison = StringComparison.Ordinal,
        Func<IEnumerable<T>> builtins = null,   // lazily seeded, builtins-first
        Func<T, bool> validate = null,          // extra guards (HubTool.invoke != null)
        string registryName = null)             // for the shared warn messages
    { ... }

    public IReadOnlyList<T> All { get; }        // guarded snapshot — fixes the live-list leak
    public bool TryGet(string key, out T value);
    public T GetOrWarn(string key);             // the Get-with-warning variant, one shared message
    public void Register(T value);              // warn+ignore invalid; replace-by-key else append
    internal bool Remove(string key);
    internal void ResetForTests();
}

// Shape B — Shape A + asset discovery (4 adopters: ShowcaseRegistry,
// ThemeBundleRegistry, NeoWidgetPresets, AnimationPresetRegistry; +1 new: ShapeEffectDefinition)
public class NeoAssetRegistry<TAsset, TEntry> : NeoKeyedRegistry<TEntry>
    where TAsset : ScriptableObject
{
    // projection: TAsset -> TEntry (identity for NeoWidgetPresets; def.ToShowcase()/ToBundle() otherwise)
    public void InvalidateDiscovery();
    // EnsureDiscovered keeps MANUAL registrations in their own list and REBUILDS the merged
    // view (manual-over-discovered) each generation — evicts deleted/renamed assets (the A5 bug)
    // while preserving Register calls (AnimationPresetRegistry's clear-everything flaw), fixing
    // both existing strategies' failure modes at once. ONE shared AssetPostprocessor invalidates
    // instances whose asset type matches the changed paths — deletes 4 copied postprocessor
    // classes and ends the any-.asset blanket rescan.
}
```

**Exclusions and decisions:** `NeoValidationRules`/`NeoInteractivityProviders` are keyless rule *sets* (run, not looked up) — give them a 10-line `NeoRuleSet<T>` or leave them; `IdDatabaseOptions` is a facade, not a registry — exclude; `NeoAnimatorRoles` lives in the Runtime asmdef, so it needs a Runtime twin of Shape A (or the type moves to Runtime) — and migrating it should flip first-wins → replace-with-warn (a deliberate behavior change: projects gain the override ability every other registry grants). Migration forces three currently-accidental policies to become explicit constructor arguments: `ComposerCatalogKinds`/`ComposerDevicePresets` throw→warn (desirable — a throw in their documented `[InitializeOnLoad]` usage is a domain-poisoning `TypeInitializationException`), `ThemeBundleRegistry`'s lone case-insensitivity (keep it via the `comparison` parameter, but write down why), and `AnimationPresetRegistry`'s duplicate-name handling (silent GUID-order → last-wins with a warning). Migrate incrementally — the public statics stay as thin forwarders, so no caller changes; each converted registry keeps (or gains) its `ResetForTests` and mirror test.

---

*Methodology note: findings were produced by seven parallel full-file audit passes (registries; spec pipeline; large editor files; boundary compliance; cross-surface DRY; test health; docs-vs-code) and the highest-impact claims were independently re-verified against the working tree before inclusion. Confirmations of rules that hold (EditorUI isolation, input, GUID-free addressing, UnityEvents, FlowGraphWindow's three mandates, the shared JSON codec, the documented bug-class tests) are stated explicitly in §2 so this report can serve as a baseline for the next audit.*
