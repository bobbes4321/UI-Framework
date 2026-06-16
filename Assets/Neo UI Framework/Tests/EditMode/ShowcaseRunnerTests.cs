using System.IO;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// <see cref="ShowcaseRunner"/> routing, exercised headlessly (no scene build / graphics needed):
    /// <list type="bullet">
    /// <item><see cref="ShowcaseRunner.Regenerate"/> scopes to the showcase's isolated root and routes
    ///   through <see cref="SpecBaseline.Sync"/>, returning a <see cref="SyncResult"/>;</item>
    /// <item>a planted off-spec edit makes that sync <c>refused</c> (never a silent wipe);</item>
    /// <item>the scope never leaks: <see cref="UISpecGenerator.GeneratedRoot"/> is restored afterwards,
    ///   and the showcase's assets land under its derived root, not the default one.</item>
    /// </list>
    /// <see cref="ShowcaseRunner.Open"/>'s build/open path needs a scene + graphics, so it is deferred
    /// to a PlayMode/editor run; here we only assert the API shape and the fast-path guard logic.
    /// </summary>
    public class ShowcaseRunnerTests
    {
        // a throwaway showcase whose derived root sits under a scratch folder (NOT the committed demo root)
        private const string ScratchId = "test-runner-scratch";
        private static string Root => $"{ShowcaseRegistry.ShowcasesRoot}/{ScratchId}/Generated";
        private string _specPath;

        private static Showcase MakeShowcase() => new Showcase
        {
            id = ScratchId,
            title = "Runner Scratch",
            category = "Tests",
            flowName = null,
        };

        [SetUp]
        public void SetUp()
        {
            _specPath = Path.Combine(Path.GetTempPath(), "neo-showcase-runner-spec.json");
            File.WriteAllText(_specPath, OneButton("Run/Main", "Run/Play", "Play"));
            Wipe();
        }

        [TearDown]
        public void TearDown()
        {
            Wipe();
            if (_specPath != null && File.Exists(_specPath)) File.Delete(_specPath);
            AssetDatabase.SaveAssets();
        }

        private static void Wipe()
        {
            AssetDatabase.DeleteAsset($"{ShowcaseRegistry.ShowcasesRoot}/{ScratchId}");
        }

        [Test]
        public void Regenerate_RoutesThroughSync_ReturnsResult_AndRestoresRoot()
        {
            string rootBefore = UISpecGenerator.GeneratedRoot;
            Showcase showcase = MakeShowcase();
            showcase.specPath = _specPath;

            // seed the showcase's isolated root with a first generation (scoped, like the runner does)
            using (NeoWorkspace.Scoped(showcase))
                UISpecGenerator.Generate(UISpec.FromJson(File.ReadAllText(_specPath)));

            Assert.AreEqual(rootBefore, UISpecGenerator.GeneratedRoot, "the scope must restore the root");
            Assert.IsTrue(AssetDatabase.IsValidFolder($"{Root}/Views"),
                "the showcase generated into its own derived root, not the default one");

            SyncResult result = ShowcaseRunner.Regenerate(showcase);

            Assert.IsNotNull(result, "Regenerate must return the SyncResult from SpecBaseline.Sync");
            Assert.IsTrue(result.ok, result.note);
            Assert.AreEqual(rootBefore, UISpecGenerator.GeneratedRoot,
                "Regenerate's scope must restore the root even on the happy path");
        }

        [Test]
        public void Regenerate_WithOffSpecEdit_Refuses_WithoutWiping()
        {
            Showcase showcase = MakeShowcase();
            showcase.specPath = _specPath;

            using (NeoWorkspace.Scoped(showcase))
                UISpecGenerator.Generate(UISpec.FromJson(File.ReadAllText(_specPath)));

            // plant a factory-internal child the exporter can't see — an off-spec edit
            string viewPath = $"{Root}/Views/Run_Main.prefab";
            AddInternalChild(viewPath, "HumanExtra");

            SyncResult result = ShowcaseRunner.Regenerate(showcase);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.refused, "an off-spec edit must block the regenerate");
            Assert.IsFalse(result.regenerated, "a refused regenerate must not rebuild assets");
            Assert.IsNotEmpty(result.offSpecWarnings, "the refusal must name the off-spec edit");
            Assert.IsTrue(HasInternalChild(viewPath, "HumanExtra"),
                "the refused regenerate must leave the human's project untouched");
        }

        [Test]
        public void Regenerate_NullOrSpeclessShowcase_ReturnsNull()
        {
            Assert.IsNull(ShowcaseRunner.Regenerate(null));
            Assert.IsNull(ShowcaseRunner.Regenerate(new Showcase { id = ScratchId, specPath = null }));
        }

        // ----------------------------------------------------------------- helpers

        private static string OneButton(string view, string button, string label) =>
            "{ \"views\": [ { \"id\": \"" + view + "\", \"elements\": [ " +
            "{ \"button\": { \"id\": \"" + button + "\", \"label\": \"" + label + "\" } } ] } ] }";

        private static void AddInternalChild(string prefabPath, string childName)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                UIButton button = contents.GetComponentInChildren<UIButton>(true);
                Assert.IsNotNull(button, "generated view should contain the button");
                var extra = new GameObject(childName, typeof(RectTransform));
                extra.transform.SetParent(button.transform, false);
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
        }

        private static bool HasInternalChild(string prefabPath, string childName)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                UIButton button = contents.GetComponentInChildren<UIButton>(true);
                return button != null && button.transform.Find(childName) != null;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }
}
