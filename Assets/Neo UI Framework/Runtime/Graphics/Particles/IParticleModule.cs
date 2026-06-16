namespace Neo.UI
{
    /// <summary>
    /// The extensibility seam for UI-particle behavior. A module mutates a <see cref="NeoParticle"/>'s
    /// simulation state — once at spawn and then every frame — so a consuming project can add its own
    /// forces, color/size curves or shape swaps without forking the package: implement this interface,
    /// then add a serializable config (subclass <see cref="ParticleModuleConfig"/>) that builds it,
    /// or feed instances to the emitter at runtime via <see cref="NeoParticleEmitter.AddModule"/>.
    /// Modules run in list order; keep them small and side-effect free beyond the particle they receive.
    /// </summary>
    public interface IParticleModule
    {
        /// <summary> Stable identifier (e.g. "Gravity") — used for ordering/round-trip and diagnostics. </summary>
        string Id { get; }

        /// <summary> Called once when a particle is spawned, after the emitter sets its initial state. </summary>
        /// <param name="p">The freshly spawned particle to initialize.</param>
        /// <param name="e">The emitter that owns the particle (for config/space queries).</param>
        void OnSpawn(ref NeoParticle p, NeoParticleEmitter e);

        /// <summary> Called every simulation frame for each live particle. </summary>
        /// <param name="p">The particle to advance.</param>
        /// <param name="dt">Delta time in seconds for this step.</param>
        /// <param name="e">The emitter that owns the particle.</param>
        void OnUpdate(ref NeoParticle p, float dt, NeoParticleEmitter e);
    }
}
