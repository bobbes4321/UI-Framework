using System.Collections.Generic;
using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The end-to-end Plan 1 guarantee: a human edits the EXPORTABLE layer of a generated prefab,
    /// then an agent regenerates from a STALE spec — and the human's edit must NOT be lost. Drives
    /// the whole machine: generate → mutate prefab → diff (roundTrips) → three-way merge → regenerate.
    /// </summary>
    public class RoundTripSafetyTests
    {
        private const string SpecJson = @"{ ""views"": [ { ""id"": ""Round/Main"", ""elements"": [
            { ""button"": { ""id"": ""Round/Play"", ""label"": ""Play"" } }
          ] } ] }";

        private static string ViewPath => $"{UISpecGenerator.GeneratedRoot}/Views/Round_Main.prefab";

        [SetUp]
        public void Clean() => AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);

        [OneTimeTearDown]
        public void Teardown()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void HumanPrefabEdit_SurvivesStaleRegenerate()
        {
            GenerateReport gen = UISpecGenerator.Generate(UISpec.FromJson(SpecJson));
            Assert.IsEmpty(gen.collisions, gen.ToString());
            Assert.IsEmpty(gen.issues, gen.ToString());

            // the spec the project was generated from — the merge baseline
            UISpec baseline = UISpecExporter.ExportProject();

            // a human renames the button in the editor (an exportable-layer edit)
            MutateButtonLabel("Start");

            // 1. diff reports the change as round-trippable
            UISpec current = UISpecExporter.ExportProject();
            var changes = SpecDiff.Compare(baseline, current);
            SpecChange labelChange = changes.FirstOrDefault(c => c.path.EndsWith("/label"));
            Assert.IsNotNull(labelChange, "the prefab edit must appear in the diff");
            Assert.AreEqual("Start", labelChange.after);
            Assert.IsTrue(labelChange.roundTrips, "an exportable edit round-trips");

            // 2. an agent hands us a STALE spec (still says "Play"); fold human drift in
            MergeResult merge = SpecMerge.Merge(baseline, current, theirs: baseline);
            Assert.IsEmpty(merge.conflicts, "the agent didn't touch the label, so there's no conflict");

            // 3. regenerate from the merged spec
            GenerateReport regen = UISpecGenerator.Generate(merge.merged);
            Assert.IsEmpty(regen.collisions, regen.ToString());

            // 4. the human's edit survived the regenerate (the core no-lost-work guarantee)
            Assert.AreEqual("Start", LabelOf(UISpecExporter.ExportProject()),
                "regenerating from a stale spec must NOT wipe the human's prefab edit");
        }

        [Test]
        public void ValidateAction_ReturnsOffSpecWarningsList()
        {
            var result = JsonReader.AsObject(MiniJson.Parse(
                AgentBridge.HandleRequest("{\"action\":\"validate\"}")), "result");
            Assert.IsTrue(result.ContainsKey("offSpecWarnings"), "validate must expose offSpecWarnings");
            Assert.IsInstanceOf<List<object>>(result["offSpecWarnings"]);
        }

        [Test]
        public void DiffAction_WithoutBaseline_DegradesGracefully()
        {
            var result = JsonReader.AsObject(MiniJson.Parse(
                AgentBridge.HandleRequest("{\"action\":\"diff\"}")), "result");
            Assert.AreEqual(true, result["ok"], "no baseline must degrade, not error");
            Assert.IsTrue(result.ContainsKey("note"), "the degraded path explains it needs a baseline");
        }

        private static void MutateButtonLabel(string newText)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(ViewPath);
            try
            {
                UIButton button = contents.GetComponentInChildren<UIButton>(true);
                Assert.IsNotNull(button, "generated view should contain the button");
                var label = button.transform.Find(UIWidgetFactory.LabelName).GetComponent<TMP_Text>();
                label.text = newText;
                PrefabUtility.SaveAsPrefabAsset(contents, ViewPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            AssetDatabase.SaveAssets();
        }

        private static string LabelOf(UISpec spec)
        {
            ViewSpec view = spec.views.Single(v => v.id == "Round/Main");
            ElementSpec button = Find(view.elements, e => e.kind == "button" && e.id == "Round/Play");
            return button?.label;
        }

        private static ElementSpec Find(IEnumerable<ElementSpec> elements, System.Func<ElementSpec, bool> match)
        {
            foreach (ElementSpec e in elements)
            {
                if (match(e)) return e;
                ElementSpec child = Find(e.children, match);
                if (child != null) return child;
            }
            return null;
        }
    }
}
