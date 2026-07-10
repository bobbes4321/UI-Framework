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
    /// test shape: the seed contains the three built-ins (a project may also have discoverable
    /// <see cref="ThemeBundleDefinition"/> assets alongside — and ordered before — them, which is the
    /// intended extensibility seam, not a violation), <c>Register</c> appends a new bundle and replaces by name
    /// (case-insensitive, without growing), and <c>TryGet</c> is case-insensitive. Each test that
    /// registers the probe removes it in teardown so it never leaks into sibling suites.
    /// </summary>
    public class ThemeBundleRegistryTests
    {
        private const string ProbeName = "RegistryTestProbe";

        [TearDown]
        public void RemoveProbe() => ThemeBundleRegistry.Remove(ProbeName);

        [Test]
        public void Names_ContainTheThreeBuiltIns()
        {
            // Discovery also picks up any user-authored ThemeBundleDefinition asset in the project
            // (e.g. one saved from the Setup wizard's "save as bundle") — that's the intended
            // extensibility seam, not a bug. NeoAssetRegistry orders discovered assets BEFORE the
            // code-seeded built-ins (built-ins live in the rebuild-surviving manual list, upserted
            // after the asset scan), and a project asset can even override a built-in by name — so
            // no position/order assertion is safe here. Assert only that the three built-ins are
            // present in both views of the registry.
            var names = ThemeBundleRegistry.Names.ToArray();
            var allNames = ThemeBundleRegistry.All.Select(b => b.name).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "CleanSlate", "NeonArcade", "SoftFantasy" }, names);
            CollectionAssert.IsSubsetOf(new[] { "CleanSlate", "NeonArcade", "SoftFantasy" }, allNames);
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
