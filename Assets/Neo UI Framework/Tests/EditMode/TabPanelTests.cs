using System.Linq;
using Neo.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Tab panels: a tab's "controls" wires it to a sibling <see cref="UIPanel"/> (the last loud
    /// dead-interaction gap), the link round-trips through export, and the baked prefab matches the
    /// runtime start state (selected tab's panel shown, the rest hidden).
    /// </summary>
    public class TabPanelTests
    {
        private const string SettingsSpecJson = @"{
          ""views"": [ { ""id"": ""Spec/Settings"", ""elements"": [
            { ""vstack"": { ""anchor"": ""Stretch"", ""padding"": 16, ""spacing"": 12, ""children"": [
              { ""tabbar"": { ""id"": ""SetTabs/Bar"", ""children"": [
                { ""tab"": { ""id"": ""SetTabs/General"", ""label"": ""General"", ""controls"": ""Panels/General"" } },
                { ""tab"": { ""id"": ""SetTabs/Audio"", ""label"": ""Audio"", ""controls"": ""Panels/Audio"" } }
              ] } },
              { ""panel"": { ""id"": ""Panels/General"", ""children"": [ { ""text"": { ""label"": ""G"" } } ] } },
              { ""panel"": { ""id"": ""Panels/Audio"", ""children"": [ { ""text"": { ""label"": ""A"" } } ] } }
            ] } }
          ] } ]
        }";

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AssetDatabase.SaveAssets();
        }

        private static GameObject GenerateSettingsView()
        {
            GenerateReport report = UISpecGenerator.Generate(UISpec.FromJson(SettingsSpecJson));
            Assert.IsEmpty(report.issues, report.ToString());
            Assert.IsEmpty(report.collisions, report.ToString());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Spec_Settings.prefab");
            Assert.IsNotNull(prefab, "generated settings view missing");
            return prefab;
        }

        [Test]
        public void Tabs_WireToTheirSiblingPanels()
        {
            GameObject prefab = GenerateSettingsView();

            UIPanel general = prefab.GetComponentsInChildren<UIPanel>(true).FirstOrDefault(p => p.id.Matches("Panels", "General"));
            UIPanel audio = prefab.GetComponentsInChildren<UIPanel>(true).FirstOrDefault(p => p.id.Matches("Panels", "Audio"));
            Assert.IsNotNull(general, "General panel must generate");
            Assert.IsNotNull(audio, "Audio panel must generate");

            UITab generalTab = prefab.GetComponentsInChildren<UITab>(true).First(t => t.id.Matches("SetTabs", "General"));
            UITab audioTab = prefab.GetComponentsInChildren<UITab>(true).First(t => t.id.Matches("SetTabs", "Audio"));
            Assert.AreSame(general, generalTab.targetContainer, "General tab must control the General panel");
            Assert.AreSame(audio, audioTab.targetContainer, "Audio tab must control the Audio panel");
        }

        [Test]
        public void SelectedTabsPanel_IsBakedActive_RestInactive()
        {
            GameObject prefab = GenerateSettingsView();
            UIPanel general = prefab.GetComponentsInChildren<UIPanel>(true).First(p => p.id.Matches("Panels", "General"));
            UIPanel audio = prefab.GetComponentsInChildren<UIPanel>(true).First(p => p.id.Matches("Panels", "Audio"));

            // panels hide by deactivating their GameObject, so the first (selected) tab's panel is
            // baked active and the rest inactive — baked prefab == runtime start state (WYSIWYG)
            Assert.IsTrue(general.disableGameObjectWhenHidden, "panels must vacate layout when hidden");
            Assert.IsTrue(general.gameObject.activeSelf, "the selected tab's panel is baked active");
            Assert.IsFalse(audio.gameObject.activeSelf, "an unselected tab's panel is baked inactive");
        }

        [Test]
        public void TabPanelLink_RoundTripsThroughExport()
        {
            GenerateSettingsView();

            UISpec exported = UISpecExporter.ExportProject();
            ViewSpec view = exported.views.First(v => v.id == "Spec/Settings");
            ElementSpec tabbar = view.elements.First(e => e.kind == "vstack").children.First(e => e.kind == "tabbar");
            Assert.AreEqual("Panels/General", tabbar.children[0].controls);
            Assert.AreEqual("Panels/Audio", tabbar.children[1].controls);
            Assert.AreEqual(2, view.elements.First(e => e.kind == "vstack").children.Count(c => c.kind == "panel"));

            string firstExport = exported.ToJson();
            GenerateReport regen = UISpecGenerator.Generate(UISpec.FromJson(firstExport));
            Assert.IsEmpty(regen.collisions, regen.ToString());
            string secondExport = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(firstExport, secondExport,
                "tab→panel wiring must survive export → generate → export byte-identically");
        }

        [Test]
        public void WiredTabs_PassInteractivityLint()
        {
            GenerateSettingsView();
            var issues = AgentValidation.ValidateAll();
            Assert.IsFalse(issues.Any(i => i.Contains("SetTabs") && i.Contains("controls nothing")),
                "tabs wired to panels must not trip the dead-interaction lint:\n" + string.Join("\n", issues));
        }
    }
}
