using UnityEngine;
using UnityEngine.UI;

namespace Neo.UI
{
    /// <summary>
    /// Abstract, time-driven driver that animates an existing host <see cref="Graphic"/>'s
    /// properties over a normalized 0..1 timeline — the <b>Tier-1</b> half of "fancy shader
    /// effects on UI". Tier-1 means: zero new materials, batching preserved. The effect never
    /// touches <see cref="Graphic.material"/>; it only writes serialized properties (color,
    /// <see cref="NeoShape.edgeSoftness"/>, gradient angle, vertex colors via a sibling
    /// <see cref="BaseMeshEffect"/>, …) that already ride the shared SDF material's vertex
    /// channels, so every animated shape keeps batching with every other shape.
    ///
    /// <para>The timeline drives through <see cref="Update"/> in play mode and — only when
    /// <see cref="playInEditMode"/> is explicitly opted in — through the editor tick. Per
    /// CLAUDE.md ("no editor-tick subscriptions for visuals") this defaults OFF, so the inspector
    /// is never animated behind the developer's back; the resting frame (<see cref="EvaluateRest"/>)
    /// is what the generator bakes for WYSIWYG.</para>
    ///
    /// <para>Progress is shaped by the existing Neo tween easing infrastructure
    /// (<see cref="Easing.Evaluate(Ease,float)"/>) so effects feel identical to the rest of the
    /// framework's motion; an opt-in <see cref="EaseMode.AnimationCurve"/> fallback covers custom
    /// shapes the named eases can't express. Theme-aware subclasses (e.g. ones that interpolate
    /// <see cref="ThemeColorRef"/>s) subscribe to <see cref="ThemeService.OnThemeChanged"/> exactly
    /// like <see cref="NeoGradient"/> — see <see cref="OnThemeChanged"/>.</para>
    ///
    /// <para>The set of effects is deliberately <b>open</b>: a consuming project adds an effect by
    /// subclassing this type (no enum/switch to fork). For heavy fragment effects that must break
    /// the single batch, see the Tier-2 seam (<see cref="ShapeEffectDefinition"/> +
    /// <see cref="NeoShapeVariant"/>).</para>
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Graphic))]
    public abstract class NeoShapeEffect : MonoBehaviour
    {
        [Tooltip("Animate while in edit mode (not just play mode). OFF by default so the editor " +
                 "isn't ticked for visuals — opt in only when you want to preview the motion live.")]
        [SerializeField] private bool playInEditModeFlag;

        [Tooltip("Length of one cycle in seconds.")]
        [SerializeField] private float durationSeconds = 1.5f;

        [Tooltip("Loop the timeline. When off the effect plays once then rests at its end frame.")]
        [SerializeField] private bool loopPlayback = true;

        [Tooltip("Ping-pong the timeline (0→1→0) instead of restarting at 0 each loop — the natural " +
                 "shape for breathing/sweeping effects.")]
        [SerializeField] private bool pingPong = true;

        [Tooltip("Whether progress is shaped by a named Ease or a custom AnimationCurve.")]
        [SerializeField] private EaseMode easeMode = EaseMode.Ease;

        [Tooltip("Named easing applied to the raw linear progress (used when Ease Mode = Ease).")]
        [SerializeField] private Ease ease = Ease.InOutSine;

        [Tooltip("Custom progress shaping (used when Ease Mode = AnimationCurve).")]
        [SerializeField] private AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("Normalized 0..1 phase the resting/baked frame is sampled at — what the generator " +
                 "bakes for WYSIWYG and what the effect snaps to when not playing.")]
        [Range(0f, 1f)]
        [SerializeField] private float restPhase;

        [System.NonSerialized] private Graphic _graphic;
        [System.NonSerialized] private float _elapsed;
        [System.NonSerialized] private bool _subscribed;

        // ------------------------------------------------------------------ properties

        /// <summary> Animate while in edit mode (defaults OFF — see the class remarks). </summary>
        public bool playInEditMode
        {
            get => playInEditModeFlag;
            set => playInEditModeFlag = value;
        }

        /// <summary> Length of one cycle in seconds (clamped to a tiny positive minimum). </summary>
        public float duration
        {
            get => durationSeconds;
            set => durationSeconds = Mathf.Max(0.0001f, value);
        }

        /// <summary> Loop the timeline rather than playing once and resting at the end. </summary>
        public bool loop
        {
            get => loopPlayback;
            set => loopPlayback = value;
        }

        /// <summary> Ping-pong (0→1→0) instead of restarting at 0 each loop. </summary>
        public bool pingPongLoop
        {
            get => pingPong;
            set => pingPong = value;
        }

        /// <summary> Whether progress is shaped by a named <see cref="Ease"/> or a custom curve. </summary>
        public EaseMode easingMode
        {
            get => easeMode;
            set => easeMode = value;
        }

        /// <summary> Named easing applied to the raw timeline progress. </summary>
        public Ease easing
        {
            get => ease;
            set => ease = value;
        }

        /// <summary> Custom progress shaping used when <see cref="easingMode"/> is AnimationCurve. </summary>
        public AnimationCurve easingCurve
        {
            get => easeCurve;
            set => easeCurve = value;
        }

        /// <summary> Normalized phase the resting/baked frame is sampled at. </summary>
        public float restingPhase
        {
            get => restPhase;
            set => restPhase = Mathf.Clamp01(value);
        }

        /// <summary> The host graphic this effect drives (cached; never null after <see cref="OnEnable"/>). </summary>
        public Graphic hostGraphic => _graphic != null ? _graphic : (_graphic = GetComponent<Graphic>());

        /// <summary>
        /// The host as an <see cref="NeoShape"/> when it is one (most Tier-1 effects drive shape
        /// params); null when the host is a plain Image/Text — subclasses must null-guard.
        /// </summary>
        public NeoShape hostShape => hostGraphic as NeoShape;

        /// <summary>
        /// True when this effect should subscribe to <see cref="ThemeService.OnThemeChanged"/>.
        /// Defaults false (most effects are color-agnostic); theme-interpolating subclasses
        /// override to return true.
        /// </summary>
        protected virtual bool UsesTheme => false;

        // ------------------------------------------------------------------ lifecycle

        /// <summary> Caches the host graphic and (for theme-aware effects) subscribes to theme changes. </summary>
        protected virtual void OnEnable()
        {
            _graphic = GetComponent<Graphic>();
            if (_graphic == null)
            {
                Debug.LogWarning($"[Neo.UI] {GetType().Name} on '{name}' has no Graphic to drive — effect is inert.", this);
                return;
            }

            _elapsed = 0f;
            if (UsesTheme && !_subscribed)
            {
                ThemeService.OnThemeChanged += OnThemeChanged;
                _subscribed = true;
            }

            // Start from a deterministic, WYSIWYG resting frame.
            EvaluateRest();
        }

        /// <summary> Unsubscribes from theme changes and restores the resting frame. </summary>
        protected virtual void OnDisable()
        {
            if (_subscribed)
            {
                ThemeService.OnThemeChanged -= OnThemeChanged;
                _subscribed = false;
            }

            // Leave the host in its baked rest state so a disabled effect never freezes mid-animation.
            EvaluateRest();
        }

        /// <summary> Advances the timeline. Inactive in edit mode unless <see cref="playInEditMode"/>. </summary>
        protected virtual void Update()
        {
            if (_graphic == null) return;

            bool editMode = !Application.isPlaying;
            if (editMode && !playInEditModeFlag)
                return;

            // Application.isPlaying false ⇒ no Time.deltaTime stepping; use unscaled real time so the
            // edit-mode preview advances even when the game clock is paused.
            float dt = editMode ? Time.unscaledDeltaTime : Time.deltaTime;
            _elapsed += dt;

            float linear = ComputeLinearPhase(_elapsed);
            Sample(linear);
        }

        // ------------------------------------------------------------------ evaluation

        /// <summary>
        /// Evaluates the effect at a normalized linear phase (0..1), applying easing and writing the
        /// host properties. Public so editor tooling / the generator can scrub a frame deterministically.
        /// </summary>
        public void Sample(float linearPhase01)
        {
            if (_graphic == null) return;
            float eased = Shape(Mathf.Clamp01(linearPhase01));
            ApplyAt(eased);
        }

        /// <summary>
        /// Applies the deterministic resting frame (the <see cref="restingPhase"/> sample) — the
        /// state the generator bakes and the state a stopped/disabled effect holds. WYSIWYG: the
        /// baked prefab equals this frame.
        /// </summary>
        public void EvaluateRest() => Sample(restPhase);

        /// <summary> Maps elapsed seconds → a 0..1 linear phase, honoring loop / ping-pong / one-shot. </summary>
        protected float ComputeLinearPhase(float elapsed)
        {
            float d = Mathf.Max(0.0001f, durationSeconds);
            if (!loopPlayback)
                return Mathf.Clamp01(elapsed / d);

            if (pingPong)
                return Mathf.PingPong(elapsed / d, 1f);

            return Mathf.Repeat(elapsed / d, 1f);
        }

        /// <summary> Shapes a 0..1 linear phase through the selected ease (named or curve). </summary>
        protected float Shape(float linear01)
        {
            return easeMode == EaseMode.AnimationCurve && easeCurve != null
                ? easeCurve.Evaluate(linear01)
                : Easing.Evaluate(ease, linear01);
        }

        /// <summary>
        /// Writes the host properties for the given eased 0..1 phase. Subclasses implement the
        /// actual animation here (e.g. lerp <see cref="NeoShape.edgeSoftness"/> between two values).
        /// Always null-guard <see cref="hostShape"/> / <see cref="hostGraphic"/>.
        /// </summary>
        protected abstract void ApplyAt(float easedPhase01);

        /// <summary>
        /// Called when the active theme changes (only for <see cref="UsesTheme"/> effects). Default
        /// re-applies the current rest frame so themed colors refresh live; override for finer control.
        /// </summary>
        protected virtual void OnThemeChanged(Theme theme) => EvaluateRest();

#if UNITY_EDITOR
        /// <summary> Keeps serialized values sane and re-bakes the rest frame on inspector edits. </summary>
        protected virtual void OnValidate()
        {
            durationSeconds = Mathf.Max(0.0001f, durationSeconds);
            restPhase = Mathf.Clamp01(restPhase);
            if (isActiveAndEnabled)
                EvaluateRest();
        }
#endif
    }
}
