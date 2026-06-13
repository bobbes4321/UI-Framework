using System.Linq;
using AlterEyes.UI.Editor;
using AlterEyes.UI.Menus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Tests
{
    /// <summary>
    /// P5: the settings/cheats spec sections generate real catalog assets + baked menu views, the
    /// dead-binding lint stays quiet for menu-wired controls, and export→generate→export is fixed point.
    /// </summary>
    public class SettingsAndCheatsSpecTests
    {
        private const string Json = @"
{
  ""settings"": [
    { ""id"": ""Settings/Audio"", ""groups"": [""Audio"",""Video""], ""start"": ""Audio"",
      ""items"": [
        { ""slider"":   { ""id"":""Audio/Master"", ""group"":""Audio"", ""label"":""Master Volume"", ""min"":0, ""max"":1, ""value"":0.8 } },
        { ""toggle"":   { ""id"":""Video/VSync"",  ""group"":""Video"", ""label"":""VSync"", ""value"":true } },
        { ""dropdown"": { ""id"":""Video/Quality"",""group"":""Video"", ""label"":""Quality"", ""options"":[""Low"",""Medium"",""High""], ""value"":2 } }
      ] }
  ],
  ""cheats"": [
    { ""id"": ""Cheats/Main"",
      ""items"": [
        { ""button"": { ""id"":""Player/GiveGold"", ""label"":""Give 100 Gold"" } },
        { ""toggle"": { ""id"":""Player/God"", ""label"":""God Mode"", ""value"":false } }
      ] }
  ],
  ""views"": [
    { ""id"":""Menu/Settings"", ""elements"":[ { ""settings"": { ""catalog"":""Settings/Audio"" } } ] },
    { ""id"":""Menu/Cheats"",   ""elements"":[ { ""cheats"":   { ""catalog"":""Cheats/Main"" } } ] }
  ]
}";

        private GenerateReport _report;

        [OneTimeSetUp]
        public void Generate()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            _report = UISpecGenerator.Generate(UISpec.FromJson(Json));
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(UISpecGenerator.GeneratedRoot);
            AEUISettings settings = AEUISettings.instance;
            if (settings != null && settings.animationPresets != null)
            {
                foreach (UIAnimationPreset preset in settings.animationPresets.Presets.Where(p => p == null).ToList())
                    settings.animationPresets.Remove(preset);
                EditorUtility.SetDirty(settings.animationPresets);
            }
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void Generation_HasNoIssuesOrCollisions()
        {
            Assert.IsEmpty(_report.issues, $"issues:\n{_report}");
            Assert.IsEmpty(_report.collisions, $"collisions:\n{_report}");
        }

        [Test]
        public void SettingsCatalog_AssetHasItemsAndGroups()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SettingsCatalog>(
                $"{UISpecGenerator.GeneratedRoot}/Menus/Settings_Audio.asset");
            Assert.IsNotNull(catalog, "expected a generated SettingsCatalog asset");
            Assert.AreEqual(3, catalog.items.Count);
            CollectionAssert.AreEqual(new[] { "Audio", "Video" }, catalog.groups);

            MenuItemDefinition master = catalog.Find("Audio", "Master");
            Assert.IsNotNull(master);
            Assert.AreEqual(MenuControlKind.Slider, master.kind);
            Assert.AreEqual("0.8", master.defaultValue);

            MenuItemDefinition quality = catalog.Find("Video", "Quality");
            Assert.AreEqual(MenuControlKind.Dropdown, quality.kind);
            CollectionAssert.AreEqual(new[] { "Low", "Medium", "High" }, quality.options);
        }

        [Test]
        public void CheatCatalog_IsCheatType()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MenuCatalog>(
                $"{UISpecGenerator.GeneratedRoot}/Menus/Cheats_Main.asset");
            Assert.IsInstanceOf<CheatCatalog>(catalog);
            Assert.AreEqual(2, catalog.items.Count);
        }

        [Test]
        public void SettingsView_HasPresenterTabsAndBinders()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Menu_Settings.prefab");
            Assert.IsNotNull(prefab);

            var presenter = prefab.GetComponentInChildren<SettingsMenu>(true);
            Assert.IsNotNull(presenter, "menu root should carry a SettingsMenu presenter");
            Assert.IsNotNull(presenter.catalog);

            // three controls → three binders
            Assert.AreEqual(3, prefab.GetComponentsInChildren<MenuControlBinder>(true).Length);
            // two groups → two panels, wired to tabs (start group active)
            Assert.AreEqual(2, prefab.GetComponentsInChildren<UIPanel>(true).Length);
            UIPanel audioPanel = prefab.GetComponentsInChildren<UIPanel>(true)
                .FirstOrDefault(p => p.id.Name == "Audio");
            Assert.IsNotNull(audioPanel);
        }

        [Test]
        public void CheatView_HasCheatMenuAndBinders()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{UISpecGenerator.GeneratedRoot}/Views/Menu_Cheats.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponentInChildren<CheatMenu>(true));
            Assert.AreEqual(2, prefab.GetComponentsInChildren<MenuControlBinder>(true).Length);
        }

        [Test]
        public void Validation_DoesNotFlagMenuControlsAsDeadOrUnbound()
        {
            var issues = AgentValidation.ValidateAll();
            string dump = string.Join("\n", issues);
            Assert.IsFalse(issues.Any(i => i.Contains("does nothing") &&
                                           (i.Contains("GiveGold") || i.Contains("God"))),
                "menu buttons wrongly flagged as dead:\n" + dump);
            Assert.IsFalse(issues.Any(i => i.Contains("is not in catalog")),
                "binder dead-binding false positive:\n" + dump);
        }

        [Test]
        public void ExportGenerateExport_IsFixedPoint()
        {
            string export1 = UISpecExporter.ExportProject().ToJson();
            UISpecGenerator.Generate(UISpec.FromJson(export1));
            string export2 = UISpecExporter.ExportProject().ToJson();
            Assert.AreEqual(export1, export2, "settings/cheats export must be a fixed point");
        }
    }
}
