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
        /// centered with a small fixed layout box so it reads inside a square thumbnail. Text-bearing
        /// kinds get <paramref name="label"/> so the card isn't blank.
        /// </summary>
        private static ElementSpec BuildElement(string kind, string presetName, string label)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            var element = new ElementSpec
            {
                kind = kind,
                preset = string.IsNullOrEmpty(presetName) ? null : presetName,
                layout = new LayoutSpec
                {
                    h = "center",
                    v = "center",
                    size = new LayoutSize { w = 220f, h = 96f }
                }
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
        /// Renders an already-built root into an isolated preview scene and reads it back to a square
        /// Texture2D — the same world-space-canvas / orthographic-camera recipe as the shared screenshot
        /// path, scaled like a device so the widget reads proportionally inside the thumbnail. The root is
        /// moved into the throwaway scene; the scene + camera + RenderTexture are destroyed in finally.
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
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ThemeService.TryGetColor(UIWidgetFactory.TokenBackground, out Color bg)
                    ? bg
                    : new Color(0.07f, 0.08f, 0.1f);

                var canvasGo = new GameObject("NeoUI Preset Thumb Canvas", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvasGo, scene);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                var canvasRect = (RectTransform)canvasGo.transform;
                // scale like a device so the fixed-size widget reads proportionally at any thumb edge
                UIScreenshotter.ApplyDeviceScale(canvasRect, size, size, deviceScale: true);
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
    }
}
