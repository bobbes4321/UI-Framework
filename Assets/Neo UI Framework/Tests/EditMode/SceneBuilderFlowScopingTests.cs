using System.Collections.Generic;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Regression net for the cross-spec contamination bug: the generated folder is a single shared
    /// bucket, so generating a second spec (e.g. ColorACube) alongside the showcase (GameUI) leaves
    /// both apps' views and BOTH flow graphs in it. The scene builder used to instance every view and
    /// silently pick the first flow on disk — so the showcase came up showing color-a-cube screens.
    ///
    /// The fix makes the build flow-scoped: it builds exactly one named flow and instances only the
    /// views THAT flow references, and refuses to guess when several flows are present. These tests
    /// exercise that selection logic directly (no scene / disk needed) so the invariant can't regress.
    /// </summary>
    public class SceneBuilderFlowScopingTests
    {
        private static FlowGraph MakeFlow(string name, params (string category, string view)[] views)
        {
            var graph = ScriptableObject.CreateInstance<FlowGraph>();
            graph.name = name;
            graph.graphName = name;
            var node = new UINode { name = "Screen" };
            foreach ((string category, string view) in views)
                node.showViews.Add(new UINode.ViewRef(category, view));
            graph.nodes.Add(node);
            return graph;
        }

        private FlowGraph _gameUi;
        private FlowGraph _colorACube;

        [SetUp]
        public void Setup()
        {
            _gameUi = MakeFlow("GameUI", ("Game", "HUD"), ("Menu", "Main"), ("Shop", "Store"));
            _colorACube = MakeFlow("ColorACube", ("Game", "Painting"), ("Menu", "Collection"));
        }

        [TearDown]
        public void Teardown()
        {
            if (_gameUi != null) Object.DestroyImmediate(_gameUi);
            if (_colorACube != null) Object.DestroyImmediate(_colorACube);
        }

        [Test]
        public void MultipleFlows_NoNameSpecified_Throws()
        {
            var flows = new List<FlowGraph> { _colorACube, _gameUi };
            // the heart of the bug: it must NOT silently pick one (ColorACube sorted first on disk)
            var ex = Assert.Throws<System.InvalidOperationException>(
                () => GeneratedSceneBuilder.SelectFlowGraph(flows, null));
            StringAssert.Contains("GameUI", ex.Message);
            StringAssert.Contains("ColorACube", ex.Message);
        }

        [Test]
        public void MultipleFlows_NamedFlow_SelectsThatOne()
        {
            var flows = new List<FlowGraph> { _colorACube, _gameUi };
            Assert.AreSame(_gameUi, GeneratedSceneBuilder.SelectFlowGraph(flows, "GameUI"));
            Assert.AreSame(_colorACube, GeneratedSceneBuilder.SelectFlowGraph(flows, "ColorACube"));
        }

        [Test]
        public void NamedFlow_Missing_ThrowsListingAvailable()
        {
            var flows = new List<FlowGraph> { _colorACube, _gameUi };
            var ex = Assert.Throws<System.InvalidOperationException>(
                () => GeneratedSceneBuilder.SelectFlowGraph(flows, "DoesNotExist"));
            StringAssert.Contains("GameUI", ex.Message);
        }

        [Test]
        public void SingleFlow_NoNameSpecified_UsesIt()
        {
            var flows = new List<FlowGraph> { _gameUi };
            Assert.AreSame(_gameUi, GeneratedSceneBuilder.SelectFlowGraph(flows, null));
        }

        [Test]
        public void ReferencedViewKeys_AreScopedToTheChosenFlow()
        {
            HashSet<string> keys = GeneratedSceneBuilder.CollectReferencedViewKeys(_gameUi);

            // exactly GameUI's views — none of the color-a-cube screens leak in
            CollectionAssert.AreEquivalent(
                new[] { "Game/HUD", "Menu/Main", "Shop/Store" }, keys);
            CollectionAssert.DoesNotContain(keys, "Game/Painting");
            CollectionAssert.DoesNotContain(keys, "Menu/Collection");
        }
    }
}
