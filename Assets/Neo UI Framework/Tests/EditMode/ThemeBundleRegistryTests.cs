using System.Linq;
using System.Text.RegularExpressions;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Pattern R contract for <see cref="ThemeBundleRegistry"/> — mirrors the master plan's registry
    /// test shape: the seed contains exactly the built-ins (first, in order), <c>Register</c> appends a
    /// new bundle and replaces by name (case-insensitive, without growing), and <c>TryGet</c> is
    /// case-insensitive. Each test that registers the probe removes it in teardown so the static
    /// registry is left at exactly the three built-ins (other suites, e.g. ThemeBundleTests, assert
    /// that count).
    /// </summary>
    public class ThemeBundleRegistryTests
    {
        private const string ProbeName = "RegistryTestProbe";

        [TearDown]
        public void RemoveProbe() => ThemeBundleRegistry.Remove(ProbeName);

        [Test]
        public void Names_EqualsTheThreeBuiltInsByDefault()
        {
            // each test cleans up its probe in teardown, so the default registry is exactly the
            // three built-ins, in order
            Assert.AreEqual(new[] { "CleanSlate", "NeonArcade", "SoftFantasy" },
                ThemeBundleRegistry.Names.ToArray());
            Assert.AreEqual(new[] { "CleanSlate", "NeonArcade", "SoftFantasy" },
                ThemeBundleRegistry.All.Select(b => b.name).ToArray());
        }

        [Test]
        public void TryGet_IsCaseInsensitive()
        {
            Assert.IsTrue(ThemeBundleRegistry.TryGet("neonarcade", out ThemeBundles.Bundle lower));
            Assert.AreEqual("NeonArcade", lower.name);

            Assert.IsTrue(ThemeBundleRegistry.TryGet("CLEANSLATE", out ThemeBundles.Bundle upper));
            Assert.AreEqual("CleanSlate", upper.name);

            Assert.IsFalse(ThemeBundleRegistry.TryGet("NoSuchBundle", out ThemeBundles.Bundle missing));
            Assert.IsNull(missing);
        }

        [Test]
        public void Register_AppendsANewBundle_AndItIsResolvable()
        {
            int before = ThemeBundleRegistry.All.Count;
            var probe = new ThemeBundles.Bundle { name = ProbeName, description = "probe" };

            ThemeBundleRegistry.Register(probe);

            Assert.Contains(ProbeName, ThemeBundleRegistry.Names.ToList());
            Assert.IsTrue(ThemeBundleRegistry.TryGet(ProbeName, out ThemeBundles.Bundle got));
            Assert.AreSame(probe, got);
            Assert.GreaterOrEqual(ThemeBundleRegistry.All.Count, before + 1);
        }

        [Test]
        public void Register_ReplacesByName_CaseInsensitive_WithoutGrowing()
        {
            ThemeBundleRegistry.Register(new ThemeBundles.Bundle { name = ProbeName, description = "first" });
            int afterFirst = ThemeBundleRegistry.All.Count;

            var replacement = new ThemeBundles.Bundle { name = ProbeName.ToLowerInvariant(), description = "second" };
            ThemeBundleRegistry.Register(replacement);

            Assert.AreEqual(afterFirst, ThemeBundleRegistry.All.Count, "replace-by-name must not append");
            Assert.IsTrue(ThemeBundleRegistry.TryGet(ProbeName, out ThemeBundles.Bundle got));
            Assert.AreSame(replacement, got);
            Assert.AreEqual("second", got.description);
        }

        [Test]
        public void Register_IgnoresNullOrUnnamedBundles_WarnsButNeverThrows()
        {
            int before = ThemeBundleRegistry.All.Count;
            LogAssert.Expect(LogType.Warning, new Regex("ThemeBundleRegistry: ignored a null/invalid entry"));
            LogAssert.Expect(LogType.Warning, new Regex("ThemeBundleRegistry: ignored a null/invalid entry"));
            LogAssert.Expect(LogType.Warning, new Regex("ThemeBundleRegistry: ignored a null/invalid entry"));
            Assert.DoesNotThrow(() => ThemeBundleRegistry.Register(null));
            Assert.DoesNotThrow(() => ThemeBundleRegistry.Register(new ThemeBundles.Bundle { name = null }));
            Assert.DoesNotThrow(() => ThemeBundleRegistry.Register(new ThemeBundles.Bundle { name = "" }));
            Assert.AreEqual(before, ThemeBundleRegistry.All.Count);
        }

        [Test]
        public void DeletedThemeBundleDefinitionAsset_IsEvictedOnNextDiscovery()
        {
            const string bundleName = "ZDeleteEvictionProbeBundle";
            const string path = "Assets/ZThemeBundleDeleteProbe.asset";
            var def = ScriptableObject.CreateInstance<ThemeBundleDefinition>();
            def.bundleName = bundleName;
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            ThemeBundleRegistry.InvalidateDiscovery();
            try
            {
                Assert.IsTrue(ThemeBundleRegistry.TryGet(bundleName, out _),
                    "precondition: the dropped asset is discovered");

                AssetDatabase.DeleteAsset(path);
                ThemeBundleRegistry.InvalidateDiscovery();

                Assert.IsFalse(ThemeBundleRegistry.TryGet(bundleName, out _),
                    "a deleted ThemeBundleDefinition asset must be evicted on the next discovery pass");
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                ThemeBundleRegistry.Remove(bundleName);
                ThemeBundleRegistry.InvalidateDiscovery();
            }
        }
    }
}
