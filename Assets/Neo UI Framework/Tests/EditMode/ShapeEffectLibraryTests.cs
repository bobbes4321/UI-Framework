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
    /// The Tier-1 effect-library expansion (arcSpinner / cornerMorph / borderPulse / hueShift /
    /// transformJuice) and the live signal→param binding seam (<see cref="NeoSignalParamBinding"/> +
    /// <see cref="NeoShapeEffect.TrySetLiveParam"/>): every new descriptor generates, attaches its
    /// component, and round-trips byte-identically through export → generate → export, exactly like the
    /// original three. Pins that the open-bag effect layer still grows by ADDING a descriptor — never a
    /// per-effect switch — and that the binding bag survives the round-trip.
    /// </summary>
    public class ShapeEffectLibraryTests
    {
        private const string LibrarySpecJson = @"{
          ""views"": [ { ""id"": ""Lib/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""id"": ""Lib/Spinner"", ""shape"": ""Arc"", ""thickness"": 10, ""arcSweep"": 90, ""background"": ""Primary"",
                ""effect"": { ""id"": ""arcSpinner"", ""params"": {
                  ""duration"": 1.1, ""loop"": true, ""pingPong"": false, ""ease"": ""Linear"",
                  ""spinFrom"": 0, ""spinTo"": 360, ""animateSweep"": true, ""sweepFrom"": 40, ""sweepTo"": 280 } } } },
              { ""shape"": { ""id"": ""Lib/Corner"", ""shape"": ""RoundedRect"", ""radius"": 12, ""background"": ""Success"",
                ""effect"": { ""id"": ""cornerMorph"", ""params"": {
                  ""duration"": 1.6, ""loop"": true, ""pingPong"": true, ""ease"": ""InOutSine"", ""radiusMin"": 8, ""radiusMax"": 56 } } } },
              { ""shape"": { ""id"": ""Lib/Border"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Surface"",
                ""effect"": { ""id"": ""borderPulse"", ""params"": {
                  ""duration"": 1.2, ""loop"": true, ""pingPong"": true, ""ease"": ""InOutSine"",
                  ""borderMin"": 1, ""borderMax"": 6, ""pulseAlpha"": true, ""alphaMin"": 0.3, ""alphaMax"": 1 } } } },
              { ""shape"": { ""id"": ""Lib/Hue"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Primary"",
                ""effect"": { ""id"": ""hueShift"", ""params"": {
                  ""duration"": 4, ""loop"": true, ""pingPong"": false, ""ease"": ""Linear"", ""hueFrom"": 0, ""hueTo"": 1 } } } },
              { ""shape"": { ""id"": ""Lib/Juice"", ""shape"": ""Circle"", ""background"": ""Primary"",
                ""effect"": { ""id"": ""transformJuice"", ""params"": {
                  ""duration"": 1.5, ""loop"": true, ""pingPong"": true, ""ease"": ""InOutSine"", ""bob"": 14, ""rotate"": 8 } } } },
              { ""shape"": { ""id"": ""Lib/Bound"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Primary"",
                ""effect"": { ""id"": ""glowPulse"", ""params"": {
                  ""duration"": 1.4, ""loop"": true, ""pingPong"": true, ""ease"": ""InOutSine"",
                  ""softnessMin"": 2, ""softnessMax"": 12,
                  ""bindings"": [
                    { ""signal"": ""Effects/Softness"", ""param"": ""softnessMax"", ""min"": 2, ""max"": 36 },
                    { ""signal"": ""Effects/On"", ""param"": ""enabled"" }
                  ] } } } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateLibraryView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(LibrarySpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Lib_Screen.prefab");
            Assert.IsNotNull(prefab, "generated library view prefab missing");
            return prefab;
        }

        private static ElementSpec ExportedShape(string id)
        {
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Lib/Screen");
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");
            return stack.children.First(e => e.id == id);
        }

        [Test]
        public void AllNewEffectComponents_AreAttached()
        {
            GameObject prefab = GenerateLibraryView();
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoArcSpinner>(true).FirstOrDefault(), "arcSpinner");
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoCornerMorph>(true).FirstOrDefault(), "cornerMorph");
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoBorderPulse>(true).FirstOrDefault(), "borderPulse");
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoHueShift>(true).FirstOrDefault(), "hueShift");
            Assert.IsNotNull(prefab.GetComponentsInChildren<NeoTransformJuice>(true).FirstOrDefault(), "transformJuice");
        }

        [Test]
        public void ArcSpinner_RoundTripsSpinAndSweep()
        {
            GenerateLibraryView();
            Dictionary<string, object> p = ExportedShape("Lib/Spinner").effect.parameters;
            Assert.AreEqual("arcSpinner", ExportedShape("Lib/Spinner").effect.id);
            Assert.AreEqual(0.0, (double)p["spinFrom"], 0.001);
            Assert.AreEqual(360.0, (double)p["spinTo"], 0.001);
            Assert.AreEqual(true, p["animateSweep"]);
        }

        [Test]
        public void CornerMorph_RoundTripsRadii()
        {
            GenerateLibraryView();
            Dictionary<string, object> p = ExportedShape("Lib/Corner").effect.parameters;
            Assert.AreEqual(8.0, (double)p["radiusMin"], 0.001);
            Assert.AreEqual(56.0, (double)p["radiusMax"], 0.001);
        }

        [Test]
        public void BorderPulse_RoundTripsWidthAndAlpha()
        {
            GenerateLibraryView();
            Dictionary<string, object> p = ExportedShape("Lib/Border").effect.parameters;
            Assert.AreEqual(1.0, (double)p["borderMin"], 0.001);
            Assert.AreEqual(6.0, (double)p["borderMax"], 0.001);
            Assert.AreEqual(true, p["pulseAlpha"]);
        }

        [Test]
        public void HueShift_RoundTripsHueRange()
        {
            GenerateLibraryView();
            Dictionary<string, object> p = ExportedShape("Lib/Hue").effect.parameters;
            Assert.AreEqual(0.0, (double)p["hueFrom"], 0.001);
            Assert.AreEqual(1.0, (double)p["hueTo"], 0.001);
        }

        [Test]
        public void TransformJuice_RoundTripsNonZeroChannelsOnly()
        {
            GenerateLibraryView();
            Dictionary<string, object> p = ExportedShape("Lib/Juice").effect.parameters;
            Assert.AreEqual(14.0, (double)p["bob"], 0.001);
            Assert.AreEqual(8.0, (double)p["rotate"], 0.001);
            // Zero channels must NOT leak into the bag (emitted-only-when-nonzero).
            CollectionAssert.DoesNotContain(p.Keys, "sway");
            CollectionAssert.DoesNotContain(p.Keys, "scale");
            CollectionAssert.DoesNotContain(p.Keys, "squash");
        }

        // ---------------------------------------------------------------- live signal bindings

        [Test]
        public void Bindings_GenerateInto_NeoSignalParamBinding()
        {
            GameObject prefab = GenerateLibraryView();
            NeoSignalParamBinding binding = prefab
                .GetComponentsInChildren<NeoSignalParamBinding>(true)
                .FirstOrDefault(b => b.GetComponent<NeoGlowPulse>() != null);

            Assert.IsNotNull(binding, "a glow with `bindings` must attach a NeoSignalParamBinding");
            Assert.AreEqual(2, binding.Bindings.Count);

            NeoSignalParamBinding.ParamBinding soft = binding.Bindings[0];
            Assert.AreEqual("Effects", soft.category);
            Assert.AreEqual("Softness", soft.signalName);
            Assert.AreEqual("softnessMax", soft.param);
            Assert.AreEqual(2f, soft.min, 0.001f);
            Assert.AreEqual(36f, soft.max, 0.001f);

            NeoSignalParamBinding.ParamBinding on = binding.Bindings[1];
            Assert.AreEqual(NeoSignalParamBinding.EnabledParam, on.param);
        }

        [Test]
        public void Bindings_RoundTripThroughTheParamBag()
        {
            GenerateLibraryView();
            Dictionary<string, object> p = ExportedShape("Lib/Bound").effect.parameters;
            Assert.IsTrue(p.ContainsKey("bindings"), "bindings must export");

            var list = (List<object>)p["bindings"];
            Assert.AreEqual(2, list.Count);
            var first = (Dictionary<string, object>)list[0];
            Assert.AreEqual("Effects/Softness", first["signal"]);
            Assert.AreEqual("softnessMax", first["param"]);
            Assert.AreEqual(2.0, (double)first["min"], 0.001);
            Assert.AreEqual(36.0, (double)first["max"], 0.001);
        }

        // ---------------------------------------------------------------- TrySetLiveParam unit

        [Test]
        public void TrySetLiveParam_DrivesEffectFields()
        {
            var go = new GameObject("Glow", typeof(RectTransform));
            try
            {
                go.AddComponent<NeoShape>();
                var glow = go.AddComponent<NeoGlowPulse>();

                Assert.IsTrue(glow.TrySetLiveParam("softnessMax", 25f));
                Assert.AreEqual(25f, glow.SoftnessMax, 0.001f);

                // "softness" convenience pins both ends (direct slider control).
                Assert.IsTrue(glow.TrySetLiveParam("softness", 7f));
                Assert.AreEqual(7f, glow.SoftnessMin, 0.001f);
                Assert.AreEqual(7f, glow.SoftnessMax, 0.001f);

                // Shared base param.
                Assert.IsTrue(glow.TrySetLiveParam("duration", 2f));
                Assert.AreEqual(2f, glow.duration, 0.001f);

                // Unknown param no-ops (returns false, never throws).
                Assert.IsFalse(glow.TrySetLiveParam("nonsense", 1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- registry + fixed point

        [Test]
        public void Registry_ShipsNewBuiltins_AllBatchSafe()
        {
            var ids = ShapeEffectRegistry.All.Select(d => d.Id).ToList();
            foreach (string id in new[] { "arcSpinner", "cornerMorph", "borderPulse", "hueShift", "transformJuice" })
            {
                CollectionAssert.Contains(ids, id);
                Assert.IsTrue(ShapeEffectRegistry.Get(id).BatchSafe, $"{id} is Tier-1 batch-safe");
            }
        }

        [Test]
        public void Library_Export_Generate_Export_IsFixedPoint()
        {
            GenerateLibraryView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "the new effects + bindings must round-trip byte-identically");
        }
    }
}
