using System.Collections.Generic;
using Neo.EditorUI;
using Neo.UI.Editor.Composer; // ComposerPalette — the Add Widget menu source
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Neo.UI.Editor.Authoring
{
    /// <summary>
    /// The scene-view "back to spec" overlay — the in-context counterpart to the agent/Composer flows,
    /// modelled on the one-click affordance tools like the Odin Validator put in the scene view. When a
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

        public override void OnWillBeDestroyed() => Selection.selectionChanged -= RefreshActiveView;

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
            }
            displayed = _view != null;
        }

        private void OnGUI()
        {
            if (_view == null)
            {
                EditorGUILayout.LabelField("Select a Neo UI view to author.", EditorStyles.miniLabel);
                return;
            }

            ResolveShowcase();
            EnsureDrift();

            DrawStatusHeader();
            EditorGUILayout.Space(2f);
            DrawActions();

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
                    ShowApplyPresetMenu();

                if (_showcase == null && GUILayout.Button(new GUIContent("Assign Showcase",
                        "Pick or create the showcase this view belongs to"), GUILayout.Height(20f)))
                    ShowAssignShowcaseMenu();
            }
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
            foreach (PaletteEntry e in ComposerPalette.All)
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

        private void ShowApplyPresetMenu()
        {
            GameObject target = Selection.activeGameObject;
            var menu = new GenericMenu();
            if (target == null || target == _view.gameObject)
            {
                menu.AddDisabledItem(new GUIContent("Select a widget inside the view first"));
                menu.ShowAsContext();
                return;
            }
            bool any = false;
            foreach (NeoWidgetPreset p in NeoWidgetPresets.All)
            {
                if (p == null || string.IsNullOrEmpty(p.presetName)) continue;
                any = true;
                string name = p.presetName;
                menu.AddItem(new GUIContent($"{p.category}/{name}"), false, () =>
                {
                    NeoSceneAuthoring.ApplyPreset(target, name);
                    _driftStale = true;
                });
            }
            if (!any) menu.AddDisabledItem(new GUIContent("No presets — create one in the Design System window"));
            menu.ShowAsContext();
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
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New showcase from this view…"), false, () =>
            {
                string id = Sanitize($"{_view.id.Category}-{_view.id.Name}").ToLowerInvariant();
                _showcase = NeoCapture.CreateShowcase(id, $"{_view.id.Category} {_view.id.Name}", "Custom");
                _showcaseResolved = true;
                _driftStale = true;
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

        private static string Sanitize(string value)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
            return value.Replace(' ', '-');
        }
    }
}
