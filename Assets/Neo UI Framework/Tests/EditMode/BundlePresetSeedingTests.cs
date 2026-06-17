using System.Linq;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Applying a theme bundle delivers a full component library in that bundle's personality: it seeds
    /// the built-in widget presets (so a fresh project gets them too) and overlays the bundle's
    /// corner-radius personality onto the relevant presets. The token recolor is free (presets reference
    /// tokens by name and <see cref="ThemeBundles.Apply"/> already rewrites those), so this asserts only
    /// the extra personality the names don't carry. Teardown deletes the presets folder and restores the
    /// theme exactly as <c>ThemeBundleTests</c> does, leaving no committed assets behind.
    /// </summary>
    public class BundlePresetSeedingTests
    {
        // The committed PresetsRoot and the static NeoWidgetPresets registry are shared with sibling
        // fixtures, so reset both on the way IN (not just out) — a prior test (or a stale batch run) must
        // not leave the registry pre-populated, else a freshly-seeded preset can be masked by a stale
        // entry of the same name and the bundle's radius overlay reads the wrong value.
        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(NeoWidgetPresets.PresetsRoot);
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(NeoWidgetPresets.PresetsRoot);
            AssetDatabase.SaveAssets();
            NeoWidgetPresets.ResetForTests();

            // bundles mutate the shared theme asset — restore the canonical starter system
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.theme != null)
                StarterKitBootstrap.ExpandTheme(settings.theme, new GenerateReport());
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void ApplyingBundle_SeedsPresets_WithBundleRadiusPersonality()
        {
            Assert.IsTrue(ThemeBundles.TryGet("SoftFantasy", out ThemeBundles.Bundle bundle));

            var report = new GenerateReport();
            ThemeBundles.Apply(bundle, NeoUISettings.instance, report);
            Assert.IsEmpty(report.issues, report.ToString());

            // the component library was installed as a side effect of applying the bundle
            Assert.IsTrue(NeoWidgetPresets.TryGet("Card", out NeoWidgetPreset card),
                "applying a bundle seeds the built-in preset library");
            Assert.IsTrue(Mathf.Approximately(card.radius, bundle.cardRadius),
                $"Card preset takes the bundle's cardRadius (expected {bundle.cardRadius}, got {card.radius})");

            Assert.IsTrue(NeoWidgetPresets.TryGet("Panel", out NeoWidgetPreset panel));
            Assert.IsTrue(Mathf.Approximately(panel.radius, bundle.panelRadius),
                $"Panel preset takes the bundle's panelRadius (expected {bundle.panelRadius}, got {panel.radius})");

            Assert.IsTrue(NeoWidgetPresets.TryGet("Primary Button", out NeoWidgetPreset primary));
            Assert.IsTrue(Mathf.Approximately(primary.radius, bundle.controlRadius),
                $"button presets take the bundle's controlRadius (expected {bundle.controlRadius}, got {primary.radius})");

            // surface presets pick up the bundle's default show motion
            Assert.AreEqual("ShowDefault", card.motion, "surface presets get the bundle's Show motion");
        }

        [Test]
        public void DifferentBundles_GiveDifferentRadii_AndReApplyIsIdempotent()
        {
            Assert.IsTrue(ThemeBundles.TryGet("NeonArcade", out ThemeBundles.Bundle neon));
            Assert.IsTrue(ThemeBundles.TryGet("CleanSlate", out ThemeBundles.Bundle clean));

            ThemeBundles.Apply(neon, NeoUISettings.instance, new GenerateReport());
            Assert.IsTrue(NeoWidgetPresets.TryGet("Card", out NeoWidgetPreset neonCard));
            Assert.IsTrue(Mathf.Approximately(neonCard.radius, neon.cardRadius));

            // a second bundle overwrites the personality (idempotent overlay, not an accumulation)
            ThemeBundles.Apply(clean, NeoUISettings.instance, new GenerateReport());
            Assert.IsTrue(NeoWidgetPresets.TryGet("Card", out NeoWidgetPreset cleanCard));
            Assert.IsTrue(Mathf.Approximately(cleanCard.radius, clean.cardRadius),
                "re-applying a different bundle rewrites the radius, never stacks");

            // re-applying the same bundle leaves the value stable
            ThemeBundles.Apply(clean, NeoUISettings.instance, new GenerateReport());
            Assert.IsTrue(NeoWidgetPresets.TryGet("Card", out NeoWidgetPreset again));
            Assert.IsTrue(Mathf.Approximately(again.radius, clean.cardRadius));
        }
    }
}
