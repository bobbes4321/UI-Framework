using Neo.UI.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Builds the playable showcase scene: <see cref="GeneratedSceneBuilder.Build"/> (camera, input,
    /// canvases, every generated view, flow controller) plus a <see cref="ShowcaseDirector"/> (the
    /// live HUD simulation) and the project-side <c>Game.UI.GameUIBindings</c> (the binding-guide
    /// worked example — domain signals, the typed Shop/Deals list, the coin economy).
    /// Batch entry point: <c>-executeMethod Neo.UI.Editor.ShowcaseSceneBuilder.BuildBatch</c>.
    /// </summary>
    public static class ShowcaseSceneBuilder
    {
        [MenuItem("Tools/Neo UI/Build Showcase Scene", priority = 52)]
        public static void BuildAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = Build();
            Debug.Log($"[Neo.UI] Showcase scene ready at {path} — press Play.");
        }

        /// <summary> The flow the showcase demonstrates — pins the build to the GameUI app so a second
        /// generated spec (e.g. ColorACube) sharing the generated folder can't leak its screens in. </summary>
        public const string ShowcaseFlow = "GameUI";

        /// <summary> Builds the generated-UI scene, injects the showcase director + binding example
        /// and re-saves. </summary>
        public static string Build()
        {
            string path = GeneratedSceneBuilder.Build(ShowcaseFlow); // leaves the new scene open
            new GameObject("Showcase Director", typeof(ShowcaseDirector)); // HUD output simulation

            // The shop economy / domain-signal / list wiring lives in Game.UI.GameUIBindings — the
            // worked example for Assets/docs/developer-binding-guide.md. It is the *developer's* own
            // code in Assembly-CSharp, which this package editor assembly can't reference by type, so
            // resolve + attach it reflectively. Skips cleanly if the stub hasn't been generated yet
            // (Tools ▸ Neo UI ▸ Generate Binding Stub).
            System.Type bindingsType = System.Type.GetType("Game.UI.GameUIBindings, Assembly-CSharp");
            if (bindingsType != null)
                new GameObject("Game UI Bindings", bindingsType);
            else
                Debug.Log("[Neo.UI] Showcase: Game.UI.GameUIBindings not found — generate the binding " +
                          "stub (Tools ▸ Neo UI ▸ Generate Binding Stub) to wire the shop economy.");

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
            return path;
        }

        /// <summary>
        /// Batch entry. Optional <c>-neoBundle &lt;name&gt;</c> applies a curated theme bundle and
        /// regenerates the demo spec first, so one run produces the fully themed playable scene.
        /// </summary>
        public static void BuildBatch()
        {
            bool ok = false;
            try
            {
                string bundleName = ReadArg("-neoBundle");
                if (!string.IsNullOrEmpty(bundleName))
                {
                    if (!ThemeBundles.TryGet(bundleName, out ThemeBundles.Bundle bundle))
                        throw new System.ArgumentException($"Unknown theme bundle '{bundleName}'");
                    var report = new GenerateReport();
                    ThemeBundles.Apply(bundle, NeoUISettingsBootstrap.GetOrCreateSettings(), report);
                    GenerateReport generation = UISpecGenerator.GenerateFromSpecFile(
                        System.IO.Path.GetFullPath("neo-demo-game-ui.json"));
                    if (generation.hasProblems)
                        throw new System.InvalidOperationException($"Demo generation failed:\n{generation}");
                    Debug.Log($"[Neo.UI] Applied bundle '{bundleName}' and regenerated the demo spec.");
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
