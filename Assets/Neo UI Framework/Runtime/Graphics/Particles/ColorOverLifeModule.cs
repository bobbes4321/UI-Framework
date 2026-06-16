using System;
using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Lerps a particle's color from a start to an end <see cref="ThemeColorRef"/> across its
    /// normalized age, eased by an <see cref="Ease"/>. Both endpoints are theme refs, so a particle
    /// burst stays theme-live (recolors when tokens or the active variant change). Writes the result
    /// into <see cref="NeoParticle.color"/>; the emitter pushes it onto the NeoShape each frame.
    /// </summary>
    public sealed class ColorOverLifeModule : IParticleModule
    {
        private readonly ThemeColorRef _start;
        private readonly ThemeColorRef _end;
        private readonly Ease _ease;

        /// <summary> Creates a color-over-life module from start/end theme color refs and an ease. </summary>
        public ColorOverLifeModule(ThemeColorRef start, ThemeColorRef end, Ease ease)
        {
            _start = start ?? new ThemeColorRef(Color.white);
            _end = end ?? new ThemeColorRef(new Color(1f, 1f, 1f, 0f));
            _ease = ease;
        }

        /// <inheritdoc/>
        public string Id => "ColorOverLife";

        /// <inheritdoc/>
        public void OnSpawn(ref NeoParticle p, NeoParticleEmitter e)
        {
            p.color = _start.Resolve();
        }

        /// <inheritdoc/>
        public void OnUpdate(ref NeoParticle p, float dt, NeoParticleEmitter e)
        {
            float t = Easing.Evaluate(_ease, p.NormalizedAge);
            // Resolve each frame so a live theme change is reflected; cheap (dictionary lookup).
            p.color = Color.LerpUnclamped(_start.Resolve(), _end.Resolve(), t);
        }
    }

    /// <summary> Serializable config for <see cref="ColorOverLifeModule"/>. </summary>
    [Serializable]
    public sealed class ColorOverLifeModuleConfig : ParticleModuleConfig
    {
        [Tooltip("Color at spawn (theme-token aware).")]
        public ThemeColorRef start = new ThemeColorRef(Color.white);

        [Tooltip("Color at end of life — usually transparent so particles fade out.")]
        public ThemeColorRef end = new ThemeColorRef(new Color(1f, 1f, 1f, 0f));

        [Tooltip("Easing applied to the start→end interpolation over normalized age.")]
        public Ease ease = Ease.Linear;

        /// <inheritdoc/>
        public override string Id => "ColorOverLife";

        /// <inheritdoc/>
        public override IParticleModule Build() => enabled ? new ColorOverLifeModule(start, end, ease) : null;
    }
}
