using System;
using Neo.UI.Editor;
using Neo.UI.Menus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Tests
{
    /// <summary>
    /// Smoke coverage for <see cref="MenuCatalogInspector"/> — the native replacement for the doomed
    /// Composer <c>MenuCatalogEditor</c> pane (Wave 2 Task 2.4). Doesn't drive IMGUI (the inspector's
    /// draw callbacks need a live layout pass); instead it exercises the exact
    /// <see cref="SerializedObject"/> operations the inspector's list-add and kind-picker paths perform
    /// (<see cref="ReorderableList"/> element insert, enum write by path) and asserts the edits persist
    /// on the underlying catalog asset.
    /// </summary>
    public class MenuCatalogInspectorTests
    {
        private SettingsCatalog _settings;
        private CheatCatalog _cheats;
        private UnityEditor.Editor _editor;

        [TearDown]
        public void TearDown()
        {
            if (_editor != null) UnityEngine.Object.DestroyImmediate(_editor);
            if (_settings != null) UnityEngine.Object.DestroyImmediate(_settings);
            if (_cheats != null) UnityEngine.Object.DestroyImmediate(_cheats);
        }

        [Test]
        public void CreateEditor_ResolvesForSettingsCatalog()
        {
            _settings = ScriptableObject.CreateInstance<SettingsCatalog>();
            _editor = UnityEditor.Editor.CreateEditor(_settings, typeof(MenuCatalogInspector));
            Assert.IsInstanceOf<MenuCatalogInspector>(_editor, "the [CustomEditor(typeof(MenuCatalog), true)] " +
                "attribute must resolve for the SettingsCatalog subtype");
        }

        [Test]
        public void CreateEditor_ResolvesForCheatCatalog()
        {
            _cheats = ScriptableObject.CreateInstance<CheatCatalog>();
            _editor = UnityEditor.Editor.CreateEditor(_cheats, typeof(MenuCatalogInspector));
            Assert.IsInstanceOf<MenuCatalogInspector>(_editor, "the [CustomEditor(typeof(MenuCatalog), true)] " +
                "attribute must resolve for the CheatCatalog subtype");
        }

        [Test]
        public void AddItem_ThenChangeKind_PersistsOnTheAsset()
        {
            _settings = ScriptableObject.CreateInstance<SettingsCatalog>();
            _editor = UnityEditor.Editor.CreateEditor(_settings, typeof(MenuCatalogInspector));
            SerializedObject so = _editor.serializedObject;

            // Mirrors the ReorderableList's default "+" behavior (NeoListView.Get's items list).
            SerializedProperty items = so.FindProperty("items");
            int index = items.arraySize;
            items.InsertArrayElementAtIndex(index);
            SerializedProperty added = items.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("category").stringValue = "Audio";
            added.FindPropertyRelative("name").stringValue = "Master";
            so.ApplyModifiedProperties();

            Assert.AreEqual(1, _settings.items.Count, "new item must persist on the target asset");
            Assert.AreEqual("Audio", _settings.items[0].category);
            Assert.AreEqual("Master", _settings.items[0].name);
            Assert.AreEqual(MenuControlKind.Label, _settings.items[0].kind, "default kind is Label (enum index 0, isn't picked yet)");

            // Mirrors MenuCatalogInspector.DrawKindPopup's write path: resolve the option name through
            // MenuItemSpec.Kinds (declaration order matches MenuControlKind 1:1) then write by path.
            string[] kinds = MenuItemSpec.Kinds;
            int sliderIndex = Array.IndexOf(kinds, "slider");
            Assert.GreaterOrEqual(sliderIndex, 0, "MenuItemSpec.Kinds must contain 'slider'");

            so.Update();
            SerializedProperty kindProperty = so.FindProperty("items").GetArrayElementAtIndex(0).FindPropertyRelative("kind");
            kindProperty.enumValueIndex = sliderIndex;
            so.ApplyModifiedProperties();

            Assert.AreEqual(MenuControlKind.Slider, _settings.items[0].kind, "kind change must persist on the target asset");
        }

        [Test]
        public void MenuItemSpecKinds_MatchesMenuControlKindDeclarationOrder()
        {
            // MenuCatalogInspector's kind popup indexes MenuControlKind by position in MenuItemSpec.Kinds
            // — if the two ever drift, the popup would silently write the wrong enum value.
            var expected = new[] { "label", "button", "toggle", "switch", "slider", "stepper", "dropdown", "rebind" };
            CollectionAssert.AreEqual(expected, MenuItemSpec.Kinds);
            Assert.AreEqual(expected.Length, Enum.GetValues(typeof(MenuControlKind)).Length,
                "MenuItemSpec.Kinds and MenuControlKind must stay the same length");
        }

        [Test]
        public void FavouritesToggle_OnlyBackedByCheatCatalog()
        {
            _cheats = ScriptableObject.CreateInstance<CheatCatalog>();
            _editor = UnityEditor.Editor.CreateEditor(_cheats, typeof(MenuCatalogInspector));
            SerializedProperty favourites = _editor.serializedObject.FindProperty("favouritesEnabled");
            Assert.IsNotNull(favourites, "CheatCatalog must expose favouritesEnabled for the inspector's toggle");

            favourites.boolValue = false;
            _editor.serializedObject.ApplyModifiedProperties();
            Assert.IsFalse(_cheats.favouritesEnabled);
        }
    }
}
