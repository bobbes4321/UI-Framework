using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// UI particle emitters (<c>"particles": { ...scalars..., "modules":[…], "signal":{…} }</c>):
    /// the spec generates a <see cref="NeoParticleEmitter"/>, exports back byte-identically, the
    /// module list rides <see cref="ParticleEffectRegistry"/>, and a signal block adds an
    /// <c>NeoParticleBurstOnSignal</c>. The field-name guard reads the LIVE emitter component back via
    /// <see cref="SerializedObject"/> — this is the test that catches a
    /// <see cref="SerializedObject.FindProperty"/> name drift between the
    /// generator/exporter and the emitter's serialized fields, which a pure spec→spec round-trip
    /// (both sides sharing the same wrong name) would silently miss.
    /// </summary>
    public class ParticleRoundTripTests
    {
        private const string ParticleSpecJson = @"{
          ""views"": [ { ""id"": ""Fx/Particles"", ""elements"": [
            { ""shape"": { ""id"": ""Fx/Burst"", ""shape"": ""Circle"", ""background"": ""Primary"",
              ""particles"": {
                ""capacity"": 48,
                ""burstCount"": 24,
                ""rate"": 0,
                ""emitOnEnable"": true,
                ""particleShape"": ""RoundedRect"",
                ""cornerRadiusPercent"": 40,
                ""sizeRange"": [8, 20],
                ""lifetimeRange"": [0.5, 1.2],
                ""speedRange"": [200, 500],
                ""emitAngle"": 90,
                ""emitSpread"": 120,
                ""angularVelocityRange"": [-90, 90],
                ""modules"": [
                  { ""id"": ""gravity"", ""params"": { ""acceleration"": [0, -980] } },
                  { ""id"": ""colorOverLife"", ""params"": { ""start"": ""#FFFFFFFF"", ""end"": ""#FF000000"", ""ease"": ""Linear"" } }
                ],
                ""signal"": { ""category"": ""Game"", ""name"": ""Score"", ""count"": 12 }
              } } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateParticleView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(ParticleSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Fx_Particles.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        private static ParticleSpec ExportedParticles()
        {
            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Fx/Particles");
            ElementSpec burst = view.elements.First(e => e.id == "Fx/Burst");
            Assert.IsNotNull(burst.particles, "the burst shape must export a particles block");
            return burst.particles;
        }

        [Test]
        public void Particles_RoundTrip_ScalarsModulesAndSignal()
        {
            GenerateParticleView();
            ParticleSpec p = ExportedParticles();

            Assert.AreEqual(48, p.capacity);
            Assert.AreEqual(24, p.burstCount);
            Assert.AreEqual(0f, p.rate);
            Assert.IsTrue(p.emitOnEnable);
            Assert.AreEqual("RoundedRect", p.particleShape);
            Assert.AreEqual(40f, p.cornerRadiusPercent);
            Assert.AreEqual(new[] { 8f, 20f }, p.sizeRange);
            Assert.AreEqual(new[] { 0.5f, 1.2f }, p.lifetimeRange);
            Assert.AreEqual(new[] { 200f, 500f }, p.speedRange);
            Assert.AreEqual(90f, p.emitAngle);
            Assert.AreEqual(120f, p.emitSpread);
            Assert.AreEqual(new[] { -90f, 90f }, p.angularVelocityRange);

            Assert.AreEqual(2, p.modules.Count, "gravity + colorOverLife survive");
            Assert.AreEqual("gravity", p.modules[0].id);
            Assert.AreEqual("colorOverLife", p.modules[1].id);

            Assert.IsNotNull(p.signal);
            Assert.AreEqual("Game", p.signal.category);
            Assert.AreEqual("Score", p.signal.name);
            Assert.AreEqual(12, p.signalCount);
        }

        [Test]
        public void Export_Generate_Export_IsFixedPoint_WithParticles()
        {
            GenerateParticleView();

            string firstExport = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();

            Assert.AreEqual(firstExport, secondExport,
                "particle emitters must round-trip byte-identically");
        }

        /// <summary>
        /// FIELD-NAME GUARD: reads the live <see cref="NeoParticleEmitter"/> back through a fresh
        /// <see cref="SerializedObject"/> and asserts the generator wrote the values the spec asked for.
        /// If a generator/exporter <c>FindProperty</c> name ever drifts from the emitter's actual
        /// serialized field id, the named property fetch returns null here and this test fails loudly —
        /// whereas the spec→spec round-trip above would still pass because both ends share the drift.
        /// </summary>
        [Test]
        public void Emitter_LiveSerializedFields_MatchTheSpec()
        {
            GameObject prefab = GenerateParticleView();
            NeoParticleEmitter emitter = prefab.GetComponentInChildren<NeoParticleEmitter>(true);
            Assert.IsNotNull(emitter, "particles spec must add a NeoParticleEmitter");

            var so = new SerializedObject(emitter);
            Assert.AreEqual(48, so.FindProperty("capacity").intValue);
            Assert.AreEqual(24, so.FindProperty("burstCount").intValue);
            Assert.AreEqual(0f, so.FindProperty("rate").floatValue);
            Assert.IsTrue(so.FindProperty("emitOnEnable").boolValue);
            Assert.AreEqual((int)ShapeType.RoundedRect, so.FindProperty("particleShape").enumValueIndex);
            Assert.AreEqual(40f, so.FindProperty("cornerRadiusPercent").floatValue);
            Assert.AreEqual(new Vector2(8f, 20f), so.FindProperty("sizeRange").vector2Value);
            Assert.AreEqual(new Vector2(0.5f, 1.2f), so.FindProperty("lifetimeRange").vector2Value);
            Assert.AreEqual(new Vector2(200f, 500f), so.FindProperty("speedRange").vector2Value);
            Assert.AreEqual(90f, so.FindProperty("emitAngle").floatValue);
            Assert.AreEqual(120f, so.FindProperty("emitSpread").floatValue);
            Assert.AreEqual(new Vector2(-90f, 90f), so.FindProperty("angularVelocityRange").vector2Value);
            Assert.AreEqual(2, so.FindProperty("moduleConfigs").arraySize, "both modules serialized");

            // The signal block adds the burst-on-signal trigger with the spec's count.
            var burst = emitter.GetComponent<NeoParticleBurstOnSignal>();
            Assert.IsNotNull(burst, "the signal block must add a NeoParticleBurstOnSignal");
            Assert.AreEqual("Game", burst.Category);
            Assert.AreEqual("Score", burst.SignalName);
            Assert.AreEqual(12, new SerializedObject(burst).FindProperty("count").intValue);
        }

        // ---------------------------------------------------------------- registry seam

        [Test]
        public void Registry_GetKnownModule_NonNull_BogusId_WarnsAndReturnsNull()
        {
            Assert.IsNotNull(ParticleEffectRegistry.Get(ParticleEffectRegistry.Gravity));
            Assert.IsNotNull(ParticleEffectRegistry.Get("colorOverLife"));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("ParticleEffectRegistry"));
            Assert.IsNull(ParticleEffectRegistry.Get("totally-not-a-module"));
        }

        [Test]
        public void Registry_ShipsTheBuiltInModules()
        {
            var ids = ParticleEffectRegistry.All.Select(d => d.Id).ToList();
            CollectionAssert.Contains(ids, "gravity");
            CollectionAssert.Contains(ids, "drag");
            CollectionAssert.Contains(ids, "colorOverLife");
            CollectionAssert.Contains(ids, "sizeOverLife");
        }
    }
}
