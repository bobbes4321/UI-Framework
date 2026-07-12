using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Wave 5 Task 5.3 (audit D1): <see cref="PresetFields"/> is the single descriptor table for "which
    /// fields does a NeoWidgetPreset govern" — replacing five hand-mirrored copies, two of which had
    /// shipped bugs. These tests exercise the table directly (every field's merge -> delta round-trip,
    /// the project extension seam) plus the end-to-end regression for the worst of the two bugs:
    /// <c>NeoSceneAuthoring.ApplyPreset</c> used to keep the widget's OLD icon instead of letting the
    /// preset's icon win, because icon wasn't in its hand-written kept-field list.
    /// </summary>
    public class PresetFieldsTests
    {
        private const string ScratchFolder = "Assets/NeoUITestScratchPresetFields";

        private NeoUISettings _settings;
        private GameObject _viewRoot;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            if (_settings != null && _settings.theme != null)
            {
                StarterKitBootstrap.EnsureFactoryTokens(_settings.theme);
                StarterKitBootstrap.EnsureTextStyles(_settings.theme);
            }
        }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(ScratchFolder))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratchPresetFields");

            var view = new ViewSpec { category = "PresetFields", viewName = "V" };
            _viewRoot = UISpecGenerator.BuildViewGameObject(view, _settings, new GenerateReport());
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewRoot != null) Object.DestroyImmediate(_viewRoot);
            NeoWidgetPresets.ResetForTests();
            AssetDatabase.DeleteAsset(ScratchFolder);
            AssetDatabase.SaveAssets();
        }

        private static bool InLayout(GameObject widget) =>
            ((RectTransform)widget.transform.parent).GetComponent<LayoutGroup>() != null;

        // ---------------------------------------------------------------- table completeness

        [Test]
        public void All_ContainsExactlyTheExpectedPresetFields()
        {
            var names = PresetFields.All.Select(f => f.name).ToList();

            // Every preset-governed field: the design-system references + direct scalars, the shape
            // geometry overrides (border/softness/radius/per-corner), and the motion -> animations.loop
            // custom field. Adding a field here is the deliberate tripwire — update this list with it.
            string[] expected =
            {
                "variant", "sizeVariant", "textStyle", "style", "background", "labelColor",
                "icon", "radius", "radiusUnit", "cornerRadii", "borderWidth", "borderColor",
                "softness", "padding", "padding4", "spacing", "motion",
            };
            foreach (string name in expected)
                Assert.Contains(name, names, $"PresetFields.All is missing '{name}'");
            Assert.AreEqual(expected.Length, names.Distinct().Count(),
                "expected exactly the listed preset fields — no more, no fewer, no dupes");
        }

        // ---------------------------------------------------------------- per-field merge -> delta

        // One (elementOverrideValue, presetValue) sample pair per builtin field, distinct so a merge vs.
        // an override are never accidentally equal.
        private static readonly Dictionary<string, (object elementValue, object presetValue)> SampleValues =
            new Dictionary<string, (object, object)>
            {
                ["variant"] = ("ghost", "danger"),
                ["sizeVariant"] = ("sm", "lg"),
                ["textStyle"] = ("Caption", "Body"),
                ["style"] = ("Flat", "Elevated"),
                ["background"] = ("SurfaceAlt", "Surface"),
                ["labelColor"] = ("Warning", "OnPrimary"),
                ["icon"] = ("star", "heart"),
                ["radius"] = (4f, 12f),
                ["radiusUnit"] = ("px", "percent"),
                ["cornerRadii"] = (new float[] { 1f, 2f, 3f, 4f }, new float[] { 5f, 6f, 7f, 8f }),
                ["borderWidth"] = (2f, 6f),
                ["borderColor"] = ("#111111", "#222222"),
                ["softness"] = (1f, 4f),
                ["padding"] = (4f, 16f),
                ["padding4"] = (new float[] { 1f, 2f, 3f, 4f }, new float[] { 8f, 8f, 8f, 8f }),
                ["spacing"] = (2f, 10f),
                ["motion"] = ("Spin", "Pulse"),
            };

        [Test]
        public void EveryField_MergesFromPresetThenClearsBackOutWhenUnchanged()
        {
            foreach (PresetField field in PresetFields.All)
            {
                Assert.IsTrue(SampleValues.TryGetValue(field.name, out (object elementValue, object presetValue) sample),
                    $"no sample value registered in this test for field '{field.name}' — add one above");

                var element = new ElementSpec { kind = "button" };
                var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
                try
                {
                    field.setPreset(preset, sample.presetValue);
                    Assert.IsTrue(field.IsUnsetOnElement(element), $"'{field.name}': a fresh element must start unset");

                    // Generate-time merge: an unset element field is filled from the preset.
                    field.setElement(element, field.getPreset(preset));
                    Assert.IsFalse(field.IsUnsetOnElement(element), $"'{field.name}': merge should have set it");
                    Assert.IsTrue(field.equal(field.getElement(element), field.getPreset(preset)),
                        $"'{field.name}': the merged value should equal the preset's");

                    // Export-time delta: a field that still equals the preset's value is cleared, so the
                    // link round-trips as preset name + only the override delta.
                    field.clearElement(element);
                    Assert.IsTrue(field.IsUnsetOnElement(element), $"'{field.name}': delta should have cleared it back out");

                    // An element value that DIFFERS from the preset survives (stays in the delta).
                    field.setElement(element, sample.elementValue);
                    Assert.IsFalse(field.equal(field.getElement(element), field.getPreset(preset)),
                        $"'{field.name}': the sample override must differ from the sample preset value");
                }
                finally
                {
                    Object.DestroyImmediate(preset);
                }
            }
        }

        // ---------------------------------------------------------------- extension seam

        [Test]
        public void Register_CustomField_FlowsThroughMergeAndDelta()
        {
            // A project's custom field doesn't have to live on ElementSpec/NeoWidgetPreset directly — its
            // get/set closures can keep the data anywhere (here, a side table keyed by instance) as long
            // as they honor the same contract every built-in field does.
            var elementValues = new Dictionary<ElementSpec, string>();
            var presetValues = new Dictionary<NeoWidgetPreset, string>();

            var field = new PresetField("testCustomTag",
                e => elementValues.TryGetValue(e, out string v) ? v : null,
                (e, v) => elementValues[e] = (string)v,
                p => presetValues.TryGetValue(p, out string v) ? v : null,
                (p, v) => presetValues[p] = (string)v,
                e => elementValues.Remove(e));

            PresetFields.Register(field);
            NeoWidgetPreset preset = null;
            try
            {
                Assert.IsTrue(PresetFields.All.Contains(field), "Register should have appended the field");

                var element = new ElementSpec { kind = "button" };
                preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
                presetValues[preset] = "PresetTagValue";

                Assert.IsTrue(field.IsUnsetOnElement(element));
                field.setElement(element, field.getPreset(preset));
                Assert.AreEqual("PresetTagValue", elementValues[element], "merge should flow through the custom field");

                Assert.IsTrue(field.equal(field.getElement(element), field.getPreset(preset)));
                field.clearElement(element);
                Assert.IsFalse(elementValues.ContainsKey(element), "delta should flow through the custom field");
            }
            finally
            {
                PresetFields.Remove("testCustomTag");
                if (preset != null) Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void Register_DuplicateName_IsIgnoredNotSilentlyReplaced()
        {
            int before = PresetFields.All.Count;
            var duplicate = new PresetField("icon", e => null, (e, v) => { }, p => null, (p, v) => { }, e => { });

            LogAssert.Expect(LogType.Warning, new Regex("already registered"));
            PresetFields.Register(duplicate);

            Assert.AreEqual(before, PresetFields.All.Count, "a colliding field name must not be appended");
        }

        // ---------------------------------------------------------------- ApplyPreset icon-clobber regression

        [Test]
        public void ApplyPreset_LetsThePresetsIconWin_OverTheWidgetsOldIcon()
        {
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            preset.presetName = "IconRegressionPreset";
            preset.targetKind = "button";
            preset.icon = "heart";
            NeoWidgetPresets.Register(preset);
            try
            {
                var element = new ElementSpec { kind = "button", label = "X", icon = "star" };
                GameObject widget = UISpecGenerator.BuildElementLive(
                    element, (RectTransform)_viewRoot.transform, _settings, new GenerateReport());
                Assert.IsNotNull(widget, "test fixture: expected the icon'd button to build");

                GameObject rebuilt = NeoSceneAuthoring.ApplyPreset(widget, preset.presetName);

                Assert.IsNotNull(rebuilt, "ApplyPreset should have rebuilt the widget");
                ElementSpec after = UISpecExporter.ExportElement(rebuilt, InLayout(rebuilt));
                Assert.AreEqual(preset.presetName, after.preset, "the preset link survives Apply-Preset");
                // The exported delta correctly OMITS `icon` here (ApplyPresetDelta clears any field that
                // still equals the preset's value, per its documented link-plus-delta contract) — that
                // omission itself is what proves the fix: the widget's icon now equals the preset's, so
                // there is nothing to override. Verify against the rebuilt widget's actual icon glyph.
                Transform iconChild = rebuilt.transform.Find(UIWidgetFactory.IconName);
                Assert.IsNotNull(iconChild, "the rebuilt widget must have an icon child");
                TMP_Text iconText = iconChild.GetComponent<TMP_Text>();
                Assert.IsNotNull(iconText?.text, "the icon child must render a glyph");
                Assert.IsTrue(IconMap.TryGetName(iconText.text[0], out string iconName),
                    "the rendered glyph must map back to a known icon name");
                Assert.AreEqual("heart", iconName,
                    "the preset's icon must win — the widget's old icon must not clobber it (audit D1 bug)");
            }
            finally
            {
                NeoWidgetPresets.ResetForTests();
                Object.DestroyImmediate(preset);
            }
        }
    }
}
