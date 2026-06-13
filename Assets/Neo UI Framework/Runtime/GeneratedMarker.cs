using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Marks an asset as produced by the spec generator. Idempotent re-generation only overwrites
    /// objects carrying this marker; hand-made assets at the same path are reported as collisions,
    /// never silently replaced.
    /// </summary>
    [AddComponentMenu("")]
    public class GeneratedMarker : MonoBehaviour
    {
        [Tooltip("Spec identifier this object was generated from")]
        public string specSource;

        [Tooltip("Generator schema version, for future migrations")]
        public int generatorVersion = 1;
    }
}
