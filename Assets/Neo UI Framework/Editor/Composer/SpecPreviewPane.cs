using System;
using Neo.EditorUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Neo.UI.Editor.Composer
{
    /// <summary>
    /// Center pane: a live, in-memory render of the selected view. It reuses the same render path the
    /// agent <c>preview</c> action uses (<see cref="UISpecPreview.BuildViews"/> /
    /// <c>UISpecGenerator.BuildViewGameObject</c>) — building the view's prefab subtree in a throwaway
    /// preview scene, rendering it to a texture, then DESTROYING every temp object. NOTHING is written
    /// to <c>GeneratedRoot</c>; an unsaved session can't touch committed assets.
    ///
    /// <para>Rebuilds are debounced (150 ms) so dragging a slider doesn't thrash the renderer, and the
    /// texture/temp objects from the previous build are released every rebuild (no leaks).</para>
    ///
    /// <para>Renders under the CURRENT project theme (matching the preview action) — live theme-token
    /// recolor is a later tier; a token edit shows after the next Save/regenerate.</para>
    /// </summary>
    public class SpecPreviewPane
    {
        private const double DebounceSeconds = 0.15;

        private readonly SpecDocument _document;
        private readonly Action _repaint;

        private ViewSpec _target;
        private string _highlightName;             // id/label of the element to outline (best effort)
        private (string name, int width, int height) _resolution = UISpecPreview.DefaultResolutions[0];
        private string _variant;

        private Texture2D _texture;
        private Rect? _highlightNorm;              // selected element rect in 0..1 canvas space
        private double _rebuildAt = -1;
        private bool _pending;
        private string _error;

        // panel/group preview: a view with tabs+panels (or a settings/cheats menu with groups) bakes
        // only ONE panel visible at a time. These let the user flip through them in the preview
        // without play mode — the chosen panel is forced visible at render, the rest hidden.
        private readonly System.Collections.Generic.List<string> _panelNames = new System.Collections.Generic.List<string>();
        private string _activePanel;

        public SpecPreviewPane(SpecDocument document, Action repaint)
        {
            _document = document;
            _repaint = repaint;
        }

        /// <summary> The view + element to show. Triggers a debounced rebuild when the view changes. </summary>
        public void SetTarget(ViewSpec view, ElementSpec highlight)
        {
            _highlightName = highlight != null
                ? (!string.IsNullOrEmpty(highlight.id) ? highlight.id : highlight.label)
                : null;
            if (!ReferenceEquals(view, _target))
            {
                _target = view;
                _activePanel = null; // a different view has a different set of panels/groups
                RequestRebuild();
            }
        }

        public void RequestRebuild()
        {
            _rebuildAt = EditorApplication.timeSinceStartup + DebounceSeconds;
            _pending = true;
        }

        /// <summary> Called from the window's editor-update tick — fires the debounced rebuild. </summary>
        public void Update()
        {
            if (_pending && EditorApplication.timeSinceStartup >= _rebuildAt)
            {
                _pending = false;
                Rebuild();
                _repaint?.Invoke();
            }
        }

        // ------------------------------------------------------------------ draw

        public void OnGUI(Rect rect)
        {
            DrawToolbar(new Rect(rect.x, rect.y, rect.width, 20f));
            var canvasRect = new Rect(rect.x, rect.y + 22f, rect.width, rect.height - 22f);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(canvasRect, new Color(0.12f, 0.12f, 0.13f));

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.Label(canvasRect, _error, NeoStyles.PopupSearchHint);
                return;
            }
            if (_texture == null)
            {
                GUI.Label(canvasRect, "Select a view to preview", NeoStyles.PopupSearchHint);
                return;
            }

            // letterbox the texture into the pane preserving aspect
            float scale = Mathf.Min(canvasRect.width / _texture.width, canvasRect.height / _texture.height);
            float drawW = _texture.width * scale, drawH = _texture.height * scale;
            var drawRect = new Rect(canvasRect.x + (canvasRect.width - drawW) * 0.5f,
                canvasRect.y + (canvasRect.height - drawH) * 0.5f, drawW, drawH);
            GUI.DrawTexture(drawRect, _texture, ScaleMode.ScaleToFit, false);

            if (_highlightNorm.HasValue)
            {
                Rect n = _highlightNorm.Value;
                // n is in 0..1 with y from bottom — flip to GUI space
                var hl = new Rect(drawRect.x + n.x * drawW,
                    drawRect.y + (1f - n.y - n.height) * drawH, n.width * drawW, n.height * drawH);
                DrawOutline(hl, NeoColors.Interactive);
            }
        }

        private void DrawToolbar(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.toolbar);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect resRect = GUILayoutUtility.GetRect(140f, 18f);
                NeoDropdown.ValuePopup(resRect, _resolution.name, ResolutionNames, name =>
                {
                    foreach (var res in UISpecPreview.DefaultResolutions)
                        if (res.name == name) { _resolution = res; RequestRebuild(); break; }
                }, "resolution");

                Rect varRect = GUILayoutUtility.GetRect(120f, 18f);
                NeoDropdown.ValuePopup(varRect, _variant, VariantNames, v =>
                {
                    _variant = string.IsNullOrEmpty(v) ? null : v;
                    RequestRebuild();
                }, "default variant");

                // group/tab selector — only when the view has more than one panel to flip between
                if (_panelNames.Count > 1)
                {
                    Rect panelRect = GUILayoutUtility.GetRect(140f, 18f);
                    NeoDropdown.ValuePopup(panelRect, _activePanel, () => new System.Collections.Generic.List<string>(_panelNames),
                        name => { _activePanel = name; RequestRebuild(); }, "tab / group");
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) Rebuild();
            }
            GUILayout.EndArea();
        }

        private static System.Collections.Generic.List<string> ResolutionNames()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var res in UISpecPreview.DefaultResolutions) list.Add(res.name);
            return list;
        }

        private System.Collections.Generic.List<string> VariantNames()
        {
            var list = new System.Collections.Generic.List<string>();
            NeoUISettings settings = NeoUISettings.instance;
            if (settings?.theme != null)
                foreach (Theme.ThemeVariant v in settings.theme.Variants) list.Add(v.name);
            return list;
        }

        private static void DrawOutline(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        // ------------------------------------------------------------------ render

        private void Rebuild()
        {
            Release();
            _error = null;
            _highlightNorm = null;
            if (_target == null) return;

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                _error = "Preview needs a graphics device (this editor was started with -nographics).";
                return;
            }

            // build just the selected view in-memory, under the current project theme
            var temp = new UISpec();
            temp.theme = _document.Spec.theme;
            temp.presets = _document.Spec.presets;
            temp.views.Add(_target);

            string previousVariant = null;
            NeoUISettings settings = NeoUISettings.instance;
            if (!string.IsNullOrEmpty(_variant) && settings?.theme != null)
            {
                previousVariant = settings.theme.ActiveVariantName;
                ThemeService.SetVariant(_variant);
            }

            System.Collections.Generic.List<GameObject> roots = null;
            try
            {
                roots = UISpecPreview.BuildViews(temp);
                if (roots.Count > 0)
                    RenderRoot(roots[0], _resolution.width, _resolution.height);
            }
            catch (Exception e)
            {
                _error = "Preview failed: " + e.Message;
            }
            finally
            {
                if (roots != null)
                    foreach (GameObject root in roots)
                        if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (previousVariant != null) ThemeService.SetVariant(previousVariant);
            }
        }

        private void RenderRoot(GameObject root, int width, int height)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture renderTexture = null;
            try
            {
                var cameraGo = new GameObject("NeoComposer Preview Camera");
                SceneManager.MoveGameObjectToScene(cameraGo, scene);
                var camera = cameraGo.AddComponent<Camera>();
                camera.scene = scene;
                camera.orthographic = true;
                camera.orthographicSize = height * 0.5f;
                camera.transform.position = new Vector3(0f, 0f, -100f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 1000f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ThemeService.TryGetColor(UIWidgetFactory.TokenBackground, out Color bg)
                    ? bg : new Color(0.07f, 0.08f, 0.1f);

                var canvasGo = new GameObject("NeoComposer Preview Canvas", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(canvasGo, scene);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.sizeDelta = new Vector2(width, height);
                canvasRect.position = Vector3.zero;

                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.SetParent(canvasRect, worldPositionStays: false);
                root.SetActive(true);

                var rootGroup = root.GetComponent<CanvasGroup>();
                if (rootGroup != null) rootGroup.alpha = 1f;

                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
                Canvas.ForceUpdateCanvases();

                ApplyPanelSelection(root);
                ComputeHighlight(root, width, height);

                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = renderTexture;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = renderTexture;
                _texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                _texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                _texture.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;
            }
            finally
            {
                if (renderTexture != null) { renderTexture.Release(); UnityEngine.Object.DestroyImmediate(renderTexture); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Records the panels found in the built view and, when the user has picked one in the
        /// toolbar, forces that panel visible and the rest hidden (via their CanvasGroups) so a menu's
        /// other groups / a tab layout's other panels can be inspected without entering play mode. With
        /// no pick, the authored start-panel visibility is left untouched.
        /// </summary>
        private void ApplyPanelSelection(GameObject root)
        {
            _panelNames.Clear();
            UIPanel[] panels = root.GetComponentsInChildren<UIPanel>(includeInactive: true);
            foreach (UIPanel panel in panels)
                if (!_panelNames.Contains(panel.gameObject.name))
                    _panelNames.Add(panel.gameObject.name);

            if (string.IsNullOrEmpty(_activePanel) || !_panelNames.Contains(_activePanel)) return;

            foreach (UIPanel panel in panels)
            {
                var group = panel.GetComponent<CanvasGroup>();
                if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
                bool active = panel.gameObject.name == _activePanel;
                group.alpha = active ? 1f : 0f;
                group.blocksRaycasts = active;
                panel.gameObject.SetActive(true);
            }
        }

        /// <summary> Best-effort selection outline: find the built GameObject whose name matches the
        /// selected element's id/label and record its rect in normalized canvas space. Skipped (no
        /// overlay) when it can't be resolved — precise element→object mapping is a later tier. </summary>
        private void ComputeHighlight(GameObject root, int width, int height)
        {
            if (string.IsNullOrEmpty(_highlightName)) return;
            Transform match = FindByName(root.transform, _highlightName);
            if (match == null || !(match is RectTransform rect)) return;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            // canvas is centered at origin, 1 world unit = 1 px; map to 0..1 (y from bottom)
            float minX = Mathf.Min(corners[0].x, corners[2].x), maxX = Mathf.Max(corners[0].x, corners[2].x);
            float minY = Mathf.Min(corners[0].y, corners[2].y), maxY = Mathf.Max(corners[0].y, corners[2].y);
            float nx = (minX + width * 0.5f) / width;
            float ny = (minY + height * 0.5f) / height;
            float nw = (maxX - minX) / width;
            float nh = (maxY - minY) / height;
            _highlightNorm = new Rect(Mathf.Clamp01(nx), Mathf.Clamp01(ny), Mathf.Clamp01(nw), Mathf.Clamp01(nh));
        }

        private static Transform FindByName(Transform node, string name)
        {
            if (string.Equals(node.name, name, StringComparison.Ordinal)) return node;
            // generators sometimes suffix names; also accept a contains match on the leaf
            for (int i = 0; i < node.childCount; i++)
            {
                Transform found = FindByName(node.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void Release()
        {
            if (_texture != null) { UnityEngine.Object.DestroyImmediate(_texture); _texture = null; }
        }

        public void Dispose() => Release();
    }
}
