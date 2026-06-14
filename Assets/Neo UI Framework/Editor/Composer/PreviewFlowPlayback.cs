using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Live flow playback for the Composer preview (Pillar G §G.2.3). Mirrors the in-memory
    /// instantiation pattern of <c>Tests/EditMode/GeneratedFlowPlaythroughTests.cs</c>: it builds the
    /// spec's flow-referenced views plus an in-memory <see cref="FlowGraph"/>/<see cref="FlowController"/>,
    /// starts the flow, and lets the author "click" interactive elements. Each click fires that
    /// element's REAL button/tab signal (<see cref="UIButton.Click"/> / <see cref="UIToggle.Toggle"/>),
    /// the synchronous flow dispatch advances the graph, and the set of currently-shown views is
    /// recomputed from the active node's <see cref="UINode.showViews"/>.
    ///
    /// <para><b>Preview-only, no persisted mutation.</b> Everything is built into a throwaway preview
    /// scene and a <c>CreateInstance</c> graph; <see cref="Stop"/> tears it all down. Nothing touches the
    /// spec, the baked prefab, any committed asset, or real <c>UIData</c>. View <em>visibility</em> is
    /// queried from the flow graph (the active node's <c>showViews</c>), NOT <see cref="UIView"/>'s
    /// registry — in edit mode a view's <c>OnEnable</c> never runs, so the registry is empty (exactly why
    /// the reference test asserts navigation at the graph level).</para>
    ///
    /// <para><b>Graphics-free by design</b> so it is unit-testable headlessly: <see cref="Begin"/>,
    /// <see cref="ClickElement"/>, <see cref="ClickById"/> and the visibility queries never touch a GPU.
    /// The render pane reads <see cref="ActiveViewIds"/> and renders the shown views itself.</para>
    /// </summary>
    public sealed class PreviewFlowPlayback
    {
        // all built objects (views + controller) parent under this hidden, never-saved root, so the whole
        // session tears down with one DestroyImmediate and nothing leaks into the user's scene/hierarchy.
        private GameObject _root;
        private FlowGraph _graph;
        private FlowController _controller;

        // built objects keyed by the SAME ElementSpec instances the document holds (reference equality,
        // via UISpecGenerator.ElementObjectSink) — so the render pane maps a clicked box straight to the
        // element here and we fire its real interaction.
        private readonly Dictionary<ElementSpec, GameObject> _elementObjects = new Dictionary<ElementSpec, GameObject>();
        private readonly List<UIView> _views = new List<UIView>();
        private readonly List<string> _activeViewIds = new List<string>();

        /// <summary> True between a successful <see cref="Begin"/> and the next <see cref="Stop"/>. </summary>
        public bool IsPlaying { get; private set; }

        /// <summary> The flow's currently-active node name (the status-line readout); null when stopped. </summary>
        public string CurrentNodeName => _controller != null ? _controller.activeNode?.name : null;

        /// <summary>
        /// The "Category/Name" ids the active node shows right now (empty on a non-UI node). Backed by a
        /// reused list so polling it every OnGUI pass allocates nothing.
        /// </summary>
        public IReadOnlyList<string> ActiveViewIds
        {
            get
            {
                _activeViewIds.Clear();
                if (_controller != null && _controller.activeNode is UINode node)
                    foreach (UINode.ViewRef view in node.showViews)
                        _activeViewIds.Add($"{view.category}/{view.viewName}");
                return _activeViewIds;
            }
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>
        /// Instantiates the flow + its referenced views in a throwaway preview scene and starts the flow.
        /// Returns false (with <paramref name="error"/> set) when the spec has no flow to play; idempotent
        /// — calls <see cref="Stop"/> first so a restart never leaks the previous session.
        /// </summary>
        public bool Begin(UISpec spec, out string error)
        {
            error = null;
            Stop();

            if (spec?.flow == null || spec.flow.nodes == null || spec.flow.nodes.Count == 0)
            {
                error = "This spec has no flow to play — author a flow first.";
                return false;
            }

            _graph = BuildGraph(spec.flow);

            _root = new GameObject("NeoComposer Playback") { hideFlags = HideFlags.HideAndDontSave };

            // a temp spec holding the ACTUAL document ViewSpec instances (shared ElementSpec refs) so the
            // sink keys line up with the render pane's boxes; only the flow-referenced views are built
            var temp = new UISpec { theme = spec.theme, presets = spec.presets };
            foreach (string id in CollectReferencedViews(spec.flow))
            {
                ViewSpec view = FindView(spec, id);
                if (view == null)
                {
                    // no silent failure: a flow that references a view the spec doesn't define would
                    // black-screen at that node — surface it (the node still runs, the view is just blank)
                    Debug.LogWarning($"[Neo.UI] Playback: flow references view '{id}' that the spec does not define.");
                    continue;
                }
                if (!temp.views.Contains(view)) temp.views.Add(view);
            }

            List<GameObject> roots;
            UISpecGenerator.ElementObjectSink = _elementObjects;
            try { roots = UISpecPreview.BuildViews(temp); }
            finally { UISpecGenerator.ElementObjectSink = null; }

            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                root.hideFlags = HideFlags.HideAndDontSave;
                root.transform.SetParent(_root.transform, worldPositionStays: false);
                UIView view = root.GetComponent<UIView>();
                if (view != null) _views.Add(view);
            }

            var controllerGo = new GameObject("Playback Controller") { hideFlags = HideFlags.HideAndDontSave };
            controllerGo.transform.SetParent(_root.transform, worldPositionStays: false);
            _controller = controllerGo.AddComponent<FlowController>();
            _controller.flow = _graph;
            // mirror the runtime lifecycle rule: never auto-start cross-object behavior from OnEnable
            // (it won't run in edit mode anyway) — we drive StartFlow explicitly, like the reference test
            _controller.onEnableBehaviour = FlowController.ControllerBehaviour.DoNothing;
            _controller.onDisableBehaviour = FlowController.ControllerBehaviour.StopFlow;
            _controller.goBackOnBackButton = false; // preview: don't hijack the global back-button stream
            _controller.StartFlow();

            IsPlaying = true;
            return true;
        }

        /// <summary> Stops the flow and destroys every temp object/scene/graph. Safe to call repeatedly. </summary>
        public void Stop()
        {
            // stop the flow first so its listeners disconnect before the objects they reference vanish
            if (_controller != null) _controller.StopFlow();
            _controller = null;
            _views.Clear();
            _elementObjects.Clear();
            if (_root != null) { Object.DestroyImmediate(_root); _root = null; }
            if (_graph != null) { Object.DestroyImmediate(_graph); _graph = null; }
            IsPlaying = false;
        }

        // ------------------------------------------------------------------ interaction

        /// <summary>
        /// Fires the interaction of the element the author clicked in the preview. The element is mapped
        /// by reference (the render pane hands back the same <see cref="ElementSpec"/> the sink keyed on).
        /// Returns true when a button/tab/toggle actually fired (so the pane re-renders the new state).
        /// </summary>
        public bool ClickElement(ElementSpec element)
        {
            if (!IsPlaying || element == null) return false;
            if (!_elementObjects.TryGetValue(element, out GameObject go) || go == null) return false;
            return TriggerInteraction(go);
        }

        /// <summary>
        /// Test/code entry point: clicks the button addressed by "Category/Name" anywhere in the built
        /// views (mirrors <c>GeneratedFlowPlaythroughTests.ClickButton</c>). Warns — never fails silently —
        /// when no such button exists.
        /// </summary>
        public bool ClickById(string category, string name)
        {
            if (!IsPlaying) return false;
            foreach (UIView view in _views)
            {
                if (view == null) continue;
                foreach (UIButton button in view.GetComponentsInChildren<UIButton>(includeInactive: true))
                    if (button.id.Matches(category, name)) { button.Click(); return true; }
                foreach (UIToggle toggle in view.GetComponentsInChildren<UIToggle>(includeInactive: true))
                    if (toggle.id.Matches(category, name)) { toggle.Toggle(); return true; }
            }
            Debug.LogWarning($"[Neo.UI] Playback: no clickable element '{category}/{name}' in the playing views.");
            return false;
        }

        /// <summary> True when the named view is one the active node currently shows. </summary>
        public bool IsViewActive(string category, string viewName)
        {
            foreach (string id in ActiveViewIds)
            {
                CategoryNameId.Parse(id, out string c, out string n);
                if (c == category && n == viewName) return true;
            }
            return false;
        }

        private static bool TriggerInteraction(GameObject go)
        {
            // the render pane hands back the DEEPEST element under the cursor, so a click on a button
            // resolves to the button element itself — its widget root carries the UIButton/UIToggle
            UIButton button = go.GetComponentInChildren<UIButton>(includeInactive: true);
            if (button != null) { button.Click(); return true; }
            UIToggle toggle = go.GetComponentInChildren<UIToggle>(includeInactive: true);
            if (toggle != null) { toggle.Toggle(); return true; }
            return false; // a non-interactive element — a normal click on empty space, no-op (not an error)
        }

        // ------------------------------------------------------------------ graph build

        /// <summary>
        /// Builds an in-memory <see cref="FlowGraph"/> from a <see cref="FlowSpec"/> — the same node/edge
        /// shape <c>UISpecGenerator.GenerateFlow</c> bakes, minus the asset write/report/validation (this
        /// graph is throwaway and HideAndDontSave). Kept here, not shared with the generator, so playback
        /// never reaches into the core quartet.
        /// </summary>
        private static FlowGraph BuildGraph(FlowSpec flowSpec)
        {
            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            graph.hideFlags = HideFlags.HideAndDontSave;
            graph.graphName = flowSpec.name;
            graph.nodes.Clear();

            StartNode start = graph.AddNode<StartNode>("Start");
            if (!string.IsNullOrEmpty(flowSpec.start))
                start.outputs.Add(new FlowEdge { portName = "Start", toNode = flowSpec.start, allowsBack = false });

            foreach (FlowNodeSpec nodeSpec in flowSpec.nodes)
            {
                UINode node = graph.AddNode<UINode>(nodeSpec.name);
                node.hideShownViewsOnExit = true;
                foreach (string shown in nodeSpec.views)
                {
                    CategoryNameId.Parse(shown, out string category, out string name);
                    node.showViews.Add(new UINode.ViewRef(category, name));
                }
                foreach (string hidden in nodeSpec.hide)
                {
                    CategoryNameId.Parse(hidden, out string category, out string name);
                    node.hideViews.Add(new UINode.ViewRef(category, name));
                }
                foreach (FlowEdgeSpec edgeSpec in nodeSpec.next)
                    node.outputs.Add(new FlowEdge
                    {
                        portName = edgeSpec.trigger != null ? edgeSpec.trigger.ToString() : "Next",
                        toNode = edgeSpec.to,
                        allowsBack = edgeSpec.allowsBack,
                        trigger = edgeSpec.trigger
                    });
            }

            graph.startNode = "Start";
            return graph;
        }

        // every view the flow names (shown or hidden across all nodes) — the set we must build so the
        // flow can show/hide and the author can click into them
        private static IEnumerable<string> CollectReferencedViews(FlowSpec flowSpec)
        {
            var seen = new HashSet<string>();
            foreach (FlowNodeSpec node in flowSpec.nodes)
            {
                if (node.views != null)
                    foreach (string id in node.views) if (!string.IsNullOrEmpty(id) && seen.Add(id)) yield return id;
                if (node.hide != null)
                    foreach (string id in node.hide) if (!string.IsNullOrEmpty(id) && seen.Add(id)) yield return id;
            }
        }

        private static ViewSpec FindView(UISpec spec, string id)
        {
            if (spec.views == null) return null;
            CategoryNameId.Parse(id, out string category, out string name);
            foreach (ViewSpec view in spec.views)
                if (view != null && view.category == category && view.viewName == name) return view;
            return null;
        }
    }
}
