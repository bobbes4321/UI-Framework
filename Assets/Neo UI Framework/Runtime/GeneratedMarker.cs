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

        [Tooltip("Id of the showcase whose spec+baseline owns this object, when generated/captured " +
                 "inside a scoped showcase workspace. Empty for the default generated root. Used by the " +
                 "native authoring 'Capture to Spec' flow to route a hand-built view back to its showcase.")]
        public string showcaseId;

        [Tooltip("Generator schema version, for future migrations")]
        public int generatorVersion = 1;
    }
}
