using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Scales a particle's size over its normalized age, from a start multiplier to an end multiplier
    /// (relative to <see cref="NeoParticle.startSize"/>), eased by an <see cref="Ease"/>. Drives the
    /// classic pop-in / shrink-out feel. Writes <see cref="NeoParticle.size"/>; the emitter applies it
    /// to the NeoShape's RectTransform sizeDelta each frame.
    /// </summary>
    public sealed class SizeOverLifeModule : IParticleModule
    {
        private readonly float _startScale;
        private readonly float _endScale;
        private readonly Ease _ease;

        /// <summary> Creates a size-over-life module from start/end size multipliers and an ease. </summary>
        public SizeOverLifeModule(float startScale, float endScale, Ease ease)
        {
            _startScale = Mathf.Max(0f, startScale);
            _endScale = Mathf.Max(0f, endScale);
            _ease = ease;
        }

        /// <inheritdoc/>
        public string Id => "SizeOverLife";

        /// <inheritdoc/>
        public void OnSpawn(ref NeoParticle p, NeoParticleEmitter e)
        {
            p.size = p.startSize * _startScale;
        }

        /// <inheritdoc/>
        public void OnUpdate(ref NeoParticle p, float dt, NeoParticleEmitter e)
        {
            float t = Easing.Evaluate(_ease, p.NormalizedAge);
            p.size = p.startSize * Mathf.LerpUnclamped(_startScale, _endScale, t);
        }
    }

    /// <summary> Serializable config for <see cref="SizeOverLifeModule"/>. </summary>
    [Serializable]
    public sealed class SizeOverLifeModuleConfig : ParticleModuleConfig
    {
        [Tooltip("Size multiplier at spawn (relative to the particle's start size).")]
        public float startScale = 1f;

        [Tooltip("Size multiplier at end of life — 0 shrinks the particle to nothing.")]
        public float endScale;

        [Tooltip("Easing applied to the start→end interpolation over normalized age.")]
        public Ease ease = Ease.OutCubic;

        /// <inheritdoc/>
        public override string Id => "SizeOverLife";

        /// <inheritdoc/>
        public override IParticleModule Build() => enabled ? new SizeOverLifeModule(startScale, endScale, ease) : null;
    }
}
