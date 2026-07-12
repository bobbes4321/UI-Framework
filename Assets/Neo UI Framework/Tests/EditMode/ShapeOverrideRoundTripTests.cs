using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Per-widget shape geometry overrides (border, softness, radius/per-corner) round-trip through the
    /// spec + preset layer and survive runtime. Regression net for two coupled bugs: color animators not
    /// baking their resting color in edit mode (WYSIWYG), and a ThemeShapeStyleTarget clobbering a
    /// per-widget shape override on enable (which also silently reset preset radii like Primary Button
    /// Large's 16px). The fix: per-aspect ownership flags on the style target, plus the geometry fields
    /// wired through ElementSpec / NeoWidgetPreset / PresetFields / exporter / generator.
    /// </summary>
    public class ShapeOverrideRoundTripTests
    {
        // ------------------------------------------------------------------ pure unit: aspect masking

        [Test]
        public void ShapeStyle_ApplyTo_AppliesOwnedAspects_PreservesUnowned()
        {
            var go = new GameObject("shape", typeof(RectTransform));
            try
            {
                var shape = go.AddComponent<NeoShape>();
                shape.border = 5f;
                shape.edgeSoftness = 3f;
                shape.cornerRadius = 20f;
                var style = new ShapeStyle { borderWidth = 1f, radius = 12f, softness = 0f };

                // Own radius only; border + softness are per-widget overrides the style must not touch.
                style.ApplyTo(shape, null, ShapeStyleAspects.Radius);

                Assert.AreEqual(12f, shape.cornerRadius, "owned radius aspect is applied");
                Assert.AreEqual(5f, shape.border, "un-owned border override is preserved");
                Assert.AreEqual(3f, shape.edgeSoftness, "un-owned softness override is preserved");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ThemeShapeStyleTarget_OwnedAspects_ReflectsFlags()
        {
            var go = new GameObject("shape", typeof(RectTransform));
            try
            {
                go.AddComponent<NeoShape>();
                var t = go.AddComponent<ThemeShapeStyleTarget>();
                t.applyRadius = true;
                t.applyBorder = false;
                t.applySoftness = true;
                t.applyFill = false;
                Assert.AreEqual(ShapeStyleAspects.Radius | ShapeStyleAspects.Softness, t.OwnedAspects);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------ color animator resting bake

        [Test]
        public void SelectableColorAnimator_ApplyRestingColor_BakesNormalOntoShape()
        {
            var go = new GameObject("btn", typeof(RectTransform));
            try
            {
                var shape = go.AddComponent<NeoShape>();
                var anim = go.AddComponent<UISelectableColorAnimator>();
                var red = new Color(0.9f, 0.1f, 0.1f, 1f);
                anim.colors.normal = new ThemeColorRef(red);

                anim.ApplyRestingColor(); // what OnValidate calls in edit mode

                Assert.AreEqual(red, shape.color, "editing the Normal color bakes onto the shape without entering play");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------ preset capture / apply seam

        [Test]
        public void PresetFields_CaptureThenApply_RoundTripsShapeOverrides()
        {
            var element = new ElementSpec
            {
                kind = "button",
                borderWidth = 3f,
                borderColor = "#00FF00",
                softness = 2f,
                cornerRadii = new[] { 1f, 2f, 3f, 4f }
            };
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            try
            {
                // Capture — mirrors NeoSceneAuthoring.CapturePresetFields (Create/Update Preset From Widget).
                foreach (PresetField field in PresetFields.All)
                    field.setPreset(preset, field.getElement(element));

                Assert.AreEqual(3f, preset.borderWidth, "border width is captured into the preset");
                Assert.AreEqual("#00FF00", preset.borderColor, "border color is captured into the preset");
                Assert.AreEqual(2f, preset.softness, "softness is captured into the preset");
                CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, preset.cornerRadii, "per-corner radii captured");

                // Apply — mirrors the CreateWidget-from-preset loop (clear then bake the preset's values).
                var fresh = new ElementSpec { kind = "button" };
                foreach (PresetField field in PresetFields.All)
                {
                    field.clearElement(fresh);
                    object value = field.getPreset(preset);
                    if (value != null) field.setElement(fresh, value);
                }

                Assert.AreEqual(3f, fresh.borderWidth, "border width restored from the preset");
                Assert.AreEqual("#00FF00", fresh.borderColor, "border color restored from the preset");
                Assert.AreEqual(2f, fresh.softness, "softness restored from the preset");
                CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, fresh.cornerRadii, "per-corner radii restored");
            }
            finally { Object.DestroyImmediate(preset); }
        }

        [Test]
        public void PresetFields_UnsetShapeOverrides_StayUnset()
        {
            // A preset that governs none of the shape geometry must not force zeros onto elements.
            var preset = ScriptableObject.CreateInstance<NeoWidgetPreset>();
            try
            {
                Assert.IsNull(preset.BorderWidthOrNull, "unset border width reads back as null (the -1 sentinel)");
                Assert.IsNull(preset.SoftnessOrNull, "unset softness reads back as null");
                Assert.IsNull(preset.CornerRadiiOrNull, "unset per-corner radii read back as null");
            }
            finally { Object.DestroyImmediate(preset); }
        }

        // ------------------------------------------------------------------ spec JSON round-trip

        [Test]
        public void ElementSpec_JsonRoundTrip_PreservesShapeOverrides()
        {
            const string json = @"{ ""views"": [ { ""id"": ""Ov/Screen"", ""elements"": [
              { ""shape"": { ""id"": ""Ov/Card"", ""shape"": ""RoundedRect"", ""radius"": 20, ""radiusUnit"": ""percent"",
                             ""borderWidth"": 4, ""borderColor"": ""#FF0000"", ""softness"": 6,
                             ""cornerRadii"": [8, 8, 0, 0] } }
            ] } ] }";

            UISpec reparsed = UISpec.FromJson(UISpec.FromJson(json).ToJson());
            ElementSpec el = reparsed.views.First().elements.First(e => e.kind == "shape");

            Assert.AreEqual(4f, el.borderWidth);
            Assert.AreEqual("#FF0000", el.borderColor);
            Assert.AreEqual(6f, el.softness);
            Assert.AreEqual("percent", el.radiusUnit);
            CollectionAssert.AreEqual(new[] { 8f, 8f, 0f, 0f }, el.cornerRadii);
        }

        // ------------------------------------------------------------------ full generate + export

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject Generate(string json, string viewFile)
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.issues, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/{viewFile}.prefab");
            Assert.IsNotNull(prefab, "generated view prefab missing");
            return prefab;
        }

        [Test]
        public void ShapeOverrides_Generate_BakeOntoShape_AndExportBack()
        {
            const string json = @"{ ""views"": [ { ""id"": ""OvGen/Screen"", ""elements"": [
              { ""shape"": { ""id"": ""OvGen/Card"", ""shape"": ""RoundedRect"", ""radius"": 18,
                             ""borderWidth"": 4, ""borderColor"": ""#FF0000"", ""softness"": 6 } }
            ] } ] }";

            GameObject prefab = Generate(json, "OvGen_Screen");
            NeoShape shape = prefab.GetComponentsInChildren<NeoShape>(true).First();
            Assert.AreEqual(4f, shape.border, "border width bakes onto the generated shape");
            Assert.AreEqual(6f, shape.edgeSoftness, "softness bakes onto the generated shape");
            Assert.AreEqual(18f, shape.cornerRadius, "radius bakes onto the generated shape");
            Assert.Greater(shape.outlineColor.r, 0.9f, "border color (#FF0000) bakes onto the generated shape");
            Assert.Less(shape.outlineColor.g, 0.1f);

            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec el = exported.views.First(v => v.id == "OvGen/Screen").elements.First(e => e.kind == "shape");
            Assert.AreEqual(4f, el.borderWidth, "border width is captured on export");
            Assert.AreEqual(6f, el.softness, "softness is captured on export");
            Assert.IsFalse(string.IsNullOrEmpty(el.borderColor), "border color is captured on export");
        }

        [Test]
        public void ButtonBorderOverride_ReleasesStyleOwnership_AndExports()
        {
            const string json = @"{ ""views"": [ { ""id"": ""OvBtn/Screen"", ""elements"": [
              { ""button"": { ""id"": ""OvBtn/Cta"", ""label"": ""Go"", ""borderWidth"": 3 } }
            ] } ] }";

            GameObject prefab = Generate(json, "OvBtn_Screen");
            UIButton button = prefab.GetComponentsInChildren<UIButton>(true).First();
            NeoShape shape = button.GetComponent<NeoShape>();
            ThemeShapeStyleTarget styleTarget = button.GetComponent<ThemeShapeStyleTarget>();

            Assert.IsNotNull(styleTarget, "a primary button carries the Control shape style target");
            Assert.IsFalse(styleTarget.applyBorder,
                "a per-widget border override releases the style's border ownership so runtime won't clobber it");
            Assert.IsTrue(styleTarget.applyRadius, "un-overridden aspects still follow the theme style");
            Assert.AreEqual(3f, shape.border, "the border override is baked onto the button shape");

            UISpec exported = UISpecExporter.ExportProject();
            ElementSpec el = exported.views.First(v => v.id == "OvBtn/Screen").elements
                .First(e => e.kind == "button");
            Assert.AreEqual(3f, el.borderWidth, "the customized border is captured on export (feeds Create Preset From Widget)");
        }
    }
}
