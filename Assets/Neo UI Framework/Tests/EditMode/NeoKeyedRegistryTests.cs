using System;
using System.Collections.Generic;
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
    /// Contract tests for the Pattern R registry bases (audit §4/A5/A6 — see
    /// <c>neo-ui-remediation-plan.md</c> Task 1.1): <see cref="NeoKeyedRegistry{T}"/> ("Shape A", a plain
    /// keyed code registry) and <see cref="NeoAssetRegistry{TAsset,TEntry}"/> ("Shape A" + asset
    /// discovery). These are the ONLY tests for the two base classes themselves — migrating the 15
    /// existing registries onto them (and re-pointing their own tests) is Wave 4.
    /// </summary>
    public class NeoKeyedRegistryTests
    {
        private sealed class Item
        {
            public readonly string Key;
            public readonly string Value;
            public Item(string key, string value) { Key = key; Value = value; }
        }

        private static NeoKeyedRegistry<Item> Make(
            IEnumerable<Item> builtins = null,
            Func<Item, bool> validate = null,
            string registryName = "ProbeRegistry")
        {
            Func<IEnumerable<Item>> builtinsFactory = builtins == null ? (Func<IEnumerable<Item>>)null : () => builtins;
            return new NeoKeyedRegistry<Item>(i => i.Key, builtins: builtinsFactory, validate: validate, registryName: registryName);
        }

        [Test]
        public void Register_SameKey_ReplacesInPlace_NeverDuplicates()
        {
            NeoKeyedRegistry<Item> reg = Make();

            reg.Register(new Item("x", "first"));
            reg.Register(new Item("x", "second"));

            Assert.AreEqual(1, reg.All.Count, "a same-key registration replaces, never duplicates");
            Assert.IsTrue(reg.TryGet("x", out Item got));
            Assert.AreEqual("second", got.Value, "the later registration wins");
        }

        [Test]
        public void Register_NullOrEmptyKey_WarnsAndIgnores_NeverThrows()
        {
            NeoKeyedRegistry<Item> reg = Make(registryName: "ProbeRegistry");

            LogAssert.Expect(LogType.Warning, new Regex("ProbeRegistry: ignored a null/invalid entry"));
            LogAssert.Expect(LogType.Warning, new Regex("ProbeRegistry: ignored a null/invalid entry"));

            Assert.DoesNotThrow(() => reg.Register(null));
            Assert.DoesNotThrow(() => reg.Register(new Item("", "blank-key")));

            Assert.AreEqual(0, reg.All.Count, "nothing was actually registered");
        }

        [Test]
        public void Register_FailsValidateGuard_WarnsAndIgnores()
        {
            NeoKeyedRegistry<Item> reg = Make(validate: i => i.Value != null, registryName: "GuardedRegistry");

            LogAssert.Expect(LogType.Warning, new Regex("GuardedRegistry: ignored a null/invalid entry"));
            reg.Register(new Item("bad", null));

            Assert.AreEqual(0, reg.All.Count);
            Assert.IsFalse(reg.TryGet("bad", out _));
        }

        [Test]
        public void Builtins_SeededLazily_OnlyOnce()
        {
            int seedCalls = 0;
            var reg = new NeoKeyedRegistry<Item>(i => i.Key, builtins: () =>
            {
                seedCalls++;
                return new[] { new Item("a", "A") };
            });

            Assert.AreEqual(0, seedCalls, "the builtins factory must not run at construction time");
            _ = reg.All;
            Assert.AreEqual(1, seedCalls);
            _ = reg.All;
            reg.TryGet("a", out _);
            Assert.AreEqual(1, seedCalls, "builtins are seeded exactly once, however many times the registry is queried");
        }

        [Test]
        public void Builtins_ComeFirst_ThenRegistrationOrder()
        {
            NeoKeyedRegistry<Item> reg = Make(builtins: new[] { new Item("a", "A"), new Item("b", "B") });

            reg.Register(new Item("c", "C"));

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, reg.All.Select(i => i.Key).ToArray());
        }

        [Test]
        public void Register_SameKeyAsABuiltin_ReplacesTheBuiltin()
        {
            NeoKeyedRegistry<Item> reg = Make(builtins: new[] { new Item("a", "builtin") });

            reg.Register(new Item("a", "override"));

            Assert.AreEqual(1, reg.All.Count, "overriding a built-in replaces it, never duplicates");
            Assert.IsTrue(reg.TryGet("a", out Item got));
            Assert.AreEqual("override", got.Value);
        }

        [Test]
        public void TryGet_Miss_ReturnsFalseAndDefault()
        {
            NeoKeyedRegistry<Item> reg = Make();
            Assert.IsFalse(reg.TryGet("nope", out Item value));
            Assert.IsNull(value);
            Assert.IsFalse(reg.TryGet(null, out _));
            Assert.IsFalse(reg.TryGet("", out _));
        }

        [Test]
        public void GetOrWarn_Hit_ReturnsValue()
        {
            NeoKeyedRegistry<Item> reg = Make();
            reg.Register(new Item("x", "found"));

            Item got = reg.GetOrWarn("x");
            Assert.AreEqual("found", got.Value);
        }

        [Test]
        public void GetOrWarn_Miss_LogsOneWarning_AndReturnsDefault()
        {
            NeoKeyedRegistry<Item> reg = Make(registryName: "WarnRegistry");

            LogAssert.Expect(LogType.Warning, new Regex("WarnRegistry: no entry 'missing'"));
            Item got = reg.GetOrWarn("missing");

            Assert.IsNull(got);
        }

        [Test]
        public void All_IsACachedSnapshot_ThatRebuildsOnlyOnMutation()
        {
            NeoKeyedRegistry<Item> reg = Make();
            reg.Register(new Item("a", "A"));

            var snapshotBefore = reg.All;
            Assert.AreSame(snapshotBefore, reg.All, "repeated reads with no mutation return the same cached instance");

            reg.Register(new Item("b", "B"));
            var snapshotAfter = reg.All;

            Assert.AreNotSame(snapshotBefore, snapshotAfter, "a mutation invalidates the cached snapshot");
            Assert.AreEqual(1, snapshotBefore.Count, "the earlier snapshot is untouched by the later mutation");
            Assert.AreEqual(2, snapshotAfter.Count);
        }

        [Test]
        public void Remove_DeletesByKey_AndInvalidatesSnapshot()
        {
            NeoKeyedRegistry<Item> reg = Make();
            reg.Register(new Item("a", "A"));
            _ = reg.All; // force a snapshot to exist

            Assert.IsTrue(reg.Remove("a"));
            Assert.AreEqual(0, reg.All.Count);
            Assert.IsFalse(reg.Remove("a"), "removing an already-absent key returns false");
        }

        [Test]
        public void ResetForTests_ClearsRegistrationsAndReseedsBuiltinsOnNextAccess()
        {
            NeoKeyedRegistry<Item> reg = Make(builtins: new[] { new Item("a", "A") });
            reg.Register(new Item("b", "B"));
            Assert.AreEqual(2, reg.All.Count);

            reg.ResetForTests();

            CollectionAssert.AreEqual(new[] { "a" }, reg.All.Select(i => i.Key).ToArray(),
                "reset drops manual registrations but re-seeds the built-ins fresh");
        }
    }

    /// <summary>
    /// Asset-discovery half (Shape B): <see cref="NeoAssetRegistry{TAsset,TEntry}"/> discovers assets
    /// fresh every generation (fixing audit A5 — a deleted/renamed asset is evicted, not stuck forever),
    /// while a manual (non-asset-backed) <see cref="NeoAssetRegistry{TAsset,TEntry}.Register"/> survives
    /// the rebuild. Uses a throwaway probe asset type + the scratch root so it never touches real project
    /// assets.
    /// </summary>
    public class NeoAssetRegistryTests
    {
        private const string ProbeFolderName = "NeoAssetRegistryProbe";
        private static string ProbeFolder => $"{NeoTestScratchRoot.ScratchRoot}/{ProbeFolderName}";

        [SetUp]
        public void CreateProbeFolder()
        {
            if (!AssetDatabase.IsValidFolder(NeoTestScratchRoot.ScratchRoot))
                AssetDatabase.CreateFolder("Assets", "NeoUITestScratch");
            if (!AssetDatabase.IsValidFolder(ProbeFolder))
                AssetDatabase.CreateFolder(NeoTestScratchRoot.ScratchRoot, ProbeFolderName);
        }

        [OneTimeTearDown]
        public void DeleteProbeFolder() => AssetDatabase.DeleteAsset(ProbeFolder);

        private static string AssetPath(string fileName) => $"{ProbeFolder}/{fileName}.asset";

        private static string CreateProbe(string fileName, string key)
        {
            var asset = ScriptableObject.CreateInstance<ProbeAsset>();
            asset.probeKey = key;
            string path = AssetPath(fileName);
            AssetDatabase.CreateAsset(asset, path);
            return path;
        }

        private static NeoAssetRegistry<ProbeAsset, ProbeAsset> MakeRegistry(string registryName = "ProbeAssetRegistry") =>
            new NeoAssetRegistry<ProbeAsset, ProbeAsset>(e => e.probeKey, asset => asset, registryName: registryName);

        [Test]
        public void Discovers_ExistingAssets_ByKey()
        {
            string pathA = CreateProbe("Alpha", "alpha");
            string pathB = CreateProbe("Beta", "beta");
            AssetDatabase.SaveAssets();
            try
            {
                NeoAssetRegistry<ProbeAsset, ProbeAsset> reg = MakeRegistry();

                Assert.IsTrue(reg.TryGet("alpha", out _));
                Assert.IsTrue(reg.TryGet("beta", out _));
                Assert.AreEqual(2, reg.All.Count);
            }
            finally
            {
                AssetDatabase.DeleteAsset(pathA);
                AssetDatabase.DeleteAsset(pathB);
            }
        }

        [Test]
        public void EnsureDiscovered_RebuildsFromDisk_EvictingADeletedAsset_ButPreservingAManualRegistration()
        {
            string pathA = CreateProbe("Surviving", "alpha");
            string pathB = CreateProbe("Deleted", "beta");
            AssetDatabase.SaveAssets();
            ProbeAsset manual = ScriptableObject.CreateInstance<ProbeAsset>();
            manual.probeKey = "manual";
            try
            {
                NeoAssetRegistry<ProbeAsset, ProbeAsset> reg = MakeRegistry();
                Assert.AreEqual(2, reg.All.Count, "precondition: both assets discovered");

                reg.Register(manual); // not backed by any asset
                Assert.AreEqual(3, reg.All.Count);

                AssetDatabase.DeleteAsset(pathB);
                reg.InvalidateDiscovery();

                Assert.IsFalse(reg.TryGet("beta", out _), "a deleted asset is evicted on the next discovery pass");
                Assert.IsTrue(reg.TryGet("alpha", out _), "the surviving asset is still discovered");
                Assert.IsTrue(reg.TryGet("manual", out _), "a manual (non-asset-backed) registration survives the rebuild");
                Assert.AreEqual(2, reg.All.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(manual);
                AssetDatabase.DeleteAsset(pathA);
                AssetDatabase.DeleteAsset(pathB);
            }
        }

        [Test]
        public void ManualRegistration_OverridesADiscoveredAssetOfTheSameKey()
        {
            string path = CreateProbe("Overridden", "shared");
            AssetDatabase.SaveAssets();
            ProbeAsset manual = ScriptableObject.CreateInstance<ProbeAsset>();
            manual.probeKey = "shared";
            try
            {
                NeoAssetRegistry<ProbeAsset, ProbeAsset> reg = MakeRegistry();
                Assert.IsTrue(reg.TryGet("shared", out ProbeAsset discovered));
                Assert.AreNotSame(manual, discovered);

                reg.Register(manual);
                reg.InvalidateDiscovery();

                Assert.IsTrue(reg.TryGet("shared", out ProbeAsset resolved));
                Assert.AreSame(manual, resolved, "manual wins over a discovered entry with the same key, even after a rebuild");
                Assert.AreEqual(1, reg.All.Count, "still one entry — the clash was replaced, not duplicated");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(manual);
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void DuplicateDiscoveredKey_WarnsAndKeepsOneEntry()
        {
            string pathA = CreateProbe("DupA", "dup");
            string pathB = CreateProbe("DupB", "dup");
            AssetDatabase.SaveAssets();
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("duplicate discovered key 'dup'"));
                NeoAssetRegistry<ProbeAsset, ProbeAsset> reg = MakeRegistry();

                Assert.IsTrue(reg.TryGet("dup", out _));
                Assert.AreEqual(1, reg.All.Count, "a duplicate discovered key keeps exactly one entry");
            }
            finally
            {
                AssetDatabase.DeleteAsset(pathA);
                AssetDatabase.DeleteAsset(pathB);
            }
        }

        [Test]
        public void ResetForTests_ClearsManualRegistrations_AndForcesFreshDiscovery()
        {
            string path = CreateProbe("Solo", "alpha");
            AssetDatabase.SaveAssets();
            ProbeAsset manual = ScriptableObject.CreateInstance<ProbeAsset>();
            manual.probeKey = "manual";
            try
            {
                NeoAssetRegistry<ProbeAsset, ProbeAsset> reg = MakeRegistry();
                reg.Register(manual);
                Assert.AreEqual(2, reg.All.Count);

                reg.ResetForTests();

                Assert.AreEqual(1, reg.All.Count, "reset drops the manual registration");
                Assert.IsTrue(reg.TryGet("alpha", out _), "but discovery still finds the asset on disk");
                Assert.IsFalse(reg.TryGet("manual", out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(manual);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
