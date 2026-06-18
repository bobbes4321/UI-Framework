using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Neo.UI
{
    /// <summary>
    /// Spawns and simulates pooled <see cref="NeoShape"/> particles INSIDE the UGUI canvas batch, so
    /// they inherit masking, sort order and CanvasScaler device scaling by construction — Unity's
    /// ParticleSystem renders outside the canvas and is the wrong tool here. Each live particle is a
    /// child GameObject carrying an NeoShape Graphic (sharing the one NeoShape material, so the whole
    /// burst batches and stays theme-token colorable). Emission is one-shot by default
    /// (<see cref="Burst(int)"/>); a non-zero <see cref="rate"/> enables continuous emission. A chain
    /// of <see cref="IParticleModule"/>s (built from the serialized <see cref="moduleConfigs"/>) drives
    /// per-particle behavior — the open extension seam.
    /// </summary>
    /// <remarks>
    /// v1 uses pooled NeoShape instances (one GameObject per live particle) for correctness and
    /// robustness. Future optimization: a single NeoShape-style Graphic emitting a multi-quad mesh
    /// would cut GameObjects to one for the whole system, at the cost of a custom mesh path; the
    /// pooled-instance approach was chosen first because it reuses NeoShape's batching/masking
    /// guarantees verbatim and needs no new shader work.
    /// </remarks>
    [AddComponentMenu("Neo/UI/Particles/Emitter")]
    [RequireComponent(typeof(RectTransform))]
    public class NeoParticleEmitter : MonoBehaviour
    {
        // ------------------------------------------------------------------ serialized config

        [Tooltip("Marker id so the spec exporter can round-trip this emitter (agent-first, never a GUID).")]
        [SerializeField] private string id = "";

        [Header("Pool")]
        [Tooltip("Particles pre-allocated on enable; the pool grows on demand but never shrinks below this.")]
        [SerializeField] private int capacity = 32;

        [Header("Emission")]
        [Tooltip("Emit a burst automatically when this component becomes enabled.")]
        [SerializeField] private bool emitOnEnable;
        [Tooltip("Number of particles a burst (and emitOnEnable) spawns.")]
        [SerializeField] private int burstCount = 16;
        [Tooltip("Continuous emission rate in particles/second. 0 = burst-only (the default; UI particles are overwhelmingly one-shot).")]
        [SerializeField] private float rate;

        [Header("Particle")]
        [Tooltip("Shape every spawned particle renders as (shares the one NeoShape material).")]
        [SerializeField] private ShapeType particleShape = ShapeType.Circle;
        [Tooltip("Corner radius as a percent (0-100) of the particle's half-size; 100 = fully round.")]
        [SerializeField] private float cornerRadiusPercent = 100f;
        [Tooltip("Square spawn size in canvas px (min,max picked per particle).")]
        [SerializeField] private Vector2 sizeRange = new Vector2(10f, 18f);
        [Tooltip("Lifetime in seconds (min,max picked per particle).")]
        [SerializeField] private Vector2 lifetimeRange = new Vector2(0.6f, 1.1f);
        [Tooltip("Initial speed in canvas units/s (min,max picked per particle).")]
        [SerializeField] private Vector2 speedRange = new Vector2(300f, 600f);
        [Tooltip("Emission cone: direction in degrees (0 = right, 90 = up) and full spread in degrees.")]
        [SerializeField] private float emitAngle = 90f;
        [Tooltip("Full angular spread of the emission cone in degrees.")]
        [SerializeField] private float emitSpread = 360f;
        [Tooltip("Initial angular velocity in degrees/second (min,max picked per particle).")]
        [SerializeField] private Vector2 angularVelocityRange = new Vector2(-180f, 180f);

        [Header("Modules")]
        [Tooltip("Ordered module configs; enabled ones become runtime IParticleModules on enable. " +
                 "Open seam — add your own ParticleModuleConfig subclass, or feed instances via AddModule.")]
        [SerializeReference] private List<ParticleModuleConfig> moduleConfigs = new List<ParticleModuleConfig>();

        // ------------------------------------------------------------------ runtime state

        private readonly List<NeoParticle> _all = new List<NeoParticle>();
        private readonly Stack<NeoParticle> _free = new Stack<NeoParticle>();
        private readonly List<IParticleModule> _modules = new List<IParticleModule>();
        private RectTransform _rectTransform;
        private RectTransform _container; // particles live here, NOT on transform, so a host layout group ignores them
        private float _emitAccumulator;
        private bool _built;

        // ------------------------------------------------------------------ public surface

        /// <summary> Marker id the spec exporter reads to round-trip this emitter. </summary>
        public string Id { get => id; set => id = value; }

        /// <summary> Particles pre-allocated on enable (also the pool floor). </summary>
        public int Capacity => capacity;

        /// <summary> Number of particles a <see cref="Burst()"/> spawns. </summary>
        public int BurstCount { get => burstCount; set => burstCount = Mathf.Max(0, value); }

        /// <summary> Continuous emission rate in particles/second (0 = burst-only). </summary>
        public float Rate { get => rate; set => rate = Mathf.Max(0f, value); }

        /// <summary> Live (non-pooled) particle count; 0 means no per-frame work and no allocation. </summary>
        public int ActiveCount { get; private set; }

        /// <summary> The runtime modules built from <see cref="moduleConfigs"/> plus any added via <see cref="AddModule"/>. </summary>
        public IReadOnlyList<IParticleModule> Modules => _modules;

        // ------------------------------------------------------------------ lifecycle

        private void Awake() => _rectTransform = (RectTransform)transform;

        private void OnEnable()
        {
            if (_rectTransform == null) _rectTransform = (RectTransform)transform;
            BuildModules();
            EnsureCapacity(capacity);
            _emitAccumulator = 0f;
            if (emitOnEnable) Burst();
        }

        private void OnDisable()
        {
            // Retire everything so a re-enable starts clean and nothing renders while disabled.
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                if (_all[i].alive) Retire(_all[i]);
            }
            ActiveCount = 0;
        }

        // ------------------------------------------------------------------ modules

        /// <summary>
        /// Rebuilds the runtime module list from the serialized configs (skipping disabled ones).
        /// Called on enable; safe to call again after editing configs at runtime.
        /// </summary>
        public void BuildModules()
        {
            _modules.Clear();
            if (moduleConfigs != null)
            {
                for (int i = 0; i < moduleConfigs.Count; i++)
                {
                    ParticleModuleConfig config = moduleConfigs[i];
                    if (config == null) continue;
                    IParticleModule module = config.Build();
                    if (module != null) _modules.Add(module);
                }
            }
            _built = true;
        }

        /// <summary>
        /// Appends a runtime module (the code-side seam for behavior a project doesn't want to
        /// serialize). Runs after the configured modules, in call order. No-op on null.
        /// </summary>
        public void AddModule(IParticleModule module)
        {
            if (module == null)
            {
                Debug.LogWarning($"[Neo.UI] NeoParticleEmitter '{name}': AddModule called with null — ignored.");
                return;
            }
            if (!_built) BuildModules();
            _modules.Add(module);
        }

        // ------------------------------------------------------------------ presets

        /// <summary>
        /// Overwrites this emitter's configuration from a reusable <see cref="NeoParticleEmitterPreset"/>
        /// (the named, project-shippable extension point) and rebuilds modules. No-op on null.
        /// </summary>
        public void ApplyPreset(NeoParticleEmitterPreset preset)
        {
            if (preset == null)
            {
                Debug.LogWarning($"[Neo.UI] NeoParticleEmitter '{name}': ApplyPreset called with null — ignored.");
                return;
            }
            preset.ApplyTo(this);
            if (isActiveAndEnabled) BuildModules();
        }

        /// <summary>
        /// Bulk-assigns the serialized config (used by <see cref="NeoParticleEmitterPreset.ApplyTo"/>).
        /// The module config list is deep-shared by reference clone so presets stay reusable.
        /// </summary>
        internal void ConfigureFrom(int newCapacity, int newBurstCount, float newRate,
            ShapeType shape, float radiusPercent, Vector2 sizes, Vector2 lifetimes, Vector2 speeds,
            float angle, float spread, Vector2 angularVel, List<ParticleModuleConfig> configs)
        {
            capacity = Mathf.Max(0, newCapacity);
            burstCount = Mathf.Max(0, newBurstCount);
            rate = Mathf.Max(0f, newRate);
            particleShape = shape;
            cornerRadiusPercent = radiusPercent;
            sizeRange = sizes;
            lifetimeRange = lifetimes;
            speedRange = speeds;
            emitAngle = angle;
            emitSpread = spread;
            angularVelocityRange = angularVel;
            moduleConfigs = configs ?? new List<ParticleModuleConfig>();
        }

        // ------------------------------------------------------------------ emission

        // Local spawn origin (relative to the emitter rect centre) for the NEXT spawns. Default zero =
        // the emitter centre; a click-positioned burst sets it for the burst then restores it, so the
        // emitter itself never moves (the host stays put).
        [System.NonSerialized] private Vector2 _spawnOrigin;

        /// <summary> Emits the configured <see cref="BurstCount"/> particles at once. </summary>
        public void Burst() => Burst(burstCount);

        /// <summary>
        /// Emits <paramref name="count"/> particles at once. Grows the pool if needed. No-op for
        /// non-positive counts.
        /// </summary>
        public void Burst(int count)
        {
            if (count <= 0) return;
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning($"[Neo.UI] NeoParticleEmitter '{name}': Burst ignored — emitter is disabled.");
                return;
            }
            for (int i = 0; i < count; i++) Spawn();
        }

        /// <summary>
        /// Emits <paramref name="count"/> particles from a specific LOCAL point (relative to the emitter
        /// rect's centre) — for click-positioned bursts — WITHOUT moving the emitter or its host. The
        /// origin applies to this burst only, then resets to the emitter centre.
        /// </summary>
        public void Burst(int count, Vector2 localOrigin)
        {
            Vector2 prev = _spawnOrigin;
            _spawnOrigin = localOrigin;
            try { Burst(count); }
            finally { _spawnOrigin = prev; }
        }

        // ------------------------------------------------------------------ simulation

        private void Update()
        {
            float dt = Time.deltaTime;

            // Continuous emission (opt-in via rate > 0). Burst-only systems do zero work here.
            if (rate > 0f && isActiveAndEnabled)
            {
                _emitAccumulator += rate * dt;
                while (_emitAccumulator >= 1f)
                {
                    _emitAccumulator -= 1f;
                    Spawn();
                }
            }

            if (ActiveCount == 0) return; // no live particles → no per-frame work, no allocation

            for (int i = 0; i < _all.Count; i++)
            {
                NeoParticle p = _all[i];
                if (!p.alive) continue;

                p.age += dt;
                if (p.age >= p.lifetime)
                {
                    Retire(p);
                    continue;
                }

                // Integrate, then let modules mutate state.
                p.position += p.velocity * dt;
                p.rotation += p.angularVelocity * dt;
                for (int m = 0; m < _modules.Count; m++)
                {
                    NeoParticle local = p; // modules take a ref; p is a class so this aliases the same instance
                    _modules[m].OnUpdate(ref local, dt, this);
                }

                PushToShape(p);
            }
        }

        // ------------------------------------------------------------------ pooling / shape plumbing

        private void Spawn()
        {
            NeoParticle p = Rent();
            if (p == null) return;

            p.alive = true;
            p.age = 0f;
            p.position = _spawnOrigin; // emit from the (optionally click-positioned) local origin
            p.rotation = 0f;
            p.startSize = Random.Range(sizeRange.x, sizeRange.y);
            p.size = p.startSize;
            p.lifetime = Mathf.Max(0.01f, Random.Range(lifetimeRange.x, lifetimeRange.y));
            p.angularVelocity = Random.Range(angularVelocityRange.x, angularVelocityRange.y);
            p.color = Color.white;

            float half = emitSpread * 0.5f;
            float angleDeg = emitAngle + Random.Range(-half, half);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            float speed = Random.Range(speedRange.x, speedRange.y);
            p.velocity = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * speed;

            for (int m = 0; m < _modules.Count; m++)
            {
                NeoParticle local = p;
                _modules[m].OnSpawn(ref local, this);
            }

            if (p.shape != null) p.shape.enabled = true;
            if (p.rectTransform != null) p.rectTransform.gameObject.SetActive(true);
            PushToShape(p);
            ActiveCount++;
        }

        private void Retire(NeoParticle p)
        {
            if (!p.alive) return;
            p.alive = false;
            if (p.rectTransform != null) p.rectTransform.gameObject.SetActive(false);
            _free.Push(p);
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
        }

        private NeoParticle Rent()
        {
            if (_free.Count > 0) return _free.Pop();
            return CreateParticle(); // pool grows on demand (steady-state bursts stay within capacity)
        }

        private void EnsureCapacity(int target)
        {
            for (int i = _all.Count; i < target; i++) _free.Push(CreateParticle());
        }

        /// <summary>
        /// Lazily creates (once) the full-stretch child the particles are parented to. Emitters ride
        /// controls whose root carries a layout group (button/toggle rows); parenting particles
        /// straight to <c>transform</c> let that group reposition the host as particles spawn/retire
        /// (the label visibly jumped) and clamped particles into the layout cell. A dedicated child
        /// with <see cref="LayoutElement.ignoreLayout"/> = true is invisible to any parent layout, so
        /// particles fly freely and never perturb the host. It is stretched over the emitter rect with
        /// a centered pivot, so a particle at local (0,0) still originates at the control's center —
        /// identical emit semantics to the old transform-parented path.
        /// </summary>
        private RectTransform EnsureContainer()
        {
            if (_container != null) return _container;

            var go = new GameObject("Particles", typeof(RectTransform));
            go.transform.SetParent(transform, worldPositionStays: false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // A parent HorizontalLayoutGroup/VerticalLayoutGroup must never lay this out or size it.
            var ignore = go.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;

            _container = rt;
            return _container;
        }

        /// <summary>
        /// Creates a pooled particle: a child GameObject with an NeoShape Graphic. The NeoShape uses
        /// its shared material (we never assign a per-instance material — that would break batching).
        /// Particles parent to the layout-ignored container, never to <c>transform</c>.
        /// </summary>
        private NeoParticle CreateParticle()
        {
            var go = new GameObject("NeoParticle", typeof(RectTransform));
            go.transform.SetParent(EnsureContainer(), worldPositionStays: false);
            go.SetActive(false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var shape = go.AddComponent<NeoShape>();
            shape.shape = particleShape;
            shape.cornerRadiusUnit = ShapeRadiusUnit.Percent;
            shape.cornerRadius = Mathf.Clamp(cornerRadiusPercent, 0f, 100f);
            shape.raycastTarget = false; // particles never intercept input

            var p = new NeoParticle { shape = shape, rectTransform = rt };
            _all.Add(p);
            return p;
        }

        /// <summary> Writes simulation state onto the pooled NeoShape (position, rotation, size, color). </summary>
        private void PushToShape(NeoParticle p)
        {
            if (p.rectTransform == null) return;
            p.rectTransform.anchoredPosition = p.position;
            p.rectTransform.localEulerAngles = new Vector3(0f, 0f, p.rotation);
            float s = Mathf.Max(0f, p.size);
            p.rectTransform.sizeDelta = new Vector2(s, s);
            if (p.shape != null) p.shape.color = p.color;
        }
    }
}
