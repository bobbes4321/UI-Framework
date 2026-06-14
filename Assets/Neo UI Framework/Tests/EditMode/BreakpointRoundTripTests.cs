using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar B round-trip: top-level <c>breakpoints</c> + per-element <c>overrides</c> survive
    /// generate → export → generate → export byte-identically, and a legacy spec with neither emits
    /// nothing (backward compatibility). Runs against the redirected scratch GeneratedRoot.
    /// </summary>
    public class BreakpointRoundTripTests
    {
        private const string BreakpointSpecJson = @"{
          ""breakpoints"": [
            { ""name"": ""landscape"", ""when"": { ""orientation"": ""landscape"", ""minAspect"": 1.6 } },
            { ""name"": ""narrow"", ""when"": { ""maxWidth"": 600 } }
          ],
          ""views"": [ { ""id"": ""Bp/Main"", ""elements"": [
            { ""panel"": {
                ""id"": ""Bp/Card"",
                ""layout"": { ""h"": ""leftRight"", ""v"": ""topBottom"",
                              ""offset"": { ""left"": 24, ""right"": 24, ""top"": 48, ""bottom"": 48 } },
                ""overrides"": {
                  ""landscape"": { ""h"": ""center"", ""size"": { ""w"": 900 } },
                  ""narrow"":    { ""offset"": { ""left"": 8, ""right"": 8 } }
                }
            } }
          ] } ]
        }";

        private const string LegacySpecJson = @"{
          ""views"": [ { ""id"": ""Bp/Legacy"", ""elements"": [
            { ""panel"": { ""id"": ""Bp/Plain"", ""layout"": { ""h"": ""left"", ""v"": ""top"", ""size"": { ""w"": 200, ""h"": 100 } } } }
          ] } ]
        }";

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Breakpoints_And_Overrides_RoundTrip_ByteIdentical()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(BreakpointSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "breakpoints + overrides must round-trip byte-identically");
        }

        [Test]
        public void Exported_Breakpoints_And_Overrides_AreReconstructed()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(BreakpointSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());

            UISpec exported = UISpecExporter.ExportProject();

            // global breakpoints reconstructed from the baked condition table
            Assert.AreEqual(2, exported.breakpoints.Count, "two breakpoints expected");
            BreakpointSpec landscape = exported.breakpoints.FirstOrDefault(b => b.name == "landscape");
            Assert.IsNotNull(landscape);
            Assert.AreEqual("landscape", landscape.when.orientation);
            Assert.AreEqual(1.6f, landscape.when.minAspect);
            BreakpointSpec narrow = exported.breakpoints.FirstOrDefault(b => b.name == "narrow");
            Assert.IsNotNull(narrow);
            Assert.AreEqual(600f, narrow.when.maxWidth);

            // per-element overrides reconstructed from the stored original deltas
            ElementSpec card = exported.views.First(v => v.id == "Bp/Main").elements
                .First(e => e.id == "Bp/Card");
            Assert.IsNotNull(card.overrides, "overrides must round-trip");
            Assert.AreEqual(2, card.overrides.Count);
            Assert.AreEqual("center", card.overrides["landscape"].h);
            Assert.AreEqual(900f, card.overrides["landscape"].size.w);
            Assert.AreEqual(8f, card.overrides["narrow"].offset.GetOr("left", -1f));
            Assert.AreEqual(8f, card.overrides["narrow"].offset.GetOr("right", -1f));
        }

        [Test]
        public void LegacySpec_EmitsNoBreakpointsOrOverrides()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(LegacySpecJson));
            Assert.IsEmpty(report.issues, report.ToString());

            UISpec exported = UISpecExporter.ExportProject();
            Assert.IsEmpty(exported.breakpoints, "a spec with no breakpoints must export none");

            ElementSpec plain = exported.views.First(v => v.id == "Bp/Legacy").elements
                .First(e => e.id == "Bp/Plain");
            Assert.IsNull(plain.overrides, "an element with no overrides must export none");

            // and the prefab carries no responsive component (zero behavior change)
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Bp_Legacy.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsNull(prefab.GetComponent<UIResponsiveRoot>(),
                "no overrides ⇒ no UIResponsiveRoot baked");

            string json = exported.ToJson();
            StringAssert.DoesNotContain("breakpoints", json);
            StringAssert.DoesNotContain("overrides", json);
        }

        [Test]
        public void OverridesView_BakesResponsiveRoot_AtBase()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(BreakpointSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Bp_Main.prefab");
            Assert.IsNotNull(prefab);
            var responsive = prefab.GetComponent<UIResponsiveRoot>();
            Assert.IsNotNull(responsive, "a view with overrides bakes a UIResponsiveRoot");
            Assert.AreEqual(2, responsive.conditions.Count, "both breakpoints baked into the table");
            // one base + two override entries for the single card
            Assert.AreEqual(1, responsive.bases.Count);
            Assert.AreEqual(2, responsive.entries.Count);

            // WYSIWYG: the baked card RectTransform equals its BASE (leftRight/topBottom inset 24/24/48/48),
            // i.e. a full-stretch with offsets, NOT the landscape center override.
            RectTransform card = responsive.bases[0].target;
            Assert.IsNotNull(card);
            Assert.AreEqual(0f, card.anchorMin.x, 1e-3f, "base h is leftRight (anchorMin.x = 0)");
            Assert.AreEqual(1f, card.anchorMax.x, 1e-3f, "base h is leftRight (anchorMax.x = 1), not centered");
        }
    }
}
