using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The <c>spacer</c> element kind had zero test coverage (audit §2.5) despite its export detection
    /// being a fragile heuristic (<see cref="UISpecExporter"/>: "has a <see cref="LayoutElement"/> with a
    /// positive flexible width/height and no <see cref="Graphic"/>"). This pins the whole loop: generate
    /// a spacer between two texts, confirm it comes back as kind "spacer" in the right position, and
    /// that export → generate → export is a fixed point (pattern: <see cref="SpecLayoutAndWidgetTests"/>).
    /// </summary>
    public class SpacerRoundTripTests
    {
        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""Spec/Spacer"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
              { ""text"": { ""label"": ""Above"" } },
              { ""spacer"": {} },
              { ""text"": { ""label"": ""Below"" } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Spec_Spacer.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        [Test]
        public void Spacer_Generates_AFlexibleLayoutElement_WithNoGraphic()
        {
            GameObject prefab = GenerateView();

            LayoutElement spacer = prefab.GetComponentsInChildren<LayoutElement>(true)
                .FirstOrDefault(le => le.GetComponent<Graphic>() == null);
            Assert.IsNotNull(spacer, "spacer must be a bare LayoutElement with no Graphic");
            Assert.IsTrue(spacer.flexibleWidth > 0f || spacer.flexibleHeight > 0f,
                "spacer must flex along at least one axis");
            // inside a vstack it flexes vertically (the stack's main axis) only
            Assert.AreEqual(1f, spacer.flexibleHeight);
            Assert.AreEqual(0f, spacer.flexibleWidth);
        }

        [Test]
        public void Spacer_RoundTrips_AsSpacerKind_InPosition()
        {
            GenerateView();

            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.FirstOrDefault(v => v.id == "Spec/Spacer");
            Assert.IsNotNull(view);

            ElementSpec stack = view.elements.FirstOrDefault(e => e.kind == "vstack");
            Assert.IsNotNull(stack, "vstack must export as vstack");
            CollectionAssert.AreEqual(
                new[] { "text", "spacer", "text" },
                stack.children.Select(c => c.kind).ToArray(),
                "the spacer must round-trip in place between the two texts");
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint()
        {
            GenerateView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "export -> generate -> export must be stable for a spacer, or agents can't safely round-trip it");
        }
    }
}
