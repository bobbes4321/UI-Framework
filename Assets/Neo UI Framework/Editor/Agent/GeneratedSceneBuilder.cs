using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Builds a playable scene from whatever the spec generator produced: camera, input,
    /// EventSystem, a portrait canvas with every generated view instanced (hidden — the flow shows
    /// them), a popups canvas and a FlowController running the generated graph. The missing last
    /// mile between "agent generated a UI" and "press Play and use it".
    /// </summary>
    public static class GeneratedSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/NeoUIGeneratedDemo.unity";

        /// <summary> The spec pipeline's portrait reference width — the floor for view spacing. </summary>
        private const float PortraitReferenceWidth = 1080f;

        /// <summary>
        /// Breathing room (in canvas units) added to the canvas width when laying views out
        /// side-by-side in the editor, so adjacent full-screen views have a clear gap between them.
        /// </summary>
        private const float ViewLayoutGap = 200f;

        [MenuItem("Tools/Neo UI/Build Scene From Generated UI", priority = 51)]
        public static void BuildAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = Build();
            EditorSceneManager.OpenScene(path);
            Debug.Log($"[Neo.UI] Generated-UI scene ready at {path} — press Play.");
        }

        /// <summary>
        /// Builds and saves the scene; returns its path. Throws when nothing was generated yet.
        /// <para>
        /// <paramref name="flowName"/> selects which generated flow (= which app) to build when the
        /// shared <see cref="UISpecGenerator.GeneratedRoot"/> holds more than one. The scene then
        /// instances ONLY the views that flow references — never the whole folder — so a second spec's
        /// assets sitting alongside it can't leak in. When null and exactly one flow exists, that flow
        /// is used; when null and several exist, the build throws rather than silently picking one
        /// (matching the "no silent failures" / no-blind-glob invariant in CLAUDE.md).
        /// </para>
        /// </summary>
        public static string Build(string flowName = null)
        {
            string viewsFolder = $"{UISpecGenerator.GeneratedRoot}/Views";
            if (!AssetDatabase.IsValidFolder(viewsFolder)
                || AssetDatabase.FindAssets("t:Prefab", new[] { viewsFolder }).Length == 0)
                throw new System.InvalidOperationException(
                    $"No generated views under {viewsFolder} — run a spec through the generator first");

            NeoUISettings settings = NeoUISettingsBootstrap.GetOrCreateSettings();

            // resolve the flow BEFORE NewScene unloads assets (see LoadGeneratedFlowGraphs note); the
            // chosen flow defines the app, so it also drives which views we instance
            List<FlowGraph> flows = LoadGeneratedFlowGraphs();
            FlowGraph graph = SelectFlowGraph(flows, flowName);
            HashSet<string> wantedViews = graph != null ? CollectReferencedViewKeys(graph) : null;
            // Capture the path NOW: NewScene unloads the unused graph asset and fake-nulls `graph`, and
            // AssetDatabase.GetAssetPath on a fake-null object returns "" — so reading the path AFTER
            // NewScene (as it used to) silently dropped the flow and shipped a controller-less scene.
            string graphPath = graph != null ? AssetDatabase.GetAssetPath(graph) : null;

            List<GameObject> viewPrefabs = LoadGeneratedViewPrefabs(wantedViews);

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
            var laidOut = new List<RectTransform>();
            foreach (GameObject prefab in viewPrefabs)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(canvasRect, worldPositionStays: false);
                instances.Add(instance);

                // Spread the views out side-by-side in the editor so each one is visible at a glance
                // instead of every view stacking on top of each other at the origin (impossible to
                // inspect or tweak). useCustomStartPosition snaps each container back to
                // customStartPosition (0,0,0 — centred, full-screen) the instant Play begins
                // (UIContainer.Awake), so this is purely an authoring-time layout that costs nothing at
                // runtime. The override lives on the scene instance only — the generated prefabs stay
                // untouched so the export round-trip stays byte-identical. Positions are applied below,
                // once the canvas width is known.
                if (instance.GetComponent<UIContainer>() is { } container)
                {
                    container.useCustomStartPosition = true;
                    container.customStartPosition = Vector3.zero;
                    laidOut.Add((RectTransform)instance.transform);
                }
            }

            // Space the views by the ACTUAL canvas width so full-screen views never overlap, whatever
            // the game-view resolution. A fixed offset smaller than the rendered view width (the canvas
            // resolves much wider than the 1080 portrait reference on a non-portrait game view) leaves
            // them stacked. Force a layout pass so the canvas rect resolves; clamp to the reference
            // width so a not-yet-sized canvas (headless builds, no screen) still gets a sane spread.
            Canvas.ForceUpdateCanvases();
            float spacing = Mathf.Max(canvasRect.rect.width, PortraitReferenceWidth) + ViewLayoutGap;
            for (int i = 0; i < laidOut.Count; i++)
                laidOut[i].anchoredPosition3D = new Vector3(i * spacing, 0f, 0f);
            Debug.Log($"[Neo.UI] Laid out {laidOut.Count} views at {spacing:0} spacing " +
                      $"(canvas width {canvasRect.rect.width:0}).");

            // popups parent themselves to this canvas by name (settings.popupsCanvasName)
            CreateCanvas(settings.popupsCanvasName, sortingOrder: 100);

            // re-load the graph AFTER NewScene: scene creation unloads unused assets, which fake-nulls
            // the reference loaded earlier (cold batch sessions hit this reliably — the controller would
            // silently ship with an empty graph). Re-load from the path captured BEFORE NewScene.
            if (graphPath != null) graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(graphPath);

            string summary;
            if (graph != null)
            {
                var controllerGo = new GameObject("Flow Controller", typeof(FlowController));
                controllerGo.GetComponent<FlowController>().flow = graph;
                WarnAboutMissingViews(graph, wantedViews, instances);
                summary = $"flow '{graph.graphName}' ({instances.Count} views)";
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
                Debug.LogWarning($"[Neo.UI] {summary}");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Neo.UI] Built {ScenePath}: {summary}.");
            return ScenePath;
        }

        /// <summary>
        /// Picks the one flow graph to build. Null/empty <paramref name="flowName"/> with a single flow
        /// uses it; with several flows it throws (listing them) rather than picking blindly. A named
        /// flow that doesn't exist also throws with the available names.
        /// </summary>
        internal static FlowGraph SelectFlowGraph(List<FlowGraph> flows, string flowName)
        {
            if (flows.Count == 0) return null;

            if (!string.IsNullOrEmpty(flowName))
            {
                FlowGraph match = flows.Find(g => MatchesFlowName(g, flowName));
                if (match == null)
                    throw new System.InvalidOperationException(
                        $"No generated flow named '{flowName}' under {UISpecGenerator.GeneratedRoot}/Flow " +
                        $"(found: {string.Join(", ", flows.ConvertAll(FlowLabel))})");
                return match;
            }

            if (flows.Count == 1) return flows[0];

            throw new System.InvalidOperationException(
                $"{flows.Count} generated flows share {UISpecGenerator.GeneratedRoot}/Flow " +
                $"({string.Join(", ", flows.ConvertAll(FlowLabel))}) — pass a flow name " +
                "(scene builder: Build(name); agent: {\"action\":\"buildScene\",\"flow\":\"…\"}) " +
                "so the scene isn't built from an arbitrary one.");
        }

        private static bool MatchesFlowName(FlowGraph graph, string flowName) =>
            string.Equals(graph.graphName, flowName, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(graph.name, flowName, System.StringComparison.OrdinalIgnoreCase);

        private static string FlowLabel(FlowGraph g) =>
            string.IsNullOrEmpty(g.graphName) || g.graphName == g.name ? $"'{g.name}'" : $"'{g.graphName}'";

        /// <summary>
        /// The set of "Category/Name" view keys a flow actually drives — every <see cref="UINode"/>'s
        /// shown and hidden views. This is what makes the build flow-scoped: views belonging to other
        /// specs in the shared folder are simply never instanced.
        /// </summary>
        internal static HashSet<string> CollectReferencedViewKeys(FlowGraph graph)
        {
            var keys = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (FlowNode node in graph.nodes)
            {
                if (!(node is UINode ui)) continue;
                foreach (UINode.ViewRef v in ui.showViews) keys.Add(ViewKey(v.category, v.viewName));
                foreach (UINode.ViewRef v in ui.hideViews) keys.Add(ViewKey(v.category, v.viewName));
            }
            return keys;
        }

        private static string ViewKey(string category, string name) =>
            $"{(string.IsNullOrWhiteSpace(category) ? CategoryNameId.DefaultCategory : category.Trim())}/" +
            $"{(string.IsNullOrWhiteSpace(name) ? CategoryNameId.DefaultName : name.Trim())}";

        /// <summary>
        /// Views the flow references but no generated prefab provided — a real authoring gap, surfaced
        /// loudly instead of producing a scene where some navigation targets silently do nothing.
        /// </summary>
        private static void WarnAboutMissingViews(FlowGraph graph, HashSet<string> wanted, List<GameObject> instances)
        {
            var present = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (GameObject go in instances)
            {
                UIView view = go.GetComponent<UIView>();
                if (view != null) present.Add(ViewKey(view.id.Category, view.id.Name));
            }
            foreach (string key in wanted)
                if (!present.Contains(key))
                    Debug.LogWarning(
                        $"[Neo.UI] Flow '{graph.graphName}' references view '{key}' but no generated prefab " +
                        $"exists under {UISpecGenerator.GeneratedRoot}/Views — that navigation target will do nothing.");
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

        /// <summary>
        /// Generated view prefabs to instance. When <paramref name="wanted"/> is non-null only the
        /// views whose <see cref="UIView.id"/> is in that set are loaded (flow-scoped); when null every
        /// generated view is loaded (the no-flow single-screen fallback).
        /// </summary>
        private static List<GameObject> LoadGeneratedViewPrefabs(HashSet<string> wanted)
        {
            var prefabs = new List<GameObject>();
            string folder = $"{UISpecGenerator.GeneratedRoot}/Views";
            if (!AssetDatabase.IsValidFolder(folder)) return prefabs;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null || prefab.GetComponent<GeneratedMarker>() == null) continue;
                UIView view = prefab.GetComponent<UIView>();
                if (view == null) continue;
                if (wanted != null && !wanted.Contains(ViewKey(view.id.Category, view.id.Name))) continue;
                prefabs.Add(prefab);
            }
            return prefabs;
        }

        private static List<FlowGraph> LoadGeneratedFlowGraphs()
        {
            var graphs = new List<FlowGraph>();
            string folder = $"{UISpecGenerator.GeneratedRoot}/Flow";
            // "t:FlowGraph" rides the scripted-type search index, which can lag freshly created
            // assets in cold batch sessions — enumerate the folder on disk instead (anchored on
            // dataPath: Directory APIs resolve against the process CWD, which batch runs don't
            // guarantee to be the project root)
            string absoluteFolder = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", folder);
            if (!System.IO.Directory.Exists(absoluteFolder))
            {
                Debug.Log($"[Neo.UI] LoadGeneratedFlowGraphs: no '{absoluteFolder}' on disk");
                return graphs;
            }
            foreach (string file in System.IO.Directory.GetFiles(absoluteFolder, "*.asset"))
            {
                string assetPath = $"{folder}/{System.IO.Path.GetFileName(file)}";
                var graph = AssetDatabase.LoadAssetAtPath<FlowGraph>(assetPath);
                Debug.Log($"[Neo.UI] LoadGeneratedFlowGraphs: '{assetPath}' → " +
                          $"{(graph != null ? "FlowGraph" : $"null ({AssetDatabase.GetMainAssetTypeAtPath(assetPath)})")}");
                if (graph != null) graphs.Add(graph);
            }
            return graphs;
        }
    }
}
