using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// "Connect to…" direct-manipulation flow wiring (<see cref="NeoFlowWiring"/>). Builds a bare
    /// in-memory button+view graph (no generator, no disk assets) against a throwaway
    /// <see cref="FlowGraph"/> instance, mirroring <c>SceneBuilderFlowScopingTests</c>' style.
    /// <para>
    /// <see cref="NeoFlowWiring"/> registers connected button/view ids into the live project
    /// <see cref="NeoUISettings"/> databases, exactly like <c>UISpecGenerator.RegisterId</c> — TearDown
    /// removes the "FlowWiringTest" category it uses so repeated runs never leave test ids behind in
    /// the committed settings asset.
    /// </para>
    /// </summary>
    public class FlowWiringTests
    {
        private const string Category = "FlowWiringTest";

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private FlowGraph _graph;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_graph != null) Object.DestroyImmediate(_graph);
            _graph = null;

            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null)
            {
                settings.buttonIds?.RemoveCategory(Category);
                settings.viewIds?.RemoveCategory(Category);
            }
        }

        private FlowGraph NewGraph()
        {
            _graph = ScriptableObject.CreateInstance<FlowGraph>();
            _graph.name = "TestGraph";
            return _graph;
        }

        private UIView MakeView(string name)
        {
            var go = new GameObject($"View_{name}", typeof(RectTransform));
            _spawned.Add(go);
            UIView view = go.AddComponent<UIView>();
            view.id.Category = Category;
            view.id.Name = name;
            return view;
        }

        private UIButton MakeButton(string name, UIView parentView)
        {
            var go = new GameObject($"Button_{name}", typeof(RectTransform));
            _spawned.Add(go);
            go.transform.SetParent(parentView.transform, false);
            UIButton button = go.AddComponent<UIButton>();
            button.id.Category = Category;
            button.id.Name = name;
            return button;
        }

        [Test]
        public void Connect_CreatesEdgeWithCorrectTriggerPortAndTransition()
        {
            FlowGraph graph = NewGraph();
            UIView source = MakeView("Source1");
            UIView target = MakeView("Target1");
            UIButton button = MakeButton("Go1", source);

            NeoFlowWiring.WiringResult result = NeoFlowWiring.ConnectButtonToView(
                graph, button, target, "Push/SlideLeft", allowsBack: false);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsNotNull(result.edge);
            Assert.AreEqual("Go1", result.edge.portName);
            Assert.AreEqual(result.toNode.name, result.edge.toNode);
            Assert.IsFalse(result.edge.allowsBack);
            Assert.AreEqual("Push/SlideLeft", result.edge.transition);
            Assert.AreEqual(FlowTrigger.TriggerType.ButtonClick, result.edge.trigger.type);
            Assert.AreEqual(Category, result.edge.trigger.category);
            Assert.AreEqual("Go1", result.edge.trigger.name);
            Assert.IsTrue(result.createdFromNode);
            Assert.IsTrue(result.createdToNode);
            Assert.IsFalse(result.alreadyExisted);

            // registered exactly like the generator does
            NeoUISettings settings = NeoUISettings.instance;
            Assert.IsTrue(settings.buttonIds.Contains(Category, "Go1"));
            Assert.IsTrue(settings.viewIds.Contains(Category, "Source1"));
            Assert.IsTrue(settings.viewIds.Contains(Category, "Target1"));
        }

        [Test]
        public void Connect_ReusesExistingNodeShowingTheSourceView_NoDuplicateNode()
        {
            FlowGraph graph = NewGraph();
            UIView source = MakeView("Source2");
            UIView target = MakeView("Target2");
            UIButton button = MakeButton("Go2", source);

            UINode existingNode = graph.AddNode<UINode>("Existing2");
            existingNode.showViews.Add(new UINode.ViewRef(Category, "Source2"));

            NeoFlowWiring.WiringResult result = NeoFlowWiring.ConnectButtonToView(graph, button, target, null);

            Assert.IsTrue(result.ok, result.error);
            Assert.IsFalse(result.createdFromNode, "must reuse the existing node, not create a second one");
            Assert.AreSame(existingNode, result.fromNode);
            Assert.IsTrue(result.createdToNode, "the target view had no node yet — one is expected");
            // exactly the reused source node + the newly created target node, no duplicate source node
            Assert.AreEqual(2, graph.nodes.Count(n => n is UINode));
            Assert.AreEqual(1, NeoFlowWiring.NodesShowingView(graph, Category, "Source2").Count);
        }

        [Test]
        public void Connect_DuplicateConnect_ReturnsExistingEdgeWithoutAppending()
        {
            FlowGraph graph = NewGraph();
            UIView source = MakeView("Source3");
            UIView target = MakeView("Target3");
            UIButton button = MakeButton("Go3", source);

            NeoFlowWiring.WiringResult first = NeoFlowWiring.ConnectButtonToView(graph, button, target, null);
            Assert.IsTrue(first.ok, first.error);

            NeoFlowWiring.WiringResult second = NeoFlowWiring.ConnectButtonToView(graph, button, target, null);

            Assert.IsTrue(second.ok, second.error);
            Assert.IsTrue(second.alreadyExisted);
            Assert.AreSame(first.edge, second.edge);
            Assert.AreEqual(1, first.fromNode.outputs.Count, "must not append a second identical edge");
        }

        [Test]
        public void Connect_AmbiguousSourceNode_SurfacesCandidates()
        {
            FlowGraph graph = NewGraph();
            UIView source = MakeView("Source4");
            UIView target = MakeView("Target4");
            UIButton button = MakeButton("Go4", source);

            UINode nodeA = graph.AddNode<UINode>("A4");
            nodeA.showViews.Add(new UINode.ViewRef(Category, "Source4"));
            UINode nodeB = graph.AddNode<UINode>("B4");
            nodeB.showViews.Add(new UINode.ViewRef(Category, "Source4"));

            NeoFlowWiring.WiringResult ambiguous = NeoFlowWiring.ConnectButtonToView(graph, button, target, null);

            Assert.IsFalse(ambiguous.ok);
            Assert.IsNotNull(ambiguous.fromCandidates);
            CollectionAssert.AreEquivalent(new[] { "A4", "B4" }, ambiguous.fromCandidates);
            // nothing should have been mutated on the ambiguous attempt
            Assert.IsEmpty(nodeA.outputs);
            Assert.IsEmpty(nodeB.outputs);
        }

        [Test]
        public void Connect_ExplicitFromNode_ResolvesAmbiguity()
        {
            FlowGraph graph = NewGraph();
            UIView source = MakeView("Source5");
            UIView target = MakeView("Target5");
            UIButton button = MakeButton("Go5", source);

            UINode nodeA = graph.AddNode<UINode>("A5");
            nodeA.showViews.Add(new UINode.ViewRef(Category, "Source5"));
            UINode nodeB = graph.AddNode<UINode>("B5");
            nodeB.showViews.Add(new UINode.ViewRef(Category, "Source5"));

            NeoFlowWiring.WiringResult resolved = NeoFlowWiring.ConnectButtonToView(
                graph, button, target, null, allowsBack: true, explicitFromNode: nodeB, explicitToNode: null);

            Assert.IsTrue(resolved.ok, resolved.error);
            Assert.AreSame(nodeB, resolved.fromNode);
            Assert.IsEmpty(nodeA.outputs, "the node NOT chosen must stay untouched");
            Assert.AreEqual(1, nodeB.outputs.Count);
        }

        [Test]
        public void Connect_ButtonOutsideAnyView_Errors()
        {
            FlowGraph graph = NewGraph();
            UIView target = MakeView("Target6");
            var orphan = new GameObject("OrphanButton", typeof(RectTransform));
            _spawned.Add(orphan);
            UIButton button = orphan.AddComponent<UIButton>();
            button.id.Category = Category;
            button.id.Name = "Orphan";

            NeoFlowWiring.WiringResult result = NeoFlowWiring.ConnectButtonToView(graph, button, target, null);

            Assert.IsFalse(result.ok);
            StringAssert.Contains("not inside a UIView", result.error);
        }

        [Test]
        public void NodesShowingView_ReturnsOnlyNodesThatShowIt()
        {
            FlowGraph graph = NewGraph();
            UINode match = graph.AddNode<UINode>("Match7");
            match.showViews.Add(new UINode.ViewRef(Category, "V7"));
            UINode other = graph.AddNode<UINode>("Other7");
            other.showViews.Add(new UINode.ViewRef(Category, "SomethingElse"));

            List<UINode> found = NeoFlowWiring.NodesShowingView(graph, Category, "V7");

            CollectionAssert.AreEqual(new[] { match }, found);
        }
    }
}
