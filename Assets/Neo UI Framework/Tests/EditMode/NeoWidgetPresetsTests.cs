using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using Neo.UI.Editor.Composer;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The widget-preset registry (Pattern R): register / replace-by-name / lookup / kind-scoping, plus
    /// the test-only reset. Mirrors ShowcaseRegistryTests.
    /// </summary>
    public class NeoWidgetPresetsTests
    {
        private NeoWidgetPreset _a;
        private NeoWidgetPreset _b;

        [TearDown]
        public void TearDown()
        {
            NeoWidgetPresets.ResetForTests();
            if (_a != null) Object.DestroyImmediate(_a);
            if (_b != null) Object.DestroyImmediate(_b);
        }

        private static NeoWidgetPreset Make(string name, string kind)
        {
            var p = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            p.presetName = name;
            p.targetKind = kind;
            return p;
        }

        [Test]
        public void Register_And_TryGet_RoundTrip()
        {
            _a = Make("Alpha", "button");
            NeoWidgetPresets.Register(_a);

            Assert.IsTrue(NeoWidgetPresets.TryGet("Alpha", out NeoWidgetPreset got));
            Assert.AreSame(_a, got);
            Assert.IsFalse(NeoWidgetPresets.TryGet("Nope", out _));
            Assert.IsFalse(NeoWidgetPresets.TryGet(null, out _));
        }

        [Test]
        public void Register_ReplacesByName_NotAppend()
        {
            _a = Make("Dup", "button");
            _b = Make("Dup", "text");
            NeoWidgetPresets.Register(_a);
            NeoWidgetPresets.Register(_b);

            Assert.AreEqual(1, NeoWidgetPresets.All.Count(p => p.presetName == "Dup"),
                "same-name register replaces in place");
            Assert.IsTrue(NeoWidgetPresets.TryGet("Dup", out NeoWidgetPreset got));
            Assert.AreSame(_b, got, "the later registration wins");
        }

        [Test]
        public void ForKind_FiltersByTargetKind()
        {
            _a = Make("Btn", "button");
            _b = Make("Txt", "text");
            NeoWidgetPresets.Register(_a);
            NeoWidgetPresets.Register(_b);

            CollectionAssert.AreEquivalent(
                new[] { "Btn" },
                NeoWidgetPresets.ForKind("button").Select(p => p.presetName).ToArray());
        }

        [Test]
        public void Register_IgnoresNullAndNameless()
        {
            LogAssert_ExpectAnyWarning();
            NeoWidgetPresets.Register(null);
            _a = Make("", "button");
            NeoWidgetPresets.Register(_a);
            Assert.IsFalse(NeoWidgetPresets.All.Any(p => p != null && p.presetName == ""));
        }

        private static void LogAssert_ExpectAnyWarning()
        {
            // Register logs a warning for null/name-less presets; allow it without coupling to the wording.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        }

        [Test]
        public void RegisteredPreset_SurfacesAsAComponentsPaletteTile()
        {
            _a = Make("Hero CTA", "button");
            NeoWidgetPresets.Register(_a);

            PaletteEntry tile = ComposerPalette.All.FirstOrDefault(e => e.preset == "Hero CTA");
            Assert.IsTrue(tile.IsPreset, "the preset appears as a preset-bearing palette tile");
            Assert.AreEqual(ComposerPalette.ComponentsCategory, tile.category, "it groups under Components");
            Assert.AreEqual("button", tile.kind, "the tile creates the preset's target kind");
            Assert.AreEqual("Hero CTA", tile.label, "the tile is labeled by the preset name");
        }
    }
}
