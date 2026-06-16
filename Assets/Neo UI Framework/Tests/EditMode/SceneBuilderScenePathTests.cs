using System.Reflection;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Covers the scene-path overhaul on <see cref="GeneratedSceneBuilder"/> WITHOUT building a real
    /// scene (that needs a graphics device + a generated tree). Two pure things are verified:
    /// the new <c>Build(string flowName, string scenePath)</c> overload exists with the right shape (the
    /// API contract the Hub/showcase runner relies on), and the <c>EnsureFolderTree</c> helper creates a
    /// nested parent folder tree like <c>Assets/Showcases/{id}/</c> idempotently.
    /// </summary>
    public class SceneBuilderScenePathTests
    {
        [Test]
        public void Build_HasFlowNameAndScenePathOverload()
        {
            MethodInfo overload = typeof(GeneratedSceneBuilder).GetMethod(
                "Build",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(string) },
                modifiers: null);

            Assert.IsNotNull(overload, "Build(string flowName, string scenePath) must exist");
            Assert.AreEqual(typeof(string), overload.ReturnType, "Build returns the saved scene path");
        }

        [Test]
        public void Build_StillHasLegacyParamlessOverload()
        {
            MethodInfo legacy = typeof(GeneratedSceneBuilder).GetMethod(
                "Build",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);
            Assert.IsNotNull(legacy, "Build(string flowName = null) must still exist");
        }

        [Test]
        public void EnsureFolderTree_CreatesNestedParentFolders_Idempotently()
        {
            // a scratch path well clear of any committed assets
            const string id = "neo_ws_scenebuilder_scratch";
            string folder = $"Assets/{id}/nested";
            string scenePath = $"{folder}/{id}.unity";
            try
            {
                Assert.IsFalse(AssetDatabase.IsValidFolder(folder), "precondition: scratch folder absent");

                Invoke(scenePath);
                Assert.IsTrue(AssetDatabase.IsValidFolder($"Assets/{id}"));
                Assert.IsTrue(AssetDatabase.IsValidFolder(folder), "every parent level is created");

                // calling again with the tree present is a no-op (no throw, no duplicate)
                Invoke(scenePath);
                Assert.IsTrue(AssetDatabase.IsValidFolder(folder));
            }
            finally
            {
                AssetDatabase.DeleteAsset($"Assets/{id}");
            }
        }

        [Test]
        public void EnsureFolderTree_NullOrEmpty_IsANoOp()
        {
            Assert.DoesNotThrow(() => Invoke(null));
            Assert.DoesNotThrow(() => Invoke(""));
        }

        private static void Invoke(string assetPath)
        {
            MethodInfo m = typeof(GeneratedSceneBuilder).GetMethod(
                "EnsureFolderTree", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m, "EnsureFolderTree helper must exist");
            m.Invoke(null, new object[] { assetPath });
        }
    }
}
