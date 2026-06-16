using System.Linq;
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
    /// Widget presets (Wave 1): a linked preset resolves at generate (base, element overrides), and the
    /// link survives export as a preset name + ONLY the override delta — byte-identical through
    /// export → generate → export. A missing preset degrades gracefully (loud warning, builds from the
    /// element's own fields).
    /// </summary>
    public class PresetRoundTripTests
    {
        private const string PresetName = "TestHeroCTA";

        private NeoWidgetPreset _preset;

        private const string SpecJson = @"{
          ""views"": [ { ""id"": ""Pre/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""button"": { ""id"": ""Pre/Plain"",     ""label"": ""Plain"",  ""preset"": ""TestHeroCTA"" } },
              { ""button"": { ""id"": ""Pre/Override"",  ""label"": ""Over"",   ""preset"": ""TestHeroCTA"", ""variant"": ""ghost"" } }
            ] } }
          ] } ]
        }";

        [SetUp]
        public void SetUp()
        {
            _preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            _preset.presetName = PresetName;
            _preset.targetKind = "button";
            _preset.variant = "danger";
            _preset.sizeVariant = "lg";
            NeoWidgetPresets.Register(_preset);
        }

        [TearDown]
        public void TearDown()
        {
            NeoWidgetPresets.ResetForTests();
            if (_preset != null) Object.DestroyImmediate(_preset);
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject Generate()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Pre_Screen.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        private static GameObject Button(GameObject prefab, string name) =>
            prefab.GetComponentsInChildren<UIButton>(true)
                .First(b => b.id.Matches("Pre", name)).gameObject;

        [Test]
        public void Preset_ResolvesAtGenerate_AsBase()
        {
            GameObject prefab = Generate();

            GameObject plain = Button(prefab, "Plain");
            WidgetStyleTag plainStyle = plain.GetComponent<WidgetStyleTag>();
            Assert.AreEqual("danger", plainStyle.variant, "preset variant resolves when the element sets none");
            Assert.AreEqual("lg", plainStyle.size, "preset size resolves when the element sets none");
            Assert.AreEqual(PresetName, plain.GetComponent<WidgetPresetTag>()?.presetName,
                "generated widget is tagged with its preset for round-trip");
        }

        [Test]
        public void ElementField_OverridesPreset()
        {
            GameObject prefab = Generate();

            GameObject over = Button(prefab, "Override");
            WidgetStyleTag overStyle = over.GetComponent<WidgetStyleTag>();
            Assert.AreEqual("ghost", overStyle.variant, "an explicit element variant wins over the preset");
            Assert.AreEqual("lg", overStyle.size, "the un-overridden size still comes from the preset");
        }

        [Test]
        public void Export_WritesPresetPlusOnlyTheOverrideDelta()
        {
            Generate();
            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec stack = exported.views.First(v => v.id == "Pre/Screen").elements.First(e => e.kind == "vstack");

            ElementSpec plain = stack.children.First(e => e.id == "Pre/Plain");
            Assert.AreEqual(PresetName, plain.preset, "the preset link is preserved on export");
            Assert.IsNull(plain.variant, "a field equal to the preset is dropped (delta only)");
            Assert.IsNull(plain.sizeVariant, "a field equal to the preset is dropped (delta only)");

            ElementSpec over = stack.children.First(e => e.id == "Pre/Override");
            Assert.AreEqual(PresetName, over.preset);
            Assert.AreEqual("ghost", over.variant, "an overridden field stays in the delta");
            Assert.IsNull(over.sizeVariant, "the un-overridden field is still dropped");
        }

        [Test]
        public void Export_Generate_Export_IsByteIdenticalWithPresets()
        {
            Generate();
            string first = UISpecExporter.ExportProject().ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(first));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string second = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(first, second, "presets must round-trip byte-identically through export → generate → export");
        }

        [Test]
        public void MissingPreset_FallsBackGracefullyAndWarns()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Pre/Screen"", ""elements"": [
              { ""button"": { ""id"": ""Pre/Ghosted"", ""label"": ""X"", ""preset"": ""NoSuchPreset"", ""variant"": ""secondary"" } }
            ] } ] }";

            LogAssert.Expect(LogType.Warning, new Regex("missing preset 'NoSuchPreset'"));
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));

            Assert.IsEmpty(report.issues, "a missing preset is a soft warning, not a hard issue");
            Assert.IsTrue(report.warnings.Any(w => w.Contains("NoSuchPreset")), "the fallback is surfaced as a warning");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Pre_Screen.prefab");
            GameObject button = prefab.GetComponentsInChildren<UIButton>(true).First().gameObject;
            Assert.AreEqual("secondary", button.GetComponent<WidgetStyleTag>().variant,
                "the element's own fields still apply when the preset is missing");
        }
    }
}
