using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The sacred invariant for the constraint+offset model: export → generate → export is
    /// byte-identical, for every constraint × axis, for per-child sizing modes, AND for the legacy
    /// anchor-preset path (which must stay untouched when no `layout` is present). The NeoLayoutTag
    /// marker is what makes the reverse-map deterministic; these tests are its proof.
    /// </summary>
    public class ConstraintLayoutRoundTripTests
    {
        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject Generate(string json, string viewFile)
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/{viewFile}.prefab");
            Assert.IsNotNull(prefab, $"generated view prefab '{viewFile}' missing");
            return prefab;
        }

        private static void AssertFixedPoint(string json, string viewFile)
        {
            Generate(json, viewFile);
            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(firstExport, secondExport,
                "export → generate → export must be byte-identical for the layout model");
        }

        // ----------------------------------------------------------------- per constraint × axis

        [TestCase("left", "top")]
        [TestCase("right", "top")]
        [TestCase("right", "bottom")]
        [TestCase("center", "center")]
        [TestCase("left", "bottom")]
        public void EdgeAndCenter_Constraints_RoundTripByteIdentical(string h, string v)
        {
            string json = $@"{{ ""views"": [ {{ ""id"": ""Spec/Cstr"", ""elements"": [
              {{ ""image"": {{ ""layout"": {{ ""h"": ""{h}"", ""v"": ""{v}"",
                 ""offset"": {{ ""left"": 12, ""right"": 12, ""top"": 16, ""bottom"": 16, ""h"": 8, ""v"": -8 }},
                 ""size"": {{ ""w"": 200, ""h"": 80 }} }} }} }}
            ] }} ] }}";
            AssertFixedPoint(json, "Spec_Cstr");
        }

        [TestCase("leftRight", "top")]
        [TestCase("left", "topBottom")]
        [TestCase("leftRight", "topBottom")]
        public void Stretch_Constraints_RoundTripByteIdentical(string h, string v)
        {
            string json = $@"{{ ""views"": [ {{ ""id"": ""Spec/Cstr"", ""elements"": [
              {{ ""image"": {{ ""layout"": {{ ""h"": ""{h}"", ""v"": ""{v}"",
                 ""offset"": {{ ""left"": 24, ""right"": 32, ""top"": 16, ""bottom"": 20 }},
                 ""size"": {{ ""w"": 200, ""h"": 80 }} }} }} }}
            ] }} ] }}";
            AssertFixedPoint(json, "Spec_Cstr");
        }

        [Test]
        public void Scale_Constraint_RoundTripByteIdentical()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Spec/Cstr"", ""elements"": [
              { ""image"": { ""layout"": { ""h"": ""scale"", ""v"": ""scale"",
                 ""offset"": { ""left"": 0.1, ""right"": 0.9, ""top"": 0.8, ""bottom"": 0.2 } } } }
            ] } ] }";
            AssertFixedPoint(json, "Spec_Cstr");
        }

        // ----------------------------------------------------------------- generated geometry

        [Test]
        public void RightConstraint_GluesElementToParentRightEdge()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Spec/Cstr"", ""elements"": [
              { ""image"": { ""layout"": { ""h"": ""right"", ""v"": ""top"",
                 ""offset"": { ""right"": 20, ""top"": 30 }, ""size"": { ""w"": 200, ""h"": 60 } } } }
            ] } ] }";
            GameObject prefab = Generate(json, "Spec_Cstr");
            RectTransform image = prefab.GetComponentsInChildren<Image>(true)
                .Select(i => (RectTransform)i.transform).First(r => r.name.StartsWith("image"));

            // anchored to the right edge (anchors 1, pivot 1), offset is -right
            Assert.AreEqual(new Vector2(1f, 1f), image.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), image.anchorMax);
            Assert.AreEqual(1f, image.pivot.x, 1e-4f);
            Assert.AreEqual(-20f, image.offsetMax.x, 1e-3f, "right edge offset");
            Assert.AreEqual(200f, image.offsetMax.x - image.offsetMin.x, 1e-3f, "width preserved");

            Assert.IsNotNull(image.GetComponent<NeoLayoutTag>(), "layout element must carry the round-trip tag");
        }

        [Test]
        public void StretchConstraint_GivesZeroAnchorSpanWithInsets()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Spec/Cstr"", ""elements"": [
              { ""image"": { ""layout"": { ""h"": ""leftRight"", ""v"": ""top"",
                 ""offset"": { ""left"": 24, ""right"": 24, ""top"": 16 }, ""size"": { ""h"": 50 } } } }
            ] } ] }";
            GameObject prefab = Generate(json, "Spec_Cstr");
            RectTransform image = prefab.GetComponentsInChildren<Image>(true)
                .Select(i => (RectTransform)i.transform).First(r => r.name.StartsWith("image"));

            Assert.AreEqual(0f, image.anchorMin.x, 1e-4f);
            Assert.AreEqual(1f, image.anchorMax.x, 1e-4f);
            Assert.AreEqual(24f, image.offsetMin.x, 1e-3f);
            Assert.AreEqual(-24f, image.offsetMax.x, 1e-3f);
        }

        // ----------------------------------------------------------------- per-child sizing

        [Test]
        public void PerChildSizing_FillFixedHug_RoundTripByteIdentical()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Spec/Sizing"", ""elements"": [
              { ""hstack"": { ""anchor"": ""Stretch"", ""spacing"": 8, ""children"": [
                { ""button"": { ""id"": ""Spec/A"", ""label"": ""A"", ""layout"": { ""sizing"": { ""w"": ""fixed"", ""h"": ""fixed"" }, ""size"": { ""w"": 120, ""h"": 48 } } } },
                { ""button"": { ""id"": ""Spec/B"", ""label"": ""B"", ""layout"": { ""sizing"": { ""w"": ""fill"" } } } }
              ] } }
            ] } ] }";
            AssertFixedPoint(json, "Spec_Sizing");
        }

        [Test]
        public void FillSizing_DrivesFlexibleAndGroupForceExpand()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Spec/Sizing"", ""elements"": [
              { ""hstack"": { ""anchor"": ""Stretch"", ""children"": [
                { ""button"": { ""id"": ""Spec/Fill"", ""label"": ""Fill"", ""layout"": { ""sizing"": { ""w"": ""fill"" } } } }
              ] } }
            ] } ] }";
            GameObject prefab = Generate(json, "Spec_Sizing");
            UIButton button = prefab.GetComponentsInChildren<UIButton>(true).First(b => b.id.Matches("Spec", "Fill"));
            var le = button.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.AreEqual(1f, le.flexibleWidth, 1e-4f, "fill sets flexibleWidth=1");

            var group = prefab.GetComponentInChildren<HorizontalLayoutGroup>(true);
            Assert.IsNotNull(group);
            Assert.IsTrue(group.childForceExpandWidth, "a fill child OR-s force-expand onto the group");
        }

        // ----------------------------------------------------------------- legacy stays byte-identical

        [Test]
        public void LegacyAnchorSpec_RoundTripByteIdentical_NoLayoutEmitted()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Spec/Legacy"", ""elements"": [
              { ""image"": { ""anchor"": ""TopRight"", ""size"": [200, 60], ""position"": [-20, -20] } },
              { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 12, ""children"": [
                { ""button"": { ""id"": ""Spec/Go"", ""label"": ""Go"" } }
              ] } }
            ] } ] }";
            Generate(json, "Spec_Legacy");
            string firstExport = UISpecExporter.ExportProject().ToJson();
            Assert.IsFalse(firstExport.Contains("\"layout\""),
                "a legacy spec must export ZERO layout objects (no NeoLayoutTag stamped)");
            Assert.IsTrue(firstExport.Contains("\"anchor\""), "legacy anchor must survive");

            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(firstExport, secondExport, "legacy path must stay byte-identical");
        }
    }
}
