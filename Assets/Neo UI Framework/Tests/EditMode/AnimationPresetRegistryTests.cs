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
    /// Pattern R contract for <see cref="AnimationPresetRegistry"/> (Task 4.1 — migrated onto
    /// <see cref="NeoAssetRegistry{TAsset,TEntry}"/>): register / replace-by-name / lookup, a duplicate
    /// discovered <see cref="UIAnimationPreset.presetName"/> is last-discovered-wins with a warning
    /// naming both asset paths (the base class's shared policy), invalid registrations warn and never
    /// throw, and a deleted asset is evicted on the next discovery pass. Mirrors
    /// <see cref="NeoWidgetPresetsTests"/>/<see cref="ShowcaseRegistryTests"/>.
    /// </summary>
    public class AnimationPresetRegistryTests
    {
        [TearDown]
        public void ResetRegistry() => AnimationPresetRegistry.ResetForTests();

        private static UIAnimationPreset MakeInMemory(string category, string name)
        {
            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = category;
            preset.presetName = name;
            return preset;
        }

        [Test]
        public void Register_And_TryGet_RoundTrip()
        {
            UIAnimationPreset preset = MakeInMemory("Show", "APRAlpha");
            try
            {
                AnimationPresetRegistry.Register(preset);

                Assert.IsTrue(AnimationPresetRegistry.TryGet("APRAlpha", out UIAnimationPreset got));
                Assert.AreSame(preset, got);
                Assert.IsFalse(AnimationPresetRegistry.TryGet("APRNope", out _));
            }
            finally { Object.DestroyImmediate(preset); }
        }

        [Test]
        public void Register_ReplacesByName_NotAppend()
        {
            UIAnimationPreset a = MakeInMemory("Show", "APRDup");
            UIAnimationPreset b = MakeInMemory("Hide", "APRDup");
            try
            {
                AnimationPresetRegistry.Register(a);
                int afterFirst = AnimationPresetRegistry.All.Count;
                AnimationPresetRegistry.Register(b);

                Assert.AreEqual(afterFirst, AnimationPresetRegistry.All.Count, "same-name register replaces, never duplicates");
                Assert.IsTrue(AnimationPresetRegistry.TryGet("APRDup", out UIAnimationPreset got));
                Assert.AreSame(b, got, "the later registration wins");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void Register_NullOrNameless_WarnsButNeverThrows()
        {
            LogAssert.Expect(LogType.Warning, new Regex("AnimationPresetRegistry: ignored a null/invalid entry"));
            LogAssert.Expect(LogType.Warning, new Regex("AnimationPresetRegistry: ignored a null/invalid entry"));
            UIAnimationPreset nameless = MakeInMemory("Show", "");
            try
            {
                Assert.DoesNotThrow(() => AnimationPresetRegistry.Register(null));
                Assert.DoesNotThrow(() => AnimationPresetRegistry.Register(nameless));
            }
            finally { Object.DestroyImmediate(nameless); }
        }

        [Test]
        public void DiscoveredAsset_DeletedFromDisk_IsEvictedOnNextDiscovery()
        {
            const string presetName = "APRDeleteProbe";
            const string path = "Assets/ZAnimPresetDeleteProbe.asset";
            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = "Show";
            preset.presetName = presetName;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
            try
            {
                Assert.IsTrue(AnimationPresetRegistry.TryGet(presetName, out _),
                    "precondition: the dropped asset is discovered");

                AssetDatabase.DeleteAsset(path);
                AnimationPresetRegistry.InvalidateDiscovery();

                Assert.IsFalse(AnimationPresetRegistry.TryGet(presetName, out _),
                    "a deleted UIAnimationPreset asset must be evicted on the next discovery pass");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                AnimationPresetRegistry.InvalidateDiscovery();
            }
        }

        [Test]
        public void DuplicateDiscoveredPresetName_WarnsAndKeepsOneEntry_LastDiscoveredWins()
        {
            const string presetName = "APRDupOnDisk";
            const string pathA = "Assets/ZAnimPresetDupA.asset";
            const string pathB = "Assets/ZAnimPresetDupB.asset";
            var a = ScriptableObject.CreateInstance<UIAnimationPreset>();
            a.category = "Show";
            a.presetName = presetName;
            var b = ScriptableObject.CreateInstance<UIAnimationPreset>();
            b.category = "Hide";
            b.presetName = presetName;
            AssetDatabase.CreateAsset(a, pathA);
            AssetDatabase.CreateAsset(b, pathB);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
            try
            {
                // Discovery order (AssetDatabase.FindAssets) isn't guaranteed, so match both paths regardless
                // of which one is reported as the earlier vs. later discovery.
                LogAssert.Expect(LogType.Warning, new Regex(
                    $"duplicate discovered key '{presetName}'(?=.*ZAnimPresetDupA)(?=.*ZAnimPresetDupB)"));

                Assert.IsTrue(AnimationPresetRegistry.TryGet(presetName, out _));
                Assert.AreEqual(1, AnimationPresetRegistry.All.Count(p => p.presetName == presetName),
                    "a duplicate discovered preset name keeps exactly one entry");
            }
            finally
            {
                AssetDatabase.DeleteAsset(pathA);
                AssetDatabase.DeleteAsset(pathB);
                AnimationPresetRegistry.InvalidateDiscovery();
            }
        }

        [Test]
        public void Resolve_UnwiredDiscoveredPreset_ResolvesWithoutLoggingAWarning()
        {
            const string presetName = "APRResolveProbe";
            const string path = "Assets/ZAnimPresetResolveProbe.asset";
            var preset = ScriptableObject.CreateInstance<UIAnimationPreset>();
            preset.category = "Show";
            preset.presetName = presetName;
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            AnimationPresetRegistry.InvalidateDiscovery();
            try
            {
                NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
                Assume.That(settings.animationPresets == null || !settings.animationPresets.Contains(presetName),
                    "precondition: not wired into the settings database");

                // No LogAssert.Expect here: the wired-database probe must stay silent on this expected
                // miss (Wave-1 note) — an unhandled warning would fail this test.
                UIAnimationPreset resolved = AnimationPresetRegistry.Resolve(settings, presetName);

                Assert.IsNotNull(resolved, "resolves purely through discovery");
                Assert.AreEqual(presetName, resolved.presetName);
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                AnimationPresetRegistry.InvalidateDiscovery();
            }
        }
    }
}
