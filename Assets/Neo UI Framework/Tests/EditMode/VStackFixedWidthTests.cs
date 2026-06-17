using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Behavior regression for the vstack force-expand defect: a vstack force-expands its children's
    /// width so rows fill the column, which silently stomped a child's authored width. The per-child
    /// <c>sizing:"fixed"</c> seam is the opt-out — after the fix it adds a <see cref="ContentSizeFitter"/>
    /// (PreferredSize) so a fixed child honors its size even inside the force-expanding column, while a
    /// <c>fill</c> (or default) sibling still expands. <see cref="ConstraintLayoutRoundTripTests"/> only
    /// exercised an HSTACK, which is why the vstack case slipped through.
    /// </summary>
    public class VStackFixedWidthTests
    {
        private const float VStackWidth = 600f;
        private const float FixedChildWidth = 240f;

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

        /// <summary>
        /// A fixed-width image inside a force-expanding vstack must keep its authored 240 width
        /// (laid out, not just configured), and must carry the ContentSizeFitter that overrides the
        /// parent's childForceExpandWidth.
        /// </summary>
        [Test]
        public void FixedChild_InForceExpandingVStack_KeepsAuthoredWidth()
        {
            string json = $@"{{ ""views"": [ {{ ""id"": ""Spec/VFix"", ""elements"": [
              {{ ""vstack"": {{ ""anchor"": ""Stretch"", ""spacing"": 8, ""children"": [
                {{ ""image"": {{ ""layout"": {{ ""sizing"": {{ ""w"": ""fixed"" }}, ""size"": {{ ""w"": {FixedChildWidth}, ""h"": 80 }} }} }} }},
                {{ ""image"": {{ ""layout"": {{ ""sizing"": {{ ""w"": ""fill"" }} }} }} }}
              ] }} }}
            ] }} ] }}";

            GameObject prefab = Generate(json, "Spec_VFix");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var group = instance.GetComponentInChildren<VerticalLayoutGroup>(true);
                Assert.IsNotNull(group, "the vstack must build a VerticalLayoutGroup");
                Assert.IsTrue(group.childForceExpandWidth,
                    "the vstack force-expands width (rows fill the column) — this is what the fix must override per-child");

                // Drive the column to a known fixed width so realized child widths are deterministic
                // regardless of canvas sizing (a Stretch vstack otherwise inherits the view-root width).
                var columnRect = (RectTransform)group.transform;
                columnRect.anchorMin = columnRect.anchorMax = new Vector2(0.5f, 0.5f);
                columnRect.pivot = new Vector2(0.5f, 0.5f);
                columnRect.sizeDelta = new Vector2(VStackWidth, 400f);

                var images = instance.GetComponentsInChildren<Image>(true)
                    .Select(i => (RectTransform)i.transform)
                    .Where(r => r.name.StartsWith("image"))
                    .ToList();
                Assert.AreEqual(2, images.Count, "expected the two image rows");

                // Identify fixed vs fill by their sizing tag (order in the column is deterministic but
                // the tag is authoritative).
                RectTransform fixedRow = images.First(r =>
                    r.GetComponent<NeoLayoutTag>() != null && r.GetComponent<NeoLayoutTag>().sizingW == LayoutSizingModes.Fixed);
                RectTransform fillRow = images.First(r =>
                    r.GetComponent<NeoLayoutTag>() != null && r.GetComponent<NeoLayoutTag>().sizingW == LayoutSizingModes.Fill);

                // The fix: a fixed child carries a ContentSizeFitter on the width axis so force-expand
                // can't stomp it (matching how HugSizing and a button escape).
                var fitter = fixedRow.GetComponent<ContentSizeFitter>();
                Assert.IsNotNull(fitter, "a sizing:fixed child must get a ContentSizeFitter to escape force-expand");
                Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.horizontalFit,
                    "fixed width fits to PreferredSize (= the authored min=preferred extent)");

                // Force a layout pass and assert realized widths.
                LayoutRebuilder.ForceRebuildLayoutImmediate(columnRect);

                Assert.AreEqual(FixedChildWidth, fixedRow.rect.width, 1f,
                    "fixed child keeps its authored 240 width, NOT the full vstack column width");
                Assert.Less(fixedRow.rect.width, VStackWidth - 1f,
                    "fixed child must NOT have stretched to the column width");

                // Contrast: the fill sibling still absorbs the column width.
                Assert.Greater(fillRow.rect.width, FixedChildWidth + 1f,
                    "a fill sibling still expands to fill the column");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
