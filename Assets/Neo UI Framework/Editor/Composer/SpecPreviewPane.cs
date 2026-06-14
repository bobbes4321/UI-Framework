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

        /// <summary>
        /// Editor-only viewport model: the device the preview renders at, plus the free-resize / rotate /
        /// zoom chrome state. PURELY editor chrome — none of this touches the spec or the generated
        /// prefab; it only changes how the (unchanged) view is rendered and framed.
        /// </summary>
        private sealed class ViewportState
        {
            public int width = 1080;       // current device px (the canvas render size)
            public int height = 1920;
            public bool freeMode;          // "Responsive": drag handles drive width/height directly
            public string presetId;        // selected preset id (null in free mode)
            public bool landscape;         // rotate toggle — swaps w/h on apply
            public float zoom = 1f;        // 1 = fit; user can pin a zoom
            public bool fitToWindow = true;// auto-zoom to the pane

            public void ApplyPreset(DevicePreset preset)
            {
                presetId = preset.id;
                freeMode = false;
                // honor the current rotate toggle when applying a (portrait) preset
                width = landscape ? preset.height : preset.width;
                height = landscape ? preset.width : preset.height;
            }
        }

        private readonly SpecDocument _document;
        private readonly Action _repaint;
        private readonly ComposerCanvas _canvas;   // Tier 3: direct manipulation on the rendered view

        private ViewSpec _target;
        private ElementSpec _selected;             // element to outline / drive the canvas selection
        private readonly ViewportState _viewport = new ViewportState();
        private string _variant;

        // --- device free-resize gesture (distinct from ComposerCanvas's ELEMENT handles, which live
        // INSIDE the device rect; these device handles live in the LETTERBOX MARGIN so the two never
        // hit-test against each other — see the handle-zone split with Pillar D) ---
        private enum DeviceHandle { None, Right, Bottom, Corner }
        private DeviceHandle _activeHandle;
        private int _deviceControlId;
        private Vector2 _dragStartMouse;
        private int _dragStartW, _dragStartH;
        private float _dragScale = 1f;             // device-px → screen-px scale captured at drag start
        private bool _showReadout;                 // dimension readout follows a live drag

        // cached toolbar styles (built once — never allocate GUIStyles per OnGUI pass)
        private GUIStyle _readoutStyle;
        private GUIStyle _breakpointStyle;

        private Texture2D _texture;
        // every built element's geometry (normalized rect + exact anchoredPos/size) captured from the
        // live build before the temp objects are destroyed — the canvas hit-tests / drags against this
        private readonly System.Collections.Generic.Dictionary<ElementSpec, ElementBox> _boxes =
            new System.Collections.Generic.Dictionary<ElementSpec, ElementBox>();
        private double _rebuildAt = -1;
        private bool _pending;
        private string _error;

        // panel/group preview: a view with tabs+panels (or a settings/cheats menu with groups) bakes
        // only ONE panel visible at a time. These let the user flip through them in the preview
        // without play mode — the chosen panel is forced visible at render, the rest hidden.
        private readonly System.Collections.Generic.List<string> _panelNames = new System.Collections.Generic.List<string>();
        private string _activePanel;

        public SpecPreviewPane(SpecDocument document, Action repaint, Action<string> selectPath)
        {
            _document = document;
            _repaint = repaint;
            _canvas = new ComposerCanvas(document, selectPath, repaint);
            // start on the first registered preset (the legacy phone-portrait default) so the viewport
            // opens on a real device rather than an arbitrary custom size
            if (ComposerDevicePresets.All.Count > 0)
                _viewport.ApplyPreset(ComposerDevicePresets.All[0]);
        }

        /// <summary> The view + element to show. Triggers a debounced rebuild when the view changes. </summary>
        public void SetTarget(ViewSpec view, ElementSpec selected)
        {
            _selected = selected;
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

            // scroll-wheel-over-pane zoom (Pillar D owns pan/marquee on the texture; zoom is ours)
            HandleZoomScroll(canvasRect);

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

            // device rect: fit-to-window picks the largest scale that fits, then the user's zoom
            // multiplies it. The texture is the device px; we draw it at scale into the pane.
            float fitScale = Mathf.Min(canvasRect.width / _texture.width, canvasRect.height / _texture.height);
            float scale = _viewport.fitToWindow ? fitScale * _viewport.zoom : _viewport.zoom;
            float drawW = _texture.width * scale, drawH = _texture.height * scale;
            var drawRect = new Rect(canvasRect.x + (canvasRect.width - drawW) * 0.5f,
                canvasRect.y + (canvasRect.height - drawH) * 0.5f, drawW, drawH);
            GUI.DrawTexture(drawRect, _texture, ScaleMode.ScaleToFit, false);

            // device free-resize handles — drawn + driven in the LETTERBOX MARGIN (outside drawRect) so
            // they never collide with ComposerCanvas's element handles (which live inside drawRect)
            if (_viewport.freeMode)
                HandleDeviceResize(canvasRect, drawRect, scale);

            // hand the rendered geometry to the WYSIWYG canvas: it owns selection outlines, resize
            // handles, marquee and all the drag gestures (move/resize/reparent) on top of the texture
            _canvas.OnGUI(drawRect, scale, _target, null, _boxes, _selected);

            // live dimension readout during a device drag ("412 × 915")
            if (_showReadout && Event.current.type == EventType.Repaint)
            {
                EnsureToolbarStyles();
                string label = $"{_viewport.width} × {_viewport.height}";
                var size = _readoutStyle.CalcSize(new GUIContent(label));
                var badge = new Rect(drawRect.center.x - size.x * 0.5f - 6f, drawRect.yMax - size.y - 10f,
                    size.x + 12f, size.y + 4f);
                EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.65f));
                GUI.Label(badge, label, _readoutStyle);
            }
        }

        // ------------------------------------------------------------------ zoom / device-resize

        private void HandleZoomScroll(Rect paneRect)
        {
            Event e = Event.current;
            if (e.type != EventType.ScrollWheel || !paneRect.Contains(e.mousePosition)) return;
            // scroll up = zoom in; clamp to a sane range
            float factor = e.delta.y < 0f ? 1.1f : 1f / 1.1f;
            _viewport.zoom = Mathf.Clamp(_viewport.zoom * factor, 0.1f, 8f);
            e.Use();
            _repaint?.Invoke();
        }

        /// <summary>
        /// Draws and drives the three device-resize handles (right edge, bottom edge, bottom-right
        /// corner) in the letterbox margin just outside the rendered device rect. Dragging converts the
        /// screen delta back to device px (dividing by the live render scale) and debounce-rebuilds.
        /// </summary>
        private void HandleDeviceResize(Rect paneRect, Rect drawRect, float scale)
        {
            const float thickness = 6f;     // how far into the margin the handle strip sits
            const float gap = 2f;           // small gap so the handle is clearly OUTSIDE the device rect
            var rightRect = new Rect(drawRect.xMax + gap, drawRect.y, thickness, drawRect.height);
            var bottomRect = new Rect(drawRect.x, drawRect.yMax + gap, drawRect.width, thickness);
            var cornerRect = new Rect(drawRect.xMax + gap, drawRect.yMax + gap, thickness, thickness);

            if (Event.current.type == EventType.Repaint)
            {
                Color c = NeoColors.Containers;
                EditorGUI.DrawRect(rightRect, c);
                EditorGUI.DrawRect(bottomRect, c);
                EditorGUI.DrawRect(cornerRect, c);
            }
            EditorGUIUtility.AddCursorRect(rightRect, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(bottomRect, MouseCursor.ResizeVertical);
            EditorGUIUtility.AddCursorRect(cornerRect, MouseCursor.ResizeUpLeft);

            _deviceControlId = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    DeviceHandle hit = cornerRect.Contains(e.mousePosition) ? DeviceHandle.Corner
                        : rightRect.Contains(e.mousePosition) ? DeviceHandle.Right
                        : bottomRect.Contains(e.mousePosition) ? DeviceHandle.Bottom
                        : DeviceHandle.None;
                    if (hit == DeviceHandle.None) break;
                    _activeHandle = hit;
                    _dragStartMouse = e.mousePosition;
                    _dragStartW = _viewport.width;
                    _dragStartH = _viewport.height;
                    _dragScale = scale <= 0f ? 1f : scale;
                    _showReadout = true;
                    GUIUtility.hotControl = _deviceControlId;
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == _deviceControlId && _activeHandle != DeviceHandle.None:
                    Vector2 d = (e.mousePosition - _dragStartMouse) / _dragScale; // screen px → device px
                    if (_activeHandle == DeviceHandle.Right || _activeHandle == DeviceHandle.Corner)
                        _viewport.width = Mathf.Max(64, _dragStartW + Mathf.RoundToInt(d.x));
                    if (_activeHandle == DeviceHandle.Bottom || _activeHandle == DeviceHandle.Corner)
                        _viewport.height = Mathf.Max(64, _dragStartH + Mathf.RoundToInt(d.y));
                    RequestRebuild();
                    e.Use();
                    _repaint?.Invoke();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == _deviceControlId:
                    _activeHandle = DeviceHandle.None;
                    _showReadout = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    RequestRebuild();
                    break;
            }
        }

        private void EnsureToolbarStyles()
        {
            _readoutStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _breakpointStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = NeoColors.TextDim }
            };
        }

        private void DrawToolbar(Rect rect)
        {
            EnsureToolbarStyles();
            GUILayout.BeginArea(rect, EditorStyles.toolbar);
            using (new EditorGUILayout.HorizontalScope())
            {
                // preset dropdown over the device-preset registry (the single source of truth)
                string presetLabel = CurrentPresetLabel();
                Rect presetRect = GUILayoutUtility.GetRect(120f, 18f);
                NeoDropdown.ValuePopup(presetRect, presetLabel, PresetLabels, label =>
                {
                    foreach (DevicePreset preset in ComposerDevicePresets.All)
                        if (preset.label == label) { _viewport.ApplyPreset(preset); RequestRebuild(); return; }
                    // a label with no matching preset should never happen (options come from the
                    // registry) — warn rather than fail silently, per the runtime-robustness rules
                    Debug.LogWarning($"[Neo.UI] No device preset registered for label '{label}'.");
                }, "Custom");

                // custom W / H — editing either switches to free mode and clears the preset selection
                GUILayout.Space(4f);
                int newW = EditorGUILayout.IntField(_viewport.width, GUILayout.Width(46f));
                GUILayout.Label("×", _breakpointStyle, GUILayout.Width(8f));
                int newH = EditorGUILayout.IntField(_viewport.height, GUILayout.Width(46f));
                if (newW != _viewport.width || newH != _viewport.height)
                {
                    _viewport.width = Mathf.Max(64, newW);
                    _viewport.height = Mathf.Max(64, newH);
                    _viewport.freeMode = true;
                    _viewport.presetId = null;
                    RequestRebuild();
                }

                // rotate — swaps w/h (also flips the landscape flag so a later preset apply honors it)
                if (GUILayout.Button("⟲", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                {
                    (_viewport.width, _viewport.height) = (_viewport.height, _viewport.width);
                    _viewport.landscape = !_viewport.landscape;
                    RequestRebuild();
                }

                // zoom: fit toggle + manual zoom field
                bool fit = GUILayout.Toggle(_viewport.fitToWindow, "Fit", EditorStyles.toolbarButton, GUILayout.Width(34f));
                if (fit != _viewport.fitToWindow)
                {
                    _viewport.fitToWindow = fit;
                    if (fit) _viewport.zoom = 1f; // fit re-baselines the multiplier
                    _repaint?.Invoke();
                }
                float newZoom = EditorGUILayout.FloatField(_viewport.zoom, GUILayout.Width(40f));
                if (!Mathf.Approximately(newZoom, _viewport.zoom))
                {
                    _viewport.zoom = Mathf.Clamp(newZoom, 0.1f, 8f);
                    _repaint?.Invoke();
                }

                Rect varRect = GUILayoutUtility.GetRect(110f, 18f);
                NeoDropdown.ValuePopup(varRect, _variant, VariantNames, v =>
                {
                    _variant = string.IsNullOrEmpty(v) ? null : v;
                    RequestRebuild();
                }, "default variant");

                // group/tab selector — only when the view has more than one panel to flip between
                if (_panelNames.Count > 1)
                {
                    Rect panelRect = GUILayoutUtility.GetRect(120f, 18f);
                    NeoDropdown.ValuePopup(panelRect, _activePanel, () => new System.Collections.Generic.List<string>(_panelNames),
                        name => { _activePanel = name; RequestRebuild(); }, "tab / group");
                }

                GUILayout.FlexibleSpace();

                // breakpoint indicator — placeholder until Pillar B's IActiveBreakpoint lands (see
                // ApplyActiveBreakpoint). Shows the live device dims so the slot is visibly wired.
                GUILayout.Label($"{_viewport.width}×{_viewport.height} (breakpoints pending)", _breakpointStyle);

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) Rebuild();
            }
            GUILayout.EndArea();
        }

        private string CurrentPresetLabel()
        {
            if (_viewport.freeMode || _viewport.presetId == null) return null; // → "Custom" empty label
            return ComposerDevicePresets.TryGet(_viewport.presetId, out DevicePreset p) ? p.label : null;
        }

        private static System.Collections.Generic.List<string> PresetLabels()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (DevicePreset preset in ComposerDevicePresets.All) list.Add(preset.label);
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

        // ------------------------------------------------------------------ render

        private void Rebuild()
        {
            Release();
            _error = null;
            _boxes.Clear();
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
            // collect each element's built GameObject (by ElementSpec reference) so we can read back
            // exact rects/positions for the canvas; cleared again the moment the build is done
            var sink = new System.Collections.Generic.Dictionary<ElementSpec, GameObject>();
            UISpecGenerator.ElementObjectSink = sink;
            try
            {
                roots = UISpecPreview.BuildViews(temp);
                if (roots.Count > 0)
                    RenderRoot(roots[0], sink, _viewport.width, _viewport.height);
            }
            catch (Exception e)
            {
                _error = "Preview failed: " + e.Message;
            }
            finally
            {
                UISpecGenerator.ElementObjectSink = null;
                if (roots != null)
                    foreach (GameObject root in roots)
                        if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (previousVariant != null) ThemeService.SetVariant(previousVariant);
            }

            // After every re-render, tell the (Pillar B) active-breakpoint system what device the
            // preview is now showing, so it can apply the breakpoint override a real device of this
            // aspect would trigger. Wired during Wave-2 integration — see ApplyActiveBreakpoint.
            ApplyActiveBreakpoint(_viewport.width, _viewport.height);
        }

        // TODO(Wave2-merge): wire to Pillar B's IActiveBreakpoint. Pillar B owns that interface and is
        // NOT in this worktree yet (defining it here would collide at merge). Until then this is a no-op
        // integration point: the viewport calls it after each re-render with the live device w/h, and the
        // toolbar shows "(breakpoints pending)". To wire: resolve the active breakpoint for (w,h) from
        // Pillar B's registry and apply its overrides, then drive the breakpoint indicator label off it.
        private void ApplyActiveBreakpoint(int width, int height)
        {
            // intentionally empty — Pillar B integration point (see DrawToolbar's indicator label)
        }

        private void RenderRoot(GameObject root,
            System.Collections.Generic.Dictionary<ElementSpec, GameObject> sink, int width, int height)
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
                // CanvasScaler-equivalent: the Composer preview ALWAYS scales like a device (deviceScale
                // = true) so the same view at 320-wide and 1920-wide reads proportionally. The agent
                // render matrix keeps the no-scaler default (RenderOptions.None) — see UIScreenshotter.
                UIScreenshotter.ApplyDeviceScale(canvasRect, width, height, deviceScale: true);
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
                CaptureBoxes(sink, width, height);

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

        /// <summary>
        /// Reads each built element's geometry off its live RectTransform — the normalized canvas rect
        /// (for hit-testing / overlay) plus the exact <c>anchoredPosition</c> and rect size (the base a
        /// drag adds its delta to). Keyed by the SAME <see cref="ElementSpec"/> instances the document
        /// holds, so the canvas maps a hit straight back to the spec node it must mutate. Must run while
        /// the temp objects are still alive (before the build is destroyed).
        /// </summary>
        private void CaptureBoxes(System.Collections.Generic.Dictionary<ElementSpec, GameObject> sink,
            int width, int height)
        {
            var corners = new Vector3[4];
            foreach (var entry in sink)
            {
                if (entry.Value == null || !(entry.Value.transform is RectTransform rect)) continue;
                rect.GetWorldCorners(corners);
                // canvas is centered at origin, 1 world unit = 1 px; map to 0..1 (y from bottom)
                float minX = Mathf.Min(corners[0].x, corners[2].x), maxX = Mathf.Max(corners[0].x, corners[2].x);
                float minY = Mathf.Min(corners[0].y, corners[2].y), maxY = Mathf.Max(corners[0].y, corners[2].y);
                _boxes[entry.Key] = new ElementBox
                {
                    norm = new Rect((minX + width * 0.5f) / width, (minY + height * 0.5f) / height,
                        (maxX - minX) / width, (maxY - minY) / height),
                    anchoredPos = rect.anchoredPosition,
                    size = rect.rect.size,
                    pivot = rect.pivot,
                };
            }
        }

        private void Release()
        {
            if (_texture != null) { UnityEngine.Object.DestroyImmediate(_texture); _texture = null; }
        }

        public void Dispose() => Release();
    }
}
