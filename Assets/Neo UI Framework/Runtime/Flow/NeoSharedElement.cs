using UnityEngine;

namespace Neo.UI
{
    /// <summary>
    /// Marks a widget as a shared element for view transitions: when a navigation cut's transition
    /// has <see cref="ViewTransitionAsset.sharedElements"/> on and a widget with the SAME key exists
    /// in both the outgoing and the incoming view, the widget flies its own frame across the cut
    /// (Figma Smart Animate / Flutter Hero) instead of hiding with one view and showing with the
    /// other. Sharing is an explicit intent — the key is authored, never inferred from widget ids.
    /// Round-trips through the spec as the element-level <c>"sharedElement"</c> field.
    /// </summary>
    [AddComponentMenu("Neo/UI/Flow/Shared Element")]
    public class NeoSharedElement : MonoBehaviour
    {
        [Tooltip("Match key — widgets with the same key in the outgoing and incoming view pair up")]
        public string key;
    }
}
