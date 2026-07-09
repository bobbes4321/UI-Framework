using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// The native-Unity authoring entry point: create one Neo widget directly into the open scene the way
    /// Unity's own <c>GameObject → UI → …</c> commands do. Every create routes through
    /// <see cref="UISpecGenerator.BuildElementLive"/> — the SAME path generation uses — so a widget made
    /// here is byte-identical to one generated from a spec, which is what lets it round-trip back to JSON
    /// (Phase 2 capture). This is callable static API: the <c>GameObject/Neo UI/…</c> menu (NeoCreateMenu)
    /// and the scene-view overlay's "Add Widget" both call it, so the behaviour lives in exactly one place.
    /// </summary>
    public static class NeoSceneAuthoring
    {
        /// <summary>
        /// Creates a widget of <paramref name="kind"/> under the best parent derived from the current
        /// <see cref="Selection"/> (an existing Neo container/view if one is selected, else a Canvas —
        /// found or bootstrapped). Returns the created root, or null if the kind couldn't be built.
        /// </summary>
        public static GameObject CreateWidget(string kind) =>
            CreateWidget(kind, Selection.activeGameObject);

        /// <summary>
        /// Creates a widget of <paramref name="kind"/> under <paramref name="parentSelection"/> when it is a
        /// suitable UI parent, otherwise under the resolved/created Canvas. Used by the overlay's "Add Widget"
        /// to drop into a specific element.
        /// </summary>
        public static GameObject CreateWidget(string kind, GameObject parentSelection) =>
            CreateWidget(kind, null, parentSelection);

        /// <summary>
        /// Creates a widget of <paramref name="kind"/> styled by a reusable <see cref="NeoWidgetPreset"/>
        /// (<paramref name="presetName"/>, null/empty = a bare kind) under <paramref name="parentSelection"/>.
        /// The preset rides on the element so the factory bakes it + stamps the link (a dragged "Primary
        /// Button" palette tile / "More Widgets" preset entry lands as a real, round-tripping preset link).
        /// </summary>
        public static GameObject CreateWidget(string kind, string presetName, GameObject parentSelection)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            ElementSpec element = SpecFactory.NewElement(kind);
            if (!string.IsNullOrEmpty(presetName))
            {
                // NewElement seeds kind defaults (a button gets variant "primary"), and a SET element
                // field overrides the preset at generate — so without this clear, a "Ghost Button" tile
                // spawned primary. Clear the preset-governed fields (same rule as ApplyPreset) so the
                // PRESET drives the look; identity/content fields (id/label) aren't preset-governed and
                // survive.
                foreach (PresetField field in PresetFields.All) field.clearElement(element);
                element.preset = presetName;
            }
            string label = string.IsNullOrEmpty(presetName) ? Humanize(kind) : presetName;
            return Place(element, kind, parentSelection, $"Create Neo {label}");
        }

        /// <summary>
        /// Creates a widget of <paramref name="kind"/> with <paramref name="presetName"/>'s styling BAKED
        /// into the element and NO preset link — the "disconnected copy" counterpart to
        /// <see cref="CreateWidget(string, string, GameObject)"/> (which keeps the widget following the
        /// preset asset, Doozy's "Link" mode). Use it for a one-off variation that must not restyle when
        /// the preset is later edited; the baked fields export as the element's own values. Falls back to
        /// a bare <paramref name="kind"/> (with a warning, never silently) when the preset can't be
        /// resolved.
        /// </summary>
        public static GameObject CreateWidgetDetached(string kind, string presetName, GameObject parentSelection)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            if (string.IsNullOrEmpty(presetName) || !NeoWidgetPresets.TryGet(presetName, out NeoWidgetPreset preset))
            {
                if (!string.IsNullOrEmpty(presetName))
                    Debug.LogWarning($"Neo UI: preset '{presetName}' not found — creating a bare '{kind}' instead.");
                return CreateWidget(kind, parentSelection);
            }

            ElementSpec element = SpecFactory.NewElement(kind);
            foreach (PresetField field in PresetFields.All)
            {
                // Same clear as the linked path (kind defaults must not shadow the preset), then bake the
                // preset's own values in as plain element fields — visually identical to a linked spawn,
                // but with no `preset` reference for the exporter/generator to resolve later.
                field.clearElement(element);
                object value = field.getPreset(preset);
                if (!PresetFields.IsUnset(value)) field.setElement(element, value);
            }
            return Place(element, kind, parentSelection, $"Create Neo {presetName} (Detached)");
        }

        /// <summary>
        /// Creates an empty stretched <see cref="UIView"/> root — the container hand-built UI lives inside
        /// and the unit Phase 2 capture turns into a spec view. Stamps <see cref="GeneratedMarker.showcaseId"/>
        /// from the active scene's showcase when there is one, so capture can route it home with no prompt.
        /// </summary>
        public static GameObject CreateView()
        {
            NeoUISettings settings = PrepareSettings();
            ResolveParent(Selection.activeGameObject, requireCanvas: true, out RectTransform parent, out _);

            var view = new ViewSpec { category = "Main", viewName = UniqueViewName(parent) };
            var report = new GenerateReport();
            GameObject go = UISpecGenerator.BuildViewGameObject(view, settings, report);
            FinishCreate(go, parent, "Create Neo View", report);
            return go;
        }

        /// <summary>
        /// Instantiates a curated <see cref="NeoLayoutTemplates"/> scaffold's element tree under
        /// <paramref name="parentSelection"/> (or the resolved/created Canvas) — the native-authoring
        /// counterpart to the Composer's spec-level template insert. Every top-level element of every
        /// view/popup the template declares is built through the SAME
        /// <see cref="UISpecGenerator.BuildElementLive"/> path <see cref="CreateWidget(string, string, GameObject)"/>
        /// uses, so the result is byte-identical to a generated one; a template's own title/message/close
        /// popup chrome (it has none of the kinds this seam builds) is out of scope — only its
        /// <c>elements</c> are inserted. All created roots land under one undo step. Returns the first
        /// created root (for selection), or null if the template failed to load/parse or built nothing.
        /// </summary>
        public static GameObject InsertTemplate(TemplateEntry entry, GameObject parentSelection)
        {
            string json;
            try { json = entry.loadJson?.Invoke(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Neo UI: template '{entry.id}' failed to load: {e.Message}");
                return null;
            }
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"Neo UI: template '{entry.id}' produced no JSON; nothing inserted.");
                return null;
            }

            UISpec fragment;
            try { fragment = UISpec.FromJson(json); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Neo UI: template '{entry.id}' is not a valid UISpec: {e.Message}");
                return null;
            }

            NeoUISettings settings = PrepareSettings();
            ResolveParent(parentSelection, requireCanvas: true, out RectTransform parent, out _);

            string undoLabel = $"Insert Template: {entry.label}";
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoLabel);

            var report = new GenerateReport();
            GameObject firstRoot = null;
            foreach (List<ElementSpec> elements in TopLevelElementLists(fragment))
            foreach (ElementSpec element in elements)
            {
                GameObject go = UISpecGenerator.BuildElementLive(element, parent, settings, report);
                if (go == null) continue;
                if (go.transform.parent != parent) go.transform.SetParent(parent, worldPositionStays: false);
                GameObjectUtility.EnsureUniqueNameForSibling(go);
                Undo.RegisterCreatedObjectUndo(go, undoLabel);
                if (firstRoot == null) firstRoot = go;
            }
            Undo.CollapseUndoOperations(undoGroup);

            if (firstRoot == null)
            {
                Debug.LogWarning($"Neo UI: template '{entry.id}' produced no elements to insert.");
                return null;
            }
            Selection.activeGameObject = firstRoot;
            EditorGUIUtility.PingObject(firstRoot);
            if (report.issues.Count > 0)
                Debug.LogWarning($"Neo UI: insert template '{entry.id}' reported — {string.Join("; ", report.issues)}");
            return firstRoot;
        }

        private static IEnumerable<List<ElementSpec>> TopLevelElementLists(UISpec fragment)
        {
            foreach (ViewSpec view in fragment.views)
                if (view?.elements != null) yield return view.elements;
            foreach (PopupSpec popup in fragment.popups)
                if (popup?.elements != null) yield return popup.elements;
        }

        /// <summary>
        /// Re-styles <paramref name="widget"/> to a reusable <see cref="NeoWidgetPreset"/> by rebuilding it
        /// through the generator under that preset. Keeps the widget's identity + content (kind/id/label)
        /// but drops every preset-governed field (incl. icon — see <see cref="PresetFields"/>) so the
        /// PRESET drives the look (otherwise the widget's own old values would override the preset and
        /// nothing would change). Placement and sibling order are preserved; the swap is one undo step.
        /// </summary>
        public static GameObject ApplyPreset(GameObject widget, string presetName)
        {
            if (widget == null || string.IsNullOrEmpty(presetName)) return null;
            ElementSpec current = ExportForPresetWorkflow(widget);
            if (current == null) return null; // already warned

            // Keep identity + content (kind/id/label) but explicitly clear every preset-governed field via
            // the shared PresetFields table so the PRESET's own values win when it's resolved at generate.
            // Routing this through the table (instead of hand-listing kind/id/label/icon, as before) is
            // what fixes the audit's icon-clobber bug: icon is preset-governed, so it's cleared here
            // instead of being carried over from the widget's old value.
            var spec = new ElementSpec { kind = current.kind, id = current.id, label = current.label };
            foreach (PresetField field in PresetFields.All) field.clearElement(spec);
            spec.preset = presetName;
            return RebuildInPlace(widget, spec, "Apply Neo Preset", $"applying preset '{presetName}'");
        }

        /// <summary>
        /// Captures <paramref name="widget"/>'s current styling into a NEW <see cref="NeoWidgetPreset"/>
        /// asset at <paramref name="assetPath"/> — the native counterpart to the (doomed) Composer's
        /// "Create From This…" (<c>SpecInspector.CreatePresetFromElement</c>/<c>CapturePreset</c>) — then
        /// relinks the widget to it via <see cref="ApplyPreset"/> (its overrides become the preset's base).
        /// Returns the created preset, or null (with a warning) if the widget isn't a recognized Neo
        /// widget or <paramref name="assetPath"/> is empty.
        /// </summary>
        public static NeoWidgetPreset CreatePresetFromWidget(GameObject widget, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            ElementSpec current = ExportForPresetWorkflow(widget);
            if (current == null) return null; // already warned

            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            preset.presetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            preset.targetKind = current.kind;
            CapturePresetFields(preset, current);
            AssetDatabase.CreateAsset(preset, assetPath);
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.InvalidateDiscovery();
            PresetThumbnailCache.Invalidate();

            ApplyPreset(widget, preset.presetName);
            return preset;
        }

        /// <summary>
        /// Pushes <paramref name="widget"/>'s current styling into its already-linked preset asset — every
        /// other widget referencing it updates on the next regenerate/rebuild. The native counterpart to
        /// the (doomed) Composer's "Update Preset" (<c>SpecInspector.UpdatePresetFromElement</c>). No-ops
        /// (with a warning) when the widget isn't linked to a preset.
        /// </summary>
        public static bool UpdatePresetFromWidget(GameObject widget)
        {
            ElementSpec current = ExportForPresetWorkflow(widget);
            if (current == null) return false; // already warned
            if (string.IsNullOrEmpty(current.preset) || !NeoWidgetPresets.TryGet(current.preset, out NeoWidgetPreset preset))
            {
                Debug.LogWarning($"Neo UI: '{widget.name}' isn't linked to a preset — nothing to update.");
                return false;
            }
            CapturePresetFields(preset, current);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.InvalidateDiscovery();
            PresetThumbnailCache.Invalidate();
            return true;
        }

        /// <summary>
        /// Clears <paramref name="widget"/>'s preset-governed style overrides so it falls back to its
        /// linked preset's own values — the native counterpart to the (doomed) Composer's "Reset To Preset"
        /// (<c>SpecInspector.ResetElementToPreset</c>). Unlike <see cref="ApplyPreset"/> (which drops every
        /// field back to bare kind/id/label), this only clears the preset-governed fields, so layout,
        /// bindings, and other element-owned data survive the reset. Rebuilds in place, preserving
        /// placement/sibling order. No-ops (with a warning) when the widget isn't linked to a preset.
        /// </summary>
        public static GameObject ResetWidgetToPreset(GameObject widget)
        {
            ElementSpec current = ExportForPresetWorkflow(widget);
            if (current == null) return null; // already warned
            if (string.IsNullOrEmpty(current.preset))
            {
                Debug.LogWarning($"Neo UI: '{widget.name}' isn't linked to a preset — nothing to reset.");
                return null;
            }
            ClearPresetGovernedFields(current);
            return RebuildInPlace(widget, current, "Reset Neo Widget To Preset", $"resetting '{widget.name}' to its preset");
        }

        // ---------------------------------------------------------------- preset workflow helpers

        /// <summary>
        /// Exports <paramref name="widget"/>'s live spec for a preset action (Apply/Create/Update/Reset),
        /// the exact seam <see cref="ApplyPreset"/> always used (<see cref="UISpecExporter.ExportElement"/>).
        /// Warns and returns null when <paramref name="widget"/> isn't parented under a RectTransform (a
        /// root object) or isn't a recognized Neo widget; a null <paramref name="widget"/> returns null
        /// quietly (no selection is not itself a failure).
        /// </summary>
        public static ElementSpec ExportForPresetWorkflow(GameObject widget)
        {
            if (widget == null) return null;
            if (!(widget.transform.parent is RectTransform))
            {
                Debug.LogWarning("Neo UI: can't run a preset action on a root object — select a widget inside a view.");
                return null;
            }
            ElementSpec element = TryExportForPresetWorkflow(widget);
            if (element == null)
                Debug.LogWarning($"Neo UI: '{widget.name}' isn't a recognized Neo widget.");
            return element;
        }

        /// <summary>
        /// Quiet variant of <see cref="ExportForPresetWorkflow"/> for UI status checks (e.g. enabling a
        /// button while the user browses the scene) — same export, but never logs, since a null result
        /// there just means "nothing preset-able selected yet", not a failed user action.
        /// </summary>
        public static ElementSpec TryExportForPresetWorkflow(GameObject widget)
        {
            if (widget == null || !(widget.transform.parent is RectTransform parent)) return null;
            bool inLayout = parent.GetComponent<LayoutGroup>() != null;
            return UISpecExporter.ExportElement(widget, inLayout);
        }

        /// <summary> Copies the preset-governed fields FROM <paramref name="element"/> ONTO <paramref name="preset"/>,
        /// via the shared <see cref="PresetFields"/> table (audit D1) so this always agrees with the
        /// generator's merge and the exporter's delta on which fields a preset owns. </summary>
        private static void CapturePresetFields(NeoWidgetPreset preset, ElementSpec element)
        {
            foreach (PresetField field in PresetFields.All)
                field.setPreset(preset, field.getElement(element));
        }

        /// <summary> Nulls out every preset-governed field on <paramref name="element"/> in place, via the
        /// shared <see cref="PresetFields"/> table (audit D1). </summary>
        private static void ClearPresetGovernedFields(ElementSpec element)
        {
            foreach (PresetField field in PresetFields.All)
                field.clearElement(element);
        }

        /// <summary>
        /// Rebuilds <paramref name="widget"/> from <paramref name="spec"/> in place: same parent, sibling
        /// index, and (outside a layout group) anchors/position/size — the placement-preserving swap
        /// <see cref="ApplyPreset"/>/<see cref="ResetWidgetToPreset"/> share. One undo step.
        /// </summary>
        private static GameObject RebuildInPlace(GameObject widget, ElementSpec spec, string undoLabel, string failureContext)
        {
            var parent = (RectTransform)widget.transform.parent;
            bool inLayout = parent.GetComponent<LayoutGroup>() != null;

            NeoUISettings settings = PrepareSettings();
            var report = new GenerateReport();
            GameObject built = UISpecGenerator.BuildElementLive(spec, parent, settings, report);
            if (built == null)
            {
                Debug.LogWarning($"Neo UI: {failureContext} failed — {string.Join("; ", report.issues)}");
                return null;
            }

            int index = widget.transform.GetSiblingIndex();
            var src = (RectTransform)widget.transform;
            var dst = (RectTransform)built.transform;
            if (!inLayout)
            {
                dst.anchorMin = src.anchorMin; dst.anchorMax = src.anchorMax; dst.pivot = src.pivot;
                dst.anchoredPosition = src.anchoredPosition; dst.sizeDelta = src.sizeDelta;
            }
            dst.SetSiblingIndex(index);
            built.name = widget.name;

            Undo.RegisterCreatedObjectUndo(built, undoLabel);
            Undo.DestroyObjectImmediate(widget);
            Selection.activeGameObject = built;
            EditorGUIUtility.PingObject(built);
            if (report.issues.Count > 0)
                Debug.LogWarning($"Neo UI: {failureContext} — {string.Join("; ", report.issues)}");
            return built;
        }

        // ---------------------------------------------------------------- breakpoint overrides
        //
        // Wave 2 Task 2.3: native parity for the (doomed) Composer's BreakpointBar. Deliberately
        // capture-based, matching the rest of native authoring — there is no per-field breakpoint
        // inspector; the developer drags/resizes the widget directly in the scene view (ordinary
        // RectTransform handles, no special "editing mode") and then captures the result.

        /// <summary>
        /// The spec's top-level breakpoint declarations for <paramref name="showcase"/>, read from its
        /// committed baseline (<see cref="SpecBaseline.Load"/>) — the standing source of truth for
        /// "what breakpoints exist", independent of whether any view in the CURRENT scene happens to
        /// carry a live <see cref="UIResponsiveRoot"/> yet (a view may be the first to ever capture an
        /// override). Empty when the showcase has no baseline yet or declares none.
        /// </summary>
        public static List<BreakpointSpec> GetBreakpoints(Showcase showcase)
        {
            if (showcase == null) return new List<BreakpointSpec>();
            using (NeoWorkspace.Scoped(showcase))
            {
                UISpec baseline = SpecBaseline.Load();
                return baseline != null ? baseline.breakpoints : new List<BreakpointSpec>();
            }
        }

        /// <summary>
        /// Scene-view preview aid (Pillar C, editor-only — no serialization): applies
        /// <paramref name="breakpoint"/>'s already-baked layout to <paramref name="view"/> via the same
        /// runtime driver a live build uses (<see cref="UIResponsiveRoot.SetActiveBreakpoint"/>), or
        /// forces the base when <paramref name="breakpoint"/> is null/empty ("(base)"). No resize
        /// handling is built — this only switches vectors the view already knows about. No-ops (with a
        /// warning) when the view has no baked <see cref="UIResponsiveRoot"/> yet — there is nothing to
        /// preview until at least one element has a captured override.
        /// </summary>
        public static void PreviewBreakpoint(UIView view, string breakpoint)
        {
            if (view == null) return;
            var responsive = view.GetComponent<UIResponsiveRoot>();
            if (responsive == null)
            {
                Debug.LogWarning($"Neo UI: '{view.name}' has no baked breakpoint overrides yet — nothing to preview.");
                return;
            }
            responsive.SetActiveBreakpoint(string.IsNullOrEmpty(breakpoint) ? UIResponsiveRoot.BaseBreakpoint : breakpoint);
        }

        /// <summary>
        /// Captures <paramref name="widget"/>'s CURRENT live layout — however the developer just
        /// dragged/resized it in the scene view — as <paramref name="breakpoint"/>'s override delta
        /// against <paramref name="baseLayout"/> (a snapshot of the widget's layout taken BEFORE the
        /// drag; the overlay caches this on selection since <see cref="ConstraintLayout.Detect"/> always
        /// re-reads the LIVE RectTransform and has no memory of a "before" value on its own).
        /// <para>
        /// The widget is restored to <paramref name="baseLayout"/> immediately after diffing — the scene
        /// must stay WYSIWYG at base — and the delta is baked directly onto the view's
        /// <see cref="UIResponsiveRoot"/> as a real entry/base pair, the SAME shape
        /// <c>UISpecGenerator.BakeResponsiveRoot</c> produces from spec. That is what lets the standing
        /// <see cref="NeoCapture.CaptureView"/> export pick it up through the EXISTING, unmodified
        /// <c>UISpecExporter</c> breakpoint-reconstruction pass (<c>ReconstructOverrides</c> /
        /// <c>FromResponsiveDelta</c>) — this method never patches spec JSON itself.
        /// </para>
        /// Returns null (with a warning) when the widget isn't part of the constraint layout model (no
        /// <see cref="NeoLayoutTag"/> — breakpoints only apply to that model) or nothing round-trippable
        /// changed since selection.
        /// </summary>
        public static SyncResult CaptureLayoutOverride(GameObject widget, UIView view, Showcase showcase,
            string breakpoint, LayoutSpec baseLayout)
        {
            if (widget == null || view == null || showcase == null || string.IsNullOrEmpty(breakpoint)) return null;
            var rect = widget.transform as RectTransform;
            var tag = widget.GetComponent<NeoLayoutTag>();
            if (rect == null || tag == null)
            {
                Debug.LogWarning($"Neo UI: '{widget.name}' isn't placed through the constraint layout model — " +
                                  "breakpoint overrides need 'layout', not the legacy anchor/position fields.");
                return null;
            }

            List<BreakpointSpec> breakpoints = GetBreakpoints(showcase);
            if (!breakpoints.Exists(b => b != null && b.name == breakpoint))
                Debug.LogWarning($"Neo UI: '{breakpoint}' isn't declared in '{showcase.id}'s spec breakpoints — " +
                                  "capturing anyway, but the override will never apply until it is.");

            LayoutSpec current = ConstraintLayout.Detect(rect, tag);
            if (current == null)
            {
                Debug.LogWarning($"Neo UI: '{widget.name}' has no detectable layout to capture.");
                return null;
            }
            LayoutSpec delta = DiffLayout(baseLayout, current);
            if (delta == null || delta.IsEmpty)
            {
                Debug.LogWarning($"Neo UI: '{widget.name}' hasn't changed since it was selected — nothing to " +
                                  $"capture for '{breakpoint}'.");
                return null;
            }

            RectVectors overrideVectors = CaptureVectors(rect);
            var parentLayout = rect.parent != null ? rect.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;

            // Restore the rect to its base state right away — the scene must stay WYSIWYG at base; the
            // breakpoint's own resolved vectors were already captured above.
            if (baseLayout != null && !baseLayout.IsEmpty)
                ConstraintLayout.Apply(rect, baseLayout, parentLayout);
            RectVectors baseVectors = CaptureVectors(rect);

            UIResponsiveRoot responsive = view.GetComponent<UIResponsiveRoot>();
            if (responsive == null) responsive = view.gameObject.AddComponent<UIResponsiveRoot>();
            SyncConditions(responsive, breakpoints);
            UpsertBase(responsive, rect, baseVectors);
            UpsertEntry(responsive, breakpoint, rect, overrideVectors, delta);

            return NeoCapture.CaptureView(view, showcase, force: false);
        }

        private readonly struct RectVectors
        {
            public readonly Vector2 anchorMin, anchorMax, offsetMin, offsetMax, sizeDelta, pivot;
            public RectVectors(RectTransform r)
            {
                anchorMin = r.anchorMin; anchorMax = r.anchorMax;
                offsetMin = r.offsetMin; offsetMax = r.offsetMax;
                sizeDelta = r.sizeDelta; pivot = r.pivot;
            }
        }

        private static RectVectors CaptureVectors(RectTransform rect) => new RectVectors(rect);

        /// <summary> Rebuilds the condition table wholesale from the canonical spec breakpoints — cheap,
        /// and it keeps the table's order/content correct even the first time a view ever bakes one. </summary>
        private static void SyncConditions(UIResponsiveRoot responsive, List<BreakpointSpec> breakpoints)
        {
            responsive.conditions.Clear();
            foreach (BreakpointSpec bp in breakpoints)
            {
                var cond = new UIResponsiveRoot.ResponsiveCondition { name = bp.name };
                if (bp.when != null)
                {
                    cond.orientation = bp.when.orientation ?? string.Empty;
                    cond.minAspect = bp.when.minAspect ?? float.NaN;
                    cond.maxAspect = bp.when.maxAspect ?? float.NaN;
                    cond.minWidth = bp.when.minWidth ?? float.NaN;
                    cond.maxWidth = bp.when.maxWidth ?? float.NaN;
                }
                responsive.conditions.Add(cond);
            }
        }

        private static void UpsertBase(UIResponsiveRoot responsive, RectTransform rect, RectVectors v)
        {
            UIResponsiveRoot.ResponsiveBase existing = responsive.bases.Find(b => b != null && b.target == rect);
            if (existing == null)
            {
                existing = new UIResponsiveRoot.ResponsiveBase { target = rect };
                responsive.bases.Add(existing);
            }
            existing.anchorMin = v.anchorMin; existing.anchorMax = v.anchorMax;
            existing.offsetMin = v.offsetMin; existing.offsetMax = v.offsetMax;
            existing.sizeDelta = v.sizeDelta; existing.pivot = v.pivot;
        }

        private static void UpsertEntry(UIResponsiveRoot responsive, string breakpoint, RectTransform rect,
            RectVectors v, LayoutSpec delta)
        {
            UIResponsiveRoot.ResponsiveEntry existing = responsive.entries.Find(
                e => e != null && e.target == rect && e.breakpoint == breakpoint);
            if (existing == null)
            {
                existing = new UIResponsiveRoot.ResponsiveEntry { breakpoint = breakpoint, target = rect };
                responsive.entries.Add(existing);
            }
            existing.anchorMin = v.anchorMin; existing.anchorMax = v.anchorMax;
            existing.offsetMin = v.offsetMin; existing.offsetMax = v.offsetMax;
            existing.sizeDelta = v.sizeDelta; existing.pivot = v.pivot;
            existing.delta = BuildResponsiveDelta(delta);
        }

        /// <summary> Mirrors the generator's private <c>ToResponsiveDelta</c> mapping so a manually
        /// captured delta bakes into the SAME force-text shape the generator would have produced from
        /// spec — the exporter's existing <c>FromResponsiveDelta</c> reconstructs it back into
        /// <c>overrides[...]</c> unchanged, byte-identically. </summary>
        private static UIResponsiveRoot.ResponsiveDelta BuildResponsiveDelta(LayoutSpec delta)
        {
            var result = new UIResponsiveRoot.ResponsiveDelta
            {
                h = delta.h ?? string.Empty,
                v = delta.v ?? string.Empty,
                sizeW = delta.size != null && delta.size.w.HasValue ? delta.size.w.Value : float.NaN,
                sizeH = delta.size != null && delta.size.h.HasValue ? delta.size.h.Value : float.NaN,
                sizingW = delta.sizing != null ? (delta.sizing.w ?? string.Empty) : string.Empty,
                sizingH = delta.sizing != null ? (delta.sizing.h ?? string.Empty) : string.Empty
            };
            if (delta.offset != null && !delta.offset.IsEmpty)
                foreach (KeyValuePair<string, float> entry in delta.offset.values)
                {
                    result.offsetKeys.Add(entry.Key);
                    result.offsetValues.Add(entry.Value);
                }
            return result;
        }

        /// <summary> Field-by-field inverse of <see cref="LayoutSpec.MergedWith"/>: only fields where
        /// <paramref name="current"/> differs from <paramref name="baseLayout"/> are copied into the
        /// delta (an unset field lets the merge fall back to the base), so a captured override stays a
        /// minimal, human-readable delta rather than a flattened copy of the whole layout. </summary>
        private static LayoutSpec DiffLayout(LayoutSpec baseLayout, LayoutSpec current)
        {
            if (current == null) return null;
            baseLayout = baseLayout ?? new LayoutSpec();
            var delta = new LayoutSpec
            {
                h = !string.Equals(current.h, baseLayout.h, System.StringComparison.Ordinal) ? current.h : null,
                v = !string.Equals(current.v, baseLayout.v, System.StringComparison.Ordinal) ? current.v : null,
                offset = DiffOffset(baseLayout.offset, current.offset),
                size = DiffSize(baseLayout.size, current.size),
                sizing = DiffSizing(baseLayout.sizing, current.sizing)
            };
            return delta.IsEmpty ? null : delta;
        }

        private static LayoutOffset DiffOffset(LayoutOffset baseOffset, LayoutOffset current)
        {
            if (current == null || current.IsEmpty) return null;
            var result = new LayoutOffset();
            foreach (KeyValuePair<string, float> entry in current.values)
            {
                float baseValue = baseOffset != null && baseOffset.TryGet(entry.Key, out float b) ? b : 0f;
                if (!Mathf.Approximately(baseValue, entry.Value)) result.Set(entry.Key, entry.Value);
            }
            return result.IsEmpty ? null : result;
        }

        private static LayoutSize DiffSize(LayoutSize baseSize, LayoutSize current)
        {
            if (current == null) return null;
            var result = new LayoutSize();
            if (current.w.HasValue && !(baseSize != null && baseSize.w.HasValue
                    && Mathf.Approximately(baseSize.w.Value, current.w.Value)))
                result.w = current.w;
            if (current.h.HasValue && !(baseSize != null && baseSize.h.HasValue
                    && Mathf.Approximately(baseSize.h.Value, current.h.Value)))
                result.h = current.h;
            return result.IsEmpty ? null : result;
        }

        private static LayoutSizing DiffSizing(LayoutSizing baseSizing, LayoutSizing current)
        {
            if (current == null) return null;
            var result = new LayoutSizing();
            if (!string.IsNullOrEmpty(current.w) && !(baseSizing != null
                    && string.Equals(baseSizing.w, current.w, System.StringComparison.Ordinal)))
                result.w = current.w;
            if (!string.IsNullOrEmpty(current.h) && !(baseSizing != null
                    && string.Equals(baseSizing.h, current.h, System.StringComparison.Ordinal)))
                result.h = current.h;
            return result.IsEmpty ? null : result;
        }

        // ---------------------------------------------------------------- placement

        private static GameObject Place(ElementSpec element, string kind, GameObject parentSelection, string undoLabel)
        {
            NeoUISettings settings = PrepareSettings();
            ResolveParent(parentSelection, requireCanvas: true, out RectTransform parent, out _);

            var report = new GenerateReport();
            GameObject go = UISpecGenerator.BuildElementLive(element, parent, settings, report);
            if (go == null)
            {
                Debug.LogWarning($"Neo UI: could not create a '{kind}' — {string.Join("; ", report.issues)}");
                return null;
            }
            FinishCreate(go, parent, undoLabel, report);
            return go;
        }

        // BuildElementLive parents through the factory; BuildViewGameObject returns a detached root (in
        // the generate flow it becomes a prefab), so reparent it here. Parenting BEFORE the create-undo
        // means a single Undo removes the whole subtree, parenting and all.
        private static void FinishCreate(GameObject go, RectTransform parent, string undoLabel, GenerateReport report)
        {
            if (go == null) return;
            if (parent != null && go.transform.parent != parent)
                go.transform.SetParent(parent, worldPositionStays: false);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            Undo.RegisterCreatedObjectUndo(go, undoLabel);
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            if (report != null && report.issues.Count > 0)
                Debug.LogWarning($"Neo UI: '{undoLabel}' reported — {string.Join("; ", report.issues)}");
        }

        private static NeoUISettings PrepareSettings()
        {
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            // The factory styles widgets through theme tokens/text styles; fill any gaps so a freshly
            // created widget renders themed rather than blank (same prep generation does — UISpecGenerator:188).
            if (settings != null && settings.theme != null)
            {
                StarterKitBootstrap.EnsureFactoryTokens(settings.theme);
                StarterKitBootstrap.EnsureTextStyles(settings.theme);
            }
            return settings;
        }

        // ---------------------------------------------------------------- parent / canvas bootstrap

        /// <summary>
        /// Picks the parent a new widget should drop into, mirroring Unity's built-in UI create: prefer the
        /// selected RectTransform when it already lives under a Canvas; otherwise find-or-create a Canvas
        /// (and an EventSystem wired for the New Input System). <paramref name="createdCanvas"/> reports
        /// whether a Canvas was bootstrapped this call.
        /// </summary>
        private static void ResolveParent(GameObject selection, bool requireCanvas,
            out RectTransform parent, out bool createdCanvas)
        {
            createdCanvas = false;
            if (selection != null)
            {
                var rt = selection.GetComponent<RectTransform>();
                if (rt != null && selection.GetComponentInParent<Canvas>() != null)
                {
                    parent = rt;
                    return;
                }
            }
            parent = (RectTransform)FindOrCreateCanvas(out createdCanvas).transform;
        }

        private static Canvas FindOrCreateCanvas(out bool created)
        {
            created = false;
            Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Exclude);
            if (canvas != null) return canvas;

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            created = true;

            // New Input System only (hard constraint): never StandaloneInputModule.
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Exclude) == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }
            return canvas;
        }

        // ---------------------------------------------------------------- naming

        private static string UniqueViewName(RectTransform parent)
        {
            const string baseName = "View";
            if (parent == null) return baseName;
            int n = 0;
            string candidate = baseName;
            while (ChildNamed(parent, $"Main_{candidate}")) candidate = baseName + (++n);
            return candidate;
        }

        private static bool ChildNamed(RectTransform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return true;
            return false;
        }

        // "vstack" → "Vertical Stack" when the palette knows it, else a title-cased fallback.
        internal static string Humanize(string kind)
        {
            foreach (PaletteEntry e in NeoWidgetPalette.All)
                if (e.kind == kind) return e.label;
            if (string.IsNullOrEmpty(kind)) return kind;
            return char.ToUpperInvariant(kind[0]) + kind.Substring(1);
        }
    }
}
