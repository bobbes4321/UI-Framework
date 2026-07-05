using System.Collections.Generic;
using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 2.8: the bundle apply-diff preview (the B6 fix — <see cref="ThemeBundles.PreviewDiff"/>) and
    /// the "Save current look as bundle" round-trip (<see cref="ThemeBundles.SaveDefinitionFromTheme"/> /
    /// <see cref="ThemeBundles.SaveDefinition"/>). Mirrors <see cref="ThemeBundleTests"/>' teardown
    /// hygiene: bundles mutate the SHARED theme asset, so the canonical starter system is restored and any
    /// definition assets this suite writes are deleted.
    /// </summary>
    public class BundleDiffAndSaveTests
    {
        private const string TestFolder = "Assets/NeoBundleTestScratch";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            ThemeBundleRegistry.InvalidateDiscovery();

            NeoUISettings settings = NeoUISettings.instance;
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

        private static NeoUISettings ApplyCleanSlate(out ThemeBundles.Bundle bundle)
        {
            NeoUISettings settings = NeoUISettings.instance;
            Assert.IsTrue(ThemeBundles.TryGet("CleanSlate", out bundle));
            ThemeBundles.Apply(bundle, settings, new GenerateReport());
            return settings;
        }

        [Test]
        public void PreviewDiff_AfterApply_IsZero()
        {
            NeoUISettings settings = ApplyCleanSlate(out ThemeBundles.Bundle bundle);

            ThemeBundles.BundleDiff diff = ThemeBundles.PreviewDiff(bundle, settings.theme, settings);

            Assert.IsTrue(diff.IsEmpty, "re-applying a just-applied bundle must change nothing:\n" + diff.Summarize());
            Assert.AreEqual(0, diff.TokenChangeCount);
            Assert.IsEmpty(diff.styleChanges, string.Join("; ", diff.styleChanges));
        }

        [Test]
        public void PreviewDiff_OneEditedToken_ReportsExactlyIt()
        {
            NeoUISettings settings = ApplyCleanSlate(out ThemeBundles.Bundle bundle);
            Theme theme = settings.theme;

            // Perturb a single token in a single variant away from the bundle's value.
            theme.SetToken(UIWidgetFactory.TokenPrimary, Color.red, "Dark");

            ThemeBundles.BundleDiff diff = ThemeBundles.PreviewDiff(bundle, theme, settings);

            Assert.AreEqual(1, diff.TokenChangeCount, "exactly one token differs:\n" + diff.Summarize());
            Assert.AreEqual(1, diff.VariantsAffected);
            ThemeBundles.VariantTokenDiff vd = diff.variants.Single();
            Assert.AreEqual("Dark", vd.variant);
            Assert.IsEmpty(vd.added);
            CollectionAssert.AreEqual(new[] { UIWidgetFactory.TokenPrimary }, vd.changed);
            Assert.IsEmpty(diff.styleChanges, "no shape/text/motion knob changed");
            Assert.IsFalse(diff.IsEmpty);
        }

        [Test]
        public void SaveDefinitionFromTheme_RoundTrips_ToNearZeroDiff()
        {
            NeoUISettings settings = ApplyCleanSlate(out _);
            Theme theme = settings.theme;

            ThemeBundleDefinition def =
                ThemeBundles.SaveDefinitionFromTheme("RoundTripProbe", theme, settings, TestFolder);
            Assert.IsNotNull(def);
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(def)), "asset was created");

            // Reversing the capture (ToBundle) and diffing against the SAME theme it came from must read
            // ~zero — proving what the bundle model captures survives the round-trip.
            ThemeBundles.Bundle captured = def.ToBundle();
            ThemeBundles.BundleDiff diff = ThemeBundles.PreviewDiff(captured, theme, settings);

            Assert.IsTrue(diff.IsEmpty,
                "captured look must round-trip to a zero diff against its source theme:\n" + diff.Summarize());
        }

        [Test]
        public void SaveDefinition_SingleVariantCustom_PreservesTokensAndFields()
        {
            // The exact shape of the wizard's input: a single-variant "Custom" bundle. The shared writer
            // must preserve it byte-for-byte (delegation proof — the wizard's output is unchanged).
            var tokens = new Dictionary<string, Color>
            {
                [UIWidgetFactory.TokenPrimary] = new Color(0.1f, 0.2f, 0.3f),
                [UIWidgetFactory.TokenBackground] = new Color(0.9f, 0.8f, 0.7f),
            };
            var custom = new ThemeBundles.Bundle
            {
                name = "WizardProbe",
                description = "probe",
                palettes = new List<(string, Dictionary<string, Color>)> { ("Custom", tokens) },
                cardRadius = 14f, panelRadius = 14f, controlRadius = 9f,
                shadowSoftness = 16f, motionDuration = 0.19f, motionEase = "OutCubic",
                headlineSpacing = -0.5f,
            };

            ThemeBundleDefinition def = ThemeBundles.SaveDefinition(custom, TestFolder);
            ThemeBundles.Bundle back = def.ToBundle();

            Assert.AreEqual("WizardProbe", back.name);
            Assert.AreEqual(14f, back.cardRadius);
            Assert.AreEqual(9f, back.controlRadius);
            Assert.AreEqual(0.19f, back.motionDuration, 1e-4f);
            Assert.AreEqual(1, back.palettes.Count);
            Assert.AreEqual("Custom", back.palettes[0].variant);
            Assert.AreEqual(2, back.palettes[0].tokens.Count);
            Assert.IsTrue(back.palettes[0].tokens.ContainsKey(UIWidgetFactory.TokenPrimary));
        }
    }
}
