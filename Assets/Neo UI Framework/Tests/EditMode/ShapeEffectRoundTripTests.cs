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
    /// Open-bag shape effects (<c>"effect": { "id", "params" }</c>): the Tier-1 built-ins
    /// (glowPulse / sheenSweep / gradientCycle) generate onto an element, export back, and the
    /// re-exported effect bag is byte-identical (export → generate → export is a fixed point). Plus a
    /// <see cref="ShapeEffectRegistry"/> seam test mirroring the device-preset/layout-constraint
    /// registry conventions. The effect/params layer never grows a per-effect switch — these tests
    /// pin that the descriptor round-trip stays deterministic.
    /// </summary>
    public class ShapeEffectRoundTripTests
    {
        // One shape per effect so each effect attaches to a distinct host (deterministic detection).
        private const string EffectSpecJson = @"{
          ""views"": [ { ""id"": ""Fx/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""id"": ""Fx/Glow"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Primary"",
                ""effect"": { ""id"": ""glowPulse"", ""params"": {
                  ""duration"": 1.5, ""loop"": true, ""pingPong"": true, ""ease"": ""InOutSine"",
                  ""softnessMin"": 2, ""softnessMax"": 12, ""pulseAlpha"": true, ""alphaMin"": 0.4, ""alphaMax"": 1 } } } },
              { ""shape"": { ""id"": ""Fx/Sheen"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Primary"",
                ""effect"": { ""id"": ""sheenSweep"", ""params"": {
                  ""duration"": 2, ""loop"": true, ""fromAngle"": -45, ""toAngle"": 225 } } } },
              { ""shape"": { ""id"": ""Fx/Cycle"", ""shape"": ""RoundedRect"", ""radius"": 16,
                ""gradient"": { ""from"": ""PrimaryHover"", ""to"": ""Primary"", ""angle"": 0 },
                ""effect"": { ""id"": ""gradientCycle"", ""params"": {
                  ""duration"": 3, ""loop"": true, ""cycleAngle"": true, ""cycleColors"": false } } } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateEffectView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(EffectSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Fx_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        private static ElementSpec ExportedShape(string id)
        {
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Fx/Screen");
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");
            return stack.children.First(e => e.id == id);
        }

        [Test]
        public void GlowPulse_Generates_AndExportsTheSharedTimeline()
        {
            GenerateEffectView();
            ElementSpec glow = ExportedShape("Fx/Glow");

            Assert.IsNotNull(glow.effect, "glow shape must export an effect bag");
            Assert.AreEqual("glowPulse", glow.effect.id);
            Dictionary<string, object> p = glow.effect.parameters;
            Assert.IsNotNull(p);
            Assert.AreEqual(1.5, (double)p["duration"], 0.001);
            Assert.AreEqual(true, p["loop"]);
            Assert.AreEqual(true, p["pingPong"]);
            Assert.AreEqual("InOutSine", p["ease"]);
            Assert.AreEqual(2.0, (double)p["softnessMin"], 0.001);
            Assert.AreEqual(12.0, (double)p["softnessMax"], 0.001);
            Assert.AreEqual(true, p["pulseAlpha"]);
        }

        [Test]
        public void SheenSweep_RoundTripsFromAndToAngle()
        {
            GenerateEffectView();
            ElementSpec sheen = ExportedShape("Fx/Sheen");

            Assert.IsNotNull(sheen.effect);
            Assert.AreEqual("sheenSweep", sheen.effect.id);
            Assert.AreEqual(-45.0, (double)sheen.effect.parameters["fromAngle"], 0.001);
            Assert.AreEqual(225.0, (double)sheen.effect.parameters["toAngle"], 0.001);
        }

        [Test]
        public void GradientCycle_RoundTripsCycleFlags()
        {
            GenerateEffectView();
            ElementSpec cycle = ExportedShape("Fx/Cycle");

            Assert.IsNotNull(cycle.effect);
            Assert.AreEqual("gradientCycle", cycle.effect.id);
            Assert.AreEqual(true, cycle.effect.parameters["cycleAngle"]);
            Assert.AreEqual(false, cycle.effect.parameters["cycleColors"]);
        }

        [Test]
        public void EffectComponents_AreActuallyAttached()
        {
            GameObject prefab = GenerateEffectView();
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoGlowPulse>(true).FirstOrDefault(),
                "glowPulse must attach an NeoGlowPulse");
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoSheenSweep>(true).FirstOrDefault(),
                "sheenSweep must attach an NeoSheenSweep");
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoGradientCycle>(true).FirstOrDefault(),
                "gradientCycle must attach an NeoGradientCycle");
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_WithEffects()
        {
            GenerateEffectView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "shape effects must round-trip byte-identically");
        }

        // ---------------------------------------------------------------- registry seam

        [Test]
        public void Registry_GetKnownId_NonNull_BogusId_WarnsAndReturnsNull()
        {
            Assert.IsNotNull(ShapeEffectRegistry.Get(ShapeEffectRegistry.GlowPulse));
            Assert.IsNotNull(ShapeEffectRegistry.Get("sheenSweep"));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("ShapeEffectRegistry"));
            Assert.IsNull(ShapeEffectRegistry.Get("totally-not-an-effect"));
        }

        [Test]
        public void Registry_ShipsBuiltinsAndExposesBatchSafety()
        {
            var ids = ShapeEffectRegistry.All.Select(d => d.Id).ToList();
            CollectionAssert.Contains(ids, "glowPulse");
            CollectionAssert.Contains(ids, "sheenSweep");
            CollectionAssert.Contains(ids, "gradientCycle");
            CollectionAssert.Contains(ids, "variant");

            // Tier-1 drivers stay batch-safe; the Tier-2 variant is the deliberate batch split.
            Assert.IsTrue(ShapeEffectRegistry.Get("glowPulse").BatchSafe, "Tier-1 glowPulse is batch-safe");
            Assert.IsFalse(ShapeEffectRegistry.Get("variant").BatchSafe, "Tier-2 variant breaks the shared batch");
        }
    }
}
