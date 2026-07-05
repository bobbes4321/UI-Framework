using System;
using Neo.EditorUI;
using Neo.UI.Editor.Authoring;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// The shared "Preset" section drawn on widget-root component inspectors (Task 3.2) — the second
    /// route to the SAME preset workflow the scene-view overlay (<see cref="NeoSceneOverlay"/>) exposes,
    /// for a user who never noticed the overlay. Apply / Create From Widget / Update / Reset all route
    /// through the one <see cref="NeoSceneAuthoring"/> API, so the two surfaces can never drift.
    /// <para>
    /// Editor-perf discipline (matching the overlay): the widget's spec is exported ONCE per inspected
    /// object — cached, keyed on the GameObject's instance id — never re-exported per repaint. The
    /// enable/disable state is derived from that cache via the shared <see cref="PresetGating"/> helper
    /// the overlay uses too. Destructive actions (Apply/Create/Reset rebuild the inspected widget in
    /// place) are deferred with <see cref="EditorApplication.delayCall"/> so the editor never destroys
    /// its own target mid-OnInspectorGUI.
    /// </para>
    /// </summary>
    public sealed class PresetWorkflowGUI
    {
        private const string FoldoutKey = "NeoUI.Inspector.PresetWorkflow";

        private int _cachedId;
        private PresetGating _gating;

        /// <summary>
        /// Draws the collapsed-by-default "Preset" foldout for <paramref name="widgetRoot"/> (the inspected
        /// component's GameObject). Hidden entirely for multi-object selection (<paramref name="targetCount"/>
        /// &gt; 1) — the workflow is single-target only, exactly like the overlay.
        /// </summary>
        public void Draw(GameObject widgetRoot, int targetCount)
        {
            if (targetCount != 1 || widgetRoot == null) return;
            Refresh(widgetRoot);

            if (!NeoGUI.BeginFoldoutSection(FoldoutKey, "Preset",
                    _gating.hasPreset ? _gating.PresetName : null))
            {
                NeoGUI.EndFoldoutSection();
                return;
            }

            if (!_gating.hasWidget)
            {
                EditorGUILayout.LabelField(
                    "Select a Neo widget inside a view to style it with a reusable preset.",
                    EditorStyles.wordWrappedMiniLabel);
                NeoGUI.EndFoldoutSection();
                return;
            }

            EditorGUILayout.LabelField("Linked preset",
                _gating.hasPreset ? _gating.PresetName : "(none)", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                // Apply… — the visual card picker, anchored to this button's rect (like the overlay).
                if (GUILayout.Button(new GUIContent("Apply…",
                        "Re-style this widget to a reusable component preset"), GUILayout.Height(22f)))
                    ShowApplyPopup(GUILayoutUtility.GetLastRect(), widgetRoot);

                if (GUILayout.Button(new GUIContent("Create From Widget",
                        "Save this widget's styling as a new reusable preset"), GUILayout.Height(22f)))
                    CreateFromWidget(widgetRoot);
            }

            using (new EditorGUI.DisabledScope(!_gating.hasPreset))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Update Preset",
                        "Push this widget's current styling into its linked preset"), GUILayout.Height(22f)))
                    Defer(widgetRoot, go => NeoSceneAuthoring.UpdatePresetFromWidget(go));

                if (GUILayout.Button(new GUIContent("Reset To Preset",
                        "Clear this widget's overrides back to its linked preset's values"), GUILayout.Height(22f)))
                    Defer(widgetRoot, go => NeoSceneAuthoring.ResetWidgetToPreset(go));
            }

            NeoGUI.EndFoldoutSection();
        }

        // Export the widget's spec once per inspected object (editor-perf: never per repaint). A widget
        // rebuilt by Apply/Reset lands as a NEW GameObject that Selection repoints to, so the id-keyed
        // cache invalidates naturally on the next inspector build.
        private void Refresh(GameObject widgetRoot)
        {
            int id = widgetRoot.GetInstanceID();
            if (id == _cachedId) return;
            _cachedId = id;
            _gating = PresetGating.For(widgetRoot);
        }

        private void ShowApplyPopup(Rect anchor, GameObject widgetRoot)
        {
            ElementSpec current = _gating.captured;
            if (current == null) return;
            PopupWindow.Show(anchor, new PresetPickerPopup(current.kind, current.preset, name =>
            {
                if (string.IsNullOrEmpty(name)) return; // "(none)" — ApplyPreset always needs a name
                // The popup callback already runs OUTSIDE OnInspectorGUI, so the in-place rebuild is safe.
                NeoSceneAuthoring.ApplyPreset(widgetRoot, name);
                _cachedId = 0; // force a re-export against whatever survives
            }));
        }

        private void CreateFromWidget(GameObject widgetRoot)
        {
            string kind = _gating.captured != null ? _gating.captured.kind : "Widget";
            string path = EditorUtility.SaveFilePanelInProject("Create Widget Preset",
                $"{kind}Preset", "asset", "Save the reusable preset asset", NeoWidgetPresets.PresetsRoot);
            if (string.IsNullOrEmpty(path)) return;
            Defer(widgetRoot, go => NeoSceneAuthoring.CreatePresetFromWidget(go, path));
        }

        // Run a widget-mutating action after the current OnInspectorGUI pass — Apply/Create/Reset destroy
        // the inspected GameObject to rebuild it, which must never happen while the editor is drawing it.
        private void Defer(GameObject widgetRoot, Action<GameObject> action)
        {
            _cachedId = 0;
            EditorApplication.delayCall += () =>
            {
                if (widgetRoot != null) action(widgetRoot);
            };
        }
    }

    /// <summary>
    /// The pure enable/disable gate shared by the widget-root inspectors' "Preset" section and the
    /// scene-view overlay's preset row: given a widget's exported spec (null when the object isn't a
    /// recognized, in-view Neo widget), tells both surfaces whether the four actions apply. Extracted so
    /// the two routes derive "can I preset this?" from ONE definition rather than duplicating it.
    /// </summary>
    public readonly struct PresetGating
    {
        /// <summary> The widget's exported spec, or null when it isn't a preset-able Neo widget. </summary>
        public readonly ElementSpec captured;

        /// <summary> True when the object is a recognized Neo widget (Apply + Create From Widget apply). </summary>
        public readonly bool hasWidget;

        /// <summary> True when the widget is already linked to a preset (Update + Reset apply). </summary>
        public readonly bool hasPreset;

        public PresetGating(ElementSpec captured)
        {
            this.captured = captured;
            hasWidget = captured != null;
            hasPreset = hasWidget && !string.IsNullOrEmpty(captured.preset);
        }

        /// <summary> The linked preset name, or null when unlinked. </summary>
        public string PresetName => captured?.preset;

        /// <summary> Quietly exports <paramref name="widgetRoot"/> for gating (never logs — a null result
        /// just means "nothing preset-able here", not a failed user action). </summary>
        public static PresetGating For(GameObject widgetRoot) =>
            new PresetGating(NeoSceneAuthoring.TryExportForPresetWorkflow(widgetRoot));
    }
}
