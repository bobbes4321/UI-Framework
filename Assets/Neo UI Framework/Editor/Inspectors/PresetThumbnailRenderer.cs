using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Neo.UI.Editor
{
    /// <summary>
    /// Renders a single widget — a <see cref="NeoWidgetPreset"/> applied to its
    /// <see cref="NeoWidgetPreset.targetKind"/> (or a bare element kind) — to a small in-memory
    /// <see cref="Texture2D"/>, so a palette/picker can show a visual card instead of a text row (UX
    /// goal #2 of the widget-presets plan). It reuses the EXISTING in-memory render path
    /// (<c>UISpecPreview.BuildViews</c> builds the throwaway view; the world-space-canvas + orthographic
    /// camera + <c>ReadPixels</c> recipe is the same one <see cref="UIScreenshotter"/> and (while it
    /// lives) the Composer's <c>SpecPreviewPane</c> use) — no new rendering tech, no committed assets.
    /// <para>
    /// Headless-safe: returns <c>null</c> (never throws) when there is no graphics device or when a render
    /// faults, so callers fall back to a label. Every temp GameObject / RenderTexture / preview scene is
    /// destroyed. Callers must NOT render per <c>OnGUI</c> — go through <see cref="PresetThumbnailCache"/>.
    /// </para>
    /// </summary>
    public static class PresetThumbnailRenderer
    {
        /// <summary> Thumbnail edge for the palette card grid (px). </summary>
        public const int PaletteSize = 96;

        /// <summary> Thumbnail edge for the (larger) inspector preset picker (px). </summary>
        public const int PickerSize = 180;

        /// <summary>
        /// Canvas edge (canvas units = px) the widget lays out on before capture — generously larger
        /// than any widget's natural size so nothing is squeezed; the camera then zooms to fit the
        /// content bounds (see <see cref="Capture"/>), so the texture size only sets output resolution.
        /// </summary>
        private const int LayoutCanvasSize = 1024;

        /// <summary>
        /// Renders <paramref name="preset"/> applied to its target kind to a <paramref name="size"/>×
        /// <paramref name="size"/> texture. Returns null (graceful) when headless, when the preset is
        /// null/name-less, or when rendering throws.
        /// </summary>
        public static Texture2D Render(NeoWidgetPreset preset, int size)
        {
            if (preset == null) return null;
            ElementSpec element = BuildElement(preset.targetKind, preset.presetName, preset.presetName);
            if (element == null) return null;
            element.preset = preset.presetName;
            return RenderElement(element, size);
        }

        /// <summary>
        /// Renders a bare element <paramref name="kind"/> (no preset) to a square texture — the generic
        /// palette thumbnail. Returns null under the same graceful conditions as <see cref="Render(NeoWidgetPreset,int)"/>.
        /// </summary>
        public static Texture2D Render(string kind, int size)
        {
            ElementSpec element = BuildElement(kind, null, kind);
            if (element == null) return null;
            return RenderElement(element, size);
        }

        // ------------------------------------------------------------------ spec assembly

        /// <summary>
        /// A minimal one-element spec: a single view holding one element of <paramref name="kind"/>,
        /// centered with NO layout size so the factory's natural widget size stands (the capture camera
        /// zooms to fit — forcing a thumbnail-proportional box squeezed real widgets to a fraction of
        /// their designed width and wrapped their labels). Text-bearing kinds get <paramref name="label"/>
        /// so the card isn't blank.
        /// </summary>
        private static ElementSpec BuildElement(string kind, string presetName, string label)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            var element = new ElementSpec
            {
                kind = kind,
                preset = string.IsNullOrEmpty(presetName) ? null : presetName,
                layout = new LayoutSpec { h = "center", v = "center" }
            };
            if (WantsLabel(kind) && !string.IsNullOrEmpty(label)) element.label = label;
            return element;
        }

        private static bool WantsLabel(string kind)
        {
            switch (kind)
            {
                case "button":
                case "toggle":
                case "switch":
                case "tab":
                case "text":
                case "label":
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ render

        private static Texture2D RenderElement(ElementSpec element, int size)
        {
            if (size <= 0) return null;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;

            var view = new ViewSpec { category = "PresetThumb", viewName = "Thumb" };
            view.elements.Add(element);
            var spec = new UISpec();
            spec.views.Add(view);

            List<GameObject> roots = null;
            try
            {
                // existing in-memory build path: ensures factory tokens/text styles, commits no assets.
                roots = UISpecPreview.BuildViews(spec);
                GameObject root = (roots != null && roots.Count > 0) ? roots[0] : null;
                if (root == null) return null;
                return Capture(root, size);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Neo.UI] PresetThumbnailRenderer failed to render '{element.kind}': {e.Message}");
                return null;
            }
            finally
            {
                if (roots != null)
                    foreach (GameObject root in roots)
                        if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Renders an already-built root into an isolated preview scene and reads it back to a square,
        /// alpha-transparent Texture2D — the same world-space-canvas / orthographic-camera recipe as the
        /// shared screenshot path. The widget lays out at its NATURAL size on a large 1:1 canvas
        /// (<see cref="LayoutCanvasSize"/>) and the camera zooms to fit the settled content bounds plus a
        /// small margin — so a 240px-wide button scales down into a 116px thumbnail intact instead of
        /// being label-wrapped by a thumbnail-sized layout box. The root is moved into the throwaway
        /// scene; the scene + camera + RenderTexture are destroyed in finally.
        /// </summary>
        private static Texture2D Capture(GameObject root, int size)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture renderTexture = null;
            try
            {
                var cameraGo = new GameObject("NeoUI Preset Thumb Camera");
                SceneManager.MoveGameObjectToScene(cameraGo, scene);
                var camera = cameraGo.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.orthographicSize = size * 0.5f;
                camera.transform.position = new Vector3(0f, 0f, -100f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000f;
                // transparent clear — the card chrome (accent/hover/selected tint) is drawn underneath by
                // the picker's OnGUI, so the thumbnail composites over it instead of stamping an opaque
                // app-theme background color over that tint
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;

                var canvasGo = new GameObject("NeoUI Preset Thumb Canvas", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvasGo, scene);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                var canvasRect = (RectTransform)canvasGo.transform;
                // 1 canvas unit = 1 px, no device-scaler emulation — the widget lays out at natural
                // size on a canvas big enough that nothing is squeezed; the camera frames it below
                UIScreenshotter.ApplyDeviceScale(canvasRect, LayoutCanvasSize, LayoutCanvasSize, deviceScale: false);
                canvasRect.position = Vector3.zero;

                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetParent(canvasRect, worldPositionStays: false);
                root.SetActive(true);
                var rootGroup = root.GetComponent<CanvasGroup>();
                if (rootGroup != null) rootGroup.alpha = 1f;

                // two passes + a root rebuild: nested layout / TMP preferred sizes settle on the second
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
                Canvas.ForceUpdateCanvases();

                FrameContent(camera, canvasRect, (RectTransform)root.transform, size);

                renderTexture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                var pixels = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
                pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;
                return pixels;
            }
            finally
            {
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Centers the camera on the widget's settled bounds and sets the orthographic size so the
        /// content fills the square capture with a small margin. Bounds come from the view root's
        /// CHILDREN (the element subtree) — the view root itself stretches to the full layout canvas,
        /// so including it would frame the whole canvas and shrink the widget to a speck.
        /// </summary>
        private static void FrameContent(Camera camera, RectTransform canvasRect, RectTransform viewRoot, int size)
        {
            bool any = false;
            var bounds = new Bounds();
            for (int i = 0; i < viewRoot.childCount; i++)
            {
                if (!(viewRoot.GetChild(i) is RectTransform child) || !child.gameObject.activeInHierarchy) continue;
                Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, child);
                if (!any) { bounds = b; any = true; }
                else bounds.Encapsulate(b);
            }

            float extent = any ? Mathf.Max(bounds.extents.x, bounds.extents.y) : 0f;
            if (extent < 1f)
            {
                camera.orthographicSize = size * 0.5f; // degenerate content: keep the legacy full-canvas frame
                return;
            }

            // canvas is centered at the world origin at scale 1, so canvas-relative bounds are world units
            camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -100f);
            camera.orthographicSize = extent * 1.1f; // square target: half-width == half-height == ortho size
        }
    }
}
