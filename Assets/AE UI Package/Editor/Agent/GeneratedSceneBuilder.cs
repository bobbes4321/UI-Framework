using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlterEyes.UI.Editor
{
    /// <summary>
    /// Builds a playable scene from whatever the spec generator produced: camera, input,
    /// EventSystem, a portrait canvas with every generated view instanced (hidden — the flow shows
    /// them), a popups canvas and a FlowController running the generated graph. The missing last
    /// mile between "agent generated a UI" and "press Play and use it".
    /// </summary>
    public static class GeneratedSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/AEUIGeneratedDemo.unity";

        [MenuItem("Tools/AlterEyes UI/Build Scene From Generated UI", priority = 51)]
        public static void BuildAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = Build();
            EditorSceneManager.OpenScene(path);
            Debug.Log($"[AlterEyes.UI] Generated-UI scene ready at {path} — press Play.");
        }

        /// <summary> Builds and saves the scene; returns its path. Throws when nothing was generated yet. </summary>
        public static string Build()
        {
            List<GameObject> viewPrefabs = LoadGeneratedViewPrefabs();
            if (viewPrefabs.Count == 0)
                throw new System.InvalidOperationException(
                    $"No generated views under {UISpecGenerator.GeneratedRoot}/Views — run a spec through the generator first");

            AEUISettings settings = AEUISettingsBootstrap.GetOrCreateSettings();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            _ = eventSystemGo;
            var backInputGo = new GameObject("Back Button Input (Esc)", typeof(BackButtonInput));
            _ = backInputGo;

            RectTransform canvasRect = CreateCanvas("Canvas", sortingOrder: 0);

            var instances = new List<GameObject>();
            foreach (GameObject prefab in viewPrefabs)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(canvasRect, worldPositionStays: false);
                instances.Add(instance);
            }

            // popups parent themselves to this canvas by name (settings.popupsCanvasName)
            CreateCanvas(settings.popupsCanvasName, sortingOrder: 100);

            // load AFTER NewScene: scene creation unloads unused assets, which fake-nulls a
            // ScriptableObject loaded earlier (cold batch sessions hit this reliably — the
            // controller would silently ship with an empty graph)
            FlowGraph graph = LoadGeneratedFlowGraph();

            string summary;
            if (graph != null)
            {
                var controllerGo = new GameObject("Flow Controller", typeof(FlowController));
                controllerGo.GetComponent<FlowController>().flow = graph;
                summary = $"flow '{graph.name}'";
            }
            else
            {
                // No flow (e.g. a single-screen recipe). Views default to InstantHide and nothing
                // would show them, so reveal the first view directly — the scene is playable instead
                // of blank. Author a `flow` section when you want navigation between views.
                GameObject shown = ShowFirstView(instances);
                summary = shown != null
                    ? $"no flow — showing view '{shown.name}' directly (add a \"flow\" section for navigation)"
                    : "no flow and no views to show";
                Debug.LogWarning($"[AlterEyes.UI] {summary}");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[AlterEyes.UI] Built {ScenePath}: {viewPrefabs.Count} views, {summary}.");
            return ScenePath;
        }

        private static RectTransform CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f); // the spec pipeline's portrait default
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// Reveals the first view (by name, for determinism) so a flow-less scene isn't blank —
        /// overrides the instance's baked InstantHide start behaviour. Returns the shown instance.
        /// </summary>
        private static GameObject ShowFirstView(List<GameObject> instances)
        {
            GameObject first = null;
            foreach (GameObject instance in instances)
            {
                if (instance.GetComponent<UIView>() == null) continue;
                if (first == null || string.CompareOrdinal(instance.name, first.name) < 0) first = instance;
            }
            if (first != null)
                first.GetComponent<UIView>().onStartBehaviour = ContainerStartBehaviour.InstantShow;
            return first;
        }

        private static List<GameObject> LoadGeneratedViewPrefabs()
        {
            var prefabs = new List<GameObject>();
            string folder = $"{UISpecGenerator.GeneratedRoot}/Views";
            if (!AssetDatabase.IsValidFolder(folder)) return prefabs;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab != null && prefab.GetComponent<UIView>() != null
                    && prefab.GetComponent<GeneratedMarker>() != null)
                    prefabs.Add(prefab);
            }
            return prefabs;
        }

        private static FlowGraph LoadGeneratedFlowGraph()
        {
            string folder = $"{UISpecGenerator.GeneratedRoot}/Flow";
            // "t:FlowGraph" rides the scripted-type search index, which can lag freshly created
            // assets in cold batch sessions — enumerate the folder on disk instead (anchored on
            // dataPath: Directory APIs resolve against the process CWD, which batch runs don't
            // guarantee to be the project root)
            string absoluteFolder = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", folder);
            if (!System.IO.Directory.Exists(absoluteFolder))
            {
                Debug.Log($"[AlterEyes.UI] LoadGeneratedFlowGraph: no '{absoluteFolder}' on disk");
                return null;
            }
            foreach (string file in System.IO.Directory.GetFiles(absoluteFolder, "*.asset"))
            {
                string assetPath = $"{folder}/{System.IO.Path.GetFileName(file)}";
                var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(assetPath);
                Debug.Log($"[AlterEyes.UI] LoadGeneratedFlowGraph: '{assetPath}' → " +
                          $"{(graph != null ? "FlowGraph" : $"null ({AssetDatabase.GetMainAssetTypeAtPath(assetPath)})")}");
                if (graph != null) return graph;
            }
            return null;
        }
    }
}
