using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The view-transition spec surface: a flow edge's "transition" field (a ViewTransitionAsset full
    /// name) bakes onto the generated FlowEdge, and an element's "sharedElement" field bakes a
    /// NeoSharedElement marker — both round-trip export→generate→export byte-identically. Uses a
    /// self-registered in-memory ViewTransitionAsset so the test never depends on the shipped library
    /// (mirrors ElementAnimationsRoundTripTests' self-made temp preset).
    /// </summary>
    public class TransitionRoundTripTests
    {
        private const string TransitionCategory = "Test";
        private const string TransitionName = "Fade";
        private const string TransitionFullName = TransitionCategory + "/" + TransitionName;

        private const string SpecJson = @"{
          ""views"": [
            { ""id"": ""RT/Screen"", ""elements"": [
                { ""button"": { ""id"": ""RT/Go"", ""label"": ""Go"" } },
                { ""shape"": { ""shape"": ""Circle"", ""size"": [40, 40], ""sharedElement"": ""hero"" } }
            ] },
            { ""id"": ""RT/Screen2"", ""elements"": [
                { ""text"": { ""label"": ""Second"" } }
            ] }
          ],
          ""flow"": {
            ""name"": ""RTFlow"",
            ""start"": ""First"",
            ""nodes"": [
              { ""name"": ""First"", ""view"": ""RT/Screen"", ""next"": [
                { ""on"": { ""button"": ""RT/Go"" }, ""to"": ""Second"", ""transition"": """ + TransitionFullName + @""" }
              ] },
              { ""name"": ""Second"", ""view"": ""RT/Screen2"", ""next"": [] }
            ]
          }
        }";

        private ViewTransitionAsset _transition;

        [SetUp]
        public void SetUp()
        {
            _transition = ScriptableObject.CreateInstance<ViewTransitionAsset>();
            _transition.category = TransitionCategory;
            _transition.transitionName = TransitionName;
            ViewTransitionRegistry.Register(_transition);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
            ViewTransitionRegistry.ResetForTests();

            // NeoUISettings is a committed asset — EnsureRuntimeResolvable may have appended the test's
            // in-memory transition to it; strip it back out so the settings asset is left exactly as found.
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.viewTransitions != null)
            {
                int removed = settings.viewTransitions.RemoveAll(t => t == null || t.fullName == TransitionFullName);
                if (removed > 0)
                {
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }
            }

            if (_transition != null) Object.DestroyImmediate(_transition);
        }

        private static FlowGraph GenerateAndLoadFlow()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>($"{UISpecGenerator.FlowFolder}/RTFlow.asset");
            Assert.IsNotNull(graph, "generated flow graph missing");
            return graph;
        }

        private static GameObject LoadScreenPrefab() =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{UISpecGenerator.ViewsFolder}/RT_Screen.prefab");

        [Test]
        public void Generate_BakesEdgeTransition_AndSharedElement()
        {
            FlowGraph graph = GenerateAndLoadFlow();

            UINode first = graph.nodes.OfType<UINode>().First(n => n.name == "First");
            FlowEdge edge = first.outputs.First(e => e.toNode == "Second");
            Assert.AreEqual(TransitionFullName, edge.transition, "edge transition name is copied onto the runtime FlowEdge");

            GameObject prefab = LoadScreenPrefab();
            Assert.IsNotNull(prefab, "generated view prefab missing");
            var shared = prefab.GetComponentInChildren<NeoSharedElement>(true);
            Assert.IsNotNull(shared, "sharedElement bakes a NeoSharedElement component");
            Assert.AreEqual("hero", shared.key);
        }

        [Test]
        public void Generate_MissingTransitionName_ReportsIssue_NeverSilent()
        {
            ViewTransitionRegistry.ResetForTests(); // the registered "Test/Fade" is now unresolvable

            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsTrue(report.issues.Any(i => i.Contains(TransitionFullName)),
                $"a missing transition name must be reported, not silently dropped:\n{report}");
        }

        [Test]
        public void Export_RecoversTransitionAndSharedElement_AndRoundTripsByteIdentical()
        {
            GenerateAndLoadFlow();

            UISpec exported = UISpecExporter.ExportProject();
            Assert.IsNotNull(exported.flow, "flow graph exports back to a FlowSpec");
            FlowNodeSpec firstNode = exported.flow.nodes.First(n => n.name == "First");
            FlowEdgeSpec exportedEdge = firstNode.next.First(e => e.to == "Second");
            Assert.AreEqual(TransitionFullName, exportedEdge.transition, "transition name recovered on export");

            ElementSpec shape = exported.views.First(v => v.id == "RT/Screen").elements.First(e => e.kind == "shape");
            Assert.AreEqual("hero", shape.sharedElement, "sharedElement key recovered on export");

            string first = UISpecExporter.ExportProject().ToJson();
            UISpecGenerator.Generate(UISpec.FromJson(first));
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second, "transition + sharedElement must round-trip byte-identically");
        }

        [Test]
        public void EdgeWithoutTransition_ExportsWithoutKey()
        {
            var edge = new FlowEdgeSpec { to = "Foo" };
            var json = edge.ToJsonObject();
            Assert.IsFalse(json.ContainsKey("transition"), "an edge with no transition must omit the key entirely (absent != empty)");
        }
    }
}
