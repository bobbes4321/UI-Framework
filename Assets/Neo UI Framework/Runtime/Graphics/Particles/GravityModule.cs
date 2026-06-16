using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Applies a constant acceleration (gravity/wind) to a particle's velocity each frame. Expressed
    /// in canvas units per second squared; negative Y falls, positive Y rises. The emitter integrates
    /// position from velocity, so this module only touches velocity.
    /// </summary>
    public sealed class GravityModule : IParticleModule
    {
        private readonly Vector2 _acceleration;

        /// <summary> Creates a gravity module with the given acceleration (canvas units/s²). </summary>
        public GravityModule(Vector2 acceleration) => _acceleration = acceleration;

        /// <inheritdoc/>
        public string Id => "Gravity";

        /// <inheritdoc/>
        public void OnSpawn(ref NeoParticle p, NeoParticleEmitter e) { }

        /// <inheritdoc/>
        public void OnUpdate(ref NeoParticle p, float dt, NeoParticleEmitter e)
        {
            p.velocity += _acceleration * dt;
        }
    }

    /// <summary> Serializable config for <see cref="GravityModule"/>. </summary>
    [Serializable]
    public sealed class GravityModuleConfig : ParticleModuleConfig
    {
        [Tooltip("Constant acceleration in canvas units/s² (negative Y falls).")]
        public Vector2 acceleration = new Vector2(0f, -900f);

        /// <inheritdoc/>
        public override string Id => "Gravity";

        /// <inheritdoc/>
        public override IParticleModule Build() => enabled ? new GravityModule(acceleration) : null;
    }
}
