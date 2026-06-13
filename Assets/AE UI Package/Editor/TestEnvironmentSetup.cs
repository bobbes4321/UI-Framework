using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Batch-mode helper: imports the TMP Essential Resources (fonts/settings TMP labels need)
    /// so play-mode tests can create TMP texts headlessly without the importer-window prompt.
    /// </summary>
    public static class TestEnvironmentSetup
    {
        public static void ImportTmpEssentials()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                Debug.Log("[AlterEyes.UI] TMP essentials already imported.");
                return;
            }

            string packageCache = Path.Combine(Directory.GetCurrentDirectory(), "Library", "PackageCache");
            string uguiFolder = Directory.GetDirectories(packageCache, "com.unity.ugui@*").FirstOrDefault();
            if (uguiFolder == null)
            {
                Debug.LogError("[AlterEyes.UI] com.unity.ugui not found in the package cache.");
                EditorApplication.Exit(1);
                return;
            }

            string packagePath = Path.Combine(uguiFolder, "Package Resources", "TMP Essential Resources.unitypackage");
            if (!File.Exists(packagePath))
            {
                Debug.LogError($"[AlterEyes.UI] TMP essentials package not found at {packagePath}");
                EditorApplication.Exit(1);
                return;
            }

            AssetDatabase.ImportPackage(packagePath, interactive: false);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[AlterEyes.UI] Imported TMP essentials: {AssetDatabase.IsValidFolder("Assets/TextMesh Pro")}");
        }
    }
}
