using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Neo.UI.Tests
{
    /// <summary>
    /// <see cref="GeneratedSceneBuilder"/>'s actual scene construction never executed in any test
    /// (audit §2.5) — <see cref="SceneBuilderScenePathTests"/> only checks method shapes via reflection
    /// and <see cref="SceneBuilderFlowScopingTests"/> only checks the view-selection logic in isolation.
    /// This generates a small flow-bearing spec into the scratch root and runs the REAL
    /// <see cref="GeneratedSceneBuilder.Build(string,string)"/>, asserting the produced scene has a
    /// Canvas + EventSystem + wired FlowController and instances ONLY the views the flow references —
    /// the cross-spec contamination bug <see cref="SceneBuilderFlowScopingTests"/> guards at the unit
    /// level, exercised here end to end.
    /// </summary>
    public class SceneBuilderExecutionTests
    {
        private const string ScratchScenePath = "Assets/NeoUITestScratch/Scenes/SceneBuilderExecutionTest.unity";

        private const string SpecJson = @"{
          ""views"": [
            { ""id"": ""Menu/Main"", ""elements"": [
              { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
                { ""button"": { ""id"": ""Menu/Play"", ""label"": ""Play"" } }
              ] } }
            ] },
            { ""id"": ""Game/HUD"", ""elements"": [
              { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
                { ""button"": { ""id"": ""HUD/Pause"", ""label"": ""Pause"" } }
              ] } }
            ] },
            { ""id"": ""Extra/Unused"", ""elements"": [
              { ""text"": { ""label"": ""Not referenced by any flow node"" } }
            ] }
          ],
          ""flow"": {
            ""name"": ""SceneBuilderExec"", ""start"": ""MainMenu"",
            ""nodes"": [
              { ""name"": ""MainMenu"", ""view"": ""Menu/Main"",
                ""next"": [ { ""to"": ""Playing"", ""on"": { ""button"": ""Menu/Play"" } } ] },
              { ""name"": ""Playing"", ""view"": ""Game/HUD"",
                ""next"": [ { ""to"": ""MainMenu"", ""on"": { ""button"": ""HUD/Pause"" } } ] }
            ]
          }
        }";

        [OneTimeSetUp]
        public void GenerateDemo()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.collisions, report.ToString());
            Assert.IsEmpty(report.issues, report.ToString());
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            // step off the built scene before deleting its asset, so the delete doesn't fight the
            // still-active loaded scene
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScratchScenePath);
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Build_ProducesAPlayableScene_ScopedToTheFlowsViews()
        {
            string builtPath = GeneratedSceneBuilder.Build("SceneBuilderExec", ScratchScenePath);

            Assert.AreEqual(ScratchScenePath, builtPath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScratchScenePath),
                "the scene asset must be written to disk");

            Scene scene = SceneManager.GetActiveScene();
            Assert.AreEqual(ScratchScenePath, scene.path, "the built scene must be the active/open scene");

            GameObject[] roots = scene.GetRootGameObjects();
            Assert.IsTrue(roots.Any(r => r.GetComponent<Canvas>() != null), "scene must contain a Canvas");
            Assert.IsTrue(roots.Any(r => r.GetComponent<EventSystem>() != null), "scene must contain an EventSystem");
            FlowController controller = roots.Select(r => r.GetComponent<FlowController>()).FirstOrDefault(c => c != null);
            Assert.IsNotNull(controller, "scene must contain a FlowController");
            Assert.AreEqual("SceneBuilderExec", controller.flow?.graphName, "the controller must be wired to the built flow");

            UIView[] views = Object.FindObjectsByType<UIView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            CollectionAssert.AreEquivalent(
                new[] { "Menu/Main", "Game/HUD" },
                views.Select(v => $"{v.id.Category}/{v.id.Name}").ToArray(),
                "only the views the flow references should be instanced -- 'Extra/Unused' must not leak in");
        }
    }
}
