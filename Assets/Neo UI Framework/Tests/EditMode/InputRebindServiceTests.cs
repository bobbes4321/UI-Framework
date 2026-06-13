using Neo.UI.Menus;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Neo.UI.Tests
{
    /// <summary>
    /// P6: the rebind persistence path (override → save → reload → reset) through the settings store.
    /// The interactive capture (PerformInteractiveRebinding) needs a device update loop and is verified
    /// in-editor; the data path is pure and covered here.
    /// </summary>
    public class InputRebindServiceTests
    {
        private InputActionAsset _asset;

        [SetUp]
        public void SetUp()
        {
            UserSettingsService.ClearAll();
            UserSettingsService.Store = new InMemoryUserSettingsStore();

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _asset.name = "Controls";
            InputActionMap map = _asset.AddActionMap("Gameplay");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_asset);
            UserSettingsService.ClearAll();
        }

        private InputAction Jump => _asset.FindAction("Gameplay/Jump");

        [Test]
        public void DisplayString_ReflectsCurrentBinding()
        {
            Assert.IsFalse(string.IsNullOrEmpty(InputRebindService.DisplayString(Jump, 0)));
        }

        [Test]
        public void SaveAndLoadOverrides_RoundTripsThroughStore()
        {
            Jump.ApplyBindingOverride(0, "<Keyboard>/enter");
            InputRebindService.SaveOverrides(_asset);
            Assert.IsTrue(UserSettingsService.Store.Has("InputRebinds/Controls"));

            // wipe the live override; the store still holds it
            Jump.RemoveBindingOverride(0);
            Assert.AreEqual("<Keyboard>/space", Jump.bindings[0].effectivePath);

            InputRebindService.LoadOverrides(_asset);
            Assert.AreEqual("<Keyboard>/enter", Jump.bindings[0].effectivePath);
        }

        [Test]
        public void ResetBinding_RestoresDefaultAndPersists()
        {
            Jump.ApplyBindingOverride(0, "<Keyboard>/enter");
            InputRebindService.SaveOverrides(_asset);

            InputRebindService.ResetBinding(Jump, 0, _asset);
            Assert.AreEqual("<Keyboard>/space", Jump.bindings[0].effectivePath);

            // the reset persisted: a fresh asset loading the saved state comes up at the default,
            // not the pre-reset override (mirrors a real next-launch)
            var fresh = ScriptableObject.CreateInstance<InputActionAsset>();
            fresh.name = "Controls";
            InputActionMap map = fresh.AddActionMap("Gameplay");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            InputRebindService.LoadOverrides(fresh);
            Assert.AreEqual("<Keyboard>/space", fresh.FindAction("Gameplay/Jump").bindings[0].effectivePath);
            Object.DestroyImmediate(fresh);
        }
    }
}
