using System;
using Neo.EditorUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Neo.UI.Editor
{
    /// <summary>
    /// A self-contained, offscreen "stage" motion previews play on when NOTHING suitable is selected in
    /// the scene — owned by the Design System <see cref="MotionTab"/> (long-lived, right-pane viewport)
    /// and by <see cref="AnimationPresetBrowserPopup"/> in stage-fallback mode (popup-lived, inline
    /// viewport), so both are fully self-sufficient for browsing motion (a scene selection still WINS;
    /// previewing in real context beats this synthetic card). It owns a hidden, never-saved GameObject
    /// hierarchy in an isolated preview scene (the same world-space-canvas / orthographic-camera recipe
    /// <see cref="UIScreenshotter"/> and <see cref="PresetThumbnailRenderer"/> use), a stage camera
    /// rendering into a persistent <see cref="RenderTexture"/>, and a dummy card built from
    /// <see cref="NeoShape"/> (+ a TMP label) that the animation plays on.
    /// <para>
    /// Unlike the thumbnail renderer (which spins up a preview scene, renders ONCE and tears it all down),
    /// this stage is LONG-LIVED: it re-renders the same RT live while a preview is running. It adds NO
    /// editor-tick of its own — the Motion tab's existing preview <c>Tick</c> already repaints the window
    /// while a preview is live, and the stage re-renders during those Repaints (see
    /// <see cref="RenderLive"/> / <see cref="RenderStatic"/>). When idle it renders nothing and holds no
    /// update subscription, so it costs nothing until first needed.
    /// </para>
    /// <para>
    /// Lifetime: everything is created lazily on first use and torn down in <see cref="Dispose"/>
    /// (<see cref="MotionTab.State.Dispose"/> calls it). <see cref="EnsureBuilt"/> is self-healing — after
    /// a domain reload (which destroys the preview scene) or graphics-device RT loss it rebuilds whatever
    /// went away, so a stale State reference never renders against dead Unity objects. The dummy
    /// <see cref="NeoShape"/> never gets its own material (shared-material rule) — only its shape/color
    /// fields are set.
    /// </para>
    /// </summary>
    internal sealed class MotionPreviewStage : IDisposable
    {
        // Canvas + card dimensions in canvas units (== px at scale 1). The card sits small inside a
        // generous canvas so slide/scale/rotate motion has room to read without clipping. The RT renders
        // at RenderScale× these units so the viewport stays crisp at pane width on high-DPI displays.
        private const float CanvasWidth = 380f;
        private const float CanvasHeight = 220f;
        private const float CardWidth = 200f;
        private const float CardHeight = 120f;
        private const int RenderScale = 2;

        private Scene _scene;
        private bool _sceneValid;
        private Camera _camera;
        private RectTransform _canvasRect;
        private RectTransform _cardRect;   // the preview target the animation plays on
        private RenderTexture _rt;
        private bool _rendered;            // the RT holds a valid frame since the last (re)build / RT (re)create
        private bool _lastWasLive;         // last frame rendered was an animating frame (forces one static re-render on live→static)

        /// <summary> The stage viewport's width:height aspect as a compile-time constant — for sizing a
        /// viewport rect BEFORE any stage instance exists (the preset browser popup's window size). </summary>
        internal const float AspectRatio = CanvasWidth / CanvasHeight;

        /// <summary> The stage viewport's width:height aspect (for <c>GUILayoutUtility.GetAspectRect</c>). </summary>
        public float Aspect => AspectRatio;

        /// <summary>
        /// The stage card's <see cref="RectTransform"/> — the target Motion-tab previews (button /
        /// hover-dwell / scrub) play on when there's no scene selection. Lazily builds the stage; returns
        /// null only if the hierarchy could not be built.
        /// </summary>
        public RectTransform Target
        {
            get { EnsureBuilt(); return _cardRect; }
        }

        /// <summary>
        /// Aspect-correct IMGUI viewport over the stage's RenderTexture — the one presentation of the
        /// stage (Design System Motion tab right pane + the preset browser popup's inline fallback).
        /// Re-renders the stage ONLY on Repaint and ONLY while <paramref name="live"/> (a preview/scrub
        /// is posing the card); idle frames return the cached RT — zero render cost when nothing is
        /// animating. The caller's preview machinery owns repainting its window while live.
        /// </summary>
        public void DrawViewport(bool live, float maxWidth = 460f) =>
            DrawViewport(GUILayoutUtility.GetAspectRect(Aspect, GUILayout.MaxWidth(maxWidth)), live);

        /// <summary> Rect-positioned variant for manual-layout hosts (the preset browser popup). </summary>
        public void DrawViewport(Rect rect, bool live)
        {
            if (Event.current.type != EventType.Repaint) return;
            Texture tex = live ? RenderLive() : RenderStatic();
            if (tex != null)
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
            else
            {
                // Headless (-nographics) / stage build failure: keep the layout, say why (no silent gap).
                EditorGUI.DrawRect(rect, NeoColors.SectionBackground);
                GUI.Label(rect, "Stage preview needs a graphics device.", EditorStyles.centeredGreyMiniLabel);
            }
        }

        /// <summary>
        /// Re-renders the stage this frame and returns the RT texture — call every Repaint while a preview
        /// or scrub is LIVE on the stage (the card transform/alpha changed). Returns null when headless.
        /// </summary>
        public Texture RenderLive()
        {
            EnsureBuilt();
            _lastWasLive = true;
            return RenderNow();
        }

        /// <summary>
        /// Returns the stage texture for an IDLE (non-animating) frame. Renders once after a (re)build, RT
        /// loss, or a live→static transition; otherwise returns the cached RT untouched — so a window that
        /// merely repaints on mouse-move pays zero render cost while nothing is animating. Null when headless.
        /// </summary>
        public Texture RenderStatic()
        {
            EnsureBuilt();
            if (_rendered && !_lastWasLive && _rt != null && _rt.IsCreated()) return _rt;
            _lastWasLive = false;
            return RenderNow();
        }

        // ------------------------------------------------------------------ render

        private Texture RenderNow()
        {
            if (_cardRect == null || _camera == null) return null;   // build failed
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null; // -nographics: no device to render with
            EnsureRenderTexture();
            if (_rt == null) return null;

            // Flush pending graphic/CanvasGroup state (the Fade channel drives alpha, which the canvas
            // applies during its update) so the offscreen frame reflects the current pose. One pass is
            // enough — the card is fixed-size with no nested layout groups to settle.
            Canvas.ForceUpdateCanvases();

            _camera.targetTexture = _rt;
            _camera.Render();
            _camera.targetTexture = null;
            _rendered = true;
            return _rt;
        }

        // ------------------------------------------------------------------ build (self-healing)

        private void EnsureBuilt()
        {
            // A live hierarchy: nothing to do but ensure the RT survived (device resets release it). The
            // Unity-null checks catch a domain reload / scene close having destroyed our objects.
            if (_cardRect != null && _camera != null && _canvasRect != null && _sceneValid && _scene.IsValid())
            {
                EnsureRenderTexture();
                return;
            }
            Teardown();  // clear any partial remnants first
            Build();
        }

        private void Build()
        {
            _scene = EditorSceneManager.NewPreviewScene();
            _sceneValid = true;

            // --- stage camera (frames the whole canvas: orthoSize == half the canvas height) ---
            var cameraGo = new GameObject("NeoUI Motion Stage Camera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraGo, _scene);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.scene = _scene;                       // restrict rendering to the preview scene
            _camera.orthographic = true;
            _camera.orthographicSize = CanvasHeight * 0.5f;
            _camera.transform.position = new Vector3(0f, 0f, -100f);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 1000f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = ResolveColor(UIWidgetFactory.TokenBackground, new Color(0.09f, 0.10f, 0.12f, 1f));

            // --- world-space canvas sized 1:1 to the canvas units (camera zooms nothing; RT resolution alone sets crispness) ---
            var canvasGo = new GameObject("NeoUI Motion Stage Canvas", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(canvasGo, _scene);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _camera;
            _canvasRect = (RectTransform)canvasGo.transform;
            UIScreenshotter.ApplyDeviceScale(_canvasRect, Mathf.RoundToInt(CanvasWidth), Mathf.RoundToInt(CanvasHeight), deviceScale: false);
            _canvasRect.position = Vector3.zero;

            BuildCard(_canvasRect);

            // Belt-and-suspenders: preview-scene objects are already isolated + unsaved, but flag the whole
            // tree HideAndDontSave so nothing can leak into a saved scene even if something reparents it.
            ApplyHideFlagsRecursive(_canvasRect);

            // Flush the freshly-built graphics so the very first render isn't blank.
            Canvas.ForceUpdateCanvases();

            _rendered = false;
            _lastWasLive = false;
            EnsureRenderTexture();
        }

        // A plausible small card: a rounded-rect body (Surface) with a thin outline, an accent pill
        // "title bar" (Primary), and a centered TMP label — enough for move/rotate/scale/fade/color to
        // read against. Built through UIWidgetFactory.CreateLabel so the label picks up the project font.
        private void BuildCard(RectTransform canvasRect)
        {
            Color surface = ResolveColor(UIWidgetFactory.TokenSurfaceElevated, new Color(0.16f, 0.17f, 0.20f, 1f));
            Color outline = ResolveColor(UIWidgetFactory.TokenOutline, new Color(1f, 1f, 1f, 0.10f));
            Color accent = ResolveColor(UIWidgetFactory.TokenPrimary, new Color(0.30f, 0.55f, 1f, 1f));

            var cardGo = new GameObject("Motion Stage Card", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(cardGo, _scene);
            _cardRect = (RectTransform)cardGo.transform;
            _cardRect.SetParent(canvasRect, worldPositionStays: false);
            _cardRect.anchorMin = _cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            _cardRect.pivot = new Vector2(0.5f, 0.5f);
            _cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            _cardRect.anchoredPosition = Vector2.zero;

            NeoShape body = cardGo.AddComponent<NeoShape>();  // shared material — never set body.material
            body.shape = ShapeType.RoundedRect;
            body.cornerRadius = 16f;
            body.color = surface;
            body.border = 1.5f;
            body.outlineColor = outline;

            // A CanvasGroup up front so the Fade channel always has one to drive (StartPreview would add
            // one on demand, but owning it keeps the stage tree stable across previews).
            cardGo.AddComponent<CanvasGroup>();

            // Accent "title bar" pill, inset from the top edge.
            var barGo = new GameObject("Accent", typeof(RectTransform));
            var barRect = (RectTransform)barGo.transform;
            barRect.SetParent(_cardRect, worldPositionStays: false);
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(-32f, 10f);      // 16px inset each side, 10px tall
            barRect.anchoredPosition = new Vector2(0f, -18f);
            NeoShape bar = barGo.AddComponent<NeoShape>();   // shared material again
            bar.shape = ShapeType.Pill;
            bar.color = accent;

            UIWidgetFactory.CreateLabel(_cardRect, "Preview", UIWidgetFactory.TokenTextDefault, 22f);
        }

        private static void ApplyHideFlagsRecursive(Transform root)
        {
            root.gameObject.hideFlags = HideFlags.HideAndDontSave;
            for (int i = 0; i < root.childCount; i++)
                ApplyHideFlagsRecursive(root.GetChild(i));
        }

        // ------------------------------------------------------------------ render texture

        private void EnsureRenderTexture()
        {
            if (_rt != null && _rt.IsCreated()) return;
            if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); _rt = null; }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return; // no device: leave null, callers fall back

            int w = Mathf.RoundToInt(CanvasWidth * RenderScale);
            int h = Mathf.RoundToInt(CanvasHeight * RenderScale);
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "NeoUI Motion Stage RT"
            };
            _rt.Create();
            if (_camera != null) _camera.aspect = (float)w / h; // match the RT so the framed canvas isn't stretched
            _rendered = false;                                  // a fresh RT holds no frame yet
        }

        // ------------------------------------------------------------------ teardown

        public void Dispose() => Teardown();

        private void Teardown()
        {
            // Destroy the card FIRST (DestroyImmediate → OnDisable runs) so any live tween/animator stops
            // before the RT and scene go — a leaked tween over a destroyed target throws from the heartbeat.
            if (_cardRect != null) UnityEngine.Object.DestroyImmediate(_cardRect.gameObject);
            _cardRect = null;
            if (_camera != null) UnityEngine.Object.DestroyImmediate(_camera.gameObject);
            _camera = null;
            if (_canvasRect != null) UnityEngine.Object.DestroyImmediate(_canvasRect.gameObject);
            _canvasRect = null;
            if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); _rt = null; }
            if (_sceneValid && _scene.IsValid())
            {
                try { EditorSceneManager.ClosePreviewScene(_scene); }
                catch { /* scene already gone (e.g. domain reload) — nothing to close */ }
            }
            _sceneValid = false;
            _rendered = false;
            _lastWasLive = false;
        }

        private static Color ResolveColor(string token, Color fallback) =>
            ThemeService.TryGetColor(token, out Color c) ? c : fallback;
    }
}
