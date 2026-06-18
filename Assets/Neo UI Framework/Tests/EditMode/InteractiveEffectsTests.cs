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
    /// Pointer-driven interactivity: the effect-bag <c>trigger</c>/<c>triggerMode</c> (→
    /// <see cref="NeoEffectTrigger"/>), the particle-bag <c>atPointer</c> (→
    /// <see cref="NeoParticlePointerBurst"/>), and the element <c>pointerGlow</c> (→
    /// <see cref="NeoPointerReactor"/>). Each generates its runtime component and round-trips through
    /// export → generate → export byte-identically, so the interactivity survives an agent regenerate.
    /// </summary>
    public class InteractiveEffectsTests
    {
        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""Ix/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""shape"": { ""id"": ""Ix/Sheen"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Warning"",
                ""effect"": { ""id"": ""sheenSweep"",
                  ""params"": { ""trigger"": ""hover"", ""triggerMode"": ""hold"", ""duration"": 0.7, ""loop"": false, ""ease"": ""InOutSine"", ""fromAngle"": -60, ""toAngle"": 240 } } } },
              { ""shape"": { ""id"": ""Ix/Spark"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""Primary"",
                ""particles"": { ""capacity"": 40, ""burstCount"": 16, ""rate"": 0, ""atPointer"": true,
                  ""particleShape"": ""Circle"", ""sizeRange"": [5,10], ""lifetimeRange"": [0.5,1], ""speedRange"": [150,300] } } },
              { ""shape"": { ""id"": ""Ix/Glow"", ""shape"": ""RoundedRect"", ""radius"": 16, ""background"": ""SurfaceElevated"",
                ""pointerGlow"": { ""color"": ""#9FC2FFCC"", ""size"": 150, ""softness"": 60 } } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject Generate()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Ix_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        private static ElementSpec Exported(string id)
        {
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Ix/Screen");
            ElementSpec stack = view.elements.First(e => e.kind == "vstack");
            return stack.children.First(e => e.id == id);
        }

        [Test]
        public void Trigger_AttachesNeoEffectTrigger_AndRoundTrips()
        {
            GameObject prefab = Generate();
            NeoEffectTrigger trig = prefab.GetComponentsInChildren<NeoEffectTrigger>(true)
                .FirstOrDefault(t => t.GetComponent<NeoSheenSweep>() != null);
            Assert.IsNotNull(trig, "trigger:hover must attach a NeoEffectTrigger to the effect host");
            Assert.AreEqual(NeoEffectTrigger.TriggerOn.Hover, trig.Trigger);
            Assert.AreEqual(NeoEffectTrigger.TriggerMode.Hold, trig.Mode);

            Dictionary<string, object> p = Exported("Ix/Sheen").effect.parameters;
            Assert.AreEqual("hover", p["trigger"]);
            Assert.AreEqual("hold", p["triggerMode"]);
        }

        [Test]
        public void AtPointer_AttachesPointerBurst_AndRoundTrips()
        {
            GameObject prefab = Generate();
            NeoParticlePointerBurst burst = prefab.GetComponentsInChildren<NeoParticlePointerBurst>(true).FirstOrDefault();
            Assert.IsNotNull(burst, "atPointer must attach a NeoParticlePointerBurst");
            Assert.IsNotNull(burst.GetComponent<NeoParticleEmitter>(), "pointer burst requires an emitter");

            Assert.AreEqual(true, Exported("Ix/Spark").particles.atPointer);
        }

        [Test]
        public void PointerGlow_AttachesReactor_AndRoundTrips()
        {
            GameObject prefab = Generate();
            NeoPointerReactor reactor = prefab.GetComponentsInChildren<NeoPointerReactor>(true).FirstOrDefault();
            Assert.IsNotNull(reactor, "pointerGlow must attach a NeoPointerReactor");
            Assert.AreEqual(150f, reactor.GlowSize, 0.5f);
            Assert.AreEqual(60f, reactor.GlowSoftness, 0.5f);

            PointerGlowSpec glow = Exported("Ix/Glow").pointerGlow;
            Assert.IsNotNull(glow, "pointerGlow must export");
            Assert.AreEqual(150f, glow.size.Value, 0.5f);
            Assert.AreEqual(60f, glow.softness.Value, 0.5f);
        }

        [Test]
        public void Interactive_Export_Generate_Export_IsFixedPoint()
        {
            Generate();
            string first = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(first));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second, "trigger / atPointer / pointerGlow must round-trip byte-identically");
        }
    }
}
