using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Creates the single settings asset (+ databases and default theme) on demand.
    /// Everything lives under the package's Resources folder so it is loadable at runtime.
    /// </summary>
    public static class AEUISettingsBootstrap
    {
        public const string PackageRoot = "Assets/AE UI Package";
        public const string ResourcesFolder = PackageRoot + "/Resources";
        public const string DatabasesFolder = ResourcesFolder + "/Databases";
        public const string SettingsAssetPath = ResourcesFolder + "/" + AEUISettings.ResourcesPath + ".asset";

        [MenuItem("Tools/AlterEyes UI/Create or Repair Settings", priority = 100)]
        public static AEUISettings EnsureSettings()
        {
            EnsureFolder(ResourcesFolder);
            EnsureFolder(DatabasesFolder);

            var settings = AssetDatabase.LoadAssetAtPath<AEUISettings>(SettingsAssetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<AEUISettings>();
                AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            }

            settings.viewIds = EnsureDatabase<ViewIdDatabase>(settings.viewIds, "ViewIdDatabase");
            settings.buttonIds = EnsureDatabase<ButtonIdDatabase>(settings.buttonIds, "ButtonIdDatabase");
            settings.toggleIds = EnsureDatabase<ToggleIdDatabase>(settings.toggleIds, "ToggleIdDatabase");
            settings.sliderIds = EnsureDatabase<SliderIdDatabase>(settings.sliderIds, "SliderIdDatabase");
            settings.tagIds = EnsureDatabase<TagIdDatabase>(settings.tagIds, "TagIdDatabase");
            settings.streamIds = EnsureDatabase<StreamIdDatabase>(settings.streamIds, "StreamIdDatabase");
            settings.panelIds = EnsureDatabase<PanelIdDatabase>(settings.panelIds, "PanelIdDatabase");
            settings.popupDatabase = EnsureDatabase<PopupDatabase>(settings.popupDatabase, "PopupDatabase");
            settings.animationPresets = EnsureDatabase<AnimationPresetDatabase>(settings.animationPresets, "AnimationPresetDatabase");

            if (settings.theme == null)
            {
                string themePath = ResourcesFolder + "/DefaultTheme.asset";
                var theme = AssetDatabase.LoadAssetAtPath<Theme>(themePath);
                if (theme == null)
                {
                    theme = ScriptableObject.CreateInstance<Theme>();
                    theme.SetToken("Primary", new Color(0.23f, 0.53f, 1f));
                    theme.SetToken("Background", new Color(0.08f, 0.13f, 0.24f));
                    theme.SetToken("Accent", new Color(1f, 0.72f, 0.2f));
                    theme.SetToken("TextDefault", Color.white);
                    AssetDatabase.CreateAsset(theme, themePath);
                }
                settings.theme = theme;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AEUISettings.instance = settings;
            return settings;
        }

        /// <summary> Settings, creating them if missing (used by editor tooling that needs them). </summary>
        public static AEUISettings GetOrCreateSettings()
        {
            AEUISettings settings = AEUISettings.instance;
            return settings != null ? settings : EnsureSettings();
        }

        private static T EnsureDatabase<T>(T current, string assetName) where T : ScriptableObject
        {
            if (current != null) return current;
            string path = $"{DatabasesFolder}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
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
