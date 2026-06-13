using AlterEyes.UI.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Builds the playable showcase scene: <see cref="GeneratedSceneBuilder.Build"/> (camera, input,
    /// canvases, every generated view, flow controller) plus a <see cref="ShowcaseDirector"/> so the
    /// HUD simulates a live game and the shop's bound list streams rows from UIData.
    /// Batch entry point: <c>-executeMethod AlterEyes.UI.Editor.ShowcaseSceneBuilder.BuildBatch</c>.
    /// </summary>
    public static class ShowcaseSceneBuilder
    {
        [MenuItem("Tools/AlterEyes UI/Build Showcase Scene", priority = 52)]
        public static void BuildAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = Build();
            Debug.Log($"[AlterEyes.UI] Showcase scene ready at {path} — press Play.");
        }

        /// <summary> Builds the generated-UI scene, injects the showcase director and re-saves. </summary>
        public static string Build()
        {
            string path = GeneratedSceneBuilder.Build(); // leaves the new scene open
            var directorGo = new GameObject("Showcase Director", typeof(ShowcaseDirector));
            _ = directorGo;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
            return path;
        }

        /// <summary>
        /// Batch entry. Optional <c>-aeuiBundle &lt;name&gt;</c> applies a curated theme bundle and
        /// regenerates the demo spec first, so one run produces the fully themed playable scene.
        /// </summary>
        public static void BuildBatch()
        {
            bool ok = false;
            try
            {
                string bundleName = ReadArg("-aeuiBundle");
                if (!string.IsNullOrEmpty(bundleName))
                {
                    if (!ThemeBundles.TryGet(bundleName, out ThemeBundles.Bundle bundle))
                        throw new System.ArgumentException($"Unknown theme bundle '{bundleName}'");
                    var report = new GenerateReport();
                    ThemeBundles.Apply(bundle, AEUISettingsBootstrap.GetOrCreateSettings(), report);
                    GenerateReport generation = UISpecGenerator.GenerateFromSpecFile(
                        System.IO.Path.GetFullPath("aeui-demo-game-ui.json"));
                    if (generation.hasProblems)
                        throw new System.InvalidOperationException($"Demo generation failed:\n{generation}");
                    Debug.Log($"[AlterEyes.UI] Applied bundle '{bundleName}' and regenerated the demo spec.");
                }
                Build();
                ok = true;
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        private static string ReadArg(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
    }
}
