using Neo.UI.Editor;
using Neo.UI.Editor.Authoring;
using Neo.UI.Menus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// The native (human-first) settings/cheats drop: dragging a Settings/Cheats Menu tile must build a
    /// live, catalog-backed menu — a non-generated <see cref="SettingsCatalog"/>/<see cref="CheatCatalog"/>
    /// SO the developer owns plus a presenter with WYSIWYG rows — NOT a spec <c>settings</c> element
    /// pointing at a catalog that was never generated (the old bug, which built an empty menu). Editing the
    /// catalog and calling <see cref="NeoSceneAuthoring.RebuildMenu"/> re-materialises the rows with no
    /// spec/generate round-trip.
    /// </summary>
    public class NativeMenuAuthoringTests
    {
        private NeoUISettings _settings;
        private GameObject _canvasRoot;
        private GameObject _viewRoot;
        private GameObject _created;
        private string _catalogPath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            if (_settings != null && _settings.theme != null)
            {
                StarterKitBootstrap.EnsureFactoryTokens(_settings.theme);
                StarterKitBootstrap.EnsureTextStyles(_settings.theme);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _canvasRoot = new GameObject("NativeMenuTestCanvas", typeof(Canvas));
            var view = new ViewSpec { category = "NativeMenu", viewName = "V" };
            _viewRoot = UISpecGenerator.BuildViewGameObject(view, _settings, new GenerateReport());
            _viewRoot.transform.SetParent(_canvasRoot.transform, worldPositionStays: false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_created != null) Object.DestroyImmediate(_created);
            if (!string.IsNullOrEmpty(_catalogPath)) AssetDatabase.DeleteAsset(_catalogPath);
            if (_canvasRoot != null) Object.DestroyImmediate(_canvasRoot);
            _created = null;
            _catalogPath = null;
            Selection.activeGameObject = null;
        }

        [Test]
        public void DroppingCheatsMenu_BuildsNativePresenterCatalogAndRows()
        {
            _created = NeoSceneAuthoring.CreateWidget("cheats", _viewRoot);
            Assert.IsNotNull(_created, "the cheats tile must spawn a menu");

            var presenter = _created.GetComponent<CheatMenu>();
            Assert.IsNotNull(presenter, "a cheats drop must add a CheatMenu presenter");
            Assert.IsInstanceOf<CheatCatalog>(presenter.catalog, "the presenter must own a real CheatCatalog");
            Assert.IsFalse(presenter.buildOnStart, "a native menu is baked WYSIWYG, not rebuilt on play");

            _catalogPath = AssetDatabase.GetAssetPath(presenter.catalog);
            Assert.IsTrue(_catalogPath.StartsWith(NeoSceneAuthoring.MenuCatalogFolder),
                "the catalog must be a developer-owned asset, not a generated one");

            Assert.Greater(presenter.catalog.items.Count, 0, "a dropped menu is seeded with starter controls");
            Assert.AreEqual(presenter.catalog.items.Count, presenter.contentRoot.childCount,
                "every seeded item materialises a row at author time (WYSIWYG)");
        }

        [Test]
        public void DroppingSettingsMenu_BuildsSettingsCatalog()
        {
            _created = NeoSceneAuthoring.CreateWidget("settings", _viewRoot);
            var presenter = _created.GetComponent<SettingsMenu>();
            Assert.IsNotNull(presenter, "a settings drop must add a SettingsMenu presenter");
            Assert.IsInstanceOf<SettingsCatalog>(presenter.catalog);
            _catalogPath = AssetDatabase.GetAssetPath(presenter.catalog);
            Assert.Greater(presenter.contentRoot.childCount, 0, "the menu must render rows, not an empty box");
        }

        [Test]
        public void RebuildMenu_AfterAddingItem_MaterialisesTheNewRow()
        {
            _created = NeoSceneAuthoring.CreateWidget("settings", _viewRoot);
            var presenter = _created.GetComponent<SettingsMenu>();
            MenuCatalog catalog = presenter.catalog;
            _catalogPath = AssetDatabase.GetAssetPath(catalog);
            int before = presenter.contentRoot.childCount;

            // exactly the workflow that motivated this: add a control to the SO, then rebuild.
            catalog.items.Add(new MenuItemDefinition
            {
                category = "Player", name = "SpawnDogs", kind = MenuControlKind.Button, label = "Spawn Dogs"
            });
            // Rebuild replaces the menu root in place (old presenter destroyed); track the new root.
            GameObject rebuilt = NeoSceneAuthoring.RebuildMenu(presenter);
            _created = rebuilt;
            Assert.IsNotNull(rebuilt, "rebuild must return the new menu root");

            var newPresenter = rebuilt.GetComponent<SettingsMenu>();
            Assert.AreEqual(before + 1, newPresenter.contentRoot.childCount,
                "editing the catalog and rebuilding must add the new control's row — no spec/generate step");
        }

        [Test]
        public void GroupedMenu_BuildsSelfWiringTabs()
        {
            _created = NeoSceneAuthoring.CreateWidget("settings", _viewRoot);
            var presenter = _created.GetComponent<SettingsMenu>();
            MenuCatalog catalog = presenter.catalog;
            _catalogPath = AssetDatabase.GetAssetPath(catalog);

            // Add categories (groups) + assign items to them — the exact edit that produced dead tabs before.
            catalog.groups.Add("Audio");
            catalog.groups.Add("Video");
            foreach (MenuItemDefinition item in catalog.items)
                item.group = item.Category == "Audio" ? "Audio" : "Video";

            GameObject rebuilt = NeoSceneAuthoring.RebuildMenu(presenter);
            _created = rebuilt;

            // Tabs must be real UITab components with a serialized container reference (they self-wire on
            // enable), not plain buttons carrying a runtime-only click listener that dies when baked.
            UITab[] tabs = rebuilt.GetComponentsInChildren<UITab>(true);
            Assert.AreEqual(2, tabs.Length, "each group must bake a UITab");
            foreach (UITab tab in tabs)
                Assert.IsNotNull(tab.targetContainer, "each tab must reference the panel it controls (serialized wiring)");
        }
    }
}
