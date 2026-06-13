using UnityEngine;

namespace AlterEyes.UI
{
    /// <summary>
    /// Marker for a z-stack container: children keep their own anchors/positions and layer on top
    /// of each other instead of being stacked by a layout group (card art + corner badges + pinned
    /// labels). Inside a layout parent the overlay itself sizes normally (grid cell / LayoutElement);
    /// the spec exporter uses this marker to round-trip the "overlay" element kind.
    /// </summary>
    [AddComponentMenu("AlterEyes/UI/Containers/UI Overlay")]
    public class UIOverlay : MonoBehaviour
    {
    }
}
