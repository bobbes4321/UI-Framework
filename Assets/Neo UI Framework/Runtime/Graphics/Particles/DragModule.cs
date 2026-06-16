using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Damps a particle's velocity toward zero each frame (linear drag / air resistance), giving
    /// confetti and sparks a natural settle. The coefficient is a per-second damping fraction applied
    /// exponentially so the result is frame-rate independent.
    /// </summary>
    public sealed class DragModule : IParticleModule
    {
        private readonly float _drag;

        /// <summary> Creates a drag module. <paramref name="drag"/> is the fraction of velocity shed per second (0 = none). </summary>
        public DragModule(float drag) => _drag = Mathf.Max(0f, drag);

        /// <inheritdoc/>
        public string Id => "Drag";

        /// <inheritdoc/>
        public void OnSpawn(ref NeoParticle p, NeoParticleEmitter e) { }

        /// <inheritdoc/>
        public void OnUpdate(ref NeoParticle p, float dt, NeoParticleEmitter e)
        {
            // Frame-rate independent exponential damping: v *= (1 - drag)^dt.
            float factor = Mathf.Pow(Mathf.Max(0f, 1f - _drag), dt);
            p.velocity *= factor;
            p.angularVelocity *= factor;
        }
    }

    /// <summary> Serializable config for <see cref="DragModule"/>. </summary>
    [Serializable]
    public sealed class DragModuleConfig : ParticleModuleConfig
    {
        [Tooltip("Fraction of velocity shed per second (0 = no drag, 1 = stops within a second).")]
        [Range(0f, 1f)] public float drag = 0.4f;

        /// <inheritdoc/>
        public override string Id => "Drag";

        /// <inheritdoc/>
        public override IParticleModule Build() => enabled ? new DragModule(drag) : null;
    }
}
