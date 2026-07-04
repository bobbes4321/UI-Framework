using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The opt-in legacy→layout migration (A4): each of the 16 anchor presets migrates to a `layout`
    /// that generates a PIXEL-IDENTICAL RectTransform, and migration is idempotent. Proves the
    /// preset→constraint re-expression is faithful so an author can move an old spec onto the richer
    /// model without a visual diff.
    /// </summary>
    public class SpecMigrationTests
    {
        private static readonly string[] Presets =
        {
            "TopLeft", "Top", "TopRight", "Left", "Center", "Right", "BottomLeft", "Bottom", "BottomRight",
            "Stretch", "StretchTop", "StretchBottom", "StretchLeft", "StretchRight",
            "StretchHorizontal", "StretchVertical"
        };

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static string LegacySpec(string preset) =>
            $@"{{ ""views"": [ {{ ""id"": ""Mig/View"", ""elements"": [
              {{ ""image"": {{ ""anchor"": ""{preset}"", ""size"": [200, 60], ""position"": [-20, -20] }} }}
            ] }} ] }}";

        private static (Vector2 min, Vector2 max, Vector2 pivot, Vector2 oMin, Vector2 oMax) Geometry(UISpec spec)
        {
            GenerateReport report = UISpecGenerator.Generate(spec);
            Assert.IsEmpty(report.issues, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Mig_View.prefab");
            Assert.IsNotNull(prefab, "migration view prefab missing");
            RectTransform r = prefab.GetComponentsInChildren<Image>(true)
                .Select(i => (RectTransform)i.transform).First(rt => rt.name.StartsWith("image"));
            return (r.anchorMin, r.anchorMax, r.pivot, r.offsetMin, r.offsetMax);
        }

        [Test]
        public void EachPreset_MigratesToPixelIdenticalGeneration([ValueSource(nameof(Presets))] string preset)
        {
            UISpec legacy = UISpec.FromJson(LegacySpec(preset));
            var legacyGeom = Geometry(legacy);
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);

            UISpec migrated = SpecMigration.MigrateLegacyToLayout(UISpec.FromJson(LegacySpec(preset)));
            // the migrated element must carry a layout and have dropped its legacy anchor/position
            ElementSpec el = migrated.views[0].elements[0];
            Assert.IsNotNull(el.layout, $"{preset}: migration must produce a layout");
            Assert.IsNull(el.anchor, $"{preset}: legacy anchor must be cleared");
            Assert.IsNull(el.position, $"{preset}: legacy position must be cleared");

            var migratedGeom = Geometry(migrated);

            Assert.AreEqual(legacyGeom.min, migratedGeom.min, $"{preset}: anchorMin");
            Assert.AreEqual(legacyGeom.max, migratedGeom.max, $"{preset}: anchorMax");
            Assert.AreEqual(legacyGeom.pivot, migratedGeom.pivot, $"{preset}: pivot");
            AssertApprox(legacyGeom.oMin, migratedGeom.oMin, $"{preset}: offsetMin");
            AssertApprox(legacyGeom.oMax, migratedGeom.oMax, $"{preset}: offsetMax");
        }

        private static void AssertApprox(Vector2 a, Vector2 b, string label)
        {
            Assert.AreEqual(a.x, b.x, 1e-2f, label + ".x");
            Assert.AreEqual(a.y, b.y, 1e-2f, label + ".y");
        }

        [Test]
        public void Migration_IsIdempotent()
        {
            UISpec once = SpecMigration.MigrateLegacyToLayout(UISpec.FromJson(LegacySpec("TopRight")));
            string firstJson = once.ToJson();
            UISpec twice = SpecMigration.MigrateLegacyToLayout(once);
            Assert.AreEqual(firstJson, twice.ToJson(), "migrating an already-migrated spec is a no-op");
        }

        [Test]
        public void Migration_DoesNotMutateInput()
        {
            UISpec input = UISpec.FromJson(LegacySpec("Center"));
            string before = input.ToJson();
            SpecMigration.MigrateLegacyToLayout(input);
            Assert.AreEqual(before, input.ToJson(), "migration must return a new spec, not mutate the input");
        }

        [Test]
        public void InLayoutChild_Size_MigratesToFixedSizing()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Mig/Stack"", ""elements"": [
              { ""vstack"": { ""anchor"": ""Stretch"", ""children"": [
                { ""button"": { ""id"": ""Mig/Btn"", ""label"": ""X"", ""size"": [120, 48] } }
              ] } }
            ] } ] }";
            UISpec migrated = SpecMigration.MigrateLegacyToLayout(UISpec.FromJson(json));
            ElementSpec stack = migrated.views[0].elements[0];
            ElementSpec child = stack.children[0];
            Assert.IsNotNull(child.layout, "in-layout child migrates its size to sizing");
            Assert.AreEqual(LayoutSizingModes.Fixed, child.layout.sizing.w);
            Assert.AreEqual(LayoutSizingModes.Fixed, child.layout.sizing.h);
            Assert.AreEqual(120f, child.layout.size.w);
            Assert.IsNull(child.size, "legacy size array cleared");
        }
    }
}
