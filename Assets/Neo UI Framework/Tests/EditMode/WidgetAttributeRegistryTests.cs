using System.Collections.Generic;
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
    /// Covers the widget-attribute extensibility seams
    /// (extensibility-seam-widget-attributes-plan.md):
    ///  - the widget-attribute option sets (variants/sizes/aligns/shape names) seed exactly the
    ///    built-ins and accept project Register() calls (Pattern R — Wave 4 Task 4.2 migrated the four
    ///    sets onto <see cref="NeoKeyedRegistry{T}"/>, one per attribute, OrdinalIgnoreCase-keyed);
    ///  - a project ButtonVariantAsset / ButtonSizeAsset on NeoUISettings flows through
    ///    UIWidgetFactory ahead of the built-in switch, and round-trips (Pattern A);
    ///  - an IconMapOverlay glyph resolves through IconMap before the built-in Lucide dict.
    /// Built-in behavior must stay byte-identical when nothing is registered/overlaid
    /// (the existing IconAndVariantTests are the golden net for that).
    /// </summary>
    public class WidgetAttributeRegistryTests
    {
        // ------------------------------------------------------------------ Pattern R: option sets

        [Test]
        public void OptionSets_SeedExactlyTheBuiltIns()
        {
            CollectionAssert.AreEqual(
                new[] { "primary", "secondary", "ghost", "danger" }, NeoWidgetOptions.ButtonVariants);
            CollectionAssert.AreEqual(new[] { "sm", "md", "lg" }, NeoWidgetOptions.ButtonSizes);
            CollectionAssert.AreEqual(new[] { "left", "center", "right" }, NeoWidgetOptions.Aligns);
            CollectionAssert.AreEqual(
                new[] { "roundedRect", "circle", "pill", "checkmark", "chevron", "cross", "ring", "arc" },
                NeoWidgetOptions.ShapeNames);
        }

        [Test]
        public void Register_AppendsNewValue_AndIsIdempotent()
        {
            int before = NeoWidgetOptions.ButtonVariants.Length;
            NeoWidgetOptions.RegisterVariant("success");
            CollectionAssert.Contains(NeoWidgetOptions.ButtonVariants, "success");
            Assert.AreEqual(before + 1, NeoWidgetOptions.ButtonVariants.Length, "registering adds one");

            // case-insensitive no-op (built-in + re-register)
            NeoWidgetOptions.RegisterVariant("success");
            NeoWidgetOptions.RegisterVariant("PRIMARY");
            Assert.AreEqual(before + 1, NeoWidgetOptions.ButtonVariants.Length,
                "re-registering an existing id (any case) is a no-op");

            NeoWidgetOptions.RegisterSize("xl");
            NeoWidgetOptions.RegisterAlign("justify");
            NeoWidgetOptions.RegisterShape("star");
            CollectionAssert.Contains(NeoWidgetOptions.ButtonSizes, "xl");
            CollectionAssert.Contains(NeoWidgetOptions.Aligns, "justify");
            CollectionAssert.Contains(NeoWidgetOptions.ShapeNames, "star");
        }

        [Test]
        public void All_ReturnsSnapshot_CallerCannotMutateSeed()
        {
            string[] snapshot = NeoWidgetOptions.ButtonVariants;
            snapshot[0] = "tampered";
            CollectionAssert.DoesNotContain(NeoWidgetOptions.ButtonVariants, "tampered",
                ".All must hand out a copy, not the backing list");
        }

        [Test]
        public void Register_SameKeyDifferentCasing_ReplacesInPlace_NeverDuplicates()
        {
            int before = NeoWidgetOptions.ButtonVariants.Length;

            // "PRIMARY" is the same key as the built-in "primary" (OrdinalIgnoreCase) — replaces in
            // place rather than appending a second row.
            NeoWidgetOptions.RegisterVariant("PRIMARY");

            Assert.AreEqual(before, NeoWidgetOptions.ButtonVariants.Length, "same key (any case) never duplicates");
            CollectionAssert.Contains(NeoWidgetOptions.ButtonVariants, "PRIMARY");
        }

        [Test]
        public void Register_NullOrEmpty_WarnsAndIgnores_NeverThrows()
        {
            int before = NeoWidgetOptions.ButtonVariants.Length;
            LogAssert.Expect(LogType.Warning, new Regex("NeoWidgetOptions\\.ButtonVariants: ignored a null/invalid entry"));
            LogAssert.Expect(LogType.Warning, new Regex("NeoWidgetOptions\\.ButtonVariants: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => NeoWidgetOptions.RegisterVariant(null));
            Assert.DoesNotThrow(() => NeoWidgetOptions.RegisterVariant(""));

            Assert.AreEqual(before, NeoWidgetOptions.ButtonVariants.Length, "nothing was actually registered");
        }

        // ------------------------------------------------------------------ Pattern A: variant asset

        private const string VariantSpecJson = @"{
          ""views"": [ { ""id"": ""Reg/Screen"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 10, ""children"": [
              { ""button"": { ""id"": ""Reg/Save"", ""label"": ""Save"", ""variant"": ""success"", ""size"": ""xl"" } }
            ] } }
          ] } ]
        }";

        [Test]
        public void ProjectVariantAndSize_FlowThroughFactory_AndRoundTrip()
        {
            NeoUISettings settings = NeoUISettings.instance;
            Assert.IsNotNull(settings, "settings asset must exist (Create or Repair Settings)");

            List<ButtonVariantAsset> savedVariants = settings.buttonVariants;
            List<ButtonSizeAsset> savedSizes = settings.buttonSizes;
            try
            {
                settings.buttonVariants = new List<ButtonVariantAsset>
                {
                    new ButtonVariantAsset
                    {
                        name = "success",
                        contentToken = UIWidgetFactory.TokenTextOnPrimary,
                        colors = new SelectableColorSet
                        {
                            normal = new ThemeColorRef(UIWidgetFactory.TokenSuccess),
                            highlighted = new ThemeColorRef(UIWidgetFactory.TokenSuccessHover),
                            pressed = new ThemeColorRef(UIWidgetFactory.TokenSuccessPressed),
                            selected = new ThemeColorRef(UIWidgetFactory.TokenSuccessHover),
                            disabled = new ThemeColorRef(new Color(0.5f, 0.5f, 0.5f, 0.5f))
                        }
                    }
                };
                settings.buttonSizes = new List<ButtonSizeAsset>
                {
                    new ButtonSizeAsset { name = "xl", height = 88f, labelStyle = UIWidgetFactory.TextStyleButtonLabelLarge }
                };

                GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(VariantSpecJson));
                Assert.IsEmpty(report.issues, report.ToString());

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{UISpecGenerator.GeneratedRoot}/Views/Reg_Screen.prefab");
                Assert.IsNotNull(prefab, "generated view prefab missing");

                GameObject save = prefab.GetComponentsInChildren<UIButton>(true)
                    .First(b => b.id.Matches("Reg", "Save")).gameObject;

                // variant colors came from the asset, not the built-in switch
                SelectableColorSet colors = save.GetComponent<UISelectableColorAnimator>().colors;
                Assert.AreEqual(UIWidgetFactory.TokenSuccess, colors.normal.token,
                    "success variant must use the asset's color set");
                Assert.AreEqual(UIWidgetFactory.TokenSuccessHover, colors.highlighted.token);

                // size dimensions came from the asset
                Assert.AreEqual(88f, save.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight,
                    "xl size height comes from the ButtonSizeAsset");

                // WidgetStyleTag stores the free strings, so they round-trip through export
                WidgetStyleTag tag = save.GetComponent<WidgetStyleTag>();
                Assert.AreEqual("success", tag.variant);
                Assert.AreEqual("xl", tag.size);

                UISpec exported = UISpecExporter.ExportProject();
                ElementSpec exportedSave = exported.views.First(v => v.id == "Reg/Screen")
                    .elements.First(e => e.kind == "vstack").children.First(e => e.id == "Reg/Save");
                Assert.AreEqual("success", exportedSave.variant, "project variant round-trips");
                Assert.AreEqual("xl", exportedSave.sizeVariant, "project size round-trips");
            }
            finally
            {
                settings.buttonVariants = savedVariants;
                settings.buttonSizes = savedSizes;
            }
        }

        [Test]
        public void BuiltInVariant_IsByteIdentical_WhenNoAssetOverridesIt()
        {
            // With no project variants registered, the built-in danger variant must be untouched.
            NeoUISettings settings = NeoUISettings.instance;
            List<ButtonVariantAsset> saved = settings.buttonVariants;
            try
            {
                settings.buttonVariants = new List<ButtonVariantAsset>(); // empty: pure built-in path

                const string json = @"{ ""views"": [ { ""id"": ""Reg/Danger"", ""elements"": [
                    { ""button"": { ""id"": ""Reg/Del"", ""label"": ""Delete"", ""variant"": ""danger"" } } ] } ] }";
                GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
                Assert.IsEmpty(report.issues, report.ToString());
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{UISpecGenerator.GeneratedRoot}/Views/Reg_Danger.prefab");
                SelectableColorSet colors = prefab.GetComponentsInChildren<UIButton>(true)
                    .First(b => b.id.Matches("Reg", "Del")).GetComponent<UISelectableColorAnimator>().colors;
                Assert.AreEqual(UIWidgetFactory.TokenDanger, colors.normal.token);
                Assert.AreEqual(UIWidgetFactory.TokenDangerHover, colors.highlighted.token);
                Assert.AreEqual(UIWidgetFactory.TokenDangerPressed, colors.pressed.token);
            }
            finally
            {
                settings.buttonVariants = saved;
            }
        }

        // ------------------------------------------------------------------ Pattern A: icon overlay

        [Test]
        public void IconOverlay_ResolvesBeforeBuiltInDict_AndReverses()
        {
            NeoUISettings settings = NeoUISettings.instance;
            IconMapOverlay saved = settings.iconOverlay;
            IconMapOverlay overlay = ScriptableObject.CreateInstance<IconMapOverlay>();
            try
            {
                overlay.glyphs = new List<IconMapOverlay.Entry>
                {
                    new IconMapOverlay.Entry { name = "brand-logo", codepoint = "F1A0" }
                };
                overlay.aliases = new List<IconMapOverlay.Alias>
                {
                    new IconMapOverlay.Alias { alias = "logo", target = "brand-logo" }
                };
                settings.iconOverlay = overlay;

                Assert.IsTrue(IconMap.TryGetGlyph("brand-logo", out char glyph), "overlay glyph resolves");
                Assert.AreEqual((char)0xF1A0, glyph);
                Assert.IsTrue(IconMap.TryGetGlyph("logo", out char aliased), "overlay alias resolves");
                Assert.AreEqual(glyph, aliased);

                Assert.IsTrue(IconMap.TryGetName(glyph, out string name), "overlay glyph reverses");
                Assert.AreEqual("brand-logo", name);

                CollectionAssert.Contains(IconMap.Names.ToList(), "brand-logo",
                    "overlay names appear in the picker source");

                // built-ins still resolve with an overlay present
                Assert.IsTrue(IconMap.TryGetGlyph("play", out _));
                Assert.IsTrue(IconMap.TryGetGlyph("home", out _), "built-in aliases still work");
            }
            finally
            {
                settings.iconOverlay = saved;
                Object.DestroyImmediate(overlay);
            }
        }

        [TearDown]
        public void ResetRegistries() => NeoWidgetOptions.ResetAttributeRegistriesForTests();

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }
    }
}
