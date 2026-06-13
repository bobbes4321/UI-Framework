using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// End-to-end for the agent pillar: parse the worked-example spec → generate real assets →
    /// validate → export back to spec text → re-parse and compare. Generated assets are deleted
    /// afterwards; the settings/databases the package owns are left in place.
    /// </summary>
    public class GeneratorEndToEndTests
    {
        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);

            // compact the preset database: deleting preset assets leaves null entries behind
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.animationPresets != null)
            {
                foreach (UIAnimationPreset preset in settings.animationPresets.Presets.Where(p => p == null).ToList())
                    settings.animationPresets.Remove(preset);
                EditorUtility.SetDirty(settings.animationPresets);
            }

            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Generate_Validate_Export_RoundTrip()
        {
            UISpec spec = UISpec.FromJson(SpecTests.WorkedExampleJson);

            // ---- generate
            GenerateReport report = UISpecGenerator.Generate(spec);
            Assert.IsEmpty(report.issues, $"generation issues:\n{report}");
            Assert.IsEmpty(report.collisions, $"generation collisions:\n{report}");

            NeoUISettings settings = NeoUISettings.instance;
            Assert.IsNotNull(settings, "settings asset should exist after generation");

            // theme tokens landed
            Assert.IsTrue(settings.theme.TryGetColor("Primary", out Color primary));
            Assert.AreEqual("#3A86FF", ColorUtils.ToHex(primary));

            // presets landed + registered
            UIAnimationPreset slideIn = settings.animationPresets.Get("SlideInLeft");
            Assert.IsNotNull(slideIn);
            Assert.IsTrue(slideIn.animation.move.enabled);
            Assert.AreEqual(UIMoveDirection.Left, slideIn.animation.move.fromDirection);
            Assert.IsTrue(slideIn.animation.fade.enabled);
            Assert.That(slideIn.animation.move.settings.duration, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.AreEqual(Ease.OutCubic, slideIn.animation.move.settings.ease);

            // view prefab landed with the right components
            string viewPath = $"{UISpecGenerator.GeneratedRoot}/Views/Menu_Main.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(viewPath);
            Assert.IsNotNull(prefab, $"expected generated prefab at {viewPath}");
            Assert.IsNotNull(prefab.GetComponent<GeneratedMarker>());

            UIView view = prefab.GetComponent<UIView>();
            Assert.IsNotNull(view);
            Assert.AreEqual("Menu/Main", view.id.ToString());

            UIContainerUIAnimator animator = prefab.GetComponent<UIContainerUIAnimator>();
            Assert.IsNotNull(animator);
            Assert.IsTrue(animator.showAnimation.move.enabled, "show animation should come from the SlideInLeft preset");
            Assert.AreEqual(UIMoveDirection.Left, animator.showAnimation.move.fromDirection);

            UIButton playButton = prefab.GetComponentsInChildren<UIButton>(true)
                .FirstOrDefault(b => b.id.Matches("Action", "Play"));
            Assert.IsNotNull(playButton, "Action/Play button should exist in the generated view");
            UIActionBehaviour click = playButton.GetBehaviour(BehaviourTrigger.Click);
            Assert.IsNotNull(click);
            Assert.IsTrue(click.sendSignal);
            Assert.AreEqual("Gameplay/StartPainting", click.signalStream.ToString());

            // theme targets bound
            Assert.IsTrue(prefab.GetComponentsInChildren<ThemeColorTarget>(true).Any(t => t.token == "Primary"));

            // ids registered in the databases
            Assert.IsTrue(settings.viewIds.Contains("Menu", "Main"));
            Assert.IsTrue(settings.viewIds.Contains("Menu", "Settings"));
            Assert.IsTrue(settings.buttonIds.Contains("Action", "Play"));
            Assert.IsTrue(settings.streamIds.Contains("Gameplay", "StartPainting"));

            // flow graph landed
            string flowPath = $"{UISpecGenerator.GeneratedRoot}/Flow/TestUI.asset";
            var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(flowPath);
            Assert.IsNotNull(graph);
            Assert.IsNotNull(graph.GetNode("Start"));
            Assert.IsNotNull(graph.GetNode("MainMenu"));
            Assert.IsNotNull(graph.GetNode("Settings"));
            Assert.IsEmpty(graph.Validate(), "generated graph should validate clean");

            var mainMenu = (UINode)graph.GetNode("MainMenu");
            Assert.AreEqual("Menu", mainMenu.showViews[0].category);
            FlowEdge toSettings = mainMenu.outputs.First(e => e.toNode == "Settings");
            Assert.AreEqual(FlowTrigger.TriggerType.ButtonClick, toSettings.trigger.type);

            // ---- validate
            var issues = AgentValidation.ValidateAll();
            Assert.IsEmpty(issues, $"validation should pass after generation:\n{string.Join("\n", issues)}");

            // ---- idempotent re-generation: same spec → updates, no collisions, no duplicates
            GenerateReport second = UISpecGenerator.Generate(spec);
            Assert.IsEmpty(second.collisions, $"re-generation collided:\n{second}");
            Assert.IsEmpty(second.issues, $"re-generation issues:\n{second}");
            Assert.AreEqual(1, settings.animationPresets.Presets.Count(p => p != null && p.presetName == "SlideInLeft"));

            // ---- export and compare
            UISpec exported = UISpecExporter.ExportProject();
            Assert.IsTrue(exported.theme.tokens.ContainsKey("Primary"));
            Assert.AreEqual("#3A86FF", exported.theme.tokens["Primary"]);
            Assert.IsTrue(exported.presets.Any(p => p.name == "SlideInLeft"));

            ViewSpec exportedMain = exported.views.FirstOrDefault(v => v.id == "Menu/Main");
            Assert.IsNotNull(exportedMain, "exported spec should contain Menu/Main");
            Assert.AreEqual("SlideInLeft", exportedMain.showAnimation);
            ElementSpec exportedPlay = exportedMain.elements.FirstOrDefault(e => e.id == "Action/Play");
            Assert.IsNotNull(exportedPlay);
            Assert.AreEqual("Gameplay", exportedPlay.onClickSignal.category);

            Assert.IsNotNull(exported.flow);
            Assert.AreEqual("MainMenu", exported.flow.start);
            Assert.IsTrue(exported.flow.nodes.Any(n => n.name == "Settings"));

            // exported text re-parses
            UISpec reparsed = UISpec.FromJson(exported.ToJson());
            Assert.AreEqual(exported.views.Count, reparsed.views.Count);
        }

        [Test]
        public void Generator_ReportsCollision_ForHandMadeAssetAtGeneratedPath()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Collision/Test"" } ] }";

            // plant a hand-made prefab (no GeneratedMarker) at the generated path
            string folder = $"{UISpecGenerator.GeneratedRoot}/Views";
            if (!AssetDatabase.IsValidFolder(UISpecGenerator.GeneratedRoot))
                AssetDatabase.CreateFolder("Assets", "Neo UI Generated");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(UISpecGenerator.GeneratedRoot, "Views");

            var handMade = new GameObject("Collision_Test", typeof(RectTransform));
            PrefabUtility.SaveAsPrefabAsset(handMade, $"{folder}/Collision_Test.prefab");
            Object.DestroyImmediate(handMade);

            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.AreEqual(1, report.collisions.Count, $"expected a collision report:\n{report}");
            StringAssert.Contains("Collision/Test", report.collisions[0]);

            // the hand-made prefab must be untouched
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/Collision_Test.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsNull(prefab.GetComponent<UIView>(), "hand-made prefab must not be overwritten");
        }
    }
}
