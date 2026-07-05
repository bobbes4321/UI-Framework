using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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

            // Set-membership semantics, not exact-set equality: a project can legitimately have its
            // own discovered button presets (e.g. the committed demo/showcase widget presets)
            // coexisting with our test-registered "Btn" here. Assert (1) our preset is returned,
            // (2) the wrong-kind preset never leaks in, and (3) every returned preset actually
            // matches the requested kind — that's the real contract ForKind promises.
            var buttons = NeoWidgetPresets.ForKind("button").ToList();
            CollectionAssert.Contains(buttons.Select(p => p.presetName).ToList(), "Btn",
                "the test-registered button preset is returned");
            Assert.IsFalse(buttons.Any(p => p.presetName == "Txt"), "a text-kind preset must not leak into a button filter");
            Assert.IsTrue(buttons.All(p => p.targetKind == "button"), "every returned preset must target the requested kind");
        }

        [Test]
        public void Register_IgnoresNullAndNameless_WarnsButNeverThrows()
        {
            LogAssert.Expect(LogType.Warning, new Regex("NeoWidgetPresets: ignored a null/invalid entry"));
            LogAssert.Expect(LogType.Warning, new Regex("NeoWidgetPresets: ignored a null/invalid entry"));
            Assert.DoesNotThrow(() => NeoWidgetPresets.Register(null));
            _a = Make("", "button");
            Assert.DoesNotThrow(() => NeoWidgetPresets.Register(_a));
            Assert.IsFalse(NeoWidgetPresets.All.Any(p => p != null && p.presetName == ""));
        }

        [Test]
        public void DeletedNeoWidgetPresetAsset_IsEvictedOnNextDiscovery()
        {
            const string presetName = "ZDeleteEvictionProbePreset";
            const string path = "Assets/ZWidgetPresetDeleteProbe.asset";
            var asset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            asset.presetName = presetName;
            asset.targetKind = "button";
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.InvalidateDiscovery();
            try
            {
                Assert.IsTrue(NeoWidgetPresets.TryGet(presetName, out _),
                    "precondition: the dropped asset is discovered");

                AssetDatabase.DeleteAsset(path);
                NeoWidgetPresets.InvalidateDiscovery();

                Assert.IsFalse(NeoWidgetPresets.TryGet(presetName, out _),
                    "a deleted NeoWidgetPreset asset must be evicted on the next discovery pass");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                NeoWidgetPresets.InvalidateDiscovery();
            }
        }

        [Test]
        public void RegisteredPreset_SurfacesAsAComponentsPaletteTile()
        {
            _a = Make("Hero CTA", "button");
            NeoWidgetPresets.Register(_a);

            PaletteEntry tile = NeoWidgetPalette.All.FirstOrDefault(e => e.preset == "Hero CTA");
            Assert.IsTrue(tile.IsPreset, "the preset appears as a preset-bearing palette tile");
            Assert.AreEqual(NeoWidgetPalette.ComponentsCategory, tile.category, "it groups under Components");
            Assert.AreEqual("button", tile.kind, "the tile creates the preset's target kind");
            Assert.AreEqual("Hero CTA", tile.label, "the tile is labeled by the preset name");
        }
    }
}
