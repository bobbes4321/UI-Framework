using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The built-in widget-preset library seeds idempotently and is discoverable through the registry.
    /// Mirrors the other "create or repair" bootstrap tests; teardown deletes only the presets folder so
    /// the run leaves no committed assets behind.
    /// </summary>
    public class PresetLibraryBootstrapTests
    {
        // Both fixtures share the committed PresetsRoot and the static NeoWidgetPresets registry, so a
        // sibling test (or a prior batch run whose DeleteAsset hadn't flushed) can leave the registry
        // populated or assets on disk. Reset BOTH on the way in as well as out, so each test starts from
        // a known-clean state regardless of run order — these tests assert "first run creates" and
        // discovery counts, which only hold from a clean baseline.
        [SetUp]
        public void SetUp() => ResetState();

        [TearDown]
        public void TearDown() => ResetState();

        private static void ResetState()
        {
            AssetDatabase.DeleteAsset(NeoWidgetPresets.PresetsRoot);
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.ResetForTests();
        }

        [Test]
        public void CreateOrRepair_IsIdempotent()
        {
            GenerateReport first = PresetLibraryBootstrap.CreateOrRepair();
            Assert.Greater(first.created.Count, 0, "the first run creates the library");
            Assert.IsEmpty(first.collisions, first.ToString());
            int total = first.created.Count;

            GenerateReport second = PresetLibraryBootstrap.CreateOrRepair();
            Assert.AreEqual(0, second.created.Count, "a second run creates nothing new");
            Assert.AreEqual(total, second.updated.Count, "a second run repairs every existing preset in place");
        }

        [Test]
        public void SeededPresets_AreDiscoverableByName()
        {
            PresetLibraryBootstrap.CreateOrRepair();
            NeoWidgetPresets.InvalidateDiscovery();

            Assert.IsTrue(NeoWidgetPresets.TryGet("Primary Button", out NeoWidgetPreset primary));
            Assert.AreEqual("button", primary.targetKind);
            Assert.AreEqual("primary", primary.variant);

            Assert.IsTrue(NeoWidgetPresets.TryGet("Section Header", out NeoWidgetPreset header));
            Assert.AreEqual("text", header.targetKind);
            Assert.IsTrue(NeoWidgetPresets.ForKind("button").Any(p => p.presetName == "Primary Button"));
        }
    }
}
