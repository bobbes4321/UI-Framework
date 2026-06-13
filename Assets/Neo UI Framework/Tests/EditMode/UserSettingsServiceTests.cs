using System.Text.RegularExpressions;
using Neo.UI;
using Neo.UI.Menus;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Neo.UI.Tests
{
    /// <summary>
    /// P1: the value store + binding hub. Pure synchronous service tests — no play mode needed.
    /// </summary>
    public class UserSettingsServiceTests
    {
        private SettingsCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            Signals.ClearAll();
            UserSettingsService.ClearAll();
            UserSettingsService.Store = new InMemoryUserSettingsStore();

            _catalog = ScriptableObject.CreateInstance<SettingsCatalog>();
            _catalog.category = "Settings";
            _catalog.menuName = "Test";
            _catalog.items.Add(new MenuItemDefinition
            {
                category = "Audio", name = "Master", kind = MenuControlKind.Slider, defaultValue = "0.8"
            });
            _catalog.items.Add(new MenuItemDefinition
            {
                category = "Video", name = "VSync", kind = MenuControlKind.Toggle, defaultValue = "True"
            });
            _catalog.items.Add(new MenuItemDefinition
            {
                category = "Video", name = "Quality", kind = MenuControlKind.Dropdown, defaultValue = "2"
            });
            UserSettingsService.RegisterCatalog(_catalog);
        }

        [TearDown]
        public void TearDown()
        {
            UserSettingsService.ClearAll();
            Signals.ClearAll();
            if (_catalog != null) Object.DestroyImmediate(_catalog);
        }

        [Test]
        public void Get_FallsBackToCatalogDefault()
        {
            Assert.AreEqual(0.8f, UserSettingsService.Get<float>("Audio", "Master"), 1e-4f);
            Assert.IsTrue(UserSettingsService.Get<bool>("Video", "VSync"));
            Assert.AreEqual(2, UserSettingsService.Get<int>("Video", "Quality"));
        }

        [Test]
        public void Set_Commit_PersistsAndSignals()
        {
            float received = -1f;
            Signals.On<float>(UserSettingsService.SettingsCategory, "Audio/Master", v => received = v);

            UserSettingsService.Set("Audio", "Master", 0.3f);

            Assert.AreEqual(0.3f, received, 1e-4f, "committed change should fire the Settings signal");
            Assert.IsTrue(UserSettingsService.Store.TryGet("Audio/Master", out string stored));
            Assert.AreEqual(0.3f, UserSettingsService.Get<float>("Audio", "Master"), 1e-4f);
            Assert.AreEqual("0.3", stored);
        }

        [Test]
        public void Set_Preview_SignalsButDoesNotPersist()
        {
            float committed = -1f, preview = -1f;
            Signals.On<float>(UserSettingsService.SettingsCategory, "Audio/Master", v => committed = v);
            Signals.On<float>(UserSettingsService.SettingsCategory + UserSettingsService.PreviewSuffix, "Audio/Master", v => preview = v);

            UserSettingsService.Set("Audio", "Master", 0.42f, commit: false);

            Assert.AreEqual(0.42f, preview, 1e-4f, "preview should fire on the .Preview stream");
            Assert.AreEqual(-1f, committed, "preview must not fire the committed stream");
            Assert.IsFalse(UserSettingsService.Store.Has("Audio/Master"), "preview must not persist");
        }

        [Test]
        public void Get_UnknownId_WarnsAndReturnsDefault()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Get on unknown setting"));
            Assert.AreEqual(0, UserSettingsService.Get<int>("Nope", "Nope"));
        }

        [Test]
        public void Store_RoundTripsAllValueTypes()
        {
            UserSettingsService.Set("T", "B", true);
            UserSettingsService.Set("T", "I", 7);
            UserSettingsService.Set("T", "F", 1.25f);
            UserSettingsService.Set("T", "S", "hello");

            // a fresh service reading the same store must recover the values
            Assert.IsTrue(UserSettingsService.Get<bool>("T", "B"));
            Assert.AreEqual(7, UserSettingsService.Get<int>("T", "I"));
            Assert.AreEqual(1.25f, UserSettingsService.Get<float>("T", "F"), 1e-4f);
            Assert.AreEqual("hello", UserSettingsService.Get<string>("T", "S"));
        }

        [Test]
        public void Bind_GetterIsSourceOfTruth_SetterDriven()
        {
            float live = 11f;
            UserSettingsService.Bind<float>("Game", "Speed", () => live, v => live = v, persist: false);

            Assert.AreEqual(11f, UserSettingsService.Get<float>("Game", "Speed"), 1e-4f);

            UserSettingsService.Set("Game", "Speed", 25f);
            Assert.AreEqual(25f, live, 1e-4f, "setter should drive the bound value");
            Assert.IsFalse(UserSettingsService.Store.Has("Game/Speed"), "persist:false must not write the store");
        }

        [Test]
        public void ResetToDefault_RestoresCatalogValue()
        {
            UserSettingsService.Set("Audio", "Master", 0.1f);
            Assert.AreEqual(0.1f, UserSettingsService.Get<float>("Audio", "Master"), 1e-4f);

            UserSettingsService.ResetToDefault(_catalog.Find("Audio", "Master"));
            Assert.AreEqual(0.8f, UserSettingsService.Get<float>("Audio", "Master"), 1e-4f);
        }

        [Test]
        public void FireCheat_SignalsOnCheatStream()
        {
            bool fired = false;
            Signals.On(UserSettingsService.CheatCategory, "Player/GiveGold", () => fired = true);
            UserSettingsService.FireCheat("Player", "GiveGold");
            Assert.IsTrue(fired);
        }
    }
}
