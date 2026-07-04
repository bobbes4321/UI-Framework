using System.Collections.Generic;
using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Wave 4 Task 4.3 mirror tests: <see cref="ShapeEffectRegistry"/>, <see cref="ParticleEffectRegistry"/>,
    /// <see cref="LayoutConstraints"/>, <see cref="LayoutSizingModes"/> and <see cref="BreakpointConditions"/>
    /// migrated onto the shared <see cref="NeoKeyedRegistry{T}"/> base — per the Wave 4 shared instructions,
    /// each gets a replace-on-duplicate-override case and an invalid-register-warns-never-throws case.
    /// Also covers the formerly-silent misses the task fixed (<see cref="ParticleEffectRegistry.GetForConfig"/>,
    /// <see cref="BreakpointConditions.TryGet"/>) and the new <see cref="ShapeEffectDefinitions"/> asset
    /// registry (eviction on delete).
    /// </summary>
    public class AgentDescriptorRegistryMigrationTests
    {
        // ------------------------------------------------------------------ ShapeEffectRegistry

        private sealed class StubShapeEffectDescriptor : IShapeEffectDescriptor
        {
            public string Id { get; }
            public StubShapeEffectDescriptor(string id) { Id = id; }
            public bool BatchSafe => true;
            public void Apply(GameObject host, IDictionary<string, object> parameters) { }
            public bool TryExport(GameObject host, out IDictionary<string, object> parameters) { parameters = null; return false; }
        }

        [Test]
        public void ShapeEffectRegistry_Register_SameId_ReplacesInPlace()
        {
            try
            {
                int before = ShapeEffectRegistry.All.Count;
                var first = new StubShapeEffectDescriptor("probeEffect");
                var second = new StubShapeEffectDescriptor("probeEffect");

                ShapeEffectRegistry.Register(first);
                Assert.AreEqual(before + 1, ShapeEffectRegistry.All.Count);
                ShapeEffectRegistry.Register(second);
                Assert.AreEqual(before + 1, ShapeEffectRegistry.All.Count, "a same-id registration replaces, never duplicates");
                Assert.AreSame(second, ShapeEffectRegistry.Get("probeEffect"));
            }
            finally
            {
                ShapeEffectRegistry.ResetForTests();
            }
        }

        [Test]
        public void ShapeEffectRegistry_Register_Null_WarnsAndNeverThrows()
        {
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("ShapeEffectRegistry"));
                Assert.DoesNotThrow(() => ShapeEffectRegistry.Register(null));
            }
            finally
            {
                ShapeEffectRegistry.ResetForTests();
            }
        }

        // ------------------------------------------------------------------ ParticleEffectRegistry

        private sealed class StubParticleModuleConfig : ParticleModuleConfig
        {
            public override string Id => "probeModule";
            public override IParticleModule Build() => null;
        }

        private sealed class StubParticleModuleDescriptor : IParticleModuleDescriptor
        {
            public string Id { get; }
            public StubParticleModuleDescriptor(string id) { Id = id; }
            public System.Type ConfigType => typeof(StubParticleModuleConfig);
            public ParticleModuleConfig Build(IDictionary<string, object> parameters) => new StubParticleModuleConfig();
            public IDictionary<string, object> Export(ParticleModuleConfig config) => new Dictionary<string, object>();
        }

        [Test]
        public void ParticleEffectRegistry_Register_SameId_ReplacesInPlace()
        {
            try
            {
                int before = ParticleEffectRegistry.All.Count;
                var first = new StubParticleModuleDescriptor("probeModule");
                var second = new StubParticleModuleDescriptor("probeModule");

                ParticleEffectRegistry.Register(first);
                Assert.AreEqual(before + 1, ParticleEffectRegistry.All.Count);
                ParticleEffectRegistry.Register(second);
                Assert.AreEqual(before + 1, ParticleEffectRegistry.All.Count, "a same-id registration replaces, never duplicates");
                Assert.AreSame(second, ParticleEffectRegistry.Get("probeModule"));
            }
            finally
            {
                ParticleEffectRegistry.ResetForTests();
            }
        }

        [Test]
        public void ParticleEffectRegistry_Register_Null_WarnsAndNeverThrows()
        {
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("ParticleEffectRegistry"));
                Assert.DoesNotThrow(() => ParticleEffectRegistry.Register(null));
            }
            finally
            {
                ParticleEffectRegistry.ResetForTests();
            }
        }

        [Test]
        public void ParticleEffectRegistry_GetForConfig_UnregisteredType_WarnsAndReturnsNull()
        {
            // A config subclass with no registered descriptor (audit A3 — this used to fail silently).
            var orphanConfig = new StubParticleModuleConfig();

            LogAssert.Expect(LogType.Warning, new Regex("ParticleEffectRegistry.GetForConfig"));
            Assert.IsNull(ParticleEffectRegistry.GetForConfig(orphanConfig));
        }

        // ------------------------------------------------------------------ LayoutConstraints

        private sealed class StubLayoutConstraint : ILayoutConstraint
        {
            public string Id { get; }
            public LayoutAxis Axis { get; }
            public StubLayoutConstraint(string id, LayoutAxis axis) { Id = id; Axis = axis; }
            public bool Stretches => false;
            public void Apply(RectTransform rect, LayoutOffsetValue offset, float? size) { }
            public bool TryDetect(RectTransform rect, out LayoutOffsetValue offset, out float? size)
            { offset = default; size = null; return false; }
        }

        [Test]
        public void LayoutConstraints_Register_Null_WarnsAndNeverThrows()
        {
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("LayoutConstraints"));
                Assert.DoesNotThrow(() => LayoutConstraints.Register(null));
            }
            finally
            {
                LayoutConstraints.ResetForTests();
            }
        }

        [Test]
        public void LayoutConstraints_SameIdDifferentAxis_DoesNotCollide()
        {
            try
            {
                // Registering a probe id under ONE axis must not leak into the other axis's lookup —
                // proves the composite "{Id}:{Axis}" key, not a bare Id key.
                int before = LayoutConstraints.All.Count;
                LayoutConstraints.Register(new StubLayoutConstraint("probeConstraint", LayoutAxis.Horizontal));
                Assert.AreEqual(before + 1, LayoutConstraints.All.Count);
                Assert.IsNotNull(LayoutConstraints.Get("probeConstraint", LayoutAxis.Horizontal));

                LogAssert.Expect(LogType.Warning, new Regex("no constraint"));
                Assert.IsNull(LayoutConstraints.Get("probeConstraint", LayoutAxis.Vertical));
            }
            finally
            {
                LayoutConstraints.ResetForTests();
            }
        }

        // ------------------------------------------------------------------ LayoutSizingModes

        private sealed class StubSizingMode : ILayoutSizingMode
        {
            public string Id { get; }
            public StubSizingMode(string id) { Id = id; }
            public bool WantsForceExpand => false;
            public void Apply(GameObject go, bool horizontal, float? size) { }
            public bool TryDetect(GameObject go, bool horizontal) => false;
        }

        [Test]
        public void LayoutSizingModes_Register_SameId_ReplacesInPlace()
        {
            try
            {
                int before = LayoutSizingModes.All.Count;
                var first = new StubSizingMode("probeSizing");
                var second = new StubSizingMode("probeSizing");

                LayoutSizingModes.Register(first);
                Assert.AreEqual(before + 1, LayoutSizingModes.All.Count);
                LayoutSizingModes.Register(second);
                Assert.AreEqual(before + 1, LayoutSizingModes.All.Count, "a same-id registration replaces, never duplicates");
                Assert.AreSame(second, LayoutSizingModes.Get("probeSizing"));
            }
            finally
            {
                LayoutSizingModes.ResetForTests();
            }
        }

        [Test]
        public void LayoutSizingModes_Register_Null_WarnsAndNeverThrows()
        {
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("LayoutSizingModes"));
                Assert.DoesNotThrow(() => LayoutSizingModes.Register(null));
            }
            finally
            {
                LayoutSizingModes.ResetForTests();
            }
        }

        // ------------------------------------------------------------------ BreakpointConditions

        private sealed class StubBreakpointCondition : IBreakpointCondition
        {
            public string Id { get; }
            public StubBreakpointCondition(string id) { Id = id; }
            public bool IsActive(BreakpointCondition condition) => false;
            public bool Matches(BreakpointCondition condition, BreakpointEnv env) => false;
        }

        [Test]
        public void BreakpointConditions_Register_SameId_ReplacesInPlace()
        {
            try
            {
                int before = BreakpointConditions.All.Count;
                var first = new StubBreakpointCondition("probeCondition");
                var second = new StubBreakpointCondition("probeCondition");

                BreakpointConditions.Register(first);
                Assert.AreEqual(before + 1, BreakpointConditions.All.Count);
                BreakpointConditions.Register(second);
                Assert.AreEqual(before + 1, BreakpointConditions.All.Count, "a same-id registration replaces, never duplicates");
                Assert.IsTrue(BreakpointConditions.TryGet("probeCondition", out IBreakpointCondition resolved));
                Assert.AreSame(second, resolved);
            }
            finally
            {
                BreakpointConditions.ResetForTests();
            }
        }

        [Test]
        public void BreakpointConditions_Register_Null_WarnsAndNeverThrows()
        {
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("BreakpointConditions"));
                Assert.DoesNotThrow(() => BreakpointConditions.Register(null));
            }
            finally
            {
                BreakpointConditions.ResetForTests();
            }
        }

        [Test]
        public void BreakpointConditions_TryGet_UnknownId_WarnsAndReturnsFalse()
        {
            // Audit A3 — this used to fail silently despite copying from the warning LayoutConstraints.Get.
            LogAssert.Expect(LogType.Warning, new Regex("BreakpointConditions.TryGet"));
            Assert.IsFalse(BreakpointConditions.TryGet("totally-not-a-condition", out IBreakpointCondition result));
            Assert.IsNull(result);
        }

        // ------------------------------------------------------------------ ShapeEffectDefinitions (asset-backed)

        private const string ProbeFolderName = "ShapeEffectDefinitionsProbe";
        private static string ProbeFolder => $"{NeoTestScratchRoot.ScratchRoot}/{ProbeFolderName}";

        [SetUp]
        public void CreateProbeFolder()
        {
            if (!AssetDatabase.IsValidFolder(NeoTestScratchRoot.ScratchRoot))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratch");
            if (!AssetDatabase.IsValidFolder(ProbeFolder))
                AssetDatabase.CreateFolder(NeoTestScratchRoot.ScratchRoot, ProbeFolderName);
        }

        [OneTimeTearDown]
        public void DeleteProbeFolder() => AssetDatabase.DeleteAsset(ProbeFolder);

        private static string CreateDefinitionAsset(string fileName, string id)
        {
            var asset = ScriptableObject.CreateInstance<ShapeEffectDefinition>();
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            string path = $"{ProbeFolder}/{fileName}.asset";
            AssetDatabase.CreateAsset(asset, path);
            return path;
        }

        [Test]
        public void ShapeEffectDefinitions_DiscoversAndEvictsDeletedAsset()
        {
            string path = CreateDefinitionAsset("Probe", "probeDefinition");
            AssetDatabase.SaveAssets();
            try
            {
                ShapeEffectDefinitions.ResetForTests();
                Assert.IsTrue(ShapeEffectDefinitions.TryGet("probeDefinition", out _), "a fresh definition asset is discovered");

                AssetDatabase.DeleteAsset(path);
                ShapeEffectDefinitions.ResetForTests(); // forces a fresh discovery scan

                Assert.IsFalse(ShapeEffectDefinitions.TryGet("probeDefinition", out _), "a deleted definition asset is evicted, not stuck forever");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                ShapeEffectDefinitions.ResetForTests();
            }
        }
    }
}
