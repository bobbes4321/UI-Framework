using System.Collections.Generic;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pillar C's device-preset registry (Pattern R, mirroring <see cref="ComposerCatalogKindsTests"/>):
    /// <see cref="ComposerDevicePresets"/> ships a useful built-in spread through the seam,
    /// <see cref="ComposerDevicePresets.Register"/> adds/replaces a preset by id, and
    /// <see cref="UISpecPreview.DefaultResolutions"/> is a DERIVED view of the registry (single source
    /// of truth) so the headless agent matrix and the Composer viewport always agree.
    /// </summary>
    public class DevicePresetRegistryTests
    {
        [Test]
        public void All_ShipsTheBuiltInSpread()
        {
            var ids = new List<string>();
            foreach (DevicePreset p in ComposerDevicePresets.All) ids.Add(p.id);

            // the legacy trio (kept first so the agent matrix/indices are unchanged)
            CollectionAssert.Contains(ids, "phone-portrait");
            CollectionAssert.Contains(ids, "phone-landscape");
            CollectionAssert.Contains(ids, "tablet-portrait");
            // a representative slice of the wider spread
            CollectionAssert.Contains(ids, "phone-s");
            CollectionAssert.Contains(ids, "phone-l");
            CollectionAssert.Contains(ids, "tablet-landscape");
            CollectionAssert.Contains(ids, "desktop-16-9");
            CollectionAssert.Contains(ids, "ultrawide-21-9");
            CollectionAssert.Contains(ids, "square");
        }

        [Test]
        public void Builtins_ListedInRegistrationOrder_LegacyTrioFirst()
        {
            Assert.GreaterOrEqual(ComposerDevicePresets.All.Count, 3);
            Assert.AreEqual("phone-portrait", ComposerDevicePresets.All[0].id);
            Assert.AreEqual("phone-landscape", ComposerDevicePresets.All[1].id);
            Assert.AreEqual("tablet-portrait", ComposerDevicePresets.All[2].id);
        }

        [Test]
        public void PhonePortrait_MatchesLegacyDimensions()
        {
            Assert.IsTrue(ComposerDevicePresets.TryGet("phone-portrait", out DevicePreset p));
            Assert.AreEqual(1080, p.width);
            Assert.AreEqual(1920, p.height);
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(ComposerDevicePresets.TryGet("nope-not-a-device", out _));
            Assert.IsFalse(ComposerDevicePresets.TryGet(null, out _));
        }

        [Test]
        public void Register_AppendsNovelPreset_ThenReplacesByIdInPlace()
        {
            const string id = "test-watch";
            int before = ComposerDevicePresets.All.Count;

            ComposerDevicePresets.Register(new DevicePreset(id, "Watch", 396, 484));
            Assert.AreEqual(before + 1, ComposerDevicePresets.All.Count, "a novel id appends");
            Assert.IsTrue(ComposerDevicePresets.TryGet(id, out DevicePreset got));
            Assert.AreEqual("Watch", got.label);
            Assert.AreEqual(396, got.width);

            // re-registering the same id replaces in place (no duplicate row)
            ComposerDevicePresets.Register(new DevicePreset(id, "Watch XL", 448, 568));
            Assert.AreEqual(before + 1, ComposerDevicePresets.All.Count, "same id replaces, never duplicates");
            Assert.IsTrue(ComposerDevicePresets.TryGet(id, out DevicePreset got2));
            Assert.AreEqual("Watch XL", got2.label);
            Assert.AreEqual(448, got2.width);
        }

        [Test]
        public void DefaultResolutions_DeriveFromTheRegistry()
        {
            (string name, int width, int height)[] matrix = UISpecPreview.DefaultResolutions;

            // one row per registered preset, same order, same id/width/height
            Assert.AreEqual(ComposerDevicePresets.All.Count, matrix.Length);
            for (int i = 0; i < matrix.Length; i++)
            {
                DevicePreset preset = ComposerDevicePresets.All[i];
                Assert.AreEqual(preset.id, matrix[i].name);
                Assert.AreEqual(preset.width, matrix[i].width);
                Assert.AreEqual(preset.height, matrix[i].height);
            }
        }

        [Test]
        public void DefaultResolutions_ReflectANewlyRegisteredPreset()
        {
            const string id = "test-kiosk";
            int before = UISpecPreview.DefaultResolutions.Length;
            ComposerDevicePresets.Register(new DevicePreset(id, "Kiosk", 1080, 1920));

            (string name, int width, int height)[] matrix = UISpecPreview.DefaultResolutions;
            Assert.AreEqual(before + 1, matrix.Length, "the derived matrix grows with the registry");

            bool found = false;
            foreach (var row in matrix) if (row.name == id) found = true;
            Assert.IsTrue(found, "a registered preset surfaces in the agent resolution matrix");
        }
    }
}
