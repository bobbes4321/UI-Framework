using Neo.UI.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Regression for architecture audit A8: <see cref="UIWidgetFactory.ApplyTextOutline"/> used to
    /// hardcode the committed default <see cref="UISpecGenerator.DefaultGeneratedRoot"/> plus a
    /// literal "Materials" subfolder instead of deriving the folder from the CURRENT
    /// <see cref="UISpecGenerator.GeneratedRoot"/>, so showcase and test-scratch generates leaked
    /// outline materials into the committed shared root (and scratch teardown never deleted them).
    /// </summary>
    public class TextOutlineGeneratedRootTests
    {
        private const string OutlineSpecJson = @"{
          ""views"": [ { ""id"": ""OutlineRoot/Screen"", ""elements"": [
            { ""text"": { ""label"": ""Outlined"", ""outlineColor"": ""#000000"", ""outlineWidth"": 0.3 } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void ApplyTextOutline_WritesMaterialUnderCurrentGeneratedRoot_NotTheHardcodedDefault()
        {
            // NeoTestScratchRoot (the assembly-wide [SetUpFixture]) has already redirected
            // GeneratedRoot to a scratch folder for the whole EditMode run.
            Assert.AreNotEqual(UISpecGenerator.DefaultGeneratedRoot, UISpecGenerator.GeneratedRoot,
                "expected the scratch fixture to have redirected GeneratedRoot for this test run");

            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(OutlineSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/OutlineRoot_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            var text = prefab.GetComponentInChildren<TextMeshProUGUI>();
            Assert.IsNotNull(text, "expected the generated text element");
            Assert.IsNotNull(text.fontSharedMaterial, "expected an outline material to be assigned");

            string materialPath = AssetDatabase.GetAssetPath(text.fontSharedMaterial);
            StringAssert.StartsWith($"{UISpecGenerator.GeneratedRoot}/Materials/", materialPath,
                "outline material should be written under the CURRENT GeneratedRoot");
            Assert.IsFalse(materialPath.StartsWith(UISpecGenerator.DefaultGeneratedRoot + "/"),
                "outline material leaked into the committed default GeneratedRoot: " + materialPath);
        }
    }
}
