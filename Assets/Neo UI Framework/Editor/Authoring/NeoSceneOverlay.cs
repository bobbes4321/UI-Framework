using System.Collections.Generic;
using Neo.EditorUI;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// The scene-view "back to spec" overlay — the in-context counterpart to editing a spec via the
    /// agent bridge, modelled on the one-click affordance tools like the Odin Validator put in the
    /// scene view. When a
    /// Neo <see cref="UIView"/> is selected it surfaces a drift-status dot (green = in sync, yellow = safe
    /// drift, red = off-spec) plus one-click Capture-to-Spec / Validate / Check-Drift, and an Add-Widget
    /// menu that drops a widget into the selection through the SAME native path as the GameObject menu.
    /// <para>
    /// Editor-perf discipline: the active view is resolved only on <see cref="Selection.selectionChanged"/>
    /// (one parent walk, no scene scans), and the expensive drift scan runs once per change / on demand —
    /// cached, never per repaint. All scoped work goes through <see cref="NeoWorkspace"/> so it reads the
    /// selected view's showcase root, not the committed default root.
    /// </para>
    /// </summary>
    [Overlay(typeof(SceneView), OverlayId, "Neo UI", defaultDisplay: true)]
    public sealed class NeoSceneOverlay : Overlay
    {
        public const string OverlayId = "neo-ui-scene-authoring";

        private UIView _view;
        private Showcase _showcase;
        private bool _showcaseResolved;
        private DriftStatus _drift;
        private bool _driftStale = true;
        private SyncResult _lastCapture;
        private List<string> _validation;

        // The Preset row's "what's selected" cache — refreshed on selection change (RefreshActiveView),
        // never re-exported per repaint (same editor-perf discipline as _drift).
        private GameObject _presetTarget;
        private ElementSpec _presetCaptured;

        // The Connect-to row's "what's selected" cache — a UIButton anywhere in the selection's parent
        // chain (mirrors how _view resolves), refreshed alongside everything else on selection change.
        private UIButton _selectedButton;

        // "Connect to…" pick mode — a static one-shot so it survives whichever SceneView/overlay
        // instance ends up handling the resolving click; only ONE session can be armed at a time.
        // The anchor rect is captured ONCE at arm time and reused to open the confirm popup later:
        // PopupWindow.Show must run inside an IMGUI pass of THIS container (coordinate spaces don't
        // translate across SceneView's duringSceneGui and the overlay's own panel), so the scene-click/
        // hierarchy-selection handlers only ever stash a pending result for the next OnGUI to consume.
        private static bool _pickArmed;
        private static UIButton _pickButton;
        private static UIView _pickSourceView;
        private static Rect _pickAnchorRect;
        private static UIButton _pendingConnectButton;
        private static UIView _pendingConnectTarget;

        // The Breakpoint row (Task 2.3 — native parity for the doomed Composer's BreakpointBar). The
        // breakpoint list is showcase-scoped and cached like _drift (recomputed on selection change /
        // on demand, never per repaint); the selection itself is UI-only state, not persisted.
        private List<BreakpointSpec> _breakpoints;
        private bool _breakpointsStale = true;
        private string _selectedBreakpoint = ""; // "" = "(base)"

        public override VisualElement CreatePanelContent()
        {
            var imgui = new IMGUIContainer(OnGUI) { style = { minWidth = 250 } };
            return imgui;
        }

        public override void OnCreated()
        {
            Selection.selectionChanged += RefreshActiveView;
            RefreshActiveView();
        }

        public override void OnWillBeDestroyed()
        {
            Selection.selectionChanged -= RefreshActiveView;
            if (_pickArmed) CancelPick("overlay closed");
        }

        // Cheap: one upward component walk, only when the selection actually changes.
        private void RefreshActiveView()
        {
            UIView view = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<UIView>(true)
                : null;
            if (view != _view)
            {
                _view = view;
                _showcase = null;
                _showcaseResolved = false;
                _drift = null;
                _driftStale = true;
                _lastCapture = null;
                _validation = null;
                _breakpoints = null;
                _breakpointsStale = true;
                _selectedBreakpoint = "";
            }
            displayed = _view != null;
            _selectedButton = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<UIButton>(true)
                : null;
            RefreshPresetCapture();
        }

        // Quiet (never-logs) export of the current selection for the Preset row's enable/disable state —
        // computed once per selection change, not per repaint (TryExportForPresetWorkflow, not the loud
        // ExportForPresetWorkflow the action buttons use).
        private void RefreshPresetCapture()
        {
            GameObject target = Selection.activeGameObject;
            _presetTarget = (_view != null && target != null && target != _view.gameObject) ? target : null;
            _presetCaptured = _presetTarget != null ? NeoSceneAuthoring.TryExportForPresetWorkflow(_presetTarget) : null;
        }

        private void OnGUI()
        {
            // Must run before the early-return below: a scene click resolving the pick can leave
            // Selection untouched (the click is consumed so it never reaches Unity's own scene-pick
            // logic), so _view is still whatever it was when the pick armed — but we open the popup
            // unconditionally the first OnGUI pass after a valid target is resolved.
            if (_pendingConnectButton != null && _pendingConnectTarget != null)
            {
                UIButton button = _pendingConnectButton;
                UIView target = _pendingConnectTarget;
                _pendingConnectButton = null;
                _pendingConnectTarget = null;
                UnityEditor.PopupWindow.Show(_pickAnchorRect, new ConnectToPopup(button, target));
            }

            if (_view == null)
            {
                EditorGUILayout.LabelField("Select a Neo UI view to author.", EditorStyles.miniLabel);
                return;
            }

            ResolveShowcase();
            EnsureDrift();
            EnsureBreakpoints();

            DrawStatusHeader();
            EditorGUILayout.Space(2f);
            DrawActions();
            DrawConnectRow();

            if (_lastCapture != null && (_lastCapture.refused || _lastCapture.offSpecWarnings.Count > 0))
                DrawCaptureFindings();
            if (_validation != null)
                DrawValidation();
        }

        // ---------------------------------------------------------------- sections

        private void DrawStatusHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect dot = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(16f));
                dot.y += 3f; dot.height = 10f;
                EditorGUI.DrawRect(dot, _drift != null ? _drift.DotColor : NeoColors.TextSubtle);
                EditorGUILayout.LabelField($"{_view.id.Category}/{_view.id.Name}", EditorStyles.boldLabel);
            }
            string scope = _showcase != null ? $"showcase: {_showcase.id}" : "no showcase yet";
            EditorGUILayout.LabelField($"{scope} · {(_drift != null ? _drift.Summary : "—")}",
                EditorStyles.miniLabel);
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Capture to Spec",
                        "Fold this view's edits back into its showcase spec + baseline"), GUILayout.Height(22f)))
                    Capture(force: false);

                if (GUILayout.Button(new GUIContent("Validate"), GUILayout.Height(22f)))
                    RunValidate();

                if (GUILayout.Button(new GUIContent("Check Drift"), GUILayout.Height(22f)))
                {
                    _driftStale = true;
                    EnsureDrift();
                    DriftWindow.Open();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("+ Add Widget",
                        "Create a Neo widget under the selected element"), GUILayout.Height(20f)))
                    ShowAddWidgetMenu();

                if (GUILayout.Button(new GUIContent("Apply Preset",
                        "Re-style the selected widget to a reusable component preset"), GUILayout.Height(20f)))
                    ShowApplyPresetPopup(GUILayoutUtility.GetLastRect());

                if (_showcase == null && GUILayout.Button(new GUIContent("Assign Showcase",
                        "Pick or create the showcase this view belongs to"), GUILayout.Height(20f)))
                    ShowAssignShowcaseMenu();
            }

            DrawPresetWorkflowRow();
            DrawBreakpointRow();
        }

        // Create/Update/Reset-from-selection — the native counterpart to the (doomed) Composer's
        // SpecInspector preset workflow (SpecInspector.cs:271-321/:301-312). Enabled state reads the
        // cached _presetCaptured (refreshed on selection change, never re-exported per repaint).
        private void DrawPresetWorkflowRow()
        {
            // Same enable/disable gate the widget-root inspectors use (PresetWorkflowGUI, Task 3.2) — one
            // definition of "can I preset this?" shared by both surfaces.
            var gating = new PresetGating(_presetCaptured);
            bool hasWidget = gating.hasWidget;
            bool hasPreset = gating.hasPreset;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasWidget))
                {
                    if (GUILayout.Button(new GUIContent("Create Preset",
                            "Save the selected widget's styling as a new reusable preset"), GUILayout.Height(20f)))
                        CreatePresetFromSelection();
                }
                using (new EditorGUI.DisabledScope(!hasPreset))
                {
                    if (GUILayout.Button(new GUIContent("Update Preset",
                            "Push the widget's current styling into its linked preset"), GUILayout.Height(20f)))
                    {
                        NeoSceneAuthoring.UpdatePresetFromWidget(_presetTarget);
                        RefreshPresetCapture();
                        _driftStale = true;
                    }
                    if (GUILayout.Button(new GUIContent("Reset To Preset",
                            "Clear this widget's overrides back to its linked preset"), GUILayout.Height(20f)))
                    {
                        NeoSceneAuthoring.ResetWidgetToPreset(_presetTarget);
                        RefreshPresetCapture();
                        _driftStale = true;
                    }
                }
            }
        }

        // "Connect to…" — direct-manipulation flow wiring (the scene-view counterpart of hand-editing a
        // flow edge). Shows "Connect →" only when the selection resolves to a UIButton (cached in
        // RefreshActiveView, same discipline as _presetTarget); clicking arms the static pick session so
        // the next scene click / hierarchy selection resolves the target view.
        private void DrawConnectRow()
        {
            if (_pickArmed)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Click a target view… (Esc cancels)", EditorStyles.miniLabel);
                    if (GUILayout.Button("Cancel", GUILayout.Width(50f))) CancelPick(null);
                }
                return;
            }

            if (_selectedButton == null) return;

            if (GUILayout.Button(new GUIContent("Connect →",
                    "Wire this button's click to navigate to a view — pick the target in the Scene View or Hierarchy"),
                    GUILayout.Height(20f)))
                ArmPick();
        }

        private void ArmPick()
        {
            _pickButton = _selectedButton;
            _pickSourceView = _view;
            _pickAnchorRect = GUILayoutUtility.GetLastRect();
            _pickArmed = true;
            SceneView.duringSceneGui += OnScenePick;
            Selection.selectionChanged += OnSelectionPick;
        }

        private static void UnsubscribePick()
        {
            SceneView.duringSceneGui -= OnScenePick;
            Selection.selectionChanged -= OnSelectionPick;
        }

        private static void CancelPick(string reason)
        {
            Debug.Log($"[Neo.UI] Connect To: pick cancelled{(string.IsNullOrEmpty(reason) ? "." : $" — {reason}.")}");
            _pickArmed = false;
            _pickButton = null;
            _pickSourceView = null;
            UnsubscribePick();
        }

        // Shared by both resolution paths (scene click / hierarchy selection): validates the target,
        // stashes it for the next OnGUI to consume (never calls PopupWindow.Show from here — duringSceneGui
        // and selectionChanged run outside this container's own IMGUI pass, wrong coordinate space).
        private static void CompletePick(UIView target)
        {
            if (!_pickArmed) return;
            if (target == null) { CancelPick("no view under the cursor"); return; }
            if (target == _pickSourceView) { CancelPick("target is the button's own view"); return; }

            _pendingConnectButton = _pickButton;
            _pendingConnectTarget = target;

            _pickArmed = false;
            _pickButton = null;
            _pickSourceView = null;
            UnsubscribePick();
        }

        private static void OnSelectionPick()
        {
            if (!_pickArmed) return;
            GameObject selected = Selection.activeGameObject;
            UIView target = selected != null ? selected.GetComponentInParent<UIView>(true) : null;
            CompletePick(target);
        }

        private static void OnScenePick(SceneView sceneView)
        {
            if (!_pickArmed) return;
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CancelPick("Esc");
                e.Use();
                sceneView.Repaint();
                return;
            }
            if (e.type != EventType.MouseDown || e.button != 0) return;

            GameObject picked = HandleUtility.PickGameObject(e.mousePosition, false);
            UIView target = picked != null ? picked.GetComponentInParent<UIView>(true) : null;
            e.Use(); // pick-mode clicks must never fall through to Unity's own scene selection
            CompletePick(target);
            sceneView.Repaint();
        }

        // Native parity for the (doomed) Composer's BreakpointBar (Task 2.3) — deliberately minimal:
        // no condition editing (that stays spec-side), just a scope picker plus Capture/Preview against
        // whichever breakpoint is picked. Hidden entirely when the showcase declares none.
        private void DrawBreakpointRow()
        {
            if (_breakpoints == null || _breakpoints.Count == 0) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Breakpoint", GUILayout.Width(70f));
                if (GUILayout.Button(string.IsNullOrEmpty(_selectedBreakpoint) ? "(base)" : _selectedBreakpoint,
                        EditorStyles.popup))
                    ShowBreakpointMenu();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selectedBreakpoint) || _presetCaptured == null))
                {
                    if (GUILayout.Button(new GUIContent("Capture Layout As Override",
                            "Save the widget's current (dragged/resized) layout as this breakpoint's override"),
                            GUILayout.Height(20f)))
                        CaptureBreakpointOverride();
                }
                if (GUILayout.Button(new GUIContent("Preview Breakpoint",
                        "Apply this breakpoint's baked layout live in the scene (editor-only, not saved)"),
                        GUILayout.Height(20f)))
                    NeoSceneAuthoring.PreviewBreakpoint(_view, _selectedBreakpoint);
            }
        }

        private void ShowBreakpointMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(base)"), string.IsNullOrEmpty(_selectedBreakpoint),
                () => _selectedBreakpoint = "");
            foreach (BreakpointSpec bp in _breakpoints)
            {
                if (bp == null || string.IsNullOrEmpty(bp.name)) continue;
                string name = bp.name;
                menu.AddItem(new GUIContent(name), _selectedBreakpoint == name, () => _selectedBreakpoint = name);
            }
            menu.ShowAsContext();
        }

        private void CaptureBreakpointOverride()
        {
            if (string.IsNullOrEmpty(_selectedBreakpoint)) return;
            if (_presetTarget == null || _presetCaptured == null)
            {
                Debug.LogWarning("Neo UI: select a widget inside the view before capturing a breakpoint override.");
                return;
            }
            if (_showcase == null) { ShowAssignShowcaseMenu(); return; }

            _lastCapture = NeoSceneAuthoring.CaptureLayoutOverride(
                _presetTarget, _view, _showcase, _selectedBreakpoint, _presetCaptured.layout);
            RefreshPresetCapture();
            _driftStale = true;
        }

        private void CreatePresetFromSelection()
        {
            if (_presetTarget == null || _presetCaptured == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Create Widget Preset",
                $"{_presetCaptured.kind}Preset", "asset", "Save the reusable preset asset", NeoWidgetPresets.PresetsRoot);
            if (string.IsNullOrEmpty(path)) return;
            NeoSceneAuthoring.CreatePresetFromWidget(_presetTarget, path);
            RefreshPresetCapture();
            _driftStale = true;
        }

        private void DrawCaptureFindings()
        {
            NeoGUI.Splitter();
            if (_lastCapture.refused)
                EditorGUILayout.HelpBox(_lastCapture.note ?? "Capture refused.", MessageType.Warning);
            foreach (OffSpecFinding f in _lastCapture.offSpecWarnings) DriftStatus.FindingRow(f);
            if (_lastCapture.refused && _lastCapture.offSpecWarnings.Count > 0
                && GUILayout.Button("Force Capture (accept the loss)", GUILayout.Height(20f)))
                Capture(force: true);
        }

        private void DrawValidation()
        {
            NeoGUI.Splitter();
            if (_validation.Count == 0)
                EditorGUILayout.LabelField("No validation issues.", EditorStyles.miniLabel);
            else
                foreach (string issue in _validation)
                    EditorGUILayout.LabelField($"• {issue}", EditorStyles.wordWrappedMiniLabel);
        }

        // ---------------------------------------------------------------- actions

        private void Capture(bool force)
        {
            if (_showcase == null) { ShowAssignShowcaseMenu(); return; }
            _lastCapture = NeoCapture.CaptureView(_view, _showcase, force);
            _driftStale = true;
            EnsureDrift();
        }

        private void RunValidate()
        {
            if (_showcase == null) { _validation = new List<string> { "Assign a showcase first." }; return; }
            using (NeoWorkspace.Scoped(_showcase))
            {
                _validation = new List<string>();
                _validation.AddRange(AgentValidation.ValidateAll());
                _validation.AddRange(AgentValidation.ValidateDesign());
            }
        }

        private void ShowAddWidgetMenu()
        {
            GameObject parent = Selection.activeGameObject != null ? Selection.activeGameObject : _view.gameObject;
            var menu = new GenericMenu();
            foreach (PaletteEntry e in NeoWidgetPalette.All)
            {
                string kind = e.kind;
                menu.AddItem(new GUIContent($"{e.category}/{e.label}"), false, () =>
                {
                    NeoSceneAuthoring.CreateWidget(kind, parent);
                    _driftStale = true;
                });
            }
            menu.ShowAsContext();
        }

        // Kind-scoped thumbnail-card grid (PresetPickerPopup) anchored to the button, replacing the old
        // flat GenericMenu — the same visual picker the Composer's inspector Preset row used.
        private void ShowApplyPresetPopup(Rect anchor)
        {
            GameObject target = Selection.activeGameObject;
            if (target == null || target == _view.gameObject)
            {
                Debug.LogWarning("Neo UI: select a widget inside the view before applying a preset.");
                return;
            }
            ElementSpec current = NeoSceneAuthoring.ExportForPresetWorkflow(target);
            if (current == null) return; // already warned

            UnityEditor.PopupWindow.Show(anchor, new PresetPickerPopup(current.kind, current.preset, name =>
            {
                if (string.IsNullOrEmpty(name)) return; // "(none)" — ApplyPreset always needs a name
                NeoSceneAuthoring.ApplyPreset(target, name);
                RefreshPresetCapture();
                _driftStale = true;
            }));
        }

        private void ShowAssignShowcaseMenu()
        {
            var menu = new GenericMenu();
            foreach (Showcase s in ShowcaseRegistry.All)
            {
                Showcase captured = s;
                menu.AddItem(new GUIContent(captured.id), false, () =>
                {
                    _showcase = captured;
                    _showcaseResolved = true;
                    _driftStale = true;
                    _breakpointsStale = true;
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New showcase from this view…"), false, () =>
            {
                string id = Sanitize($"{_view.id.Category}-{_view.id.Name}").ToLowerInvariant();
                _showcase = NeoCapture.CreateShowcase(id, $"{_view.id.Category} {_view.id.Name}", "Custom");
                _showcaseResolved = true;
                _driftStale = true;
                _breakpointsStale = true;
            });
            menu.ShowAsContext();
        }

        // ---------------------------------------------------------------- caching

        private void ResolveShowcase()
        {
            if (_showcaseResolved) return;
            _showcaseResolved = true;
            NeoCapture.TryResolveShowcase(_view, out _showcase);
        }

        // Lazy + cached: compute once after a selection change or an action, never every repaint.
        private void EnsureDrift()
        {
            if (!_driftStale) return;
            _driftStale = false;
            if (_showcase == null) { _drift = null; return; }
            using (NeoWorkspace.Scoped(_showcase))
                _drift = DriftStatus.Scan();
        }

        // Lazy + cached like EnsureDrift: the breakpoint list only changes when the showcase's spec
        // does, so it is recomputed on selection change / after a capture, never every repaint.
        private void EnsureBreakpoints()
        {
            if (!_breakpointsStale) return;
            _breakpointsStale = false;
            _breakpoints = _showcase != null ? NeoSceneAuthoring.GetBreakpoints(_showcase) : null;
        }

        private static string Sanitize(string value)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            return value.Replace(' ', '-');
        }
    }
}
