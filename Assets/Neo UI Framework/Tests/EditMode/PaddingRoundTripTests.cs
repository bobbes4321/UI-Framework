using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Per-side container padding (<c>padding4</c>): an additive, backward-compatible alternative to
    /// the uniform <c>padding</c>. Verifies the RectOffset mapping, that the per-side form survives
    /// export → generate → export byte-identically, that uniform <c>padding</c> is untouched, and the
    /// accepted normalization contract (a uniform <c>padding4:[8,8,8,8]</c> round-trips to <c>padding:8</c>).
    /// </summary>
    public class PaddingRoundTripTests
    {
        // spec array order is [left, top, right, bottom]; Unity RectOffset is (left, right, top, bottom)
        private const string PerSideJson = @"{
          ""views"": [ { ""id"": ""Spec/Padding4"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding4"": [8, 16, 24, 32], ""spacing"": 4, ""children"": [
              { ""text"": { ""label"": ""Row"", ""color"": ""TextDefault"" } }
            ] } },
            { ""grid"": { ""columns"": 2, ""cellSize"": [50, 50], ""padding4"": [1, 2, 3, 4], ""children"": [
              { ""shape"": { ""shape"": ""Circle"" } }
            ] } }
          ] } ]
        }";

        private const string UniformJson = @"{
          ""views"": [ { ""id"": ""Spec/PaddingUniform"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 12, ""spacing"": 4, ""children"": [
              { ""text"": { ""label"": ""Row"", ""color"": ""TextDefault"" } }
            ] } }
          ] } ]
        }";

        // a uniform padding4 normalizes to uniform padding on export — the documented contract
        private const string UniformPadding4Json = @"{
          ""views"": [ { ""id"": ""Spec/PaddingUniform4"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding4"": [8, 8, 8, 8], ""spacing"": 4, ""children"": [
              { ""text"": { ""label"": ""Row"", ""color"": ""TextDefault"" } }
            ] } }
          ] } ]
        }";

        // neither padding nor padding4 — legacy container, unchanged
        private const string NeitherJson = @"{
          ""views"": [ { ""id"": ""Spec/PaddingNone"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""spacing"": 4, ""children"": [
              { ""text"": { ""label"": ""Row"", ""color"": ""TextDefault"" } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Padding4_GeneratesPerSideRectOffset_AndRoundTripsByteIdentical()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(PerSideJson));
            Assert.IsEmpty(report.issues, report.ToString());

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Spec_Padding4.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");

            VerticalLayoutGroup stack = prefab.GetComponentsInChildren<VerticalLayoutGroup>(true)
                .FirstOrDefault(g => g.GetComponent<ScrollRect>() == null && g.transform.parent == prefab.transform);
            Assert.IsNotNull(stack, "vstack should become a VerticalLayoutGroup");
            // spec [left=8, top=16, right=24, bottom=32] → RectOffset(left=8, right=24, top=16, bottom=32)
            Assert.AreEqual(8, stack.padding.left);
            Assert.AreEqual(24, stack.padding.right);
            Assert.AreEqual(16, stack.padding.top);
            Assert.AreEqual(32, stack.padding.bottom);

            var grid = prefab.GetComponentInChildren<GridLayoutGroup>(true);
            Assert.IsNotNull(grid);
            Assert.AreEqual(1, grid.padding.left);
            Assert.AreEqual(3, grid.padding.right);
            Assert.AreEqual(2, grid.padding.top);
            Assert.AreEqual(4, grid.padding.bottom);

            // padding4 survives export, and export → generate → export is a fixed point
            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec exportedStack = exported.views.First(v => v.id == "Spec/Padding4")
                .elements.First(e => e.kind == "vstack");
            Assert.IsNull(exportedStack.padding, "per-side container must not emit uniform padding");
            CollectionAssert.AreEqual(new[] { 8f, 16f, 24f, 32f }, exportedStack.padding4);

            string firstExport = exported.ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(firstExport, secondExport, "padding4 export must be a stable fixed point");
        }

        [Test]
        public void UniformPadding_RoundTripsAsUniform_NoPadding4()
        {
            UISpecGenerator.Generate(UISpec.FromJson(UniformJson));
            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec stack = exported.views.First(v => v.id == "Spec/PaddingUniform")
                .elements.First(e => e.kind == "vstack");
            Assert.AreEqual(12f, stack.padding, "uniform padding must round-trip as uniform padding");
            Assert.IsNull(stack.padding4, "uniform padding must not emit padding4");

            string firstExport = exported.ToJson();
            UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            string secondExport = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(firstExport, secondExport);
        }

        [Test]
        public void UniformPadding4_NormalizesToUniformPadding()
        {
            UISpecGenerator.Generate(UISpec.FromJson(UniformPadding4Json));
            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec stack = exported.views.First(v => v.id == "Spec/PaddingUniform4")
                .elements.First(e => e.kind == "vstack");
            // documented contract: four equal sides normalize to the uniform form (semantically identical)
            Assert.AreEqual(8f, stack.padding, "uniform padding4 must normalize to uniform padding");
            Assert.IsNull(stack.padding4, "uniform padding4 must not re-emit padding4");
        }

        [Test]
        public void NoPadding_LeavesContainerUnchanged()
        {
            UISpecGenerator.Generate(UISpec.FromJson(NeitherJson));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Spec_PaddingNone.prefab");
            Assert.IsNotNull(prefab);
            VerticalLayoutGroup stack = prefab.GetComponentsInChildren<VerticalLayoutGroup>(true)
                .FirstOrDefault(g => g.GetComponent<ScrollRect>() == null && g.transform.parent == prefab.transform);
            Assert.IsNotNull(stack);
            Assert.AreEqual(0, stack.padding.left);
            Assert.AreEqual(0, stack.padding.right);
            Assert.AreEqual(0, stack.padding.top);
            Assert.AreEqual(0, stack.padding.bottom);

            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec exportedStack = exported.views.First(v => v.id == "Spec/PaddingNone")
                .elements.First(e => e.kind == "vstack");
            Assert.IsNull(exportedStack.padding4, "a container with no per-side padding emits no padding4");
        }
    }
}
