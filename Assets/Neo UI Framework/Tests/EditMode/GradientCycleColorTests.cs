using System.Collections.Generic;
using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The <c>gradientCycle</c> shape effect's cycle endpoint colors are authorable in the spec
    /// (<c>fromColorA</c>/<c>fromColorB</c>/<c>toColorA</c>/<c>toColorB</c> — theme token OR "#hex",
    /// the same model as <c>colorOverLife</c>/variant) and round-trip byte-identically through
    /// export → generate → export. Plus: a bare <c>cycleColors:true</c> bakes vivid defaults rather
    /// than the old white→gray placeholder wash (the polish gap this pins shut).
    /// </summary>
    public class GradientCycleColorTests
    {
        // One authored color is a theme token, one is raw hex — exercises both ParseColorRef branches.
        private const string AuthoredSpecJson = @"{
          ""views"": [ { ""id"": ""Fx/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""id"": ""Fx/Cycle"", ""shape"": ""RoundedRect"", ""radius"": 16,
                ""gradient"": { ""from"": ""PrimaryHover"", ""to"": ""Primary"", ""angle"": 0 },
                ""effect"": { ""id"": ""gradientCycle"", ""params"": {
                  ""duration"": 3, ""loop"": true, ""cycleAngle"": true, ""cycleColors"": true,
                  ""fromColorA"": ""Primary"", ""toColorB"": ""#FF8800CC"" } } } }
            ] } }
          ] } ]
        }";

        // No authored cycle colors — the component defaults must drive the rest frame.
        private const string DefaultsSpecJson = @"{
          ""views"": [ { ""id"": ""Fx/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""id"": ""Fx/Cycle"", ""shape"": ""RoundedRect"", ""radius"": 16,
                ""gradient"": { ""from"": ""PrimaryHover"", ""to"": ""Primary"", ""angle"": 0 },
                ""effect"": { ""id"": ""gradientCycle"", ""params"": {
                  ""duration"": 3, ""loop"": true, ""cycleColors"": true } } } }
            ] } }
          ] } ]
        }";

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static void Generate(string json)
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
        }

        private static ElementSpec ExportedCycle()
        {
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Fx/Screen");
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");
            return stack.children.First(e => e.id == "Fx/Cycle");
        }

        private static NeoGradientCycle CycleComponent()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Fx_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            var cycle = prefab.GetComponentsInChildren<NeoGradientCycle>(true).FirstOrDefault();
            Assert.IsNotNull(cycle, "gradientCycle must attach a NeoGradientCycle");
            return cycle;
        }

        [Test]
        public void AuthoredColors_RoundTrip_TokenAndHex()
        {
            Generate(AuthoredSpecJson);
            Dictionary<string, object> p = ExportedCycle().effect.parameters;

            Assert.AreEqual("Primary", p["fromColorA"], "theme token must round-trip as its token string");
            // alpha < 1 forces the 8-digit RGBA form from ColorUtils.ToHex.
            Assert.AreEqual("#FF8800CC", p["toColorB"], "raw color must round-trip as #hex");
            // Un-authored endpoints fall back to the component defaults and are still emitted.
            Assert.IsTrue(p.ContainsKey("fromColorB"));
            Assert.IsTrue(p.ContainsKey("toColorA"));
        }

        [Test]
        public void AuthoredColors_ReachTheComponent()
        {
            Generate(AuthoredSpecJson);
            NeoGradientCycle cycle = CycleComponent();

            Assert.IsTrue(cycle.FromColorA.useToken, "fromColorA authored as a token must stay a token ref");
            Assert.AreEqual("Primary", cycle.FromColorA.token);

            Assert.IsFalse(cycle.ToColorB.useToken, "toColorB authored as hex must be a raw color ref");
            Assert.AreEqual(new Color32(0xFF, 0x88, 0x00, 0xCC), (Color32)cycle.ToColorB.color);
        }

        [Test]
        public void Defaults_AreVivid_NotTheOldWhiteGrayPlaceholders()
        {
            Generate(DefaultsSpecJson);
            NeoGradientCycle cycle = CycleComponent();

            // The old placeholders were white / 0.7-gray — assert none of the four endpoints is either.
            ThemeColorRef[] endpoints = { cycle.FromColorA, cycle.FromColorB, cycle.ToColorA, cycle.ToColorB };
            var white = Color.white;
            var gray = new Color(0.7f, 0.7f, 0.7f);
            foreach (ThemeColorRef e in endpoints)
            {
                Assert.IsFalse(e.useToken, "default endpoints are raw vivid colors");
                Color c = e.color;
                Assert.IsFalse(Approximately(c, white), $"default endpoint must not be the old white placeholder ({c})");
                Assert.IsFalse(Approximately(c, gray), $"default endpoint must not be the old 0.7-gray placeholder ({c})");
                // Vivid = some channel separation (not a flat near-gray wash).
                float max = Mathf.Max(c.r, c.g, c.b);
                float min = Mathf.Min(c.r, c.g, c.b);
                Assert.Greater(max - min, 0.1f, $"default endpoint should be colorful, not gray ({c})");
            }
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_Authored()
        {
            Generate(AuthoredSpecJson);
            AssertFixedPoint();
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_Defaults()
        {
            Generate(DefaultsSpecJson);
            AssertFixedPoint();
        }

        private static void AssertFixedPoint()
        {
            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "gradientCycle colors must round-trip byte-identically");
        }

        private static bool Approximately(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f &&
            Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;
    }
}
