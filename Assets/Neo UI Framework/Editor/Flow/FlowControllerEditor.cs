using Neo.EditorUI;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// FlowController inspector: the assigned graph opens straight into the Flow Graph Editor, and
    /// in play mode the running state is shown live with start/stop/pause/back controls.
    /// </summary>
    [CustomEditor(typeof(FlowController))]
    [CanEditMultipleObjects]
    public class FlowControllerEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var controller = (FlowController)target;
            NeoGUI.ComponentHeader("Flow Controller",
                controller.flow != null ? controller.flow.name : "no graph assigned", NeoColors.Flow);

            serializedObject.UpdateIfRequiredOrScript();

            using (new NeoGUI.SectionScope("Graph"))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("flow"));
                using (new EditorGUI.DisabledScope(controller.flow == null))
                {
                    if (NeoGUI.AccentButton("Open Flow Graph Editor", NeoColors.Flow))
                        FlowGraphWindow.Open(controller.flow);
                }
            }

            using (new NeoGUI.SectionScope("Behaviour"))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("flowType"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onEnableBehaviour"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onDisableBehaviour"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("dontDestroyOnSceneChange"));
                SerializedProperty goBack = serializedObject.FindProperty("goBackOnBackButton");
                EditorGUILayout.PropertyField(goBack);
                if (goBack.boolValue || goBack.hasMultipleDifferentValues)
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("autoCreateBackInput"));
            }

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying && targets.Length == 1)
                DrawRuntimeControls(controller);
        }

        private static void DrawRuntimeControls(FlowController controller)
        {
            using (new NeoGUI.SectionScope("Runtime"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("State", EditorStyles.miniLabel, GUILayout.Width(60f));
                    NeoGUI.Badge(controller.graphState.ToString(), StateColor(controller.graphState));
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.LabelField("Active Node",
                    controller.activeNode != null ? controller.activeNode.name : "—");
                EditorGUILayout.LabelField("Previous Node",
                    controller.previousNode != null ? controller.previousNode.name : "—");
                EditorGUILayout.LabelField("History Depth", controller.history.Count.ToString());

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool playing = controller.graphState == FlowGraphState.Playing;
                    bool paused = controller.graphState == FlowGraphState.Paused;

                    using (new EditorGUI.DisabledScope(playing || paused))
                        if (GUILayout.Button("Start")) controller.StartFlow();
                    using (new EditorGUI.DisabledScope(!playing && !paused))
                        if (GUILayout.Button("Stop")) controller.StopFlow();
                    if (paused)
                    {
                        if (GUILayout.Button("Resume")) controller.ResumeFlow();
                    }
                    else
                    {
                        using (new EditorGUI.DisabledScope(!playing))
                            if (GUILayout.Button("Pause")) controller.PauseFlow();
                    }
                    using (new EditorGUI.DisabledScope(controller.history.Count == 0))
                        if (GUILayout.Button("Go Back")) controller.GoBack();
                }
            }
        }

        private static Color StateColor(FlowGraphState state)
        {
            switch (state)
            {
                case FlowGraphState.Playing: return NeoColors.Add;
                case FlowGraphState.Paused: return NeoColors.Warning;
                case FlowGraphState.Stopped: return NeoColors.Remove;
                default: return NeoColors.TextDim;
            }
        }
    }

    /// <summary>
    /// FlowGraph asset inspector: summary, open-in-editor and validate. Node data is edited in the
    /// graph window, not the inspector — the raw node list stays hidden to keep selection instant.
    /// </summary>
    [CustomEditor(typeof(FlowGraph))]
    public class FlowGraphAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var graph = (FlowGraph)target;
            NeoGUI.ComponentHeader("Flow Graph", $"{graph.nodes.Count} nodes", NeoColors.Flow);

            serializedObject.UpdateIfRequiredOrScript();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("graphName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("graphDescription"));

            SerializedProperty startNodeProperty = serializedObject.FindProperty("startNode");
            Rect startRect = EditorGUILayout.GetControlRect();
            GUIContent startLabel = EditorGUI.BeginProperty(startRect, new GUIContent("Start Node", startNodeProperty.tooltip), startNodeProperty);
            startRect = EditorGUI.PrefixLabel(startRect, startLabel);
            NeoDropdown.StringPopup(startRect, startNodeProperty,
                () =>
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (FlowNode node in graph.nodes)
                        if (node != null && node.isExecutable && !string.IsNullOrEmpty(node.name))
                            names.Add(node.name);
                    return names;
                }, "(auto: first Start node)");
            EditorGUI.EndProperty();
            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(4f);
            if (NeoGUI.AccentButton("Open Flow Graph Editor", NeoColors.Flow))
                FlowGraphWindow.Open(graph);

            if (GUILayout.Button("Validate"))
            {
                var issues = graph.Validate();
                EditorUtility.DisplayDialog("Flow Graph Validation",
                    issues.Count == 0 ? "Graph is valid — no issues found." : string.Join("\n", issues), "OK");
            }
        }
    }
}
