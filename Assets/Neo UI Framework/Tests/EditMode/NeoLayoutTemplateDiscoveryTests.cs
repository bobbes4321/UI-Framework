using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 3.1 contract: <see cref="NeoLayoutTemplateDefinition"/> assets are lazy-discovered by
    /// <see cref="NeoLayoutTemplates"/> and folded into <see cref="NeoLayoutTemplates.All"/> (the source
    /// the <c>Insert Template…</c> menu enumerates) — mirroring <c>ShowcaseRegistryTests</c>'s
    /// dropped-asset discovery/eviction test. Covers: a dropped definition appears and is evicted on
    /// delete; built-ins stay first with discovered appended after (sorted by label); and a discovered id
    /// colliding with a built-in/registered id is dropped-with-warning (earlier wins, never silent). The
    /// registry is reset around each test so it never pollutes sibling suites in the same domain.
    /// </summary>
    public class NeoLayoutTemplateDiscoveryTests
    {
        [SetUp]
        public void Reset() => NeoLayoutTemplates.ResetForTests();

        [TearDown]
        public void Cleanup() => NeoLayoutTemplates.ResetForTests();

        private const string MinimalSpec =
            "{ \"views\": [ { \"category\": \"Test\", \"name\": \"Probe\", \"elements\": [] } ] }";

        private static NeoLayoutTemplateDefinition CreateAsset(string id, string label, string assetPath, string specPath)
        {
            File.WriteAllText(specPath, MinimalSpec);
            AssetDatabase.ImportAsset(specPath);
            var def = ScriptableObject.CreateInstance<NeoLayoutTemplateDefinition>();
            def.id = id;
            def.displayName = label;
            def.specPathOverride = specPath;
            AssetDatabase.CreateAsset(def, assetPath);
            AssetDatabase.SaveAssets();
            NeoLayoutTemplates.InvalidateDiscovery();
            return def;
        }

        [Test]
        public void DroppedDefinition_AppearsInAll_ThenEvictedOnDelete()
        {
            const string id = "z-template-discovery-probe";
            const string assetPath = "Assets/ZTemplateProbe.asset";
            const string specPath = "Assets/ZTemplateProbeSpec.json";
            CreateAsset(id, "Discovery Probe", assetPath, specPath);
            try
            {
                Assert.IsTrue(NeoLayoutTemplates.TryGet(id, out TemplateEntry got),
                    "precondition: the dropped definition is discovered");
                Assert.AreEqual("Discovery Probe", got.label, "label maps from displayName");
                Assert.IsTrue(NeoLayoutTemplates.All.Any(t => t.id == id),
                    "the definition appears in All — the Insert Template… menu source");

                AssetDatabase.DeleteAsset(assetPath);
                NeoLayoutTemplates.InvalidateDiscovery();

                Assert.IsFalse(NeoLayoutTemplates.TryGet(id, out _),
                    "a deleted definition must be evicted on the next discovery pass");
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(specPath);
                NeoLayoutTemplates.InvalidateDiscovery();
            }
        }

        [Test]
        public void BuiltinsStayFirst_DiscoveredAppendedAfter()
        {
            const string id = "z-template-order-probe";
            const string assetPath = "Assets/ZTemplateOrderProbe.asset";
            const string specPath = "Assets/ZTemplateOrderProbeSpec.json";
            CreateAsset(id, "Order Probe", assetPath, specPath);
            try
            {
                var all = NeoLayoutTemplates.All;
                int builtinIndex = all.ToList().FindIndex(t => t.id == "main-menu");
                int discoveredIndex = all.ToList().FindIndex(t => t.id == id);

                Assert.GreaterOrEqual(builtinIndex, 0, "a built-in is present");
                Assert.GreaterOrEqual(discoveredIndex, 0, "the discovered definition is present");
                Assert.Less(builtinIndex, discoveredIndex, "built-ins sort before discovered definitions");
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(specPath);
                NeoLayoutTemplates.InvalidateDiscovery();
            }
        }

        [Test]
        public void DiscoveredId_CollidingWithBuiltin_IsDroppedWithWarning_BuiltinWins()
        {
            // "main-menu" is a code built-in; a discovered asset claiming the same id must lose.
            const string assetPath = "Assets/ZTemplateCollisionProbe.asset";
            const string specPath = "Assets/ZTemplateCollisionProbeSpec.json";
            CreateAsset("main-menu", "Impostor Menu", assetPath, specPath);
            try
            {
                LogAssert.Expect(LogType.Warning,
                    new Regex("Discovered layout template id 'main-menu' collides"));

                Assert.IsTrue(NeoLayoutTemplates.TryGet("main-menu", out TemplateEntry got));
                Assert.AreEqual("Main Menu", got.label,
                    "the built-in wins the id collision — the discovered impostor is dropped");
                Assert.AreEqual(1, NeoLayoutTemplates.All.Count(t => t.id == "main-menu"),
                    "a colliding id never duplicates in All");
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(specPath);
                NeoLayoutTemplates.InvalidateDiscovery();
            }
        }
    }
}
