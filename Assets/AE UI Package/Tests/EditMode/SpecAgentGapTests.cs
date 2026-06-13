using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlterEyes.UI;
using AlterEyes.UI.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// The v3 agent surface: safe areas, input fields, steppers, multi-view flow nodes and the
    /// agent bridge (generate/export/validate/screenshot over the request file) — including the
    /// exporter fixed-point guarantee for all of it.
    /// </summary>
    public class SpecAgentGapTests
    {
        private const string GapSpecJson = @"{
          ""views"": [ { ""id"": ""Gap/Form"", ""elements"": [
            { ""safearea"": { ""children"": [
              { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 24, ""spacing"": 10, ""children"": [
                { ""input"": { ""label"": ""Enter name..."" } },
                { ""stepper"": { ""id"": ""Gap/Count"", ""min"": 0, ""max"": 10, ""value"": 4, ""step"": 2 } },
                { ""progress"": { ""min"": 0, ""max"": 100, ""value"": 72 } },
                { ""text"": { ""label"": ""Left-aligned"", ""align"": ""left"" } }
              ] } }
            ] } }
          ] } ],
          ""flow"": { ""name"": ""GapFlow"", ""start"": ""Home"", ""nodes"": [
            { ""name"": ""Home"", ""views"": [""Gap/Form"", ""Gap/Hud""], ""hide"": [""Gap/Splash""],
              ""next"": [ { ""on"": { ""back"": true }, ""to"": ""Home"" } ] } ] }
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateGapView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(GapSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Gap_Form.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        [Test]
        public void SafeArea_Generates_AndHostsFreeAnchoredChildren()
        {
            GameObject prefab = GenerateGapView();
            var fitter = prefab.GetComponentInChildren<SafeAreaFitter>(true);
            Assert.IsNotNull(fitter, "safearea must add a SafeAreaFitter");
            var rect = (RectTransform)fitter.transform;
            Assert.AreEqual(Vector2.zero, rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);
            Assert.IsNotNull(fitter.GetComponentInChildren<UnityEngine.UI.VerticalLayoutGroup>(true),
                "safearea children must build inside it");
        }

        [Test]
        public void InputField_Generates_Functional()
        {
            GameObject prefab = GenerateGapView();
            var input = prefab.GetComponentInChildren<TMP_InputField>(true);
            Assert.IsNotNull(input);
            Assert.IsNotNull(input.textComponent, "input text must be wired");
            Assert.IsNotNull(input.textViewport, "input viewport must be wired");
            Assert.IsNotNull(input.placeholder, "input placeholder must be wired");
            Assert.AreEqual("Enter name...", ((TMP_Text)input.placeholder).text);
            Assert.IsNotNull(input.GetComponent<AEShape>(), "input background is AEShape-based");
        }

        [Test]
        public void Stepper_Generates_WiredAndClamped()
        {
            GameObject prefab = GenerateGapView();
            var stepper = prefab.GetComponentInChildren<UIStepper>(true);
            Assert.IsNotNull(stepper);
            Assert.AreEqual(0f, stepper.minValue);
            Assert.AreEqual(10f, stepper.maxValue);
            Assert.AreEqual(2f, stepper.stepSize);
            Assert.AreEqual(4f, stepper.currentValue);
            Assert.IsNotNull(stepper.plusButton, "plus button must be wired");
            Assert.IsNotNull(stepper.minusButton, "minus button must be wired");
            Assert.IsTrue(stepper.minusButton.id.Matches("Gap", "Count_Minus"),
                "stepper buttons carry derived ids");

            Transform valueLabel = stepper.transform.Find(UIWidgetFactory.ValueName);
            Assert.IsNotNull(valueLabel, "stepper needs a value label");
            Assert.AreEqual("4", valueLabel.GetComponent<TMP_Text>().text);

            AEUISettings settings = AEUISettings.instance;
            Assert.IsTrue(settings.buttonIds.Contains("Gap", "Count_Plus"),
                "derived button ids must be registered for validation");
        }

        [Test]
        public void FlowNodes_RoundTrip_MultiViewAndHide()
        {
            GenerateGapView();
            var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(
                $"{UISpecGenerator.GeneratedRoot}/Flow/GapFlow.asset");
            Assert.IsNotNull(graph, "flow graph missing");

            UINode home = graph.nodes.OfType<UINode>().FirstOrDefault(n => n.name == "Home");
            Assert.IsNotNull(home);
            Assert.AreEqual(2, home.showViews.Count, "both views must be shown");
            Assert.AreEqual("Hud", home.showViews[1].viewName);
            Assert.AreEqual(1, home.hideViews.Count, "hide list must populate hideViews");
            Assert.AreEqual("Splash", home.hideViews[0].viewName);

            FlowSpec exported = UISpecExporter.ExportFlow(graph);
            FlowNodeSpec exportedHome = exported.nodes.First(n => n.name == "Home");
            CollectionAssert.AreEqual(new[] { "Gap/Form", "Gap/Hud" }, exportedHome.views);
            CollectionAssert.AreEqual(new[] { "Gap/Splash" }, exportedHome.hide);
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_ForNewKinds()
        {
            GenerateGapView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "safearea/input/stepper and multi-view flow nodes must round-trip stably");

            UISpec exported = UISpec.FromJson(secondExport);
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Gap/Form");
            Assert.IsNotNull(view);
            ElementSpec safeArea = view.elements.FirstOrDefault(e => e.kind == "safearea");
            Assert.IsNotNull(safeArea, "safearea must export as safearea");
            ElementSpec stack = safeArea.children.FirstOrDefault(e => e.kind == "vstack");
            Assert.IsNotNull(stack, "safearea children must round-trip");
            CollectionAssert.AreEqual(new[] { "input", "stepper", "progress", "text" },
                stack.children.Select(c => c.kind).ToArray());
            ElementSpec stepper = stack.children.First(c => c.kind == "stepper");
            Assert.AreEqual("Gap/Count", stepper.id, "stepper id must be recovered from button ids");
            Assert.AreEqual(2f, stepper.step);
            ElementSpec progress = stack.children.First(c => c.kind == "progress");
            Assert.AreEqual(72f, progress.value, "progress start value must round-trip");
            ElementSpec text = stack.children.First(c => c.kind == "text");
            Assert.AreEqual("left", text.align, "text alignment must round-trip");
        }

        // ------------------------------------------------------------------ agent bridge

        [Test]
        public void AgentBridge_Validate_ReturnsIssueList()
        {
            var result = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(
                "{\"action\":\"validate\"}")), "result");
            Assert.IsTrue(result.ContainsKey("ok"));
            Assert.IsInstanceOf<List<object>>(result["issues"]);
        }

        [Test]
        public void AgentBridge_Export_InlinesParseableSpec()
        {
            GenerateGapView();
            var result = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(
                "{\"action\":\"export\"}")), "result");
            Assert.AreEqual(true, result["ok"]);
            UISpec spec = UISpec.FromJson((string)result["spec"]);
            Assert.IsTrue(spec.views.Any(v => v.id == "Gap/Form"));
        }

        [Test]
        public void AgentBridge_Generate_RunsSpecFile()
        {
            string specPath = "Temp/aeui-test-gap-spec.json";
            File.WriteAllText(specPath, GapSpecJson);
            try
            {
                var result = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(
                    "{\"action\":\"generate\",\"spec\":\"" + specPath + "\"}")), "result");
                Assert.AreEqual(true, result["ok"], MiniJson.Serialize(result));
                var created = (List<object>)result["created"];
                var updated = (List<object>)result["updated"];
                Assert.IsTrue(created.Count + updated.Count > 0, "generate must report what it produced");
            }
            finally
            {
                File.Delete(specPath);
            }
        }

        [Test]
        public void AgentBridge_UnknownAction_ReportsError()
        {
            var result = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(
                "{\"action\":\"nope\"}")), "result");
            Assert.AreEqual(false, result["ok"]);
            Assert.IsTrue(((string)result["error"]).Contains("nope"));
        }
    }
}
