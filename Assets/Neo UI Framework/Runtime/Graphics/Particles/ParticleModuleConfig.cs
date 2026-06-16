using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Serializable configuration for a built-in particle module. Unity cannot serialize an
    /// <see cref="IParticleModule"/> interface reference directly, so the emitter stores a list of
    /// these flat, force-text config objects (each with an <see cref="enabled"/> toggle) and converts
    /// the enabled ones into runtime module instances via <see cref="Build"/> on enable. This keeps
    /// configuration round-trippable for the spec exporter while runtime behavior stays behind the
    /// <see cref="IParticleModule"/> seam.
    /// </summary>
    /// <remarks>
    /// A consuming project that ships its own built-in module typically pairs a new
    /// <see cref="IParticleModule"/> with a runtime <see cref="NeoParticleEmitter.AddModule"/> call;
    /// the serialized config types here cover the package defaults. The set is intentionally not a
    /// sealed enum — additional config classes can subclass this without a switch.
    /// </remarks>
    [Serializable]
    public abstract class ParticleModuleConfig
    {
        [Tooltip("When off, this module is skipped and contributes no behavior.")]
        public bool enabled = true;

        /// <summary> Stable identifier mirroring the module's <see cref="IParticleModule.Id"/> (for round-trip). </summary>
        public abstract string Id { get; }

        /// <summary>
        /// Builds the runtime module this config describes, or null when <see cref="enabled"/> is
        /// false (the emitter filters nulls). Called on enable, never per frame.
        /// </summary>
        public abstract IParticleModule Build();
    }
}
