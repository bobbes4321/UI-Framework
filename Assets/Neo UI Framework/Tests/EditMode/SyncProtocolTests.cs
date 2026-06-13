using System.Collections.Generic;
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
    /// The Plan 4 protocol: <see cref="SpecBaseline.Sync"/> (and the AgentBridge <c>sync</c> action)
    /// route human editor edits back into the spec without loss. An agent handing a STALE spec to
    /// <c>sync</c> cannot wipe a human's exportable-layer edits; off-spec edits block the sync rather
    /// than vanishing; conflicting edits are surfaced, not steamrolled.
    /// </summary>
    public class SyncProtocolTests
    {
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

        // ----------------------------------------------------------------- 1. stale-spec survival

        [Test]
        public void Sync_WithStaleSpec_PreservesHumanEditInAssetsAndBaseline()
        {
            UISpecGenerator.Generate(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));
            Assert.IsTrue(NeoBaseline.Exists, "a clean generate must record the baseline");

            // a human renames the button in the editor (an exportable-layer edit)
            SetButtonLabel(ViewPath("Sync/Main"), "Start");

            // an agent hands us the STALE spec (still says "Play")
            SyncResult sync = SpecBaseline.Sync(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));

            Assert.IsTrue(sync.ok, sync.note);
            Assert.IsEmpty(sync.conflicts, "the agent didn't touch the label, so there's no conflict");
            Assert.IsTrue(sync.applied.Any(c => c.path.EndsWith("/label") && c.after == "Start"),
                "the human's rename must be folded in as an applied change");

            // the rename survived in the regenerated assets AND in the new baseline
            Assert.AreEqual("Start", LabelOf(UISpecExporter.ExportProject(), "Sync/Main", "Sync/Play"),
                "regenerating from a stale spec must NOT wipe the human's prefab edit");
            Assert.AreEqual("Start", LabelOf(NeoBaseline.Load(), "Sync/Main", "Sync/Play"),
                "the new baseline must reflect the merged (human-preserving) spec");
        }

        // ----------------------------------------------------------------- 2. disjoint edits both survive

        [Test]
        public void Sync_HumanEditsViewA_IncomingEditsViewB_BothSurvive_NoConflict()
        {
            UISpecGenerator.Generate(UISpec.FromJson(TwoViews("Play", "Go")));

            // human edits view A in the editor; incoming spec edits only view B
            SetButtonLabel(ViewPath("A/Main"), "Start");
            SyncResult sync = SpecBaseline.Sync(UISpec.FromJson(TwoViews("Play", "GoGo")));

            Assert.IsTrue(sync.ok, sync.note);
            Assert.IsEmpty(sync.conflicts, "edits to different views must not conflict");

            UISpec after = UISpecExporter.ExportProject();
            Assert.AreEqual("Start", LabelOf(after, "A/Main", "A/Play"), "human edit to view A preserved");
            Assert.AreEqual("GoGo", LabelOf(after, "B/Main", "B/Go"), "incoming edit to view B applied");
        }

        // ----------------------------------------------------------------- 3. same-field conflict

        [Test]
        public void Sync_BothChangeSameField_ReportsConflict_IncomingWinsByDefault()
        {
            UISpecGenerator.Generate(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));

            SetButtonLabel(ViewPath("Sync/Main"), "Start");                 // ours
            SyncResult sync = SpecBaseline.Sync(
                UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Begin"))); // theirs

            Assert.IsTrue(sync.conflicts.Any(c => c.path.EndsWith("/label")),
                "a field changed on both sides must be reported as a conflict");
            Assert.AreEqual("Begin", LabelOf(UISpecExporter.ExportProject(), "Sync/Main", "Sync/Play"),
                "the incoming spec wins on conflict by default (PreferTheirs)");
        }

        [Test]
        public void Sync_PreferOurs_KeepsHumanValueOnConflict()
        {
            UISpecGenerator.Generate(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));

            SetButtonLabel(ViewPath("Sync/Main"), "Start");
            SyncResult sync = SpecBaseline.Sync(
                UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Begin")), ConflictPolicy.PreferOurs);

            Assert.IsTrue(sync.conflicts.Any(c => c.path.EndsWith("/label")), "still surfaced as a conflict");
            Assert.AreEqual("Start", LabelOf(UISpecExporter.ExportProject(), "Sync/Main", "Sync/Play"),
                "PreferOurs keeps the human value");
        }

        // ----------------------------------------------------------------- 4. off-spec gate

        [Test]
        public void Sync_OffSpecEdit_RefusesWithoutForce_AndDropsWithForce()
        {
            UISpecGenerator.Generate(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));
            AddInternalChild(ViewPath("Sync/Main"), "HumanExtra"); // factory-internal, not exportable

            // refuses: nothing is regenerated, the off-spec edit is reported (never silently dropped)
            SyncResult refused = SpecBaseline.Sync(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));
            Assert.IsTrue(refused.refused, "an off-spec edit must block the sync");
            Assert.IsFalse(refused.ok);
            Assert.IsFalse(refused.regenerated, "a refused sync must not regenerate");
            Assert.IsNotEmpty(refused.offSpecWarnings, "the refusal must name the off-spec edit");
            Assert.IsTrue(HasInternalChild(ViewPath("Sync/Main"), "HumanExtra"),
                "the refused sync must leave the project untouched");

            // forced: proceeds, regenerates, and records the loss explicitly in 'dropped'
            SyncResult forced = SpecBaseline.Sync(
                UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")), force: true);
            Assert.IsTrue(forced.regenerated, "a forced sync regenerates");
            Assert.IsNotEmpty(forced.dropped, "force must echo the dropped off-spec edits, never lose them silently");
            Assert.IsFalse(HasInternalChild(ViewPath("Sync/Main"), "HumanExtra"),
                "the forced regenerate rebuilds the widget, dropping the off-spec child");
        }

        // ----------------------------------------------------------------- 5. capture (no incoming)

        [Test]
        public void Sync_NoIncoming_UpdatesBaselineToCurrent_AssetsUnchanged()
        {
            UISpecGenerator.Generate(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));
            SetButtonLabel(ViewPath("Sync/Main"), "Start");

            string prefabBefore = File.ReadAllText(ViewPath("Sync/Main"));
            SyncResult sync = SpecBaseline.CaptureEdits();

            Assert.IsTrue(sync.ok, sync.note);
            Assert.IsFalse(sync.regenerated, "capture must not regenerate");
            Assert.IsTrue(sync.baselineUpdated);
            Assert.AreEqual(prefabBefore, File.ReadAllText(ViewPath("Sync/Main")),
                "capture must leave the assets untouched");
            Assert.AreEqual("Start", LabelOf(NeoBaseline.Load(), "Sync/Main", "Sync/Play"),
                "the baseline now reflects the human's current project");
        }

        // ----------------------------------------------------------------- AgentBridge wiring

        [Test]
        public void SyncAction_RefusesOnOffSpec_AndForceProceeds()
        {
            UISpecGenerator.Generate(UISpec.FromJson(OneButton("Sync/Main", "Sync/Play", "Play")));
            AddInternalChild(ViewPath("Sync/Main"), "HumanExtra");

            string incoming = Path.Combine(Path.GetTempPath(), "neo-sync-incoming.json");
            File.WriteAllText(incoming, OneButton("Sync/Main", "Sync/Play", "Play"));
            try
            {
                var refused = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(
                    "{\"action\":\"sync\",\"incoming\":\"" + Escape(incoming) + "\"}")), "result");
                Assert.AreEqual(false, refused["ok"]);
                Assert.AreEqual(true, refused["refused"]);
                Assert.IsInstanceOf<List<object>>(refused["offSpecWarnings"]);
                Assert.Greater(((List<object>)refused["offSpecWarnings"]).Count, 0);

                var forced = JsonReader.AsObject(MiniJson.Parse(AgentBridge.HandleRequest(
                    "{\"action\":\"sync\",\"incoming\":\"" + Escape(incoming) + "\",\"force\":true}")), "result");
                Assert.AreEqual(true, forced["ok"]);
                Assert.Greater(((List<object>)forced["dropped"]).Count, 0, "force must report the dropped edit");
            }
            finally
            {
                if (File.Exists(incoming)) File.Delete(incoming);
            }
        }

        // ----------------------------------------------------------------- helpers

        private static string OneButton(string view, string button, string label) =>
            "{ \"views\": [ { \"id\": \"" + view + "\", \"elements\": [ " +
            "{ \"button\": { \"id\": \"" + button + "\", \"label\": \"" + label + "\" } } ] } ] }";

        private static string TwoViews(string labelA, string labelB) =>
            "{ \"views\": [ " +
            "{ \"id\": \"A/Main\", \"elements\": [ { \"button\": { \"id\": \"A/Play\", \"label\": \"" + labelA + "\" } } ] }, " +
            "{ \"id\": \"B/Main\", \"elements\": [ { \"button\": { \"id\": \"B/Go\", \"label\": \"" + labelB + "\" } } ] } ] }";

        private static string ViewPath(string viewId)
        {
            CategoryNameId.Parse(viewId, out string category, out string name);
            return $"{UISpecGenerator.GeneratedRoot}/Views/{category}_{name}.prefab";
        }

        private static string Escape(string path) => path.Replace("\\", "\\\\");

        private static void SetButtonLabel(string prefabPath, string newText)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                UIButton button = contents.GetComponentInChildren<UIButton>(true);
                Assert.IsNotNull(button, "generated view should contain the button");
                button.transform.Find(UIWidgetFactory.LabelName).GetComponent<TMP_Text>().text = newText;
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            AssetDatabase.SaveAssets();
        }

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

        private static string LabelOf(UISpec spec, string viewId, string buttonId)
        {
            ViewSpec view = spec.views.SingleOrDefault(v => v.id == viewId);
            ElementSpec button = view == null ? null : Find(view.elements, e => e.kind == "button" && e.id == buttonId);
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
