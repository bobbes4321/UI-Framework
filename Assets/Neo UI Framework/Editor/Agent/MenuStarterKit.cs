using System.Collections.Generic;
using System.IO;
using Neo.UI.Menus;
using UnityEditor;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Builds the default row/widget prefab library a <see cref="MenuPresenter"/> clones when it
    /// populates a menu at runtime (the dynamic / CBN-style path), and assigns it to
    /// <c>NeoUISettings.menuWidgets</c>. Author-time generation embeds the same factory widgets directly,
    /// so spec'd menus and runtime-built menus share one widget vocabulary.
    /// </summary>
    public static class MenuStarterKit
    {
        private const string Folder = "Assets/Neo UI Framework/Starter/Menus";

        [MenuItem("Tools/Neo UI/Create or Repair Menu Widget Library", priority = 22)]
        public static void CreateOrRepair()
        {
            MenuWidgetLibrary library = Build();
            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();
            settings.menuWidgets = library;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Neo.UI] Menu widget library ready at {Folder} and assigned to NeoUISettings.");
        }

        public static MenuWidgetLibrary Build()
        {
            EnsureFolder(Folder);
            var library = LoadOrCreate<MenuWidgetLibrary>($"{Folder}/MenuWidgetLibrary.asset");

            library.toggleRow = Row("ToggleRow", "Setting", UIWidgetFactory.CreateToggle(null, "Row", "Toggle", ""));
            library.switchRow = Row("SwitchRow", "Setting", UIWidgetFactory.CreateSwitch(null, "Row", "Switch"));
            library.sliderRow = Row("SliderRow", "Setting", UIWidgetFactory.CreateSlider(null, "Row", "Slider", 0f, 1f, 0.5f));
            library.stepperRow = Row("StepperRow", "Setting", UIWidgetFactory.CreateStepper(null, "Row", "Stepper", 0f, 10f, 0f, 1f));
            library.dropdownRow = Row("DropdownRow", "Setting",
                UIWidgetFactory.CreateDropdown(null, "Row", "Dropdown", new List<string> { "Option A", "Option B" }, 0));
            library.buttonRow = Save("ButtonRow", UIWidgetFactory.CreateButton(null, "Row", "Button", "Action",
                variant: UIWidgetFactory.VariantSecondary));
            library.labelRow = Save("LabelRow",
                UIWidgetFactory.CreateLabel(null, "Header", UIWidgetFactory.TokenTextMuted, 20f, name: "Header",
                    textStyle: UIWidgetFactory.TextStyleCaption).gameObject);
            library.keyRebindRow = Row("KeyRebindRow", "Action",
                UIWidgetFactory.CreateButton(null, "Row", "Rebind", "—", variant: UIWidgetFactory.VariantSecondary));
            library.categoryTab = Save("CategoryTab", UIWidgetFactory.CreateButton(null, "Row", "Tab", "Category",
                variant: UIWidgetFactory.VariantGhost));

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static GameObject Row(string prefabName, string label, GameObject control)
        {
            GameObject row = UIWidgetFactory.CreateMenuRow(null, prefabName, label, control);
            return Save(prefabName, row);
        }

        private static GameObject Save(string prefabName, GameObject root)
        {
            string path = $"{Folder}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
