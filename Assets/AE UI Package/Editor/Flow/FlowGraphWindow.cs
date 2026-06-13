using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AlterEyes.EditorUI;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Flow graph editor: pan/zoom/grid, node create menu, minimap, side inspector for the selected
    /// node, undo-aware editing — and runtime debugging: in play mode the active node and its
    /// traversal trail highlight live.
    ///
    /// The node inspector uses ONE cached SerializedObject (so foldout/expansion state survives
    /// between frames) and never rebuilds the graph view on value edits (so typing in the name field
    /// doesn't destroy the selection). Node renames propagate to every edge that targets the node
    /// by name, and to the graph's start node.
    /// </summary>
    public class FlowGraphWindow : EditorWindow
    {
        private const string NameFieldControl = "AEFlowNodeNameField";

        [SerializeField] private FlowGraph _graph; // serialized: survives domain reloads
        private FlowGraphView _graphView;
        private IMGUIContainer _inspector;
        private FlowController _liveController;
        private string _liveActiveNode;
        private readonly List<string> _liveTrail = new List<string>();

        // node inspector state — cached so IMGUI state (foldouts, list expansion) survives frames
        private SerializedObject _serializedGraph;
        private FlowNode _inspectedNode;
        private string _inspectedSignature;
        private string _nameBuffer;
        private bool _nameFieldFocused;

        [MenuItem("Tools/AlterEyes UI/Flow Graph Editor", priority = 10)]
        public static void Open() => GetWindow<FlowGraphWindow>("Flow Graph");

        public static void Open(FlowGraph graph)
        {
            var window = GetWindow<FlowGraphWindow>("Flow Graph");
            window.SetGraph(graph);
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
            EditorApplication.update += PollRuntimeState;
            Undo.undoRedoPerformed += OnUndoRedo;
            UnityEditor.Selection.selectionChanged += OnGlobalSelectionChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollRuntimeState;
            Undo.undoRedoPerformed -= OnUndoRedo;
            UnityEditor.Selection.selectionChanged -= OnGlobalSelectionChanged;
            _serializedGraph?.Dispose();
            _serializedGraph = null;
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

            if (_graph != null) _graphView.Populate(_graph);
        }

        public void SetGraph(FlowGraph graph)
        {
            _graph = graph;
            _liveController = null;
            _liveTrail.Clear();
            _inspectedNode = null;
            _inspectedSignature = null;
            _serializedGraph?.Dispose();
            _serializedGraph = graph != null ? new SerializedObject(graph) : null;
            // populate even when null so clearing the field empties the view (a stale view would
            // keep writing into the unloaded asset)
            _graphView?.Populate(graph);
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
            if (_graph != controller.flow) SetGraph(controller.flow);
            _liveController = controller;
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
                    AEUISettings settings = AEUISettings.instance;
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
            AEGUI.Splitter();
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

        private void PollRuntimeState()
        {
            if (!Application.isPlaying || _graph == null || _graphView == null)
            {
                if (_liveActiveNode != null)
                {
                    _liveActiveNode = null;
                    _liveTrail.Clear();
                    _graphView?.UpdateLiveHighlight(null, _liveTrail);
                }
                return;
            }

            if (_liveController == null || _liveController.flow != _graph)
                _liveController = FlowController.allControllers.FirstOrDefault(c => c.flow == _graph);

            string active = _liveController != null && _liveController.activeNode != null
                ? _liveController.activeNode.name
                : null;

            if (active == _liveActiveNode) return;
            if (_liveActiveNode != null)
            {
                _liveTrail.Add(_liveActiveNode);
                if (_liveTrail.Count > 10) _liveTrail.RemoveAt(0);
            }
            _liveActiveNode = active;
            _graphView.UpdateLiveHighlight(active, _liveTrail);
        }
    }

    /// <summary> The GraphView surface: nodes, edges, create menu, minimap, live highlight. </summary>
    public class FlowGraphView : GraphView
    {
        private static readonly Color ActiveColor = new Color(0.2f, 0.9f, 0.3f);
        private static readonly Color TrailColor = new Color(0.2f, 0.5f, 0.9f);

        private readonly FlowGraphWindow _window;
        private FlowGraph _graph;

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

            graphViewChanged = OnGraphViewChanged;
        }

        private const string StyleSheetPath = "Assets/AE UI Package/Editor/Flow/FlowGraph.uss";

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
                AddElement(new FlowNodeView(node, graph));

            foreach (FlowNodeView nodeView in nodes.OfType<FlowNodeView>().ToList())
                CreateEdgesFor(nodeView);

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

        public void UpdateLiveHighlight(string activeNodeName, List<string> trail)
        {
            foreach (FlowNodeView view in nodes.OfType<FlowNodeView>())
            {
                if (view.node.name == activeNodeName) view.SetHighlight(ActiveColor, 3f);
                else if (trail.Contains(view.node.name)) view.SetHighlight(TrailColor, 1.5f);
                else view.ClearHighlight();
            }
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
                AddCreateEntry<StartNode>(evt, graphPosition);
                AddCreateEntry<UINode>(evt, graphPosition);
                AddCreateEntry<SignalNode>(evt, graphPosition);
                AddCreateEntry<BackButtonNode>(evt, graphPosition);
                AddCreateEntry<PortalNode>(evt, graphPosition);
                AddCreateEntry<RandomNode>(evt, graphPosition);
                AddCreateEntry<TimeScaleNode>(evt, graphPosition);
                AddCreateEntry<ApplicationQuitNode>(evt, graphPosition);
                AddCreateEntry<PivotNode>(evt, graphPosition, "Reroute");
                AddCreateEntry<StickyNoteNode>(evt, graphPosition);
                AddCreateEntry<DebugNode>(evt, graphPosition);
                evt.menu.AppendSeparator();
            }
            base.BuildContextualMenu(evt);
        }

        private void AddCreateEntry<T>(ContextualMenuPopulateEvent evt, Vector2 position, string displayName = null) where T : FlowNode, new()
        {
            string label = displayName ?? typeof(T).Name.Replace("Node", "");
            evt.menu.AppendAction($"Create Node/{label}", _ =>
            {
                Undo.RecordObject(_graph, "Create Flow Node");
                FlowNode node = _graph.AddNode<T>(label, position);
                if (node is UINode || node is PortalNode || node is RandomNode)
                    node.outputs.Add(new FlowEdge());
                else if (!(node is StickyNoteNode || node is ApplicationQuitNode))
                    node.outputs.Add(new FlowEdge { portName = "Next" });
                EditorUtility.SetDirty(_graph);
                PopulatePreservingSelection();
            });
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
                    }
                }
            }

            if (change.movedElements != null)
            {
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

            if (dirty) EditorUtility.SetDirty(_graph);
            return change;
        }
    }

    /// <summary> Visual node: one input port, one output port per FlowEdge. </summary>
    public class FlowNodeView : Node
    {
        public readonly FlowNode node;
        public readonly Port inputPort;
        public readonly List<Port> outputPorts = new List<Port>();

        private readonly FlowGraph _graph;

        public FlowNodeView(FlowNode flowNode, FlowGraph graph)
        {
            node = flowNode;
            _graph = graph;

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
            outputPorts.Add(port);
            outputContainer.Add(port);
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
