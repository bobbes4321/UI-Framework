using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Neo.EditorUI;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Flow graph editor: pan/zoom/grid, search-as-you-type + drag-off-port node creation,
    /// organizational groups, minimap, side inspector for the selected node, undo-aware editing —
    /// and runtime debugging: in play mode the active node highlights live, traversed edges flash,
    /// and a breadcrumb strip tracks the controller's history.
    ///
    /// The node inspector uses ONE cached SerializedObject (so foldout/expansion state survives
    /// between frames) and never rebuilds the graph view on value edits (so typing in the name field
    /// doesn't destroy the selection). Node renames propagate to every edge that targets the node
    /// by name, and to the graph's start node.
    /// </summary>
    public class FlowGraphWindow : EditorWindow
    {
        private const string NameFieldControl = "NeoFlowNodeNameField";

        [SerializeField] private FlowGraph _graph; // serialized: survives domain reloads
        private FlowGraphView _graphView;
        private IMGUIContainer _inspector;
        private VisualElement _breadcrumbRoot;
        private FlowController _liveController;
        private string _liveActiveNodeName;

        // node inspector state — cached so IMGUI state (foldouts, list expansion) survives frames
        private SerializedObject _serializedGraph;
        private FlowNode _inspectedNode;
        private string _inspectedSignature;
        private string _nameBuffer;
        private bool _nameFieldFocused;

        /// <summary> Raised after any edit that mutates the graph (connect/disconnect/move/delete,
        /// rename, arrange, reroute). <see cref="OpenForSpec"/> sets this to mirror flow edits made on
        /// a transient graph back into an in-memory spec — null in normal asset-editing use. </summary>
        internal System.Action externalGraphChanged;

        internal void RaiseExternalChanged() => externalGraphChanged?.Invoke();

        [MenuItem("Tools/Neo UI/Flow Graph Editor", priority = 10)]
        public static void Open() => GetWindow<FlowGraphWindow>("Flow Graph");

        public static void Open(FlowGraph graph)
        {
            var window = GetWindow<FlowGraphWindow>("Flow Graph");
            window.SetGraph(graph);
        }

        /// <summary>
        /// Opens the window on an externally-owned (often transient) graph and routes edits back to
        /// <paramref name="onChanged"/>, so a caller can edit a flow without that flow ever becoming a
        /// committed asset. (Originally used by the retired Composer's "Flow" leaf; kept as the seam
        /// for any future in-memory/transient-graph editing surface.)
        /// </summary>
        public static FlowGraphWindow OpenForSpec(FlowGraph graph, System.Action onChanged)
        {
            var window = GetWindow<FlowGraphWindow>("Flow Graph");
            window.externalGraphChanged = onChanged;
            window.SetGraph(graph);
            window.Focus();
            return window;
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.EntityIdToObject(instanceId) is FlowGraph graph)
            {
                Open(graph);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            BuildUI();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (Application.isPlaying) TryBindLiveController(); // window opened mid-play-session
            Undo.undoRedoPerformed += OnUndoRedo;
            UnityEditor.Selection.selectionChanged += OnGlobalSelectionChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            UnityEditor.Selection.selectionChanged -= OnGlobalSelectionChanged;
            UnbindLiveController();
            _serializedGraph?.Dispose();
            _serializedGraph = null;
        }

        // Runtime debugging is entirely event-driven (FlowController.OnActiveNodeChanged/OnAdvanced)
        // — no editor-tick subscription, in play mode or otherwise.
        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                TryBindLiveController();
                // a dynamically-instantiated controller (e.g. spawned from a bootstrap prefab) may
                // not be registered yet the instant play mode is entered — one deferred retry, not a
                // recurring poll, catches that without an ongoing tick subscription.
                if (_liveController == null) EditorApplication.delayCall += RetryBindLiveControllerOnce;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode)
            {
                UnbindLiveController();
            }
        }

        private void RetryBindLiveControllerOnce()
        {
            if (Application.isPlaying) TryBindLiveController();
        }

        private void BuildUI()
        {
            rootVisualElement.Clear();

            var toolbar = new UnityEditor.UIElements.Toolbar();
            var graphField = new UnityEditor.UIElements.ObjectField { objectType = typeof(FlowGraph), value = _graph };
            graphField.RegisterValueChangedCallback(evt => SetGraph(evt.newValue as FlowGraph));
            toolbar.Add(graphField);
            toolbar.Add(new UnityEditor.UIElements.ToolbarSpacer());
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(ArrangeGraph) { text = "Arrange" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => _graphView?.FrameAll()) { text = "Frame All" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(() => _graphView?.FrameSelection()) { text = "Frame Selection" });
            toolbar.Add(new UnityEditor.UIElements.ToolbarButton(ValidateGraph) { text = "Validate" });

            var spacer = new UnityEditor.UIElements.ToolbarSpacer();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            var search = new UnityEditor.UIElements.ToolbarSearchField { tooltip = "Find a node by name — press Enter to jump to it" };
            search.style.width = 180f;
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                JumpToNode(search.value);
                evt.StopPropagation();
            });
            toolbar.Add(search);
            rootVisualElement.Add(toolbar);

            var split = new TwoPaneSplitView(1, 300f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            rootVisualElement.Add(split);

            _graphView = new FlowGraphView(this);
            _graphView.style.flexGrow = 1f;
            split.Add(_graphView);

            var inspectorRoot = new VisualElement();
            inspectorRoot.style.minWidth = 240f;
            inspectorRoot.Add(new Label("Node Inspector") { style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 6, paddingTop = 4 } });
            _inspector = new IMGUIContainer(DrawSelectedNodeInspector);
            _inspector.style.flexGrow = 1f;
            _inspector.style.paddingLeft = 4f;
            _inspector.style.paddingRight = 4f;
            inspectorRoot.Add(_inspector);
            split.Add(inspectorRoot);

            // play-mode breadcrumb strip — hidden until a live controller is bound, updated only on
            // node-change events (never per frame)
            _breadcrumbRoot = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    height = 22f,
                    paddingLeft = 6f,
                    paddingRight = 6f,
                    alignItems = Align.Center,
                    borderTopWidth = 1f,
                    borderTopColor = new Color(0f, 0f, 0f, 0.35f),
                    display = DisplayStyle.None,
                }
            };
            rootVisualElement.Add(_breadcrumbRoot);

            if (_graph != null) _graphView.Populate(_graph);
        }

        public void SetGraph(FlowGraph graph)
        {
            _graph = graph;
            UnbindLiveController();
            _inspectedNode = null;
            _inspectedSignature = null;
            _serializedGraph?.Dispose();
            _serializedGraph = graph != null ? new SerializedObject(graph) : null;
            // populate even when null so clearing the field empties the view (a stale view would
            // keep writing into the unloaded asset)
            _graphView?.Populate(graph);
            if (Application.isPlaying) TryBindLiveController();
        }

        private void OnUndoRedo()
        {
            if (_graph == null) return;
            // managed-reference lists can change shape on undo — resync everything
            _serializedGraph?.Dispose();
            _serializedGraph = new SerializedObject(_graph);
            _inspectedSignature = null;
            _graphView?.PopulatePreservingSelection();
        }

        private void ValidateGraph()
        {
            if (_graph == null) return;
            List<string> issues = _graph.Validate();
            string message = issues.Count == 0 ? "Graph is valid — no issues found." : string.Join("\n", issues);
            EditorUtility.DisplayDialog("Flow Graph Validation", message, "OK");
        }

        /// <summary> Auto-arranges nodes into clean left-to-right layers to untangle crossing edges (one undo step). </summary>
        private void ArrangeGraph()
        {
            if (_graph == null || _graphView == null) return;
            Undo.RecordObject(_graph, "Arrange Flow Graph");
            FlowGraphLayout.Arrange(_graph);
            EditorUtility.SetDirty(_graph);
            _graphView.PopulatePreservingSelection();
            _graphView.FrameAll();
            RaiseExternalChanged();
        }

        /// <summary> Selects and frames the first node whose name contains <paramref name="query"/>. </summary>
        private void JumpToNode(string query)
        {
            if (_graph == null || _graphView == null || string.IsNullOrWhiteSpace(query)) return;
            FlowNode node = _graph.nodes.FirstOrDefault(n =>
                n != null && !string.IsNullOrEmpty(n.name) &&
                n.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            if (node != null) _graphView.FrameNode(node);
        }

        // ------------------------------------------------------------------ selection / inspector

        private void OnGlobalSelectionChanged()
        {
            // selecting a FlowController in play mode opens its running graph; in edit mode the
            // window keeps whatever the user is working on
            if (!Application.isPlaying) return;
            if (UnityEditor.Selection.activeGameObject == null) return;
            var controller = UnityEditor.Selection.activeGameObject.GetComponent<FlowController>();
            if (controller == null || controller.flow == null) return;
            if (_graph != controller.flow) SetGraph(controller.flow); // rebinds via TryBindLiveController
            BindLiveController(controller); // pins the SPECIFIC selected controller (several may share a flow)
        }

        private void DrawSelectedNodeInspector()
        {
            if (_graph == null || _graphView == null)
            {
                EditorGUILayout.HelpBox("Assign a Flow Graph asset.", MessageType.Info);
                return;
            }

            // sticky selection: keep showing the last selected node so clicking into the inspector
            // (or anywhere else) never blanks it mid-edit
            FlowNodeView selectedView = _graphView.selection.OfType<FlowNodeView>().FirstOrDefault();
            if (selectedView != null && selectedView.node != _inspectedNode)
            {
                _inspectedNode = selectedView.node;
                _inspectedSignature = null;
                _nameBuffer = _inspectedNode.name;   // discard any half-typed rename of the old node
                _nameFieldFocused = false;
                GUIUtility.keyboardControl = 0; // don't carry text focus into the new node's fields
            }

            if (_inspectedNode != null && !_graph.nodes.Contains(_inspectedNode)) _inspectedNode = null;
            if (_inspectedNode == null)
            {
                EditorGUILayout.HelpBox("Select a node to edit it.", MessageType.Info);
                return;
            }

            int index = _graph.nodes.IndexOf(_inspectedNode);
            if (index < 0) return;

            if (_serializedGraph == null || _serializedGraph.targetObject != _graph)
                _serializedGraph = new SerializedObject(_graph);
            _serializedGraph.UpdateIfRequiredOrScript();

            SerializedProperty nodeProperty = _serializedGraph
                .FindProperty("nodes")
                .GetArrayElementAtIndex(index);

            DrawNodeHeader();
            DrawNameField();

            EditorGUI.BeginChangeCheck();

            SerializedProperty iterator = nodeProperty.Copy();
            SerializedProperty end = nodeProperty.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                if (iterator.name == "position" || iterator.name == "name") continue;

                // SignalNode's raw stream strings get the same database dropdowns as everything else
                if (_inspectedNode is SignalNode && iterator.name == "streamCategory")
                {
                    Rect streamRect = EditorGUILayout.GetControlRect();
                    streamRect = EditorGUI.PrefixLabel(streamRect, new GUIContent("Stream"));
                    NeoUISettings settings = NeoUISettings.instance;
                    IdDatabaseOptions.DrawCategoryNamePair(streamRect,
                        nodeProperty.FindPropertyRelative("streamCategory"),
                        nodeProperty.FindPropertyRelative("streamName"),
                        settings != null ? settings.streamIds : null);
                    continue;
                }
                if (_inspectedNode is SignalNode && iterator.name == "streamName") continue;

                EditorGUILayout.PropertyField(iterator, includeChildren: true);
            }

            if (EditorGUI.EndChangeCheck())
            {
                _serializedGraph.ApplyModifiedProperties();
                EditorUtility.SetDirty(_graph);
            }

            // dropdown popups and undo apply changes outside the change check above — detect them
            // with a per-frame signature of the inspected node (Layout only: once per GUI frame)
            // and refresh visuals in place
            if (Event.current.type == EventType.Layout)
            {
                string signature = ComputeSignature(_inspectedNode);
                if (signature != _inspectedSignature)
                {
                    _inspectedSignature = signature;
                    RefreshInspectedNodeVisuals();
                }
            }
        }

        /// <summary>
        /// Name field with explicit commit (enter or focus-loss) through a local buffer — typing
        /// never touches the graph, and IMGUI control-id reuse can't leak a pending rename from one
        /// node into another.
        /// </summary>
        private void DrawNameField()
        {
            if (_nameBuffer == null) _nameBuffer = _inspectedNode.name;

            GUI.SetNextControlName(NameFieldControl);
            _nameBuffer = EditorGUILayout.TextField("Name", _nameBuffer);

            bool focused = GUI.GetNameOfFocusedControl() == NameFieldControl;
            bool enterPressed = focused && Event.current.type == EventType.KeyDown &&
                                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (enterPressed)
            {
                Event.current.Use();
                CommitRename();
                GUIUtility.keyboardControl = 0;
                focused = false;
            }
            else if (_nameFieldFocused && !focused)
            {
                CommitRename(); // focus left the field
            }
            _nameFieldFocused = focused;
        }

        private void DrawNodeHeader()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(_inspectedNode.GetType().Name, EditorStyles.boldLabel);
            NeoGUI.Splitter();
            EditorGUILayout.Space(2f);
        }

        /// <summary> Renames the inspected node: unique name enforced, edges and start node retargeted, one undo step. </summary>
        private void CommitRename()
        {
            if (_inspectedNode == null || _graph == null) return;
            string trimmed = (_nameBuffer ?? "").Trim();
            string oldName = _inspectedNode.name;
            if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, oldName, StringComparison.Ordinal))
            {
                _nameBuffer = oldName;
                return;
            }

            string candidate = trimmed;
            int suffix = 1;
            while (_graph.nodes.Any(n => n != null && n != _inspectedNode &&
                                         string.Equals(n.name, candidate, StringComparison.Ordinal)))
                candidate = $"{trimmed} {++suffix}";

            Undo.RecordObject(_graph, "Rename Flow Node");
            _inspectedNode.name = candidate;
            if (!string.IsNullOrEmpty(oldName))
            {
                foreach (FlowNode node in _graph.nodes.Where(n => n != null))
                    foreach (FlowEdge edge in node.outputs)
                        if (string.Equals(edge.toNode, oldName, StringComparison.Ordinal))
                            edge.toNode = candidate;
                if (string.Equals(_graph.startNode, oldName, StringComparison.Ordinal))
                    _graph.startNode = candidate;
            }
            EditorUtility.SetDirty(_graph);
            _serializedGraph.Update();
            _nameBuffer = candidate;
            RaiseExternalChanged();
        }

        /// <summary> Refreshes title, port labels and edges of the inspected node without rebuilding the view. </summary>
        private void RefreshInspectedNodeVisuals()
        {
            FlowNodeView view = _graphView.GetView(_inspectedNode);
            if (view == null) return;

            if (_inspectedNode.outputs.Count != view.outputPorts.Count)
            {
                // outputs were added/removed in the inspector — structural, rebuild but keep selection
                _graphView.PopulatePreservingSelection();
                return;
            }

            view.RefreshTitle();
            view.RefreshPortLabels();
            _graphView.RebuildEdges();
        }

        private static string ComputeSignature(FlowNode node)
        {
            var builder = new StringBuilder(64);
            builder.Append(node.name).Append('|').Append(node.outputs.Count);
            foreach (FlowEdge edge in node.outputs)
            {
                builder.Append('|').Append(edge.portName).Append('>').Append(edge.toNode);
                if (edge.trigger != null) builder.Append('@').Append(edge.trigger.ToString());
            }
            return builder.ToString();
        }

        // ------------------------------------------------------------------ runtime debugging
        //
        // Entirely event-driven off FlowController.OnActiveNodeChanged/OnAdvanced — no editor-tick
        // polling. TryBindLiveController runs once on entering play mode, on SetGraph, and (via
        // OnGlobalSelectionChanged) when the user selects a FlowController GameObject.

        /// <summary> Finds the FlowController running the window's graph and binds to it. If several
        /// controllers share the same flow asset this silently takes the first — TODO: surface a
        /// controller picker instead once that's a real scenario (selecting the GameObject directly
        /// still pins the exact one via <see cref="OnGlobalSelectionChanged"/>). </summary>
        private void TryBindLiveController()
        {
            if (_graph == null || !Application.isPlaying)
            {
                UnbindLiveController();
                return;
            }
            FlowController found = FlowController.allControllers.FirstOrDefault(c => c.flow == _graph);
            if (found != _liveController) BindLiveController(found);
        }

        private void BindLiveController(FlowController controller)
        {
            UnbindLiveController();
            _liveController = controller;
            if (_liveController == null) return;
            _liveController.OnActiveNodeChanged += OnLiveActiveNodeChanged;
            _liveController.OnAdvanced += OnLiveAdvanced;
            OnLiveActiveNodeChanged(_liveController.activeNode); // sync current state immediately
        }

        private void UnbindLiveController()
        {
            if (_liveController != null)
            {
                _liveController.OnActiveNodeChanged -= OnLiveActiveNodeChanged;
                _liveController.OnAdvanced -= OnLiveAdvanced;
            }
            _liveController = null;
            ClearLiveVisuals();
        }

        private void ClearLiveVisuals()
        {
            if (_liveActiveNodeName != null) _graphView?.GetViewByName(_liveActiveNodeName)?.ClearHighlight();
            _liveActiveNodeName = null;
            UpdateBreadcrumb();
        }

        /// <summary> Restyles in place — no Populate — matching the runtime clone's active node back
        /// to this window's design-time view by NAME (never by reference: the controller runs a
        /// clone of the asset graph). </summary>
        private void OnLiveActiveNodeChanged(FlowNode node)
        {
            if (_graphView == null) return;
            if (_liveActiveNodeName != null) _graphView.GetViewByName(_liveActiveNodeName)?.ClearHighlight();
            _liveActiveNodeName = node != null ? node.name : null;
            if (_liveActiveNodeName != null) _graphView.GetViewByName(_liveActiveNodeName)?.SetHighlight(NeoColors.Flow, 2f);
            UpdateBreadcrumb();
        }

        /// <summary> Flashes the edge the flow just traversed (matched by the FROM node's name +
        /// output index — the runtime edge is a clone, never the design-time one). Jumps
        /// (<c>viaEdge == null</c>) have nothing to flash. </summary>
        private void OnLiveAdvanced(FlowNode node, FlowEdge edge)
        {
            if (edge == null || _liveController == null || _graphView == null) return;
            FlowNode fromNode = _liveController.previousNode;
            if (fromNode == null) return;
            int portIndex = fromNode.outputs.IndexOf(edge);
            if (portIndex < 0) return;
            FlowNodeView fromView = _graphView.GetViewByName(fromNode.name);
            if (fromView != null) _graphView.FlashEdge(fromView, portIndex);
        }

        /// <summary> Rebuilds the play-mode breadcrumb strip from the live controller's history —
        /// only called from node-change events, never per frame. </summary>
        private void UpdateBreadcrumb()
        {
            if (_breadcrumbRoot == null) return;
            if (_liveController == null)
            {
                _breadcrumbRoot.style.display = DisplayStyle.None;
                _breadcrumbRoot.Clear();
                return;
            }
            _breadcrumbRoot.style.display = DisplayStyle.Flex;
            _breadcrumbRoot.Clear();

            // history is a stack (most-recent first) — walk it oldest-to-newest so the strip reads
            // left-to-right like a breadcrumb trail, capped to the last 8 entries plus the active node
            var trail = _liveController.history.Take(8).Reverse().ToList();
            if (_liveActiveNodeName != null) trail.Add(_liveActiveNodeName);

            for (int i = 0; i < trail.Count; i++)
            {
                if (i > 0)
                    _breadcrumbRoot.Add(new Label(">")
                        { style = { color = new Color(1f, 1f, 1f, 0.35f), marginLeft = 3f, marginRight = 3f } });

                string nodeName = trail[i];
                bool isActive = i == trail.Count - 1;
                var crumb = new Label(nodeName)
                    { style = { color = isActive ? NeoColors.Flow : new Color(0.85f, 0.85f, 0.88f) } };
                if (isActive) crumb.style.unityFontStyleAndWeight = FontStyle.Bold;
                crumb.RegisterCallback<MouseDownEvent>(_ => JumpToNode(nodeName));
                _breadcrumbRoot.Add(crumb);
            }
        }
    }

    /// <summary> The GraphView surface: nodes, edges, groups, search-as-you-type + drag-off-port
    /// creation, minimap, live highlight. </summary>
    public class FlowGraphView : GraphView
    {
        private readonly FlowGraphWindow _window;
        private FlowGraph _graph;
        private readonly FlowNodeSearchWindowProvider _searchWindowProvider;

        /// <summary> The graph asset currently rendered — read-only outside this file (the search
        /// window provider needs it to list "Go To Node" targets). </summary>
        internal FlowGraph Graph => _graph;

        /// <summary> Shared with every output port's <see cref="FlowPortConnectorListener"/> so a
        /// drag-off-port opens the SAME search window instance as the canvas's create menu. </summary>
        internal FlowNodeSearchWindowProvider SearchProvider => _searchWindowProvider;

        public FlowGraphView(FlowGraphWindow window)
        {
            _window = window;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            var minimap = new MiniMap { anchored = true };
            minimap.SetPosition(new Rect(10f, 30f, 180f, 120f));
            Add(minimap);

            StyleSheet sheet = LoadStyleSheet();
            if (sheet != null) styleSheets.Add(sheet);

            _searchWindowProvider = ScriptableObject.CreateInstance<FlowNodeSearchWindowProvider>();
            _searchWindowProvider.Initialize(this, window);
            nodeCreationRequest = OnNodeCreationRequest;
            elementsAddedToGroup = OnElementsAddedToGroup;
            elementsRemovedFromGroup = OnElementsRemovedFromGroup;
            groupTitleChanged = OnGroupTitleChanged;

            graphViewChanged = OnGraphViewChanged;
        }

        /// <summary> The canvas's native "Create Node" entry point (right-click empty space / the
        /// stock node-creation shortcut) — opens the same search-as-you-type window a drag-off-port
        /// does, with no source port to wire. </summary>
        private void OnNodeCreationRequest(NodeCreationContext ctx)
        {
            if (_graph == null) return;
            _searchWindowProvider.PrepareGeneralCreate();
            SearchWindow.Open(new SearchWindowContext(ctx.screenMousePosition), _searchWindowProvider);
        }

        private const string StyleSheetPath = "Assets/Neo UI Framework/Editor/Flow/FlowGraph.uss";

        private static StyleSheet LoadStyleSheet()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (sheet == null)
            {
                // freshly created .uss may not be imported yet — import then retry once
                AssetDatabase.ImportAsset(StyleSheetPath);
                sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            }
            return sheet;
        }

        /// <summary> Selects a single node and frames it — used by the toolbar search box. </summary>
        public void FrameNode(FlowNode node)
        {
            FlowNodeView view = GetView(node);
            if (view == null) return;
            ClearSelection();
            AddToSelection(view);
            FrameSelection();
        }

        // ---- edge focus: selecting node(s) brightens their connections and dims the rest ----

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            RefreshEdgeFocus();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            RefreshEdgeFocus();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            RefreshEdgeFocus();
        }

        private void RefreshEdgeFocus()
        {
            var selectedNodes = new HashSet<Node>(selection.OfType<FlowNodeView>());
            bool anySelected = selectedNodes.Count > 0;
            foreach (Edge edge in edges.ToList())
            {
                edge.RemoveFromClassList("flow-edge--dim");
                edge.RemoveFromClassList("flow-edge--focus");
                if (!anySelected) continue;
                bool connected = (edge.output != null && selectedNodes.Contains(edge.output.node)) ||
                                 (edge.input != null && selectedNodes.Contains(edge.input.node));
                edge.AddToClassList(connected ? "flow-edge--focus" : "flow-edge--dim");
            }
        }

        public void Populate(FlowGraph graph)
        {
            _graph = graph;
            graphViewChanged = null;
            DeleteElements(graphElements.ToList());
            graphViewChanged = OnGraphViewChanged;

            if (graph == null) return;

            foreach (FlowNode node in graph.nodes.Where(n => n != null))
                AddElement(new FlowNodeView(node, graph, this));

            foreach (FlowNodeView nodeView in nodes.OfType<FlowNodeView>().ToList())
                CreateEdgesFor(nodeView);

            // groups are pure organizational metadata (Blender-frame style) — render last so their
            // frames wrap around already-placed node views; a group referencing a since-deleted node
            // name just skips it rather than throwing
            foreach (FlowGroup group in graph.groups)
            {
                var groupView = new FlowGroupView(group);
                AddElement(groupView);
                foreach (string nodeName in group.nodeNames.ToList())
                {
                    FlowNodeView memberView = GetViewByName(nodeName);
                    if (memberView != null) groupView.AddElement(memberView);
                }
            }

            RefreshEdgeFocus();
        }

        /// <summary> Full rebuild that restores the node selection afterwards (by node name). </summary>
        public void PopulatePreservingSelection()
        {
            var selectedNames = new HashSet<string>(
                selection.OfType<FlowNodeView>().Select(v => v.node.name));
            Populate(_graph);
            if (selectedNames.Count == 0) return;
            foreach (FlowNodeView view in nodes.OfType<FlowNodeView>())
                if (selectedNames.Contains(view.node.name))
                    AddToSelection(view);
        }

        /// <summary> Rebuilds only the edges from data, leaving node views (and selection) untouched. </summary>
        public void RebuildEdges()
        {
            graphViewChanged = null;
            DeleteElements(edges.ToList());
            foreach (FlowNodeView nodeView in nodes.OfType<FlowNodeView>().ToList())
                CreateEdgesFor(nodeView);
            graphViewChanged = OnGraphViewChanged;
            RefreshEdgeFocus();
        }

        public FlowNodeView GetView(FlowNode node) =>
            nodes.OfType<FlowNodeView>().FirstOrDefault(v => v.node == node);

        /// <summary> Matches by node NAME, never by reference — the play-mode debugging hooks only
        /// ever have a runtime clone's node, never the design-time asset's. </summary>
        public FlowNodeView GetViewByName(string name) =>
            nodes.OfType<FlowNodeView>().FirstOrDefault(v => v.node.name == name);

        private void CreateEdgesFor(FlowNodeView nodeView)
        {
            for (int i = 0; i < nodeView.node.outputs.Count && i < nodeView.outputPorts.Count; i++)
            {
                FlowEdge flowEdge = nodeView.node.outputs[i];
                if (string.IsNullOrEmpty(flowEdge.toNode)) continue;
                FlowNodeView targetView = nodes.OfType<FlowNodeView>().FirstOrDefault(v => v.node.name == flowEdge.toNode);
                if (targetView == null || targetView.inputPort == null) continue;
                Edge edge = nodeView.outputPorts[i].ConnectTo(targetView.inputPort);
                AddElement(edge);
            }
        }

        /// <summary> Briefly highlights the edge leaving <paramref name="fromView"/>'s output port at
        /// <paramref name="portIndex"/> — the play-mode "flow just crossed this edge" pulse. Restores
        /// itself via a one-shot scheduled callback, not a tick subscription. </summary>
        public void FlashEdge(FlowNodeView fromView, int portIndex)
        {
            if (fromView == null || portIndex < 0 || portIndex >= fromView.outputPorts.Count) return;
            Port outputPort = fromView.outputPorts[portIndex];
            Edge target = edges.ToList().FirstOrDefault(e => e.output == outputPort);
            if (target == null) return;
            target.AddToClassList("flow-edge--pulse");
            target.schedule.Execute(() => target.RemoveFromClassList("flow-edge--pulse")).StartingIn(600);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports
                .Where(p => p.direction != startPort.direction && p.node != startPort.node)
                .ToList();
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is Edge clickedEdge && _graph != null)
            {
                evt.menu.AppendAction("Insert Reroute Node", _ => InsertReroute(clickedEdge));
                evt.menu.AppendSeparator();
            }
            else if (evt.target is GraphView && _graph != null)
            {
                Vector2 graphPosition = contentViewContainer.WorldToLocal(evt.mousePosition);
                foreach (FlowNodeDescriptor descriptor in FlowNodeKinds.All)
                    AddCreateEntry(evt, graphPosition, descriptor);
                evt.menu.AppendSeparator();
            }

            List<FlowNodeView> selectedNodes = selection.OfType<FlowNodeView>().ToList();
            if (_graph != null && selectedNodes.Count > 0)
            {
                evt.menu.AppendAction("Group Selection", _ => GroupSelection(selectedNodes));
                evt.menu.AppendSeparator();
            }

            // the base implementation is also what turns on the "Create Node" entry it wires to
            // nodeCreationRequest — keep it last so cut/copy/paste/delete stay at the menu's tail
            base.BuildContextualMenu(evt);
        }

        private void AddCreateEntry(ContextualMenuPopulateEvent evt, Vector2 position, FlowNodeDescriptor descriptor)
        {
            evt.menu.AppendAction($"Create Node/{descriptor.menuLabel}", _ =>
            {
                CreateNodeAt(descriptor, position, "Create Flow Node");
                PopulatePreservingSelection();
                _window.RaiseExternalChanged();
            });
        }

        /// <summary> Core node-creation recipe shared by the right-click menu, the search-as-you-type
        /// window and drag-off-port creation: instantiate, uniquely name, position, add to the
        /// graph, seed default outputs. Callers repopulate afterward (batching in any edge wiring
        /// first, so port-drag creation is one undo step, not two). </summary>
        private FlowNode CreateNodeAt(FlowNodeDescriptor descriptor, Vector2 position, string undoLabel)
        {
            Undo.RecordObject(_graph, undoLabel);
            FlowNode node = descriptor.create();
            node.name = _graph.MakeUniqueNodeName(descriptor.menuLabel);
            node.position = position;
            _graph.nodes.Add(node);
            descriptor.seedDefaultOutputs(node);
            EditorUtility.SetDirty(_graph);
            return node;
        }

        /// <summary> Node creation from the Blueprint-style search window opened on empty canvas —
        /// no source port to wire. </summary>
        internal void CreateNodeFromSearch(FlowNodeDescriptor descriptor, Vector2 position)
        {
            if (_graph == null) return;
            CreateNodeAt(descriptor, position, "Create Flow Node");
            PopulatePreservingSelection();
            _window.RaiseExternalChanged();
        }

        /// <summary> Node creation from dragging a connection off an output port into empty space:
        /// creates the node AND wires the source port's edge to it, one undo step. </summary>
        internal void CreateNodeFromPortDrag(FlowNodeDescriptor descriptor, Vector2 position, FlowNodeView sourceView, int sourcePortIndex)
        {
            if (_graph == null || sourceView == null) return;
            FlowNode node = CreateNodeAt(descriptor, position, "Create Flow Node");
            if (sourcePortIndex >= 0 && sourcePortIndex < sourceView.node.outputs.Count)
                sourceView.node.outputs[sourcePortIndex].toNode = node.name;
            PopulatePreservingSelection();
            _window.RaiseExternalChanged();
        }

        /// <summary> Selects and frames an existing node by name — the "Go To Node" half of the
        /// search window. </summary>
        internal void JumpToNodeByName(string name)
        {
            FlowNode node = _graph?.GetNode(name);
            if (node != null) FrameNode(node);
        }

        /// <summary> Wraps the current node selection in a new organizational <see cref="FlowGroup"/>
        /// (Blender-frame style — zero execution semantics, default title "Group", default tint). </summary>
        private void GroupSelection(List<FlowNodeView> selectedNodes)
        {
            if (_graph == null || selectedNodes.Count == 0) return;
            Undo.RecordObject(_graph, "Group Flow Nodes");
            var group = new FlowGroup();
            foreach (FlowNodeView view in selectedNodes)
                if (!group.nodeNames.Contains(view.node.name))
                    group.nodeNames.Add(view.node.name);
            _graph.groups.Add(group);
            EditorUtility.SetDirty(_graph);
            PopulatePreservingSelection();
            _window.RaiseExternalChanged();
        }

        /// <summary>
        /// Splices a reroute (<see cref="PivotNode"/>) into an existing edge: the source now points at
        /// the reroute, and the reroute passes through to the original target. Lets the user bend a
        /// connection around other nodes by dragging the inserted knot. One undo step.
        /// </summary>
        private void InsertReroute(Edge edge)
        {
            if (!(edge.output?.node is FlowNodeView fromView) || !(edge.input?.node is FlowNodeView toView))
                return;
            int portIndex = fromView.outputPorts.IndexOf(edge.output);
            if (portIndex < 0 || portIndex >= fromView.node.outputs.Count) return;

            FlowEdge sourceEdge = fromView.node.outputs[portIndex];
            string originalTarget = sourceEdge.toNode;
            Vector2 midpoint = (fromView.node.position + toView.node.position) * 0.5f;

            Undo.RecordObject(_graph, "Insert Reroute Node");
            PivotNode reroute = _graph.AddNode<PivotNode>("Reroute", midpoint);
            reroute.outputs.Add(new FlowEdge { toNode = originalTarget });
            sourceEdge.toNode = reroute.name;
            EditorUtility.SetDirty(_graph);
            PopulatePreservingSelection();
            _window.RaiseExternalChanged();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_graph == null) return change;
            bool dirty = false;

            if (change.edgesToCreate != null)
            {
                foreach (Edge edge in change.edgesToCreate)
                {
                    if (edge.output.node is FlowNodeView fromView && edge.input.node is FlowNodeView toView)
                    {
                        Undo.RecordObject(_graph, "Connect Flow Nodes");
                        int portIndex = fromView.outputPorts.IndexOf(edge.output);
                        if (portIndex >= 0 && portIndex < fromView.node.outputs.Count)
                        {
                            fromView.node.outputs[portIndex].toNode = toView.node.name;
                            dirty = true;
                        }
                    }
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    switch (element)
                    {
                        case Edge edge when edge.output.node is FlowNodeView fromView:
                        {
                            Undo.RecordObject(_graph, "Disconnect Flow Nodes");
                            int portIndex = fromView.outputPorts.IndexOf(edge.output);
                            if (portIndex >= 0 && portIndex < fromView.node.outputs.Count)
                            {
                                fromView.node.outputs[portIndex].toNode = null;
                                dirty = true;
                            }
                            break;
                        }
                        case FlowNodeView nodeView:
                            Undo.RecordObject(_graph, "Delete Flow Node");
                            _graph.RemoveNode(nodeView.node.name);
                            dirty = true;
                            break;
                        case FlowGroupView groupView:
                            // deletes the organizational frame only — its member nodes are untouched,
                            // matching Blender-frame semantics
                            Undo.RecordObject(_graph, "Delete Flow Group");
                            _graph.groups.Remove(groupView.data);
                            dirty = true;
                            break;
                    }
                }
            }

            if (change.movedElements != null)
            {
                // GraphView bundles a dragged group's contained nodes into the same movedElements
                // batch as the group itself, so this generic "any FlowNodeView" loop already
                // persists positions for grouped nodes with no special-casing needed.
                foreach (GraphElement element in change.movedElements)
                {
                    if (element is FlowNodeView nodeView)
                    {
                        Undo.RecordObject(_graph, "Move Flow Node");
                        nodeView.node.position = nodeView.GetPosition().position;
                        dirty = true;
                    }
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(_graph);
                _window.RaiseExternalChanged();
            }
            return change;
        }

        // ---- group membership / rename — GraphView-level callbacks, no Group subclass needed ----

        private void OnElementsAddedToGroup(Group group, IEnumerable<GraphElement> elements)
        {
            if (_graph == null || !(group is FlowGroupView groupView)) return;
            List<string> toAdd = elements.OfType<FlowNodeView>().Select(v => v.node.name)
                .Where(name => !groupView.data.nodeNames.Contains(name)).ToList();
            if (toAdd.Count == 0) return; // e.g. Populate() reparenting nodes that already belong
            Undo.RecordObject(_graph, "Add To Flow Group");
            groupView.data.nodeNames.AddRange(toAdd);
            EditorUtility.SetDirty(_graph);
            _window.RaiseExternalChanged();
        }

        private void OnElementsRemovedFromGroup(Group group, IEnumerable<GraphElement> elements)
        {
            if (_graph == null || !(group is FlowGroupView groupView)) return;
            List<string> toRemove = elements.OfType<FlowNodeView>().Select(v => v.node.name)
                .Where(name => groupView.data.nodeNames.Contains(name)).ToList();
            if (toRemove.Count == 0) return;
            Undo.RecordObject(_graph, "Remove From Flow Group");
            foreach (string name in toRemove) groupView.data.nodeNames.Remove(name);
            EditorUtility.SetDirty(_graph);
            _window.RaiseExternalChanged();
        }

        private void OnGroupTitleChanged(Group group, string newTitle)
        {
            if (_graph == null || !(group is FlowGroupView groupView) || groupView.data.title == newTitle) return;
            Undo.RecordObject(_graph, "Rename Flow Group");
            groupView.data.title = newTitle;
            EditorUtility.SetDirty(_graph);
            _window.RaiseExternalChanged();
        }
    }

    /// <summary> Visual group frame for a <see cref="FlowGroup"/> — pure organizational metadata,
    /// zero execution semantics (Blender-frame style). Owns the backing data so the GraphView-level
    /// membership/rename callbacks (<see cref="FlowGraphView"/>) can write straight back into the
    /// asset without a lookup table. </summary>
    public class FlowGroupView : Group
    {
        public readonly FlowGroup data;

        public FlowGroupView(FlowGroup group)
        {
            data = group;
            title = group.title;
            // a translucent wash of the group's tint over the whole frame — simplest robust option
            // since Group's internal chrome (Scope-based) doesn't expose a dedicated tint hook
            style.backgroundColor = new Color(group.tint.r, group.tint.g, group.tint.b, 0.18f);
        }
    }

    /// <summary>
    /// Wired onto every output port in place of its default edge connector (Blueprint-style
    /// "drag off a pin to create a node"). A normal port-to-port drop still writes the edge through
    /// <see cref="FlowGraphView.OnGraphViewChanged"/> exactly like the connector it replaces — this
    /// class's <see cref="OnDrop"/> reimplements <c>Port.DefaultEdgeConnectorListener.OnDrop</c>
    /// (single-capacity cleanup + the standard graphViewChanged round trip) rather than bypassing it,
    /// so swapping the manipulator never changes existing drag-to-connect behavior. Dropping in empty
    /// space instead opens the search window remembering the source port, wiring the new node's
    /// inbound edge once a kind is picked.
    /// </summary>
    internal class FlowPortConnectorListener : IEdgeConnectorListener
    {
        private readonly FlowGraphView _view;
        private readonly FlowNodeView _sourceNodeView;
        private readonly int _sourcePortIndex;

        public FlowPortConnectorListener(FlowGraphView view, FlowNodeView sourceNodeView, int sourcePortIndex)
        {
            _view = view;
            _sourceNodeView = sourceNodeView;
            _sourcePortIndex = sourcePortIndex;
        }

        public void OnDrop(GraphView graphView, Edge edge)
        {
            var edgesToCreate = new List<Edge> { edge };
            var edgesToDelete = new List<GraphElement>();
            if (edge.input != null && edge.input.capacity == Port.Capacity.Single)
                foreach (Edge existing in edge.input.connections)
                    if (existing != edge) edgesToDelete.Add(existing);
            if (edge.output != null && edge.output.capacity == Port.Capacity.Single)
                foreach (Edge existing in edge.output.connections)
                    if (existing != edge) edgesToDelete.Add(existing);
            if (edgesToDelete.Count > 0) graphView.DeleteElements(edgesToDelete);

            List<Edge> created = edgesToCreate;
            if (graphView.graphViewChanged != null)
                created = graphView.graphViewChanged(new GraphViewChange { edgesToCreate = edgesToCreate }).edgesToCreate;

            foreach (Edge createdEdge in created)
            {
                graphView.AddElement(createdEdge);
                createdEdge.input?.Connect(createdEdge);
                createdEdge.output?.Connect(createdEdge);
            }
        }

        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
            _view.SearchProvider.PrepareForPortDrag(_sourceNodeView, _sourcePortIndex);
            SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(position)), _view.SearchProvider);
        }
    }

    /// <summary> Visual node: one input port, one output port per FlowEdge. </summary>
    public class FlowNodeView : Node
    {
        public readonly FlowNode node;
        public readonly Port inputPort;
        public readonly List<Port> outputPorts = new List<Port>();

        private readonly FlowGraph _graph;
        private readonly FlowGraphView _graphView;

        public FlowNodeView(FlowNode flowNode, FlowGraph graph, FlowGraphView graphView)
        {
            node = flowNode;
            _graph = graph;
            _graphView = graphView;

            bool reroute = FlowNodeStyle.IsReroute(flowNode);
            AddToClassList("flow-node");
            if (reroute) AddToClassList("reroute-node");

            RefreshTitle();
            ApplyAccent(reroute);
            SetPosition(new Rect(flowNode.position, new Vector2(reroute ? 40f : 200f, reroute ? 40f : 120f)));

            if (flowNode.isExecutable)
            {
                inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
                inputPort.portName = reroute ? "" : "In";
                inputContainer.Add(inputPort);
            }

            foreach (FlowEdge edge in flowNode.outputs)
                AddOutputPort(edge);

            // reroute knots are fixed single pass-throughs — no manual extra outputs
            if (!reroute)
            {
                var addPortButton = new Button(() =>
                {
                    Undo.RecordObject(_graph, "Add Flow Edge");
                    var edge = new FlowEdge { portName = "Out" };
                    node.outputs.Add(edge);
                    AddOutputPort(edge);
                    EditorUtility.SetDirty(_graph);
                    RefreshExpandedState();
                    RefreshPorts();
                }) { text = "+" };
                titleButtonContainer.Add(addPortButton);
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        /// <summary> Paints the node's category accent as a left stripe on the card border. </summary>
        private void ApplyAccent(bool reroute)
        {
            if (reroute) return; // the knot stays neutral grey
            VisualElement border = this.Q("node-border");
            if (border == null) return;
            border.style.borderLeftWidth = 4f;
            border.style.borderLeftColor = FlowNodeStyle.Accent(node);
        }

        public void RefreshTitle()
        {
            title = $"{node.name}  <{node.GetType().Name.Replace("Node", "")}>";
        }

        public void RefreshPortLabels()
        {
            for (int i = 0; i < outputPorts.Count && i < node.outputs.Count; i++)
                outputPorts[i].portName = DescribePort(node.outputs[i]);
        }

        private void AddOutputPort(FlowEdge edge)
        {
            Port port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
            port.portName = FlowNodeStyle.IsReroute(node) ? "" : DescribePort(edge);
            int portIndex = outputPorts.Count;
            outputPorts.Add(port);
            outputContainer.Add(port);
            WirePortDragToSearch(port, portIndex);
        }

        /// <summary> Replaces the port's default edge connector with one that opens the node-creation
        /// search window (remembering this port) when a drag ends outside any port. See
        /// <see cref="FlowPortConnectorListener"/> for why normal port-to-port drags are unaffected. </summary>
        private void WirePortDragToSearch(Port port, int portIndex)
        {
            if (_graphView == null) return; // defensive — always set by FlowGraphView.Populate in practice
            port.RemoveManipulator(port.edgeConnector);
            var listener = new FlowPortConnectorListener(_graphView, this, portIndex);
            port.AddManipulator(new EdgeConnector<Edge>(listener));
        }

        private static string DescribePort(FlowEdge edge)
        {
            if (edge.trigger != null && edge.trigger.type != FlowTrigger.TriggerType.None)
                return edge.trigger.ToString();
            return string.IsNullOrEmpty(edge.portName) ? "Out" : edge.portName;
        }

        public void SetHighlight(Color color, float width)
        {
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = color;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = width;
        }

        public void ClearHighlight()
        {
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 0f;
        }
    }
}
