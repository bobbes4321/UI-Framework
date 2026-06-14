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
    /// <para>Pillar G (preview fidelity) layers three render-time-only behaviors on this path, none of
    /// which touch the committed assets or the spec's persisted truth: live theme-token recolor (the
    /// document's edited <c>spec.theme</c> tokens are applied to <c>settings.theme</c> for the build then
    /// restored), live sample rows in data-bound lists (<see cref="PreviewSampleData"/>), and a
    /// breakpoint-override preview (the active breakpoint's per-element <c>overrides</c> are folded into
    /// each element's base layout via <see cref="LayoutSpec.MergedWith"/> to produce an EFFECTIVE spec —
    /// no runtime responsive driver, no generator change).</para>
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
        private int _sampleRows = 3;               // synthetic rows injected into bound lists (G.2.2)
        private string _activeBreakpointLabel = "Base"; // resolved indicator text (active edit / auto)

        // live flow playback (G.2.3): when on, the preview instantiates the spec's flow in-memory and the
        // author clicks interactive elements to walk it. Default OFF — static authoring preview as before.
        private readonly PreviewFlowPlayback _playback = new PreviewFlowPlayback();
        private bool _playMode;

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
            // the breakpoint edit scope (Pillar F) lives on the document; when it flips, the preview must
            // re-render against the effective spec for that breakpoint (G.2.3 — effective-spec approach).
            _document.ActiveBreakpointChanged += RequestRebuild;
            // edits during live playback invalidate the built flow/views (the spec changed under them) —
            // restart playback so its built objects stay in sync with the document (and references valid).
            _document.Changed += OnDocumentChangedDuringPlayback;
        }

        private void OnDocumentChangedDuringPlayback()
        {
            if (!_playMode) return;
            if (_playback.Begin(_document.Spec, out string error)) RequestRebuild();
            else ExitPlayback(error); // the flow was removed out from under playback — fall back to static
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
                // empty-state guidance: distinguish "nothing selected" from "selected view is empty"
                bool emptyView = _target != null && (_target.elements == null || _target.elements.Count == 0);
                string hint = _playMode
                    ? $"▶ Playing — node '{_playback.CurrentNodeName ?? "—"}' shows no view yet"
                    : _target == null
                        ? "Select a view to preview"
                        : emptyView
                            ? "This view is empty — drag a widget from the palette to start designing"
                            : "Building preview…";
                GUI.Label(canvasRect, hint, NeoStyles.PopupSearchHint);
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

            if (_playMode)
            {
                // play mode: clicks drive the flow, not the editing gestures — the WYSIWYG canvas stays out
                HandlePlaybackClick(drawRect);
                DrawPlaybackStatus(canvasRect);
            }
            else
            {
                // hand the rendered geometry to the WYSIWYG canvas: it owns selection outlines, resize
                // handles, marquee and all the drag gestures (move/resize/reparent) on top of the texture
                _canvas.OnGUI(drawRect, scale, _target, null, _boxes, _selected);
            }

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

                // sample-row count — only when the target view has a data-bound list (G.2.2), so the
                // toolbar stays clean for views that don't bind data
                if (TargetHasBoundList())
                {
                    GUILayout.Label("Rows", _breakpointStyle, GUILayout.Width(30f));
                    int newRows = EditorGUILayout.IntField(_sampleRows, GUILayout.Width(34f));
                    if (newRows != _sampleRows)
                    {
                        _sampleRows = Mathf.Clamp(newRows, 0, 200);
                        RequestRebuild();
                    }
                }

                // Play toggle (G.2.3) — only when the spec has a flow to play. Default OFF: the static
                // authoring preview behaves exactly as before. ON instantiates the flow in-memory and lets
                // the author click through it.
                if (SpecHasFlow())
                {
                    bool play = GUILayout.Toggle(_playMode, "▶ Play", EditorStyles.toolbarButton, GUILayout.Width(54f));
                    if (play != _playMode)
                    {
                        if (play) EnterPlayback();
                        else ExitPlayback();
                    }
                }
                else if (_playMode)
                {
                    // the flow was removed while playing — leave play mode cleanly
                    ExitPlayback();
                }

                GUILayout.FlexibleSpace();

                if (_playMode)
                {
                    // status line: the live flow node + the views it shows (also drawn on the canvas)
                    string views = string.Join(", ", _playback.ActiveViewIds);
                    GUILayout.Label($"▶ {_playback.CurrentNodeName ?? "—"}  ·  {(string.IsNullOrEmpty(views) ? "(none)" : views)}",
                        _breakpointStyle);
                }
                else
                {
                    // breakpoint indicator — shows the active EDIT scope (Pillar F) or, on base scope, the
                    // breakpoint a real device of this aspect would AUTO-resolve to (effective-spec approach).
                    GUILayout.Label($"{_viewport.width}×{_viewport.height}  ·  {_activeBreakpointLabel}", _breakpointStyle);
                }

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

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                if (_playMode || _target != null)
                    _error = "Preview needs a graphics device (this editor was started with -nographics).";
                return;
            }

            if (_playMode) RebuildPlayback();
            else RebuildStatic();
        }

        // ------------------------------------------------------------------ static authoring render

        private void RebuildStatic()
        {
            if (_target == null) return;

            // Resolve which breakpoint the preview should SHOW (G.2.3). If the author is editing an
            // override scope (Pillar F), show that breakpoint; otherwise show the one a real device of
            // this aspect would auto-resolve to (so base-scope editing still previews responsively).
            string activeBreakpoint = ResolveActiveBreakpoint(_viewport.width, _viewport.height);

            // build just the selected view in-memory. The EFFECTIVE spec folds the active breakpoint's
            // per-element overrides into each element's base layout (LayoutSpec.MergedWith) BEFORE the
            // build — no runtime UIResponsiveRoot, no generator/quartet change: the preview just renders
            // a spec whose layouts are already resolved for the chosen breakpoint.
            ViewSpec effectiveView = BuildEffectiveView(_target, activeBreakpoint);
            var views = new System.Collections.Generic.List<ViewSpec> { effectiveView };
            RenderSpecViews(views, views, _viewport.width, _viewport.height);
        }

        // ------------------------------------------------------------------ live flow playback render (G.2.3)

        /// <summary>
        /// Renders the views the playing flow's active node currently shows (stacked, like the runtime
        /// canvas). Always renders the BASE layout (no breakpoint-effective clone) so the boxes key off the
        /// document's own ElementSpec instances — the same refs <see cref="PreviewFlowPlayback"/> built
        /// against, so a clicked box maps straight back to the element to fire.
        /// </summary>
        private void RebuildPlayback()
        {
            if (!_playback.IsPlaying) return;
            var views = new System.Collections.Generic.List<ViewSpec>();
            foreach (string id in _playback.ActiveViewIds)
            {
                CategoryNameId.Parse(id, out string category, out string name);
                ViewSpec view = FindDocView(category, name);
                if (view != null && !views.Contains(view)) views.Add(view);
            }
            if (views.Count == 0) return; // a non-UI node (e.g. Start) shows nothing yet — blank, not an error
            RenderSpecViews(views, views, _viewport.width, _viewport.height);
        }

        private ViewSpec FindDocView(string category, string name)
        {
            foreach (ViewSpec view in _document.Spec.views)
                if (view != null && view.category == category && view.viewName == name) return view;
            return null;
        }

        // ------------------------------------------------------------------ shared render

        /// <summary>
        /// Builds <paramref name="renderViews"/> in-memory (live theme recolor + selected variant), pushes
        /// sample rows for <paramref name="sampleViews"/>' bound lists, renders them stacked to the texture
        /// and captures element geometry — then destroys every temp object and restores the theme. Shared
        /// by the static authoring preview (one effective view) and live playback (the active node's views).
        /// </summary>
        private void RenderSpecViews(System.Collections.Generic.List<ViewSpec> renderViews,
            System.Collections.Generic.List<ViewSpec> sampleViews, int width, int height)
        {
            var temp = new UISpec();
            temp.theme = _document.Spec.theme;
            temp.presets = _document.Spec.presets;
            foreach (ViewSpec view in renderViews) temp.views.Add(view);

            string previousVariant = null;
            NeoUISettings settings = NeoUISettings.instance;
            if (!string.IsNullOrEmpty(_variant) && settings?.theme != null)
            {
                previousVariant = settings.theme.ActiveVariantName;
                ThemeService.SetVariant(_variant);
            }

            // Live theme recolor (G.2.1): the build resolves token colors off settings.theme, NOT the
            // document's spec.theme. Apply the document's edited tokens to settings.theme transiently so
            // a color edit recolors the preview immediately, then restore them in the finally so the
            // committed theme asset is never mutated by a mere preview.
            ThemeTokenSnapshot themeSnapshot = ApplyDocumentTheme(settings);

            // Sample data for bound lists (G.2.2): synthesized rows pushed at the in-memory UIData store
            // so the preview shows a populated list. Cleared in the finally — never pollutes a real store.
            System.Collections.Generic.List<PreviewSampleData.Binding> sampleData = null;

            System.Collections.Generic.List<GameObject> roots = null;
            // collect each element's built GameObject (by ElementSpec reference) so we can read back
            // exact rects/positions for the canvas; cleared again the moment the build is done
            var sink = new System.Collections.Generic.Dictionary<ElementSpec, GameObject>();
            UISpecGenerator.ElementObjectSink = sink;
            try
            {
                roots = UISpecPreview.BuildViews(temp);
                if (roots.Count > 0)
                {
                    // push sample rows AFTER the build (the UIBoundLists are registered on enable) so
                    // UIData.Set rebuilds them, then capture geometry + render with rows present
                    sampleData = new System.Collections.Generic.List<PreviewSampleData.Binding>();
                    foreach (ViewSpec view in sampleViews)
                        sampleData.AddRange(PreviewSampleData.Populate(view, _sampleRows));
                    RenderRoots(roots, sink, width, height);
                }
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
                if (sampleData != null)
                    foreach (PreviewSampleData.Binding b in sampleData) UIData.Clear(b.category, b.name);
                themeSnapshot.Restore();
                if (previousVariant != null) ThemeService.SetVariant(previousVariant);
            }
        }

        /// <summary>
        /// Effective-spec for the breakpoint preview (G.2.3): a CLONE of the target view whose elements'
        /// <c>layout</c> is the base merged with the active breakpoint's <c>overrides[breakpoint]</c>
        /// (<see cref="LayoutSpec.MergedWith"/>). On base scope (empty breakpoint) the view is returned
        /// unchanged. Cloning via the spec serializer keeps it deep + lossless and never touches the
        /// document's own ElementSpec instances (the canvas keys off those — see SetTarget). The merge
        /// recurses through children + bound-list item templates.
        /// </summary>
        private static ViewSpec BuildEffectiveView(ViewSpec view, string breakpoint)
        {
            if (view == null || string.IsNullOrEmpty(breakpoint)) return view;
            // round-trip clone through JSON so we never alias the document's mutable specs
            ViewSpec clone = ViewSpec.Parse(view.ToJsonObject());
            foreach (ElementSpec element in clone.elements) MergeOverride(element, breakpoint);
            return clone;
        }

        private static void MergeOverride(ElementSpec element, string breakpoint)
        {
            if (element == null) return;
            if (element.overrides != null && element.overrides.TryGetValue(breakpoint, out LayoutSpec delta) && delta != null)
            {
                LayoutSpec baseLayout = element.layout ?? new LayoutSpec();
                element.layout = baseLayout.MergedWith(delta);
            }
            if (element.item != null) MergeOverride(element.item, breakpoint);
            if (element.children != null)
                foreach (ElementSpec child in element.children) MergeOverride(child, breakpoint);
        }

        /// <summary>
        /// Which breakpoint the preview shows for device (<paramref name="width"/>,<paramref name="height"/>):
        /// the author's active EDIT scope if set (Pillar F — they want to SEE what they're editing), else the
        /// first <see cref="UISpec.breakpoints"/> entry whose condition matches this device (what a real
        /// device would auto-resolve to), else base. Also refreshes the toolbar indicator label.
        /// </summary>
        private string ResolveActiveBreakpoint(int width, int height)
        {
            string editScope = _document.ActiveBreakpoint;
            if (!string.IsNullOrEmpty(editScope))
            {
                _activeBreakpointLabel = $"Editing: {editScope}";
                return editScope;
            }

            string auto = AutoBreakpoint(width, height);
            _activeBreakpointLabel = string.IsNullOrEmpty(auto) ? "Base" : $"Auto: {auto}";
            return auto;
        }

        /// <summary> First declared breakpoint whose condition matches (w,h); empty = base. Mirrors the
        /// runtime first-match-wins order and <c>UIResponsiveRoot.ResponsiveCondition.Matches</c>. </summary>
        private string AutoBreakpoint(int width, int height)
        {
            System.Collections.Generic.List<BreakpointSpec> breakpoints = _document.Spec?.breakpoints;
            if (breakpoints == null) return "";
            foreach (BreakpointSpec bp in breakpoints)
                if (bp != null && !string.IsNullOrEmpty(bp.name) && ConditionMatches(bp.when, width, height))
                    return bp.name;
            return "";
        }

        /// <summary> Editor-side mirror of the runtime breakpoint condition test (an empty condition never
        /// auto-matches — it would otherwise shadow every device). </summary>
        private static bool ConditionMatches(BreakpointCondition c, float width, float height)
        {
            if (c == null || c.IsEmpty) return false;
            float aspect = height > 0f ? width / height : 0f;
            bool portrait = height >= width;
            if (!string.IsNullOrEmpty(c.orientation) && (c.orientation == "portrait") != portrait) return false;
            if (c.minAspect.HasValue && aspect < c.minAspect.Value) return false;
            if (c.maxAspect.HasValue && aspect > c.maxAspect.Value) return false;
            if (c.minWidth.HasValue && width < c.minWidth.Value) return false;
            if (c.maxWidth.HasValue && width > c.maxWidth.Value) return false;
            return true;
        }

        /// <summary> Snapshot of theme tokens transiently overwritten for a live-recolor preview, so the
        /// committed theme asset is restored exactly afterward. </summary>
        private readonly struct ThemeTokenSnapshot
        {
            private readonly Theme _theme;
            private readonly System.Collections.Generic.List<(string variant, string token, Color color, bool had)> _entries;
            public ThemeTokenSnapshot(Theme theme,
                System.Collections.Generic.List<(string, string, Color, bool)> entries)
            { _theme = theme; _entries = entries; }

            public void Restore()
            {
                if (_theme == null || _entries == null) return;
                foreach ((string variant, string token, Color color, bool had) in _entries)
                    if (had) _theme.SetToken(token, color, variant);
                // tokens that did NOT exist before are left in place: they only ever resolve when an
                // element references them, the asset isn't saved here, and dropping them would need a
                // remove API per-variant. The committed colors (the ones that matter) are restored exactly.
            }
        }

        /// <summary>
        /// Applies the DOCUMENT's theme tokens onto <c>settings.theme</c> for the duration of one build so
        /// the preview reflects unsaved color edits (the build resolves colors off settings.theme, not the
        /// spec). Returns a snapshot to restore — nothing is saved, so the committed asset is untouched.
        /// </summary>
        private ThemeTokenSnapshot ApplyDocumentTheme(NeoUISettings settings)
        {
            ThemeSpec docTheme = _document.Spec?.theme;
            Theme theme = settings != null ? settings.theme : null;
            if (docTheme == null || theme == null || (docTheme.tokens.Count == 0 && docTheme.variants.Count == 0))
                return default;

            var entries = new System.Collections.Generic.List<(string, string, Color, bool)>();

            // default-variant tokens (variantName == null applies to every variant missing it)
            foreach (System.Collections.Generic.KeyValuePair<string, string> token in docTheme.tokens)
            {
                if (!ColorUtility.TryParseHtmlString(Hex(token.Value), out Color color)) continue;
                SnapshotToken(theme, null, token.Key, entries);
                theme.SetToken(token.Key, color);
            }
            // per-variant overrides
            foreach (var variant in docTheme.variants)
            {
                if (theme.GetVariant(variant.Key) == null) continue; // a brand-new variant only Save creates
                foreach (System.Collections.Generic.KeyValuePair<string, string> token in variant.Value)
                {
                    if (!ColorUtility.TryParseHtmlString(Hex(token.Value), out Color color)) continue;
                    SnapshotToken(theme, variant.Key, token.Key, entries);
                    theme.SetToken(token.Key, color, variant.Key);
                }
            }
            return new ThemeTokenSnapshot(theme, entries);
        }

        private static void SnapshotToken(Theme theme, string variantName, string token,
            System.Collections.Generic.List<(string, string, Color, bool)> entries)
        {
            // capture every variant the SetToken will touch so Restore puts each back exactly
            foreach (Theme.ThemeVariant v in theme.Variants)
            {
                if (variantName != null && v.name != variantName) continue;
                bool had = v.TryGetColor(token, out Color prev);
                entries.Add((v.name, token, prev, had));
            }
        }

        private static string Hex(string value)
        {
            if (string.IsNullOrEmpty(value)) return "#FFFFFF";
            return value.StartsWith("#") ? value : "#" + value;
        }

        private bool SpecHasFlow()
        {
            FlowSpec flow = _document.Spec?.flow;
            return flow != null && flow.nodes != null && flow.nodes.Count > 0;
        }

        private bool TargetHasBoundList()
        {
            if (_target?.elements == null) return false;
            foreach (ElementSpec element in _target.elements)
                if (HasBoundList(element)) return true;
            return false;
        }

        private static bool HasBoundList(ElementSpec element)
        {
            if (element == null) return false;
            if (!string.IsNullOrWhiteSpace(element.bind) && element.item != null) return true;
            if (element.children != null)
                foreach (ElementSpec child in element.children)
                    if (HasBoundList(child)) return true;
            return false;
        }

        private void RenderRoots(System.Collections.Generic.List<GameObject> roots,
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

                // multiple roots stack on the canvas exactly like the runtime (a node layering several
                // views — cheat sheet over the menu); each keeps its own anchors/layout
                foreach (GameObject root in roots)
                {
                    if (root == null) continue;
                    SceneManager.MoveGameObjectToScene(root, scene);
                    root.transform.SetParent(canvasRect, worldPositionStays: false);
                    root.SetActive(true);
                    var rootGroup = root.GetComponent<CanvasGroup>();
                    if (rootGroup != null) rootGroup.alpha = 1f;
                }

                Canvas.ForceUpdateCanvases();
                foreach (GameObject root in roots)
                    if (root != null)
                        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform);
                Canvas.ForceUpdateCanvases();

                ApplyPanelSelection(roots);
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
        private void ApplyPanelSelection(System.Collections.Generic.List<GameObject> roots)
        {
            _panelNames.Clear();
            var panelList = new System.Collections.Generic.List<UIPanel>();
            foreach (GameObject root in roots)
            {
                if (root == null) continue;
                foreach (UIPanel panel in root.GetComponentsInChildren<UIPanel>(includeInactive: true))
                {
                    panelList.Add(panel);
                    if (!_panelNames.Contains(panel.gameObject.name))
                        _panelNames.Add(panel.gameObject.name);
                }
            }

            if (string.IsNullOrEmpty(_activePanel) || !_panelNames.Contains(_activePanel)) return;

            foreach (UIPanel panel in panelList)
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

        public void Dispose()
        {
            _document.ActiveBreakpointChanged -= RequestRebuild;
            _document.Changed -= OnDocumentChangedDuringPlayback;
            _playback.Stop();
            Release();
        }

        // ------------------------------------------------------------------ playback enter/exit (G.2.3)

        private void EnterPlayback()
        {
            if (_playback.Begin(_document.Spec, out string error))
            {
                _playMode = true;
                _error = null;
                Rebuild();
            }
            else
            {
                // no silent failure — tell the author why Play did nothing, and stay in static preview
                _playMode = false;
                _error = error;
                _repaint?.Invoke();
            }
        }

        private void ExitPlayback(string error = null)
        {
            _playback.Stop();
            _playMode = false;
            _error = error;
            RequestRebuild();
        }

        /// <summary>
        /// In play mode a left-click on the rendered texture maps to the deepest element under the cursor
        /// (via the captured boxes — no physical raycast) and fires its interaction; the synchronous flow
        /// dispatch advances the graph, so we re-render to show the new active view(s).
        /// </summary>
        private void HandlePlaybackClick(Rect drawRect)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !drawRect.Contains(e.mousePosition)) return;

            ElementSpec hit = HitTestBox(e.mousePosition, drawRect);
            if (hit != null && _playback.ClickElement(hit))
            {
                Rebuild();           // active node may have changed → re-render its views immediately
                _repaint?.Invoke();
            }
            e.Use();                 // consume the click so the editing canvas never sees it in play mode
        }

        // deepest (smallest-area) element box containing the point — mirrors ComposerCanvas.HitTest but
        // standalone so play mode never drives the editing gestures
        private ElementSpec HitTestBox(Vector2 point, Rect drawRect)
        {
            ElementSpec best = null;
            float bestArea = float.MaxValue;
            foreach (var entry in _boxes)
            {
                Rect n = entry.Value.norm;
                var screen = new Rect(drawRect.x + n.x * drawRect.width,
                    drawRect.y + (1f - n.y - n.height) * drawRect.height,
                    n.width * drawRect.width, n.height * drawRect.height);
                if (!screen.Contains(point)) continue;
                float area = screen.width * screen.height;
                if (area < bestArea) { best = entry.Key; bestArea = area; }
            }
            return best;
        }

        /// <summary> A small status line pinned to the top-left of the canvas: the live flow node + the
        /// view(s) it currently shows (the click target readout). </summary>
        private void DrawPlaybackStatus(Rect canvasRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureToolbarStyles();

            string node = _playback.CurrentNodeName ?? "—";
            string views = string.Join(", ", _playback.ActiveViewIds);
            if (string.IsNullOrEmpty(views)) views = "(none)";
            string label = $"▶  Node: {node}   ·   Views: {views}";

            var size = _readoutStyle.CalcSize(new GUIContent(label));
            var badge = new Rect(canvasRect.x + 6f, canvasRect.y + 6f, size.x + 12f, size.y + 4f);
            EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(badge, label, _readoutStyle);
        }
    }
}
