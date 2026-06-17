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

        /// <summary>
        /// Creates a drag module. <paramref name="drag"/> is the fraction of velocity shed per second.
        /// It is clamped to [0, 0.999]: a fraction &gt;= 1 would make the per-second base
        /// <c>(1 - drag)</c> zero or negative, which <c>Pow(...)^dt</c> turns into an instant freeze
        /// (velocity zeroed on the first frame). Clamping degrades an out-of-range value to "very
        /// heavy drag" instead of a stuck particle.
        /// </summary>
        public DragModule(float drag) => _drag = Mathf.Clamp(drag, 0f, 0.999f);

        /// <inheritdoc/>
        public string Id => "Drag";

        /// <inheritdoc/>
        public void OnSpawn(ref NeoParticle p, NeoParticleEmitter e) { }

        /// <inheritdoc/>
        public void OnUpdate(ref NeoParticle p, float dt, NeoParticleEmitter e)
        {
            // Frame-rate independent exponential damping: v *= (1 - drag)^dt. _drag is clamped to
            // [0, 0.999] in the ctor, so the base stays in (0, 1] and a single frame can never zero
            // the velocity (no instant freeze even for an out-of-range spec value).
            float factor = Mathf.Pow(1f - _drag, dt);
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
