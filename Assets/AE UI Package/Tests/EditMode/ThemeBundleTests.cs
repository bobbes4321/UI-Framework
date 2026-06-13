using System.Collections.Generic;
using System.Linq;
using AlterEyes.UI;
using AlterEyes.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// Beautification P5: curated theme bundles (complete token/type/shape/motion systems an
    /// agent picks by name), the spec's "theme.bundle" hook, and the soft design lint
    /// (WCAG contrast, raw font sizes, off-scale spacing).
    /// </summary>
    public class ThemeBundleTests
    {
        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            // bundles mutate the shared theme asset — restore the canonical starter system
            AEUISettings settings = AEUISettings.instance;
            if (settings != null && settings.theme != null)
                StarterKitBootstrap.ExpandTheme(settings.theme, new GenerateReport());
            if (settings != null && settings.animationPresets != null)
            {
                foreach (UIAnimationPreset preset in settings.animationPresets.Presets
                             .Where(p => p == null).ToList())
                    settings.animationPresets.Remove(preset);
                EditorUtility.SetDirty(settings.animationPresets);
            }
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Bundles_DefineCompleteAccessibleSystems()
        {
            AEUISettings settings = AEUISettings.instance;
            string[] requiredTokens =
            {
                UIWidgetFactory.TokenBackground, UIWidgetFactory.TokenSurface,
                UIWidgetFactory.TokenSurfaceElevated, UIWidgetFactory.TokenOutline,
                UIWidgetFactory.TokenPrimary, UIWidgetFactory.TokenPrimaryHover,
                UIWidgetFactory.TokenPrimaryPressed, UIWidgetFactory.TokenTextOnPrimary,
                UIWidgetFactory.TokenTextStrong, UIWidgetFactory.TokenTextDefault,
                UIWidgetFactory.TokenTextMuted, UIWidgetFactory.TokenDanger,
                UIWidgetFactory.TokenDangerHover, UIWidgetFactory.TokenSuccess,
                UIWidgetFactory.TokenShadow
            };

            Assert.AreEqual(3, ThemeBundles.Names.Count(), "three curated bundles");
            foreach (string name in ThemeBundles.Names.ToList())
            {
                Assert.IsTrue(ThemeBundles.TryGet(name, out ThemeBundles.Bundle bundle));
                var report = new GenerateReport();
                ThemeBundles.Apply(bundle, settings, report);
                Assert.IsEmpty(report.issues, $"{name}: {report}");

                Theme theme = settings.theme;
                foreach ((string variant, Dictionary<string, Color> _) palette in bundle.palettes)
                {
                    Theme.ThemeVariant variant = theme.GetVariant(palette.variant);
                    Assert.IsNotNull(variant, $"{name}: variant '{palette.variant}' missing");
                    foreach (string token in requiredTokens)
                        Assert.IsTrue(variant.TryGetColor(token, out _),
                            $"{name}/{palette.variant}: token '{token}' missing");
                }

                // every bundle palette must pass its own contrast lint
                List<string> contrastWarnings = AgentValidation.ValidateDesign()
                    .Where(w => w.Contains("contrast")).ToList();
                Assert.IsEmpty(contrastWarnings, $"{name} fails its own contrast lint:\n" +
                                                 string.Join("\n", contrastWarnings));

                // radius personality lands on the shape styles
                Assert.IsTrue(theme.TryGetShapeStyle(UIWidgetFactory.StyleCard, out ShapeStyle card));
                Assert.AreEqual(bundle.cardRadius, card.radius, $"{name}: card radius");
                Assert.AreEqual(2, card.elevation, $"{name}: cards are elevation 2");

                // type scale present with committed fonts
                Assert.IsTrue(theme.TryGetTextStyle(UIWidgetFactory.TextStyleDisplay, out TextStyle display));
                Assert.IsNotNull(display.font, $"{name}: Display style must reference a font asset");

                // motion personality lands as presets
                UIAnimationPreset show = settings.animationPresets.Get("ShowDefault");
                Assert.IsNotNull(show, $"{name}: ShowDefault preset missing");
                Assert.That(show.animation.fade.settings.duration,
                    Is.EqualTo(bundle.motionDuration).Within(1e-4f), $"{name}: motion duration");
            }

            Assert.IsTrue(ThemeBundles.TryGet("neonarcade", out _), "bundle lookup is case-insensitive");
            Assert.IsFalse(ThemeBundles.TryGet("NotABundle", out _));
        }

        [Test]
        public void SpecBundle_AppliesFirst_ExplicitTokensWin()
        {
            const string json = @"{
              ""theme"": { ""bundle"": ""NeonArcade"", ""tokens"": { ""Primary"": ""#FF0000"" } },
              ""views"": [ { ""id"": ""Bundle/Screen"", ""elements"": [
                { ""button"": { ""id"": ""Bundle/Go"", ""label"": ""Go"" } } ] } ]
            }";
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.issues, report.ToString());

            Theme theme = AEUISettings.instance.theme;
            Assert.IsTrue(theme.TryGetColor("Primary", out Color primary));
            Assert.AreEqual("#FF0000", ColorUtils.ToHex(primary), "explicit spec tokens override the bundle");
            Assert.IsTrue(theme.TryGetColor(UIWidgetFactory.TokenSurface, out Color surface));
            Assert.AreEqual("#161028", ColorUtils.ToHex(surface), "bundle palette landed underneath");
        }

        [Test]
        public void SpecBundle_UnknownName_IsALoudIssue()
        {
            GenerateReport report = UISpecGenerator.Generate(
                UISpec.FromJson(@"{ ""theme"": { ""bundle"": ""VaporTrash"" } }"));
            Assert.IsTrue(report.issues.Any(i => i.Contains("VaporTrash")),
                "unknown bundles must be reported, not silently ignored");
        }

        [Test]
        public void DesignLint_FlagsRawTypeAndOffScaleSpacing()
        {
            const string json = @"{
              ""views"": [ { ""id"": ""Lint/Screen"", ""elements"": [
                { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 7, ""children"": [
                  { ""text"": { ""label"": ""Untyped"", ""fontSize"": 23 } }
                ] } }
              ] } ]
            }";
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(json));
            Assert.IsEmpty(report.issues, report.ToString());

            List<string> warnings = AgentValidation.ValidateDesign();
            Assert.IsTrue(warnings.Any(w => w.Contains("raw fontSize")),
                $"raw text should be flagged:\n{string.Join("\n", warnings)}");
            Assert.IsTrue(warnings.Any(w => w.Contains("spacing 7")),
                $"off-scale spacing should be flagged:\n{string.Join("\n", warnings)}");

            Assert.IsEmpty(AgentValidation.ValidateAll()
                    .Where(i => i.Contains("raw fontSize") || i.Contains("off the")),
                "design lint stays out of the hard validation");
        }
    }
}
