using System.IO;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using TMPro;
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
        public void Regenerate_WithFactoryDriftButNoSpecDrift_RebuildsFromSpec_NotRefused()
        {
            // A showcase prefab is the materialization of its committed spec — the user never hand-edits
            // it. When a UIWidgetFactory code change makes the current factory's widget internals diverge
            // from the older internals baked into the committed prefab, OffSpecLint flags those internals
            // even though NO spec-level human edit exists (humanChanges stays empty). Regenerate must
            // rebuild from spec instead of deadlocking. We simulate that factory-version drift with an
            // internal child the exporter can't see (a sub-widget-root edit → off-spec, no spec drift).
            Showcase showcase = MakeShowcase();
            showcase.specPath = _specPath;

            using (NeoWorkspace.Scoped(showcase))
                UISpecGenerator.Generate(UISpec.FromJson(File.ReadAllText(_specPath)));

            string viewPath = $"{Root}/Views/Run_Main.prefab";
            AddInternalChild(viewPath, "FactoryInternal");

            SyncResult result = ShowcaseRunner.Regenerate(showcase);

            Assert.IsNotNull(result);
            Assert.IsFalse(result.refused,
                "pure factory-version drift (no spec-level human edit) must NOT deadlock the showcase regenerate");
            Assert.IsTrue(result.regenerated, "the showcase must be rebuilt from spec");
            Assert.IsEmpty(result.humanChanges, "the simulated drift is below the widget root — no spec-level edit");
            // never silent: the rebuilt factory-owned internals are recorded, not swallowed
            Assert.IsNotEmpty(result.dropped,
                "the auto-forced rebuild must record the factory-owned internals it overwrote");
            Assert.IsFalse(HasInternalChild(viewPath, "FactoryInternal"),
                "regenerating from spec rebuilds the factory-owned internals — the planted child is gone");
        }

        [Test]
        public void Regenerate_WithSpecLevelHumanDrift_StillRefuses_NoAutoForce()
        {
            // The auto-force is strictly for factory-version drift. A genuine spec-level human edit
            // (here: a changed button label the exporter CAN see → humanChanges > 0) alongside an
            // off-spec finding must still refuse and surface for review — never silently forced past.
            Showcase showcase = MakeShowcase();
            showcase.specPath = _specPath;

            using (NeoWorkspace.Scoped(showcase))
                UISpecGenerator.Generate(UISpec.FromJson(File.ReadAllText(_specPath)));

            string viewPath = $"{Root}/Views/Run_Main.prefab";
            SetButtonLabel(viewPath, "Human Renamed");   // a spec-visible human edit → spec-level drift
            AddInternalChild(viewPath, "FactoryInternal"); // plus an off-spec finding to trip the gate

            SyncResult result = ShowcaseRunner.Regenerate(showcase);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.refused, "a real spec-level human edit must NOT be auto-forced past");
            Assert.IsFalse(result.regenerated, "a refused regenerate must not rebuild assets");
            Assert.IsNotEmpty(result.humanChanges, "the changed label is real spec-level drift to protect");
            Assert.IsTrue(HasInternalChild(viewPath, "FactoryInternal"),
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

        // Edit the button's label text — a change the exporter CAN see (FindChildText(go, LabelName)),
        // so it surfaces as spec-level human drift (humanChanges), unlike a sub-widget-root edit.
        private static void SetButtonLabel(string prefabPath, string label)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                UIButton button = contents.GetComponentInChildren<UIButton>(true);
                Assert.IsNotNull(button, "generated view should contain the button");
                Transform labelTf = button.transform.Find(UIWidgetFactory.LabelName);
                Assert.IsNotNull(labelTf, "button should have a Label child");
                TMP_Text text = labelTf.GetComponent<TMP_Text>();
                Assert.IsNotNull(text, "the Label child should carry a TMP_Text");
                text.text = label;
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
