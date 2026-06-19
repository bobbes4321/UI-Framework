using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Phase 4 designer-extensibility seams: dropping an asset is enough to extend the framework, no
    /// manual wiring and no C#. Covers animation-preset auto-discovery and the ThemeBundleDefinition SO.
    /// </summary>
    public class Phase4ExtensibilityTests
    {
        [Test]
        public void AnimationPreset_DiscoveredAsset_ResolvesWithoutDatabaseWiring()
        {
            const string presetName = "ZTestAutoDiscoverSlide";
            const string path = "Assets/ZTestAnimPreset.asset";
            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = "Show";
            preset.presetName = presetName;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
            try
            {
                Assert.IsTrue(AnimationPresetRegistry.TryGet(presetName, out _),
                    "registry should discover a dropped UIAnimationPreset asset");

                NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
                Assume.That(settings.animationPresets == null || !settings.animationPresets.Contains(presetName),
                    "precondition: the preset is not wired into the settings database");

                // Resolves at generate time purely through discovery.
                var view = new ViewSpec { category = "Anim", viewName = "Probe", showAnimation = presetName };
                var report = new GenerateReport();
                GameObject go = UISpecGenerator.BuildViewGameObject(view, settings, report);
                Object.DestroyImmediate(go);

                Assert.IsFalse(report.issues.Any(i => i.Contains(presetName) && i.Contains("not found")),
                    "a discovered preset should resolve at generate: " + string.Join("; ", report.issues));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                AnimationPresetRegistry.InvalidateDiscovery();
            }
        }

        [Test]
        public void ThemeBundleDefinition_DiscoveredAsset_RegistersAsBundle()
        {
            const string bundleName = "ZTestBundle";
            const string path = "Assets/ZTestThemeBundle.asset";
            var def = ScriptableObject.CreateInstance<ThemeBundleDefinition>();
            def.bundleName = bundleName;
            def.description = "test bundle";
            def.variants.Add(new ThemeBundleDefinition.Variant
            {
                name = "Dark",
                tokens = { new ThemeBundleDefinition.TokenColor { token = "Primary", color = Color.cyan } }
            });
            def.cardRadius = 10f;
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            ThemeBundleRegistry.InvalidateDiscovery();
            try
            {
                Assert.IsTrue(ThemeBundleRegistry.TryGet(bundleName, out ThemeBundles.Bundle bundle),
                    "registry should discover a dropped ThemeBundleDefinition");
                Assert.AreEqual(bundleName, bundle.name);
                Assert.AreEqual(1, bundle.palettes.Count, "one variant palette");
                Assert.AreEqual("Dark", bundle.palettes[0].variant);
                Assert.IsTrue(bundle.palettes[0].tokens.ContainsKey("Primary"));
                Assert.AreEqual(10f, bundle.cardRadius);
            }
            finally
            {
                // Remove the discovered bundle from the static registry too: discovery registers it into
                // the persistent list, which survives the asset deletion (same as ShowcaseRegistry) and
                // would otherwise pollute sibling suites that iterate All / count the built-ins.
                AssetDatabase.DeleteAsset(path);
                ThemeBundleRegistry.Remove(bundleName);
                ThemeBundleRegistry.InvalidateDiscovery();
            }
        }
    }
}
