using System.Collections.Generic;
using Neo.UI;
using Neo.UI.Editor;
using Neo.UI.Menus;
using NUnit.Framework;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// P4: a MenuControlBinder on a factory-built widget applies the stored/default value (WYSIWYG)
    /// and forwards every change back to the service. Built widgets are exercised directly — no play
    /// mode (UI value changes dispatch synchronously).
    /// </summary>
    public class MenuBindingTests
    {
        private GameObject _root;
        private RectTransform _parent;

        [SetUp]
        public void SetUp()
        {
            Signals.ClearAll();
            UserSettingsService.ClearAll();
            UserSettingsService.Store = new InMemoryUserSettingsStore();
            _root = new GameObject("Root", typeof(RectTransform));
            _parent = (RectTransform)_root.transform;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            UserSettingsService.ClearAll();
            Signals.ClearAll();
        }

        private SettingsCatalog Catalog(params MenuItemDefinition[] items)
        {
            var catalog = ScriptableObject.CreateInstance<SettingsCatalog>();
            catalog.category = "Settings";
            catalog.menuName = "Test";
            catalog.items.AddRange(items);
            UserSettingsService.RegisterCatalog(catalog);
            return catalog;
        }

        private static MenuControlBinder Bind(GameObject control, SettingsCatalog catalog, MenuItemDefinition def)
        {
            var binder = control.AddComponent<MenuControlBinder>();
            binder.Configure(catalog, def);
            binder.Wire();
            return binder;
        }

        [Test]
        public void Toggle_AppliesDefault_AndForwardsChange()
        {
            var def = new MenuItemDefinition { category = "Audio", name = "Mute", kind = MenuControlKind.Toggle, defaultValue = "False" };
            SettingsCatalog catalog = Catalog(def);

            GameObject control = UIWidgetFactory.CreateToggle(_parent, "Audio", "Mute", "");
            Bind(control, catalog, def);
            var toggle = control.GetComponent<UIToggle>();

            Assert.IsFalse(toggle.isOn, "default value should be applied to the widget");

            toggle.SetIsOn(true, animateChange: false);
            Assert.IsTrue(UserSettingsService.Get<bool>("Audio", "Mute"));
            Assert.IsTrue(UserSettingsService.Store.Has("Audio/Mute"));
        }

        [Test]
        public void Slider_CommitsValueToService()
        {
            var def = new MenuItemDefinition { category = "Audio", name = "Master", kind = MenuControlKind.Slider, min = 0f, max = 1f, defaultValue = "0.8" };
            SettingsCatalog catalog = Catalog(def);

            GameObject control = UIWidgetFactory.CreateSlider(_parent, "Audio", "Master", 0f, 1f, 0.5f);
            Bind(control, catalog, def);
            var slider = control.GetComponent<UISlider>();

            Assert.AreEqual(0.8f, slider.value, 1e-4f, "default applied without notifying");

            slider.value = 0.3f; // not dragging → commits immediately
            Assert.AreEqual(0.3f, UserSettingsService.Get<float>("Audio", "Master"), 1e-4f);
            Assert.IsTrue(UserSettingsService.Store.Has("Audio/Master"));
        }

        [Test]
        public void Dropdown_CommitsSelectedIndex()
        {
            var options = new List<string> { "Low", "Medium", "High" };
            var def = new MenuItemDefinition { category = "Video", name = "Quality", kind = MenuControlKind.Dropdown, defaultValue = "1", options = options };
            SettingsCatalog catalog = Catalog(def);

            GameObject control = UIWidgetFactory.CreateDropdown(_parent, "Video", "Quality", options, 0);
            Bind(control, catalog, def);
            var dropdown = control.GetComponent<UIDropdown>();

            Assert.AreEqual(1, dropdown.value, "default index applied");

            dropdown.value = 2;
            Assert.AreEqual(2, UserSettingsService.Get<int>("Video", "Quality"));
        }

        [Test]
        public void Stepper_CommitsValue()
        {
            var def = new MenuItemDefinition { category = "Game", name = "Lives", kind = MenuControlKind.Stepper, min = 0f, max = 9f, step = 1f, wholeNumbers = true, defaultValue = "3" };
            SettingsCatalog catalog = Catalog(def);

            GameObject control = UIWidgetFactory.CreateStepper(_parent, "Game", "Lives", 0f, 9f, 3f, 1f);
            Bind(control, catalog, def);
            var stepper = control.GetComponent<UIStepper>();

            Assert.AreEqual(3f, stepper.currentValue, 1e-4f);

            stepper.StepUp();
            Assert.AreEqual(4, UserSettingsService.Get<int>("Game", "Lives"));
        }

        [Test]
        public void CheatButton_FiresCheatSignal()
        {
            var cheatCatalog = ScriptableObject.CreateInstance<CheatCatalog>();
            cheatCatalog.category = "Cheats";
            cheatCatalog.menuName = "Main";
            var def = new MenuItemDefinition { category = "Player", name = "GiveGold", kind = MenuControlKind.Button, label = "Give Gold" };
            cheatCatalog.items.Add(def);
            UserSettingsService.RegisterCatalog(cheatCatalog);

            GameObject control = UIWidgetFactory.CreateButton(_parent, "Player", "GiveGold", "Give Gold");
            var binder = control.AddComponent<MenuControlBinder>();
            binder.Configure(cheatCatalog, def);
            binder.Wire();

            bool fired = false;
            Signals.On(UserSettingsService.CheatCategory, "Player/GiveGold", () => fired = true);
            control.GetComponent<UIButton>().Click();

            Assert.IsTrue(fired, "cheat button click should fire the Cheat signal");
            Object.DestroyImmediate(cheatCatalog);
        }
    }
}
