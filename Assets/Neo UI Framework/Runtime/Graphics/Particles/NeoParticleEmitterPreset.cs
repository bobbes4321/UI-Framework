using System.Collections.Generic;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// A named, reusable UI-particle emitter configuration (capacity, emission, particle look and the
    /// ordered module configs) — the project-shippable extension point, addressable by name like
    /// <see cref="UIAnimationPreset"/>. Apply it onto a live <see cref="NeoParticleEmitter"/> with
    /// <see cref="NeoParticleEmitter.ApplyPreset"/>; the data is copied, the asset is not linked.
    /// </summary>
    [CreateAssetMenu(menuName = "Neo UI/Particle Emitter Preset", fileName = "ParticleEmitterPreset")]
    public class NeoParticleEmitterPreset : ScriptableObject
    {
        [Tooltip("Preset category, e.g. Confetti / Sparkle / Coins.")]
        public string category = "Particles";

        [Tooltip("Preset name referenced by specs (agent-first string addressing), e.g. CoinBurst.")]
        public string presetName;

        [Header("Pool / Emission")]
        public int capacity = 32;
        public int burstCount = 16;
        [Tooltip("Continuous emission rate (particles/s); 0 = burst-only.")]
        public float rate;

        [Header("Particle")]
        public ShapeType particleShape = ShapeType.Circle;
        [Range(0f, 100f)] public float cornerRadiusPercent = 100f;
        public Vector2 sizeRange = new Vector2(10f, 18f);
        public Vector2 lifetimeRange = new Vector2(0.6f, 1.1f);
        public Vector2 speedRange = new Vector2(300f, 600f);
        public float emitAngle = 90f;
        public float emitSpread = 360f;
        public Vector2 angularVelocityRange = new Vector2(-180f, 180f);

        [Header("Modules")]
        [Tooltip("Ordered module configs applied to the emitter. Open seam — any ParticleModuleConfig subclass.")]
        [SerializeReference] public List<ParticleModuleConfig> moduleConfigs = new List<ParticleModuleConfig>();

        /// <summary> "Category/Name" — the string a spec/database looks this preset up by. </summary>
        public string fullName => $"{category}/{presetName}";

        /// <summary>
        /// Copies this preset's configuration onto an emitter. The module config list is passed by
        /// reference (the emitter rebuilds runtime module instances from it), so multiple emitters can
        /// share one preset's config without it mutating between them — module instances are per-emitter.
        /// </summary>
        public void ApplyTo(NeoParticleEmitter emitter)
        {
            if (emitter == null) return;
            emitter.ConfigureFrom(capacity, burstCount, rate, particleShape, cornerRadiusPercent,
                sizeRange, lifetimeRange, speedRange, emitAngle, emitSpread, angularVelocityRange,
                moduleConfigs);
        }
    }
}
