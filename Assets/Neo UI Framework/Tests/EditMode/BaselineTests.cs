using System.IO;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The committed baseline (<c>.neo-baseline.json</c>): written after a successful generate, its
    /// path follows <see cref="UISpecGenerator.GeneratedRoot"/> so test/scratch runs never touch the
    /// committed baseline, and a generate against a drifted tree warns (pointing at <c>sync</c>).
    /// </summary>
    public class BaselineTests
    {
        private const string SpecJson =
            "{ \"views\": [ { \"id\": \"Base/Main\", \"elements\": [ " +
            "{ \"button\": { \"id\": \"Base/Play\", \"label\": \"Play\" } } ] } ] }";

        [SetUp]
        public void Clean() => Wipe();

        [OneTimeTearDown]
        public void Teardown()
        {
            Wipe();
            AssetDatabase.SaveAssets();
        }

        private static void Wipe()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            if (File.Exists(NeoBaseline.Path)) File.Delete(NeoBaseline.Path);
        }

        [Test]
        public void BaselinePath_FollowsGeneratedRoot_NotTheCommittedRoot()
        {
            // the SetUpFixture redirects GeneratedRoot to the scratch root for the whole run
            Assert.AreEqual($"{UISpecGenerator.GeneratedRoot}/.neo-baseline.json", NeoBaseline.Path);
            Assert.AreEqual(NeoTestScratchRoot.ScratchRoot, UISpecGenerator.GeneratedRoot,
                "tests must run against the scratch root");
            StringAssert.StartsWith(NeoTestScratchRoot.ScratchRoot, NeoBaseline.Path,
                "the baseline must live under the (scratch) GeneratedRoot, so the committed baseline is never touched");
            Assert.IsFalse(NeoBaseline.Path.StartsWith(UISpecGenerator.DefaultGeneratedRoot),
                "the scratch baseline path must not point at the committed demo root");
        }

        [Test]
        public void Generate_WritesBaseline_ReflectingTheGeneratedProject()
        {
            Assert.IsFalse(NeoBaseline.Exists, "no baseline before the first generate");

            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());

            Assert.IsTrue(NeoBaseline.Exists, "a clean generate must record the baseline");
            Assert.IsTrue(File.Exists(NeoBaseline.Path));

            UISpec baseline = NeoBaseline.Load();
            Assert.IsNotNull(baseline);
            Assert.IsTrue(baseline.views.Any(v => v.id == "Base/Main"),
                "the baseline must describe the project that was just generated");

            // baseline == exported project ⇒ drift reads exactly zero immediately after a generate
            Assert.IsEmpty(SpecDiff.Compare(baseline, UISpecExporter.ExportProject()),
                "a freshly generated project must show no drift against its baseline");
        }

        [Test]
        public void Generate_AgainstDriftedTree_WarnsAndPointsAtSync()
        {
            UISpecGenerator.Generate(UISpec.FromJson(SpecJson));

            // a human edits the generated prefab (drift), then a plain regenerate runs
            EditButtonLabel("Start");
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));

            Assert.IsNotEmpty(regen.warnings, "a generate against a drifted tree must warn");
            Assert.IsTrue(regen.warnings.Any(w => w.Contains("sync")),
                "the warning must point at 'sync' as the non-destructive alternative");
            // a warning is soft — it never makes the run fail
            Assert.IsFalse(regen.hasProblems, "the drift warning must not flip the run to failed");
        }

        private static void EditButtonLabel(string newText)
        {
            string prefab = $"{UISpecGenerator.GeneratedRoot}/Views/Base_Main.prefab";
            UnityEngine.GameObject contents = PrefabUtility.LoadPrefabContents(prefab);
            try
            {
                UIButton button = contents.GetComponentInChildren<UIButton>(true);
                button.transform.Find(UIWidgetFactory.LabelName)
                    .GetComponent<TMPro.TMP_Text>().text = newText;
                PrefabUtility.SaveAsPrefabAsset(contents, prefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
        }
    }
}
