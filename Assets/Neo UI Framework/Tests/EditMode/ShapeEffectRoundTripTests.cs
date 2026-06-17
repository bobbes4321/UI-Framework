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
        public void SheenSweep_GeneratesIntoLinearGradientFill_WithContrastingStop()
        {
            // The sheen drives NeoShape's BUILT-IN gradient (color → colorB). It must force the host
            // into a linear-gradient fill and leave a contrasting bright stop, else there is nothing
            // to sweep and the card renders a flat fill. Verified on the baked (resting) prefab.
            GameObject prefab = GenerateEffectView();
            NeoSheenSweep sheen = prefab.GetComponentsInChildren<NeoSheenSweep>(true).First();
            NeoShape shape = sheen.GetComponent<NeoShape>();

            Assert.AreEqual(ShapeFillMode.LinearGradient, shape.fill,
                "sheen must force the host into a linear-gradient fill");
            Assert.AreNotEqual(shape.color, shape.colorB,
                "sheen must leave a contrasting bright stop so the sweep is visible");
        }

        [Test]
        public void SheenSweep_SeedsBrightStop_WhenBaseStopsAreEqual()
        {
            // Direct regression for the self-seed branch: a host whose two built-in stops are equal
            // (a solid base with no second stop wired) has nothing to sweep. Enabling the sheen must
            // seed a contrasting bright colorB so the common "solid card + sheen" case just works.
            var go = new GameObject("SheenSeed", typeof(RectTransform));
            try
            {
                var shape = go.AddComponent<NeoShape>();
                shape.color = new Color(0.2f, 0.2f, 0.2f, 1f);
                shape.colorB = shape.color; // equal stops → nothing to sweep

                var sheen = go.AddComponent<NeoSheenSweep>();
                sheen.SheenColor = Color.white;
                sheen.EvaluateRest(); // mimic the generator's resting-frame bake

                Assert.AreEqual(ShapeFillMode.LinearGradient, shape.fill);
                Assert.AreEqual(Color.white, shape.colorB,
                    "equal base stops must be seeded with the bright sheen color");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
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

        // ---------------------------------------------------------------- Tier-2 variant animation

        // A static variant (bare { definition }) plus an ANIMATED variant whose material float is
        // driven over the timeline by a NeoMaterialFloatCycle. The dissolve ShapeEffectDefinition
        // (id "dissolve") is a committed asset, so the variant resolves in any generated root.
        private const string VariantSpecJson = @"{
          ""views"": [ { ""id"": ""Fx2/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""id"": ""Fx2/Static"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Primary"",
                ""effect"": { ""id"": ""variant"", ""params"": { ""definition"": ""dissolve"" } } } },
              { ""shape"": { ""id"": ""Fx2/Animated"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Primary"",
                ""effect"": { ""id"": ""variant"", ""params"": {
                  ""definition"": ""dissolve"",
                  ""animate"": ""_DissolveAmount"", ""from"": 0.0, ""to"": 1.0,
                  ""duration"": 2.6, ""loop"": true, ""pingPong"": true, ""ease"": ""InOutSine"", ""restingPhase"": 0.5 } } } }
            ] } }
          ] } ]
        }";

        private static GameObject GenerateVariantView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(VariantSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Fx2_Screen.prefab");
            Assert.IsNotNull(prefab, "generated variant view prefab missing");
            return prefab;
        }

        private static ElementSpec ExportedVariantShape(string id)
        {
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Fx2/Screen");
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");
            return stack.children.First(e => e.id == id);
        }

        [Test]
        public void Variant_WithAnimate_AttachesAndConfiguresMaterialFloatCycle()
        {
            GameObject prefab = GenerateVariantView();
            NeoMaterialFloatCycle cycle = prefab
                .GetComponentsInChildren<NeoMaterialFloatCycle>(true)
                .FirstOrDefault(c => c.GetComponent<NeoShapeVariant>() != null);

            Assert.IsNotNull(cycle, "animated variant must attach a NeoMaterialFloatCycle");
            Assert.AreEqual("_DissolveAmount", cycle.PropertyName);
            Assert.AreEqual(0.0f, cycle.FromValue, 0.001f);
            Assert.AreEqual(1.0f, cycle.ToValue, 0.001f);
            Assert.AreEqual(2.6f, cycle.duration, 0.001f);
            Assert.IsTrue(cycle.loop);
            Assert.IsTrue(cycle.pingPongLoop);
            Assert.AreEqual(Ease.InOutSine, cycle.easing);
            Assert.AreEqual(0.5f, cycle.restingPhase, 0.001f);
        }

        [Test]
        public void Variant_WithoutAnimate_IsBareStaticVariant_NoCycle()
        {
            GameObject prefab = GenerateVariantView();
            // The static card carries the variant but NO material-float cycle.
            NeoShapeVariant staticVariant = prefab
                .GetComponentsInChildren<NeoShapeVariant>(true)
                .First(v => v.GetComponent<NeoMaterialFloatCycle>() == null);
            Assert.IsNotNull(staticVariant, "static variant must still attach a NeoShapeVariant");
            Assert.IsNull(staticVariant.GetComponent<NeoMaterialFloatCycle>(),
                "a variant without `animate` must not attach a NeoMaterialFloatCycle");
        }

        [Test]
        public void Variant_StaticExport_IsBareDefinitionBag()
        {
            GenerateVariantView();
            ElementSpec stat = ExportedVariantShape("Fx2/Static");
            Assert.IsNotNull(stat.effect);
            Assert.AreEqual("variant", stat.effect.id);
            Assert.AreEqual("dissolve", stat.effect.parameters["definition"]);
            // No animation keys leak onto a static variant.
            CollectionAssert.DoesNotContain(stat.effect.parameters.Keys, "animate");
            CollectionAssert.DoesNotContain(stat.effect.parameters.Keys, "duration");
        }

        [Test]
        public void Variant_AnimatedExport_RoundTripsAnimationKeys()
        {
            GenerateVariantView();
            ElementSpec anim = ExportedVariantShape("Fx2/Animated");
            Assert.IsNotNull(anim.effect);
            Assert.AreEqual("variant", anim.effect.id);
            Dictionary<string, object> p = anim.effect.parameters;
            Assert.AreEqual("dissolve", p["definition"]);
            Assert.AreEqual("_DissolveAmount", p["animate"]);
            Assert.AreEqual(0.0, (double)p["from"], 0.001);
            Assert.AreEqual(1.0, (double)p["to"], 0.001);
            Assert.AreEqual(2.6, (double)p["duration"], 0.001);
            Assert.AreEqual(true, p["loop"]);
            Assert.AreEqual(true, p["pingPong"]);
            Assert.AreEqual("InOutSine", p["ease"]);
            Assert.AreEqual(0.5, (double)p["restingPhase"], 0.001);
        }

        [Test]
        public void Variant_Export_Generate_Export_IsFixedPoint_BothStaticAndAnimated()
        {
            GenerateVariantView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "static + animated variants must round-trip byte-identically");
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
