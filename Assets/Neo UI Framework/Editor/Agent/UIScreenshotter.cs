using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Renders a UI prefab (view, popup, starter widget — anything RectTransform-rooted) to a PNG
    /// without entering play mode, so an agent can look at its own generated UI and iterate.
    /// Rendering happens in an isolated preview scene: world-space canvas sized to the requested
    /// resolution, orthographic camera, theme background — the open scene is never touched.
    ///
    /// Entry points:
    /// - <see cref="Capture(GameObject,string,int,int)"/> from editor code/tests
    /// - menu "Tools → Neo UI → Screenshot Selected Prefab"
    /// - batch: <c>-executeMethod ... UIScreenshotter.CaptureFromCommandLine -neoPrefab &lt;assetPath&gt;
    ///   -neoOut &lt;png&gt; [-neoWidth 1080 -neoHeight 1920]</c>
    /// - with the editor open: the <see cref="AgentBridge"/> "screenshot" action.
    /// </summary>
    public static class UIScreenshotter
    {
        public const string DefaultOutputFolder = "Temp/neo-screenshots";

        /// <summary> Loads a prefab by asset path and captures it. Returns the written PNG path. </summary>
        public static string CapturePrefab(string assetPath, string outputPath = null,
            int width = 1080, int height = 1920)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) throw new ArgumentException($"No prefab at '{assetPath}'");
            return Capture(prefab, outputPath, width, height);
        }

        /// <summary> Renders the prefab to a PNG (default: Temp/neo-screenshots/&lt;name&gt;.png). </summary>
        public static string Capture(GameObject prefab, string outputPath = null,
            int width = 1080, int height = 1920)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            outputPath ??= $"{DefaultOutputFolder}/{prefab.name}.png";
            return RenderInScene(width, height, outputPath, (scene, canvas) =>
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(canvas, worldPositionStays: false);
                instance.SetActive(true);
                return instance;
            });
        }

        /// <summary>
        /// Renders an already-built, live GameObject (e.g. a view assembled in-memory from a spec,
        /// never saved to disk) to a PNG. The root is moved into the preview scene and destroyed with
        /// it — pass a throwaway. This is the in-memory path the spec preview / agent render loop uses
        /// so no prefab assets are committed just to look at a layout.
        /// </summary>
        public static string CaptureLive(GameObject root, string outputPath, int width = 1080, int height = 1920)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            return RenderInScene(width, height, outputPath, (scene, canvas) =>
            {
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetParent(canvas, worldPositionStays: false);
                root.SetActive(true);
                return root;
            });
        }

        private static string RenderInScene(int width, int height, string outputPath,
            Func<Scene, RectTransform, GameObject> produceContent)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException(
                    "UIScreenshotter needs a graphics device — run Unity without -nographics");

            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture renderTexture = null;
            Texture2D pixels = null;
            try
            {
                var cameraGo = new GameObject("NeoUI Screenshot Camera");
                SceneManager.MoveGameObjectToScene(cameraGo, scene);
                var camera = cameraGo.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.orthographicSize = height * 0.5f;
                camera.transform.position = new Vector3(0f, 0f, -100f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ThemeService.TryGetColor(UIWidgetFactory.TokenBackground, out Color background)
                    ? background
                    : new Color(0.07f, 0.08f, 0.1f);

                var canvasGo = new GameObject("NeoUI Screenshot Canvas", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvasGo, scene);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.sizeDelta = new Vector2(width, height);
                canvasRect.position = Vector3.zero;

                GameObject content = produceContent(scene, canvasRect);

                // containers start hidden via a ROOT CanvasGroup; the screenshot wants the shown
                // state. Only the root — child groups encode widget states (e.g. an unchecked
                // toggle's hidden checkmark) that must render as authored.
                var rootGroup = content.GetComponent<CanvasGroup>();
                if (rootGroup != null) rootGroup.alpha = 1f;

                // two passes + an explicit root rebuild: deeply nested layout chains (stack >
                // stack > grid) and TMP preferred sizes only settle after the first pass — one
                // ForceUpdateCanvases leaves outer groups laid out against stale child sizes
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)content.transform);
                Canvas.ForceUpdateCanvases();

                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;

                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(outputPath, pixels.EncodeToPNG());
                return outputPath;
            }
            finally
            {
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/Neo UI/Screenshot Selected Prefab", priority = 3)]
        public static void CaptureSelected()
        {
            foreach (GameObject prefab in Selection.gameObjects)
            {
                string path = Capture(prefab);
                Debug.Log($"[Neo.UI] Screenshot → {Path.GetFullPath(path)}", prefab);
            }
        }

        [MenuItem("Tools/Neo UI/Screenshot Selected Prefab", validate = true)]
        private static bool CaptureSelectedValidate() => Selection.gameObjects.Length > 0;

        // ------------------------------------------------------------------ batch mode

        /// <summary> -executeMethod entry: reads -neoPrefab/-neoOut/-neoWidth/-neoHeight. </summary>
        public static void CaptureFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string prefabPath = null, outPath = null;
            int width = 1080, height = 1920;
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-neoPrefab": prefabPath = args[i + 1]; break;
                    case "-neoOut": outPath = args[i + 1]; break;
                    case "-neoWidth": int.TryParse(args[i + 1], out width); break;
                    case "-neoHeight": int.TryParse(args[i + 1], out height); break;
                }
            }
            if (string.IsNullOrEmpty(prefabPath))
                throw new ArgumentException("Missing -neoPrefab <assetPath>");
            string written = CapturePrefab(prefabPath, outPath, width, height);
            Debug.Log($"[Neo.UI] Screenshot → {Path.GetFullPath(written)}");
        }
    }
}
