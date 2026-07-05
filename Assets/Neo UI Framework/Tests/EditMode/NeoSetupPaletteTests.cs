using System.Collections.Generic;
using Neo.UI;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Design-system-cohesion-plan Phase 1.2: the New Project Setup wizard reloads its custom-color
    /// fields from the CURRENT theme on open instead of silently reseeding defaults.
    /// <see cref="NeoSetupPalette"/> is the pure, testable intent↔token map that makes that load an
    /// exact inverse of applying a custom palette (<see cref="ThemeBundles.BuildPalette"/>) — these
    /// tests lock the round trip and the "never guess a missing token" contract.
    /// </summary>
    public class NeoSetupPaletteTests
    {
        [OneTimeTearDown]
        public void Cleanup()
        {
            // this test writes arbitrary colors onto the shared starter theme asset — restore it
            NeoUISettings settings = NeoUISettings.instance;
            if (settings != null && settings.theme != null)
                StarterKitBootstrap.ExpandTheme(settings.theme, new GenerateReport());
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void ReadFrom_IsExactInverseOf_ApplyingEveryIntentToken()
        {
            NeoUISettings settings = NeoUISettings.instance;
            Theme theme = settings.theme;
            Assert.IsNotNull(theme, "starter theme must exist for this test to run");

            // one distinct, recognizable color per intent so a mixup between intents shows up immediately
            var applied = new Dictionary<string, Color>();
            int i = 0;
            foreach ((string intent, string token) in NeoSetupPalette.IntentTokens)
            {
                Color color = Color.HSVToRGB((i++ * 0.13f) % 1f, 0.8f, 0.8f);
                applied[intent] = color;
                theme.SetToken(token, color);
            }

            Dictionary<string, Color> loaded = NeoSetupPalette.ReadFrom(theme);

            Assert.AreEqual(applied.Count, loaded.Count, "every intent token round-trips");
            foreach (KeyValuePair<string, Color> entry in applied)
            {
                Assert.IsTrue(loaded.TryGetValue(entry.Key, out Color got), $"missing intent '{entry.Key}'");
                Assert.AreEqual(entry.Value, got, $"intent '{entry.Key}' didn't round-trip");
            }
        }

        [Test]
        public void ReadFrom_MissingTokens_AreAbsentNotGuessed()
        {
            var theme = ScriptableObject.CreateInstance<Theme>();
            try
            {
                Dictionary<string, Color> loaded = NeoSetupPalette.ReadFrom(theme);
                Assert.IsEmpty(loaded, "a theme with no tokens set must yield no intents, never a default guess");
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void ReadFrom_NullTheme_ReturnsEmpty()
        {
            Assert.IsEmpty(NeoSetupPalette.ReadFrom(null));
        }
    }
}
