using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Per-particle simulation state for a <see cref="NeoParticleEmitter"/>. Plain data (no
    /// MonoBehaviour): the emitter owns a pool of these, modules mutate them each frame, and the
    /// emitter pushes the result onto the pooled <see cref="NeoShape"/> instance the particle holds.
    /// Positions are in the emitter's local (RectTransform) space so particles inherit canvas
    /// masking, sort order and CanvasScaler device scaling by living inside the UGUI batch.
    /// </summary>
    public class NeoParticle
    {
        /// <summary> True while this particle is live (between spawn and retirement). </summary>
        public bool alive;

        /// <summary> Local-space position (anchoredPosition of the pooled shape's RectTransform). </summary>
        public Vector2 position;

        /// <summary> Local-space velocity in units per second; integrated each frame by the emitter. </summary>
        public Vector2 velocity;

        /// <summary> Seconds this particle has been alive. </summary>
        public float age;

        /// <summary> Total lifetime in seconds; the particle retires when <see cref="age"/> reaches it. </summary>
        public float lifetime;

        /// <summary> Z rotation in degrees (local euler Z of the pooled shape). </summary>
        public float rotation;

        /// <summary> Angular velocity in degrees per second. </summary>
        public float angularVelocity;

        /// <summary> The size the particle was spawned at (square, in canvas px); modules scale relative to this. </summary>
        public float startSize;

        /// <summary> The current size (square, in canvas px) written to the shape's RectTransform each frame. </summary>
        public float size;

        /// <summary> The current color written to <see cref="shape"/> each frame. </summary>
        public Color color = Color.white;

        /// <summary> The pooled NeoShape this particle renders through (shares the NeoShape material). </summary>
        public NeoShape shape;

        /// <summary> Cached RectTransform of <see cref="shape"/> (so modules/emitter avoid repeated GetComponent). </summary>
        public RectTransform rectTransform;

        /// <summary> Normalized age in [0,1] (age / lifetime), guarded against a zero lifetime. </summary>
        public float NormalizedAge => lifetime > 0f ? Mathf.Clamp01(age / lifetime) : 1f;
    }
}
