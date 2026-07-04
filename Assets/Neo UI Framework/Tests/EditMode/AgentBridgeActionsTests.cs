using System.Collections.Generic;
using System.IO;
using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using NUnit.Framework;
using UnityEditor;

namespace Neo.UI.Tests
{
    /// <summary>
    /// <see cref="AgentBridgeActions"/> (Task 6.3, audit E1/D9): a fake registered action dispatches
    /// through <see cref="AgentBridge.HandleRequest"/> exactly like a built-in AND shows up in the
    /// unknown-action error text (proving the registry — not a hand-list — drives both), and "sync" /
    /// "regenerateShowcase" (both funneled through the extracted <c>WriteSyncResult</c>, audit D9) return
    /// identical result key-sets for the same underlying <see cref="SyncResult"/> shape.
    /// </summary>
    public class AgentBridgeActionsTests
    {
        [TearDown]
        public void ResetRegistry() => AgentBridgeActions.ResetForTests();

        [Test]
        public void RegisteredAction_DispatchesThroughHandleRequest()
        {
            const string probeId = "probe-action";
            bool invoked = false;
            AgentBridgeActions.Register(probeId, mutatesAssets: false, handler: (request, result) =>
            {
                invoked = true;
                result["ok"] = true;
                result["echo"] = JsonReader.GetString(request, "value");
            });

            string json = AgentBridge.HandleRequest("{\"action\":\"" + probeId + "\",\"value\":\"hi\"}");
            var result = JsonReader.AsObject(MiniJson.Parse(json), "probe result");

            Assert.IsTrue(invoked, "the registered handler must run for its own action id");
            Assert.AreEqual(true, result["ok"]);
            Assert.AreEqual("hi", result["echo"]);
        }

        [Test]
        public void UnknownAction_ErrorText_EnumeratesRegistryDynamically()
        {
            const string probeId = "z-probe-action-for-error-text";
            AgentBridgeActions.Register(probeId, mutatesAssets: false, handler: (request, result) => { });

            string json = AgentBridge.HandleRequest("{\"action\":\"totally-bogus-action\"}");
            var result = JsonReader.AsObject(MiniJson.Parse(json), "unknown-action result");

            Assert.AreEqual(false, result["ok"]);
            string error = (string)result["error"];
            StringAssert.Contains(probeId, error,
                "a newly registered action must appear in the dynamically enumerated error text");
        }

        [Test]
        public void SyncAndRegenerateShowcase_ReturnIdenticalResultKeySets_OnScratchRun()
        {
            // regenerateShowcase resolves its showcase by id through ShowcaseRegistry, so the fixture
            // must go through the real registration path (NeoCapture.CreateShowcase) rather than a bare
            // `new Showcase { ... }` — an unregistered showcase can never be found by id.
            const string scratchId = "test-bridge-actions-scratch";
            string root = $"{ShowcaseRegistry.ShowcasesRoot}/{scratchId}/Generated";
            Showcase showcase = NeoCapture.CreateShowcase(scratchId, "Bridge Actions Scratch", "Tests");
            File.WriteAllText(showcase.specPath, OneButton("Scratch/Main", "Scratch/Play", "Play"));
            AssetDatabase.ImportAsset(showcase.specPath);

            try
            {
                using (NeoWorkspace.Scoped(showcase))
                    UISpecGenerator.Generate(UISpec.FromJson(File.ReadAllText(showcase.specPath)));
                Assert.IsTrue(AssetDatabase.IsValidFolder($"{root}/Views"), "precondition: seeded generation");

                string regenJson = AgentBridge.HandleRequest(
                    "{\"action\":\"regenerateShowcase\",\"showcase\":\"" + scratchId + "\"}");
                var regenResult = JsonReader.AsObject(MiniJson.Parse(regenJson), "regenerateShowcase result");
                Assert.AreEqual(true, regenResult["ok"], "regenerateShowcase should round-trip cleanly on a pristine showcase");

                string syncJson;
                using (NeoWorkspace.Scoped(showcase))
                    syncJson = AgentBridge.HandleRequest(
                        "{\"action\":\"sync\",\"incoming\":" + MiniJson.Serialize(showcase.specPath) + "}");
                var syncResult = JsonReader.AsObject(MiniJson.Parse(syncJson), "sync result");
                Assert.AreEqual(true, syncResult["ok"], "sync with the same incoming spec should also round-trip cleanly");

                CollectionAssert.AreEquivalent(syncResult.Keys, regenResult.Keys,
                    "sync and regenerateShowcase must shape identical result key-sets (both funnel through WriteSyncResult)");
            }
            finally
            {
                AssetDatabase.DeleteAsset($"{ShowcaseRegistry.ShowcasesRoot}/{scratchId}");
                AssetDatabase.DeleteAsset($"{ShowcaseRegistry.ShowcasesRoot}/Specs/{scratchId}.json");
                ShowcaseRegistry.Remove(scratchId);
                ShowcaseRegistry.InvalidateDiscovery();
                AssetDatabase.SaveAssets();
            }
        }

        private static string OneButton(string view, string button, string label) =>
            "{ \"views\": [ { \"id\": \"" + view + "\", \"elements\": [ " +
            "{ \"button\": { \"id\": \"" + button + "\", \"label\": \"" + label + "\" } } ] } ] }";
    }
}
